using System;
using System.Collections.Generic;
using UnityEngine;

namespace Collin.Stretch
{
    /// <summary>拉伸轴（网格对象本地空间）。</summary>
    public enum StretchAxis { X = 0, Y = 1, Z = 2 }

    /// <summary>段类型：fixed = 整段刚性平移；repeatable = 整段按周期复制。</summary>
    public enum SegmentType { Fixed = 0, Repeatable = 1 }

    /// <summary>一个段的描述。段 i = [sliceBounds[i], sliceBounds[i+1]]。</summary>
    [Serializable]
    public class StretchSegment
    {
        public SegmentType type = SegmentType.Fixed;
        /// <summary>repeatable 用：复制份数（含原始，1 = 不复制）。整数语义是核心（DESIGN D-4）。</summary>
        public int repeat = 1;
    }

    /// <summary>
    /// 拉伸参数资产（DESIGN D-1/D-3）：纯数据 + 纯计算，不触碰 Mesh 生成。
    /// 语义：沿 axis 以 sliceBounds 划段；三角形按重心归段后整三角形刚性放置——
    /// fixed 段随上方延展整体平移，repeatable 段复制 repeat 份首尾相接。
    /// </summary>
    [CreateAssetMenu(menuName = "Collin/Stretch/Profile", fileName = "NewStretchProfile")]
    public class StretchProfile : ScriptableObject
    {
        public StretchAxis axis = StretchAxis.Y;
        /// <summary>升序，含包围盒首尾端点。</summary>
        public List<float> sliceBounds = new List<float>();
        /// <summary>.Count == sliceBounds.Count - 1。</summary>
        public List<StretchSegment> segments = new List<StretchSegment>();

        public int SegmentCount => Mathf.Max(0, sliceBounds.Count - 1);

        /// <summary>轴坐标所属段索引；越界返回 -1。</summary>
        public int SegmentIndexAt(float coord)
        {
            for (int i = 0; i < segments.Count && i + 1 < sliceBounds.Count; i++)
            {
                if (coord >= sliceBounds[i] && coord < sliceBounds[i + 1]) return i;
            }
            // 恰落在最后一个端点上
            if (sliceBounds.Count > 0 && Mathf.Approximately(coord, sliceBounds[sliceBounds.Count - 1]))
            {
                return segments.Count - 1;
            }
            return -1;
        }

        /// <summary>位于 coord 之下的 repeatable 段贡献的总延长量。</summary>
        public float ExtensionBelow(float coord)
        {
            float sum = 0f;
            for (int i = 0; i < segments.Count && i + 1 < sliceBounds.Count; i++)
            {
                if (segments[i].type != SegmentType.Repeatable) continue;
                float hi = sliceBounds[i + 1];
                if (hi <= coord) sum += (hi - sliceBounds[i]) * (segments[i].repeat - 1);
            }
            return sum;
        }

        /// <summary>段 i 复制体 k（0..repeat-1）的起点位移：下方延展 + k×段长。</summary>
        public float OffsetFor(int segIndex, int copy)
        {
            if (segIndex < 0 || segIndex >= segments.Count) return 0f;
            float baseOffset = ExtensionBelow(sliceBounds[segIndex]);
            return baseOffset + copy * SegmentLength(segIndex);
        }

        public float SegmentLength(int i)
        {
            if (i < 0 || i + 1 >= sliceBounds.Count) return 0f;
            return sliceBounds[i + 1] - sliceBounds[i];
        }

        /// <summary>全部延展量（结果总长 - 源总长）。</summary>
        public float TotalExtension()
        {
            float sum = 0f;
            for (int i = 0; i < segments.Count; i++)
            {
                if (segments[i].type == SegmentType.Repeatable)
                {
                    sum += SegmentLength(i) * (segments[i].repeat - 1);
                }
            }
            return sum;
        }

        public float TotalFactor(float min, float max)
        {
            float src = Mathf.Max(1e-6f, max - min);
            return (src + TotalExtension()) / src;
        }

        /// <summary>校验：边界升序含端点、段数匹配、倍数合法。返回首个错误信息或 null。</summary>
        public string Validate(float min, float max)
        {
            if (sliceBounds.Count < 2) return "至少需要两个边界（首尾）";
            if (segments.Count != sliceBounds.Count - 1) return "段数与边界数不匹配";
            if (Mathf.Abs(sliceBounds[0] - min) > 1e-3f) return "首个边界应等于包围盒最小值";
            if (Mathf.Abs(sliceBounds[sliceBounds.Count - 1] - max) > 1e-3f) return "末个边界应等于包围盒最大值";
            for (int i = 1; i < sliceBounds.Count; i++)
            {
                if (sliceBounds[i] <= sliceBounds[i - 1]) return "边界必须严格升序";
            }
            foreach (var s in segments)
            {
                if (s.type == SegmentType.Repeatable && s.repeat < 1) return "复制倍数必须 ≥ 1";
            }
            return null;
        }

        /// <summary>以包围盒端点初始化：默认单段 fixed（倍数无意义）。</summary>
        public void InitFromBounds(float min, float max)
        {
            sliceBounds.Clear();
            sliceBounds.Add(min);
            sliceBounds.Add(max);
            segments.Clear();
            segments.Add(new StretchSegment { type = SegmentType.Fixed, repeat = 1 });
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
            segments.Insert(idx, new StretchSegment { type = SegmentType.Fixed, repeat = 1 });
            return idx;
        }

        /// <summary>删除第 i 个中间边界（与其段一起），保留首尾。</summary>
        public bool RemoveBound(int boundIndex)
        {
            if (boundIndex <= 0 || boundIndex >= sliceBounds.Count - 1) return false;
            sliceBounds.RemoveAt(boundIndex);
            segments.RemoveAt(boundIndex - 1);
            return true;
        }
    }
}
