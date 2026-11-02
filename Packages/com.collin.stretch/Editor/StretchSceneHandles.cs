using UnityEditor;
using UnityEngine;

namespace Collin.Stretch.EditorTooling
{
    /// <summary>
    /// Scene 视图辅助线（DESIGN §2）：
    /// - 段框线（绿=repeatable 结果尾端 / 灰=fixed）：落在拉伸后实际位置；
    /// - slice 边界框线（青）+ 周期等分线（黄）：复制体内每层可数；
    /// - 每个复制体中心标注 xN。位置计算与 StretchMeshBuilder 同源（OffsetFor）。
    /// </summary>
    public static class StretchSceneHandles
    {
        private static readonly Color BoundColor = new Color(0.2f, 0.9f, 1f, 0.95f);
        private static readonly Color PeriodColor = new Color(1f, 0.85f, 0.2f, 0.9f);
        private static readonly Color FixedColor = new Color(0.5f, 0.52f, 0.58f, 0.85f);
        private static readonly Color RepeatColor = new Color(0.25f, 0.95f, 0.45f, 0.95f);

        public static bool showGuides = true;

        public static void Draw(StretchInstance inst, StretchProfile profile, Mesh source, Mesh result)
        {
            if (!showGuides || inst == null || profile == null || source == null) return;
            var repeats = inst.EffectiveRepeats();
            var tr = inst.transform;
            var b = source.bounds;
            int ax = (int)profile.axis;

            Vector3 perpA, perpB;
            if (ax == 0) { perpA = Vector3.up; perpB = Vector3.forward; }
            else if (ax == 1) { perpA = Vector3.right; perpB = Vector3.forward; }
            else { perpA = Vector3.right; perpB = Vector3.up; }
            float halfA = b.size[(ax + 1) % 3] * 0.5f + 0.05f;
            float halfB = b.size[(ax + 2) % 3] * 0.5f + 0.05f;

            Vector3 PlaneCenter(float coordLocal, float offset)
            {
                var c = b.center;
                c[ax] = coordLocal + offset;
                return tr.TransformPoint(c);
            }

            for (int i = 0; i < profile.segmentTypes.Count; i++)
            {
                float lo = profile.sliceBounds[i];
                float hi = profile.sliceBounds[i + 1];
                bool repeatable = profile.TypeAt(i) == SegmentType.Repeatable;
                int rep = repeatable ? StretchProfile.RepeatAt(repeats, i) : 1;
                float offset = profile.OffsetFor(i, 0, repeats);

                Handles.color = repeatable ? RepeatColor : FixedColor;
                DrawRect(PlaneCenter(lo, offset), perpA * halfA, perpB * halfB);
                float tail = hi + (rep - 1) * (hi - lo);
                DrawRect(PlaneCenter(tail, offset), perpA * halfA, perpB * halfB);

                if (repeatable && rep > 1)
                {
                    var midPos = PlaneCenter((lo + tail) * 0.5f, offset) + perpA * halfA;
                    Handles.Label(midPos, $"x{rep}", new GUIStyle(GUI.skin.label)
                    {
                        normal = { textColor = RepeatColor },
                        fontSize = 12,
                        fontStyle = FontStyle.Bold,
                    });
                }
            }

            // 边界与周期线：段内细分在每个复制体重复
            for (int j = 1; j < profile.sliceBounds.Count - 1; j++)
            {
                float c = profile.sliceBounds[j];
                int segIdx = profile.SegmentIndexAt(c);
                if (segIdx < 0) continue;
                var segType = profile.TypeAt(segIdx);
                float offset = profile.OffsetFor(segIdx, 0, repeats);
                float lo = profile.sliceBounds[segIdx];
                float hi = profile.sliceBounds[segIdx + 1];

                Handles.color = BoundColor;
                DrawRect(PlaneCenter(c, offset), perpA * halfA, perpB * halfB);

                if (segType == SegmentType.Repeatable)
                {
                    int rep = StretchProfile.RepeatAt(repeats, segIdx);
                    Handles.color = PeriodColor;
                    for (int k = 1; k < rep; k++)
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
