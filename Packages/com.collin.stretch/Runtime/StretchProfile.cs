using System;
using System.Collections.Generic;
using UnityEngine;

namespace Collin.Stretch
{
    /// <summary>拉伸轴（网格对象本地空间）。</summary>
    public enum StretchAxis { X = 0, Y = 1, Z = 2 }

    /// <summary>段类型：fixed = 整段刚性平移；repeatable = 整段按周期复制。</summary>
    public enum SegmentType { Fixed = 0, Repeatable = 1 }

    /// <summary>
    /// 拉伸**共享结构**资产（DESIGN D-1，AGENTS.md §0.1 原则2 的 Model 半边）：
    /// 轴、slice 边界、每段类型。倍数属于实例状态，存于 <see cref="StretchInstance"/>，
    /// 这样同一 Profile 可被多栋楼复用而各自倍数不同。
    /// </summary>
    [CreateAssetMenu(menuName = "Collin/Stretch/Profile", fileName = "NewStretchProfile")]
    public class StretchProfile : ScriptableObject
    {
        public StretchAxis axis = StretchAxis.Y;
        /// <summary>升序，含包围盒首尾端点。</summary>
        public List<float> sliceBounds = new List<float>();
        /// <summary>.Count == sliceBounds.Count - 1。</summary>
        public List<SegmentType> segmentTypes = new List<SegmentType>();

        public int SegmentCount => Mathf.Max(0, sliceBounds.Count - 1);

        /// <summary>轴坐标所属段索引；越界返回 -1。</summary>
        public int SegmentIndexAt(float coord)
        {
            for (int i = 0; i < segmentTypes.Count && i + 1 < sliceBounds.Count; i++)
            {
                if (coord >= sliceBounds[i] && coord < sliceBounds[i + 1]) return i;
            }
            if (sliceBounds.Count > 0 && Mathf.Approximately(coord, sliceBounds[sliceBounds.Count - 1]))
            {
                return segmentTypes.Count - 1;
            }
            return -1;
        }

        public SegmentType TypeAt(int i) =>
            i >= 0 && i < segmentTypes.Count ? segmentTypes[i] : SegmentType.Fixed;

        /// <summary>位于 coord 之下的 repeatable 段贡献的总延长量。</summary>
        public float ExtensionBelow(float coord, IList<int> repeats)
        {
            float sum = 0f;
            for (int i = 0; i < segmentTypes.Count && i + 1 < sliceBounds.Count; i++)
            {
                if (segmentTypes[i] != SegmentType.Repeatable) continue;
                if (sliceBounds[i + 1] <= coord)
                {
                    sum += SegmentLength(i) * (RepeatAt(repeats, i) - 1);
                }
            }
            return sum;
        }

        /// <summary>段 i 复制体 k 的起点位移：下方延展 + k×段长。</summary>
        public float OffsetFor(int segIndex, int copy, IList<int> repeats)
        {
            if (segIndex < 0 || segIndex >= segmentTypes.Count) return 0f;
            return ExtensionBelow(sliceBounds[segIndex], repeats) + copy * SegmentLength(segIndex);
        }

        public float SegmentLength(int i)
        {
            if (i < 0 || i + 1 >= sliceBounds.Count) return 0f;
            return sliceBounds[i + 1] - sliceBounds[i];
        }

        /// <summary>repeatable 段的有效倍数（<1 视为 1）。</summary>
        public static int RepeatAt(IList<int> repeats, int i)
        {
            if (repeats == null || i < 0 || i >= repeats.Count) return 1;
            return Mathf.Max(1, repeats[i]);
        }

        public float TotalExtension(IList<int> repeats)
        {
            float sum = 0f;
            for (int i = 0; i < segmentTypes.Count; i++)
            {
                if (segmentTypes[i] == SegmentType.Repeatable)
                {
                    sum += SegmentLength(i) * (RepeatAt(repeats, i) - 1);
                }
            }
            return sum;
        }

        public float TotalFactor(float min, float max, IList<int> repeats)
        {
            float src = Mathf.Max(1e-6f, max - min);
            return (src + TotalExtension(repeats)) / src;
        }

        /// <summary>结构校验（与实例无关）：边界升序含端点、段数匹配。返回首个错误信息或 null。</summary>
        public string Validate(float min, float max)
        {
            if (sliceBounds.Count < 2) return "至少需要两个边界（首尾）";
            if (segmentTypes.Count != sliceBounds.Count - 1) return "段数与边界数不匹配";
            if (Mathf.Abs(sliceBounds[0] - min) > 1e-3f) return "首个边界应等于包围盒最小值";
            if (Mathf.Abs(sliceBounds[sliceBounds.Count - 1] - max) > 1e-3f) return "末个边界应等于包围盒最大值";
            for (int i = 1; i < sliceBounds.Count; i++)
            {
                if (sliceBounds[i] <= sliceBounds[i - 1]) return "边界必须严格升序";
            }
            return null;
        }

        /// <summary>实例校验：倍数数组长度须覆盖段数（长度由组件保证，这里仅兜底）。</summary>
        public string ValidateRepeats(IList<int> repeats)
        {
            if (repeats == null || repeats.Count != segmentTypes.Count)
            {
                return "倍数数组与段数不匹配";
            }
            return null;
        }

        /// <summary>以包围盒端点初始化：默认单段 fixed。</summary>
        public void InitFromBounds(float min, float max)
        {
            sliceBounds.Clear();
            sliceBounds.Add(min);
            sliceBounds.Add(max);
            segmentTypes.Clear();
            segmentTypes.Add(SegmentType.Fixed);
        }

        /// <summary>轴+边界一体初始化（防"轴与包围盒不同源"脚坑：Validate 会用同一轴复查）。</summary>
        public void InitFromMesh(UnityEngine.Mesh mesh, StretchAxis axis0)
        {
            axis = axis0;
            int i = (int)axis0;
            InitFromBounds(mesh.bounds.min[i], mesh.bounds.max[i]);
        }

        /// <summary>在 coord 处插入边界（若合法），返回段索引或 -1。</summary>
        public int InsertBound(float coord)
        {
            if (sliceBounds.Count < 2) return -1;
            float min = sliceBounds[0], max = sliceBounds[sliceBounds.Count - 1];
            if (coord <= min + 1e-4f || coord >= max - 1e-4f) return -1;
            int idx = -1;
            for (int i = 1; i < sliceBounds.Count - 1; i++)
            {
                if (coord < sliceBounds[i]) { idx = i; break; }
            }
            if (idx < 0) idx = sliceBounds.Count - 1;
            sliceBounds.Insert(idx, coord);
            segmentTypes.Insert(idx, SegmentType.Fixed);
            return idx;
        }

        public bool RemoveBound(int boundIndex)
        {
            if (boundIndex <= 0 || boundIndex >= sliceBounds.Count - 1) return false;
            sliceBounds.RemoveAt(boundIndex);
            segmentTypes.RemoveAt(boundIndex - 1);
            return true;
        }

        /// <summary>实例组件用：按当前段数生成默认倍数数组（全 1）。</summary>
        public int[] DefaultRepeats()
        {
            var r = new int[Mathf.Max(0, segmentTypes.Count)];
            for (int i = 0; i < r.Length; i++) r[i] = 1;
            return r;
        }
    }
}
