// 地面警戒投影专用着色器：半透明、关深度写入、带深度偏移。
//
// 为什么必须自己写（用户反馈「闪得厉害」的根因）：
// 1) URP/Unlit 与 Unlit/Color 默认 Queue=Geometry、ZWrite On —— 用它们画地面色块会
//    与地面<b>争深度</b>，即使材质 alpha < 1 也没进透明队列，结果就是剧烈闪烁；
// 2) 仅靠把多边形抬到地面之上仍不够：抬太少照样 Z 冲突，抬多了在斜面上会浮空。
//    这里 Queue=Transparent + ZWrite Off + ZTest LEqual 保证「后画、只比深度不写深度」，
//    再叠 Offset 深度偏移做双保险。
//
// LightMode 用 SRPDefaultUnlit：URP 的 DrawObjectsPass 的 shaderTagIds 首位就是它
// （已在 com.unity.render-pipelines.universal/Runtime/Passes/DrawObjectsPass.cs:87 核实），
// 内置管线亦会绘制无 UniversalForward 的该标签，因此两种管线都能画出来。
Shader "Hidden/Collin/Stealth/GroundProjection"
{
    Properties
    {
        _Color("Color (RGBA)", Color) = (1, 0.85, 0.3, 0.22)
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            Tags { "LightMode" = "SRPDefaultUnlit" }

            Cull Off
            ZWrite Off
            ZTest LEqual
            // 向相机方向做少量深度偏移，彻底压掉与地面的共面冲突
            Offset -1, -1
            Blend SrcAlpha OneMinusSrcAlpha
            ColorMask RGB

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float4 _Color;

            struct appdata
            {
                float4 vertex : POSITION;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                UNITY_FOG_COORDS(0)
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 c = _Color;
                UNITY_APPLY_FOG(i.fogCoord, c);
                return c;
            }
            ENDCG
        }
    }

    Fallback Off
}
