// 01 §3.3：本包**不依赖任何外部 shader 包**，自带 URP-compatible 的地形显示着色器。
// 存在理由（M3 交付缺口，用户实机反馈"地表没有材质，看起伏比较累"）：显示层已把坡度写进顶点色
// （绿→土黄→红棕，见 TerrainView.SlopeColor），但 URP/Lit 与 Diffuse **都不读 COLOR 语义** ⇒ 色带等于白写。
// 只做一件事：顶点色 × 廉价兰伯特主光，保证"起伏与陡弱一眼可辨"。
// 非真实光照（无 GBuffer / 无烘焙 GI / 不接反射 / 不投影），正式项目应换自己的地形材质（View 只读、不参与生成）。
Shader "Collin/CityMap/Terrain VertexColor"
{
    Properties
    {
        _BaseColor ("基础色倍乘", Color) = (1, 1, 1, 1)
        _Ambient ("环境光占比", Range(0, 1)) = 0.45
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "UniversalForward"
            Tags { "LightMode" = "UniversalForward" }

            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            // Core.hlsl 带入坐标变换（TransformObjectToHClip / TransformObjectToWorldNormal）；
            // Lighting.hlsl 带入 GetMainLight()。二者均为 URP 包内真实文件，已实测存在。
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 color      : COLOR;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 normalWS    : TEXCOORD0;
                half4  color       : TEXCOORD1;
            };

            CBUFFER_START(UnityPerMaterial)
                half4  _BaseColor;
                float  _Ambient;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.normalWS    = TransformObjectToWorldNormal(IN.normalOS);
                OUT.color       = IN.color;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 n = normalize(IN.normalWS);
                Light mainLight = GetMainLight();
                float ndl = saturate(dot(n, mainLight.direction));
                float key = lerp(ndl, 1.0, _Ambient);
                half3 c = IN.color.rgb * _BaseColor.rgb * key * mainLight.color;
                return half4(c, 1.0);
            }
            ENDHLSL
        }
    }

    // URP 之外的环境（如切回内置管线）保底：至少能看到顶点色。
    Fallback "Diffuse"
}
