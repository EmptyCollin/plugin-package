using UnityEditor;
using UnityEngine;

namespace Collin.Stretch.EditorTooling
{
    /// <summary>
    /// Scene 视图辅助线（DESIGN §2，v0.1 范围）：
    /// - slice 分界框线（青色）：垂直于拉伸轴，落在**拉伸后**的实际位置；
    /// - 固定段框线（灰）/ 可重复段框线（绿）：段起止各一条，绿段标注 xN；
    /// - 周期等分线（黄色）：repeatable 段复制体的内部边界在每个副本重复出现，
    ///   "3 层楼的段里能数清 3 层"。
    /// 位置计算与 StretchMeshBuilder 完全同源（OffsetFor），保证线与几何严丝合缝。
    /// 拖拽把手推迟到 v0.2（D-4 修订），v0.1 倍数走面板整数输入。
    /// </summary>
    public static class StretchSceneHandles
    {
        private static readonly Color BoundColor = new Color(0.2f, 0.9f, 1f, 0.95f);
        private static readonly Color PeriodColor = new Color(1f, 0.85f, 0.2f, 0.9f);
        private static readonly Color FixedColor = new Color(0.5f, 0.52f, 0.58f, 0.85f);
        private static readonly Color RepeatColor = new Color(0.25f, 0.95f, 0.45f, 0.95f);

        public static bool showGuides = true;

        public static void Draw(StretchPreview preview, StretchProfile profile, Mesh source)
        {
            if (!showGuides || preview == null || profile == null || source == null) return;

            var tr = preview.transform;
            var b = source.bounds;
            int ax = (int)profile.axis;
            Vector3 perpA, perpB;
            if (ax == 0) { perpA = Vector3.up; perpB = Vector3.forward; }
            else if (ax == 1) { perpA = Vector3.right; perpB = Vector3.forward; }
            else { perpA = Vector3.right; perpB = Vector3.up; }
            // perpA 对应轴 = (ax+1)%3 分量，perpB 对应 (ax+2)%3 分量
            float halfA = b.size[(ax + 1) % 3] * 0.5f + 0.05f;
            float halfB = b.size[(ax + 2) % 3] * 0.5f + 0.05f;

            Vector3 PlaneCenter(float coordLocal, float offset)
            {
                var c = b.center;
                c[ax] = coordLocal + offset;
                return tr.TransformPoint(c);
            }

            // 段框线与标签
            for (int i = 0; i < profile.segments.Count; i++)
            {
                float lo = profile.sliceBounds[i];
                float hi = profile.sliceBounds[i + 1];
                var seg = profile.segments[i];
                bool repeatable = seg.type == SegmentType.Repeatable;
                float offset = profile.OffsetFor(i, 0);

                Handles.color = repeatable ? RepeatColor : FixedColor;
                DrawRect(PlaneCenter(lo, offset), perpA * halfA, perpB * halfB);
                float tail = hi + (repeatable ? (Mathf.Max(1, seg.repeat) - 1) * (hi - lo) : 0f);
                DrawRect(PlaneCenter(tail, offset), perpA * halfA, perpB * halfB);

                if (repeatable)
                {
                    var midPos = PlaneCenter((lo + tail) * 0.5f, offset);
                    Handles.Label(midPos + perpA * halfA,
                        $"repeatable x{Mathf.Max(1, seg.repeat)}",
                        new GUIStyle(GUI.skin.label)
                        {
                            normal = { textColor = RepeatColor },
                            fontSize = 12,
                            fontStyle = FontStyle.Bold,
                        });
                }
            }

            // slice 边界（中间边界）在结果空间逐副本重复绘制 → 周期感可视化
            for (int j = 1; j < profile.sliceBounds.Count - 1; j++)
            {
                float c = profile.sliceBounds[j];
                int segIdx = profile.SegmentIndexAt(c);
                if (segIdx < 0) continue;
                float offset = profile.OffsetFor(segIdx, 0);
                var seg = profile.segments[segIdx];
                bool repeatable = seg.type == SegmentType.Repeatable;

                Handles.color = BoundColor;
                DrawRect(PlaneCenter(c, offset), perpA * halfA, perpB * halfB);

                if (repeatable)
                {
                    int repeats = Mathf.Max(1, seg.repeat);
                    float lo = profile.sliceBounds[segIdx];
                    float hi = profile.sliceBounds[segIdx + 1];
                    // 该边界在复制体 1..repeats-1 中的位置
                    Handles.color = PeriodColor;
                    for (int k = 1; k < repeats; k++)
                    {
                        DrawRect(PlaneCenter(c, offset + k * (hi - lo)),
                            perpA * halfA * 0.85f, perpB * halfB * 0.85f);
                    }
                }
            }
        }

        private static void DrawRect(Vector3 center, Vector3 halfA, Vector3 halfB)
        {
            var p0 = center + halfA + halfB;
            var p1 = center + halfA - halfB;
            var p2 = center - halfA - halfB;
            var p3 = center - halfA + halfB;
            Handles.DrawLine(p0, p1);
            Handles.DrawLine(p1, p2);
            Handles.DrawLine(p2, p3);
            Handles.DrawLine(p3, p0);
        }
    }
}
