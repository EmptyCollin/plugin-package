using System;
using System.Collections.Generic;
using UnityEngine;

namespace Collin.CityMap
{
    /// <summary>
    /// 高度场数据层（Modules/01 §2）。工作数组为 float；持久化侧用 int16 量化（D-26）——
    /// 量化只在资产层做，求解过程不量化以免误差累积。
    /// <para>区分 <b>基底</b> 与 <b>最终</b> 两个高度（01 §2.4 的 API 拆分）：
    /// 规划（地块朝向、平整约束目标平面）用基底；道路纵坡、建筑地坪用最终。
    /// 混用会让约束把上一轮结果当输入而逐轮漂移（01 §2.4 正反馈警告）。</para>
    /// </summary>
    public sealed class TerrainField
    {
        public readonly CityGrid Grid;
        /// <summary>最终高度（工作数组；S1b 就地施加约束）。</summary>
        public readonly float[] Heights;
        /// <summary>S1a 基底（约束拟合的输入），规划计算专用。</summary>
        public readonly float[] BaseHeights;

        public TerrainField(CityGrid grid)
        {
            Grid = grid;
            Heights = new float[grid.CellCount];
            BaseHeights = new float[grid.CellCount];
        }

        /// <summary>int16 量化步长：1cm（10 §2.3）。±327.67m 量程。</summary>
        public const float QuantStep = 0.01f;

        public static short EncodeQuant(float meters)
            => (short)Mathf.Clamp(Mathf.RoundToInt(meters / QuantStep), short.MinValue, short.MaxValue);

        public static float DecodeQuant(short q) => q * QuantStep;

        /// <summary>双线性采样（格中心格点系）。越界钳到边缘值，不抛。</summary>
        public float Sample(float[] arr, float x, float z)
        {
            var g = Grid;
            var fx = (x - g.Origin.x) / g.CellSize - 0.5f;
            var fz = (z - g.Origin.y) / g.CellSize - 0.5f;
            var x0 = (int)Math.Floor(fx);
            var z0 = (int)Math.Floor(fz);
            var tx = fx - x0;
            var tz = fz - z0;

            var cols = g.Columns;
            var rows = g.Rows;
            var xa = Mathf.Clamp(x0, 0, cols - 1);
            var xb = Mathf.Clamp(x0 + 1, 0, cols - 1);
            var za = Mathf.Clamp(z0, 0, rows - 1);
            var zb = Mathf.Clamp(z0 + 1, 0, rows - 1);

            var a = arr[za * cols + xa];
            var b = arr[za * cols + xb];
            var c = arr[zb * cols + xa];
            var d = arr[zb * cols + xb];
            return Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), tz);
        }

        public float GetHeight(float x, float z) => Sample(Heights, x, z);
        public float GetBaseHeight(float x, float z) => Sample(BaseHeights, x, z);

        /// <summary>
        /// 中心差分法线（eps = 半格）。<b>显示 mesh 顶点法线必须用同一函数</b>
        /// （01 §3.3：不用 <c>Mesh.RecalculateNormals</c>，否则与采样口径不一致）。
        /// </summary>
        public Vector3 GetNormal(float x, float z, bool useBase = false)
        {
            var arr = useBase ? BaseHeights : Heights;
            var e = Grid.CellSize * 0.5f;
            var hx = (Sample(arr, x + e, z) - Sample(arr, x - e, z)) / (2f * e);
            var hz = (Sample(arr, x, z + e) - Sample(arr, x, z - e)) / (2f * e);
            var n = new Vector3(-hx, 1f, -hz);
            return n.sqrMagnitude > 1e-12f ? n.normalized : Vector3.up;
        }

        public float GetSlopeDeg(float x, float z, bool useBase = false)
            => Vector3.Angle(Vector3.up, GetNormal(x, z, useBase));

        /// <summary>把基底复制到工作数组（每次施加约束前调用 = 幂等的来源，01 §3.2）。</summary>
        public void CommitBaseAsWorking() => Array.Copy(BaseHeights, Heights, BaseHeights.Length);

        /// <summary>区域高度统计（面板/自检用）。返回 false = 无样本。</summary>
        public bool TryRegionStats(Vector2 center, Vector2 halfSize, bool useBase,
                                   out float median, out float min, out float max)
        {
            var list = new List<float>(256);
            var g = Grid;
            g.CellRectForCircle(center, Mathf.Max(halfSize.x, halfSize.y) + g.CellSize,
                                out var minCx, out var minCz, out var maxCx, out var maxCz);
            var arr = useBase ? BaseHeights : Heights;
            for (var cz = minCz; cz <= maxCz; cz++)
            for (var cx = minCx; cx <= maxCx; cx++)
            {
                var cc = g.CellCenterWorld(cx, cz);
                if (Mathf.Abs(cc.x - center.x) > halfSize.x + g.CellSize * 0.5f) continue;
                if (Mathf.Abs(cc.y - center.y) > halfSize.y + g.CellSize * 0.5f) continue;
                list.Add(arr[cz * g.Columns + cx]);
            }
            min = max = median = 0f;
            if (list.Count == 0) return false;
            list.Sort();
            min = list[0]; max = list[list.Count - 1];
            median = list.Count % 2 == 1 ? list[list.Count / 2]
                                         : (list[list.Count / 2 - 1] + list[list.Count / 2]) * 0.5f;
            return true;
        }
    }

    /// <summary>约束种类（01 §3.4）。M2 生产 Parcel/Plaza；RoadCrown、WaterBuffer 由 M3/M4 接入。</summary>
    public enum FlattenKind { Parcel = 0, RoadCrown = 1, Plaza = 2, WaterBuffer = 3 }

    /// <summary>
    /// 约束强度分级（01 §3.4 的 Parcel(强) / RoadCrown(带状) / Plaza(强) / WaterBuffer(弱)）。
    /// <para>为什么必须显式建模：融合公式 <c>h = (h·Wfree + Σ w_i·plane_i)/(Wfree + Σ w_i)</c>
    /// 中，核心贴合度 = <c>Σw/(Σw+Wfree)</c>。若所有约束都用 w=1，配合 Wfree=0.6 只能贴合 62.5%，
    /// 残留 37.5% 原始坡度 ⇒ 21° 坡地上地块仍剩 8°，**放不下建筑**，直接违反 A-1-3。
    /// 设计用"强/弱"标注表达的正是这个倍率，实现不可统一取 1。</para>
    /// <para>用户可调的 zone.weight 是**乘数**（01 §2.2 的 weight 字段语义），与强度相乘。</para>
    /// </summary>
    public static class ConstraintStrength
    {
        public const float Parcel = 8f;
        public const float Plaza = 8f;
        public const float RoadCrown = 4f;
        public const float WaterBuffer = 1f;

        public static float For(FlattenKind kind)
        {
            switch (kind)
            {
                case FlattenKind.Parcel: return Parcel;
                case FlattenKind.Plaza: return Plaza;
                case FlattenKind.RoadCrown: return RoadCrown;
                case FlattenKind.WaterBuffer: return WaterBuffer;
                default: return 1f;
            }
        }

        /// <summary>核心贴合度（无重叠约束时的理论值），供面板/测试核对"强弱"是否真的起作用。</summary>
        public static float AdherenceAtCore(FlattenKind kind, float userWeight, float freeWeight)
        {
            var w = For(kind) * Mathf.Max(0f, userWeight);
            var den = w + Mathf.Max(0f, freeWeight);
            return den > 1e-6f ? w / den : 0f;
        }
    }

    /// <summary>
    /// 平整约束：目标平面 + 核心（内切后）+ 过渡带 + 权重。
    /// <para><b>为什么核心必须内切</b>（M2 实测抓到，坡度反升 4.79°→65.48°）：
    /// 若核心 = 整个地块矩形，相邻两地块在<b>共享边界上同时取全权重</b>，
    /// 而两者目标高程可差数米 ⇒ 边界格被平均成中间值、与其内侧格形成 <c>Δh/cellSize</c> 的陡坎；
    /// 地块内部越平，坎越陡 —— 与"平整"的目的完全相反。
    /// 内切后核心止于边界内侧，过渡带横跨边界，相邻约束在带内加权平均成缓坡（01 §3.4 的本意）。</para>
    /// <para>矩形模式（地块原生形状）而非"矩形 + 圆形衰减"：后者会让角部 blend 回自然坡度，破坏 A-1-3。</para>
    /// </summary>
    public readonly struct PlaneConstraint
    {
        public readonly Vector3 Normal;      // 已归一，y > 0
        public readonly float D;             // 平面方程 n·p = D
        public readonly Vector2 Center;
        public readonly Vector2 HalfSize;    // 地块<b>真实</b>半尺寸（内切前）；Radial：内半径存于 .x
        public readonly float InsetMeters;   // 核心内切量（TerrainProfile.constraintInner）
        public readonly float ExtendMeters;  // 有效衰减总长 = InsetMeters + 外延量
        public readonly float CellTolerance; // = cellSize·0.5，抵消格中心浮点毛刺（不可写死 1m）
        public readonly bool RectMode;
        public readonly float Weight;
        public readonly FlattenKind Kind;
        /// <summary>true = 硬覆盖（绝对高程）：核心处直接赋值，<b>不走 Wfree 软混合</b>。
        /// 软混合的贴合上限是 w/(w+Wfree)，永远不等于用户输入的绝对值（M2 实测差 0.78m）。</summary>
        public readonly bool Exact;

        public PlaneConstraint(Vector3 normal, float d, Vector2 center, Vector2 halfSize,
                               float insetMeters, float outerExtendMeters, float cellTolerance,
                               bool rectMode, float weight, FlattenKind kind, bool exact = false)
        {
            Normal = normal; D = d; Center = center; HalfSize = halfSize; Exact = exact;
            InsetMeters = MaxInsetFor(halfSize) > 0f
                ? Mathf.Clamp(Mathf.Max(0f, insetMeters), 0f, MaxInsetFor(halfSize))
                : 0f;
            ExtendMeters = InsetMeters + Mathf.Max(0.5f, outerExtendMeters);
            CellTolerance = Mathf.Max(0f, cellTolerance);
            RectMode = rectMode; Weight = weight; Kind = kind;
        }

        /// <summary>
        /// 内切上限 = 较短半边的 40%（<b>单一实现处</b>：构造与求解器共用，
        /// 测试若自己再算一份就会与实现的真实核心不一致）。
        /// </summary>
        public static float MaxInsetFor(Vector2 halfSize)
            => Mathf.Max(0f, Mathf.Min(halfSize.x, halfSize.y) * 0.4f);

        /// <summary>核心（全权重区）半尺寸 = 真实半尺寸 − 实际生效的内切量。</summary>
        public Vector2 CoreHalfSize => ComputeCoreHalf(HalfSize, InsetMeters);

        /// <summary>核心半尺寸的公共算法（构造内部、面板叠加绘制、测试取窗口都用它）。</summary>
        public static Vector2 ComputeCoreHalf(Vector2 halfSize, float insetRequested)
        {
            var inset = Mathf.Clamp(Mathf.Max(0f, insetRequested), 0f, MaxInsetFor(halfSize));
            return new Vector2(Mathf.Max(0f, halfSize.x - inset), Mathf.Max(0f, halfSize.y - inset));
        }

        public float HeightAt(float x, float z)
        {
            var ny = Mathf.Abs(Normal.y) < 1e-6f ? 1e-6f : Normal.y;
            return (D - Normal.x * x - Normal.z * z) / ny;
        }

        /// <summary>该点的约束权重（衰减核走 00 §3.3 唯一求值器）。0 = 不影响。</summary>
        public float WeightAt(float x, float z)
        {
            if (Weight <= 0f) return 0f;
            var core = RectMode ? CoreHalfSize : new Vector2(Mathf.Max(HalfSize.x - InsetMeters, 0f), 0f);

            float t;
            if (RectMode)
            {
                // 核心含半格容差：格中心落在核心（含边缘半格）内 → 全权重
                var ex = Mathf.Max(0f, Mathf.Abs(x - Center.x) - (core.x + CellTolerance));
                var ez = Mathf.Max(0f, Mathf.Abs(z - Center.y) - (core.y + CellTolerance));
                var excess = Mathf.Max(ex, ez);
                if (excess <= 0f) return Weight;
                if (excess >= ExtendMeters) return 0f;
                t = excess / ExtendMeters;
            }
            else
            {
                var r = Mathf.Sqrt((x - Center.x) * (x - Center.x) + (z - Center.y) * (z - Center.y));
                var inner = Mathf.Max(core.x, CellTolerance + 0.01f);
                var outer = inner + ExtendMeters;
                if (r <= inner) return Weight;
                if (r >= outer) return 0f;
                t = (r - inner) / (outer - inner);
            }
            // EaseOut：近处陡降、远尾缓延（01 §3.4"过渡带不出现台阶"）。s = 已衰减比例
            var s = Falloff.Evaluate(Falloff.Preset.EaseOut, t);
            return Weight * (1f - s);
        }

        public Bounds GetAffectedBounds()
        {
            var ext = ExtendMeters + CellTolerance;
            var core = RectMode ? CoreHalfSize : new Vector2(Mathf.Max(HalfSize.x - InsetMeters, 0f), 0f);
            var size = RectMode
                ? new Vector3((core.x + ext) * 2f, 1f, (core.y + ext) * 2f)
                : new Vector3((core.x + ext) * 2f, 1f, (core.x + ext) * 2f);
            return new Bounds(new Vector3(Center.x, 0f, Center.y), size);
        }

        public PlaneConstraint With(float outerExtendMeters = -1f, float weight = -1f, bool? rectMode = null)
            => new PlaneConstraint(Normal, D, Center, HalfSize, InsetMeters,
                                   outerExtendMeters > 0f ? outerExtendMeters : ExtendMeters - InsetMeters,
                                   CellTolerance, rectMode ?? RectMode,
                                   weight >= 0f ? weight : Weight, Kind, Exact);
    }

    /// <summary>
    /// 人工层：手动圈定的平整区域（M2 的"人工试设地块"，01 §2.2 Instance 半边）。
    /// <b>存区域不存平面</b> —— 平面每轮从基底重拟合（意图 = 区域，不是绝对高度，D-08）。
    /// </summary>
    [Serializable]
    public sealed class ManualFlattenZone
    {
        public string stableId;
        public Vector2 center;
        [Tooltip("全尺寸（米），非半径")] public Vector2 fullSize = new Vector2(120f, 80f);
        [Tooltip("核心之外的过渡带宽（米）")] public float extendMeters = 12f;
        [Tooltip("0=自动（区域中心取基底中位数）；>0 强制目标高程")] public float absoluteHeight = 0f;
        public bool rectMode = true;
        [Range(0.05f, 2f)] public float weight = 1f;
        public bool enabled = true;
    }

    /// <summary>
    /// 地块目标平面拟合（01 §3.5）：中位数基准 + 条件最小二乘；坡度过阈值退化为水平面。
    /// 纯函数，供 S1b 与测试共用。
    /// </summary>
    public static class PlaneFitting
    {
        /// <summary>
        /// 在矩形区域的格点上拟合目标平面。
        /// <para>返回 false = 样本不足（&lt;4 格，<b>或任一轴不足 3 个不同采样点</b>），
        /// 调用方跳过该约束并计 warning。只要求 ≥4 格是不够的：4 个角点会把平面完全确定
        /// （过 4 点必然零残差），得到的不是"拟合"而是插值，噪声坡地上坡率可达 40°（M2 实测）。</para>
        /// <para>平面必过"区域中心的稳健高度"：坡度合规时用拟合面在该中心的高度，
        /// 超阈值时用中位数并取水平法线。用中位数配倾斜法线会让平面整体偏移（自身不自洽）。</para>
        /// <para><paramref name="weight"/> 是<b>用户乘数</b>；本方法内部再乘 <see cref="ConstraintStrength"/>
        /// 的种类强度（Parcel/Plaza=强、RoadCrown=中、WaterBuffer=弱）。若不乘，
        /// 核心贴合度只有 w/(w+Wfree)=62.5%，陡坡地上地块仍剩 8° 坡度 —— 违反 A-1-3。</para>
        /// </summary>
        public static bool FitFromBase(TerrainField field, Vector2 center, Vector2 halfSize,
                                       float maxSlopeDeg, float insetMeters, float extendMeters,
                                       float weight, FlattenKind kind, out PlaneConstraint plane)
        {
            plane = default;
            var g = field.Grid;
            g.CellRectForCircle(center, Mathf.Max(halfSize.x, halfSize.y) + g.CellSize,
                                out var minCx, out var minCz, out var maxCx, out var maxCz);

            var xs = new List<float>(512);
            var zs = new List<float>(512);
            var hs = new List<float>(512);

            for (var cz = minCz; cz <= maxCz; cz++)
            for (var cx = minCx; cx <= maxCx; cx++)
            {
                var cc = g.CellCenterWorld(cx, cz);
                if (Mathf.Abs(cc.x - center.x) > halfSize.x + g.CellSize * 0.5f) continue;
                if (Mathf.Abs(cc.y - center.y) > halfSize.y + g.CellSize * 0.5f) continue;
                xs.Add(cc.x); zs.Add(cc.y);
                hs.Add(field.GetBaseHeight(cc.x, cc.y));
            }

            var n = hs.Count;
            if (n < 4) return false;
            // 每轴至少 3 个不同采样点，否则最小二乘退化为插值（见上方文档）
            var distinctX = new System.Collections.Generic.HashSet<int>();
            var distinctZ = new System.Collections.Generic.HashSet<int>();
            for (var i = 0; i < n; i++) { distinctX.Add(xs[i].GetHashCode()); distinctZ.Add(zs[i].GetHashCode()); }
            if (distinctX.Count < 3 || distinctZ.Count < 3) return false;

            // ① 中位数基准（抗残缺多边形极值，01 §3.5：均值会被切角拉偏）
            var sorted = new List<float>(hs);
            sorted.Sort();
            var median = n % 2 == 1 ? sorted[n / 2] : (sorted[n / 2 - 1] + sorted[n / 2]) * 0.5f;

            // ② 最小二乘拟合 h = a + b·x + c·z（中心化后的正规方程，数值稳定）
            double mx = 0, mz = 0, mh = 0;
            for (var i = 0; i < n; i++) { mx += xs[i]; mz += zs[i]; mh += hs[i]; }
            mx /= n; mz /= n; mh /= n;

            double sxx = 0, szz = 0, sxz = 0, sxh = 0, szh = 0;
            for (var i = 0; i < n; i++)
            {
                double dx = xs[i] - mx, dz = zs[i] - mz, dh = hs[i] - mh;
                sxx += dx * dx; szz += dz * dz; sxz += dx * dz;
                sxh += dx * dh; szh += dz * dh;
            }

            var det = sxx * szz - sxz * sxz;
            var scale = Math.Max(1.0, Math.Abs(sxx * szz));
            var fitted = Math.Abs(det) > 1e-9 * scale;
            double b = 0, c = 0;
            if (fitted)
            {
                b = (sxh * szz - szh * sxz) / det;
                c = (sxx * szh - sxz * sxh) / det;
            }
            // H-32 修复后，截距 a 不再参与目标高程计算（坡向用解析式、水平用中位数），
            // 故此处不再声明 a —— 留着会触发 CS0219「赋值未使用」warning，破坏 §1.6b 归零门禁。

            // ③ 坡度判定：合规用拟合面（保留轻微坡向，利于排水与观感），否则退化为水平面（中位数）
            var slopeDeg = fitted ? Mathf.Rad2Deg * (float)Math.Atan(Math.Sqrt(b * b + c * c)) : 0f;
            var useFit = fitted && slopeDeg <= maxSlopeDeg;

            Vector3 normal;
            float dAtCenter;
            if (useFit)
            {
                // H-32：坡向取 LSQ，<b>水平取中位数</b>。
                // 若水平也取 LSQ 的 a（=均值平面截距），单个离群点会把整块目标高程抬 Δ/N
                // —— 而"用中位数而非均值"的抗离群设计意图正好被 LSQ 旁路掉（M2 实测偏 0.82m）。
                normal = new Vector3(-(float)b, 1f, -(float)c).normalized;
                // 坡向相对均值点的抬升量（解析式）：b·Δx + c·Δz。
                // 不走 FitHeightAt(mx,mz) 相减 —— 那是 double 入参且两次求值，易引入舍入与类型问题。
                var tiltAtCenter = (float)(b * ((double)center.x - mx) + c * ((double)center.y - mz));
                dAtCenter = median + tiltAtCenter;               // 过均值点的坡向 + 中心处的稳健高程
            }
            else
            {
                normal = Vector3.up;
                dAtCenter = median;
            }
            // 平面过点 (center.x, dAtCenter, center.y)
            var d = normal.x * center.x + normal.y * dAtCenter + normal.z * center.y;

            plane = new PlaneConstraint(normal, d, center, halfSize, insetMeters, extendMeters,
                                        g.CellSize * 0.5f, true,
                                        weight * ConstraintStrength.For(kind), kind);
            return true;
        }
    }
}
