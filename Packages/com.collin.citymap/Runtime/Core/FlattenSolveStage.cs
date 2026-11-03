using System;
using System.Collections.Generic;
using UnityEngine;

namespace Collin.CityMap
{
    /// <summary>
    /// S1b：平整约束回写（Modules/01 §3.4，D-04/D-12）。
    /// <para>算法：<c>h = (h·Wfree + Σ w_i·plane_i) / (Wfree + Σ w_i)</c> —— 一次加权融合，
    /// <b>迭代上限 1 次</b>；迭代松弛会让改动沿空间传播，破坏局部稳定性与 A-1-7/A-2 断言。</para>
    /// <para><b>幂等关键</b>：每次先 <c>CommitBaseAsWorking()</c> 从基底重放，再施加约束。
    /// 若就地在工作数组上叠加，连续两次 S1b 会把上一轮结果当输入 ⇒ 逐轮漂移（01 §2.4 的正反馈警告）。
    /// M3/M4 接入雕刻 delta 与河流下切后，重放目标改为"基底 + delta + carve"，融合逻辑不变（01 §3.2 次序固定）。</para>
    /// <para>状态（scratch 缓冲、约束表）全部<b>实例级</b>：静态缓冲会被第二个城市上下文串用，
    /// 表现为多城/多 Runner 时脏数据互相污染。</para>
    /// </summary>
    public sealed class FlattenSolveStage : IPipelineStage
    {
        public StageId Id => StageId.S1b_Flatten;
        public DirtyBits DependsOn => StageClaims.Of(StageId.S1b_Flatten);

        readonly TerrainProfile _profile;
        readonly CityTerrain _terrain;

        readonly List<PlaneConstraint> _constraints = new List<PlaneConstraint>(128);
        readonly List<int> _touched = new List<int>(4096);
        float[] _num, _den, _target;
        byte[] _exact;
        int _scratchCells;

        public FlattenSolveStage(TerrainProfile profile, CityTerrain terrain)
        {
            _profile = profile;
            _terrain = terrain;
        }

        public StageResult Run(PipelineContext ctx, RunMode mode)
        {
            if (ctx == null) return StageResult.Fail("ctx 空");
            if (_profile == null) return StageResult.Fail("缺 TerrainProfile");
            if (_terrain == null) return StageResult.Fail("缺 CityTerrain");

            var field = TerrainBaseStage.EnsureField(ctx);
            var grid = ctx.Grid;

            // ① 重放到工作数组（幂等）
            field.CommitBaseAsWorking();

            // ② 收集约束
            _constraints.Clear();
            var skipped = CollectConstraints(ctx, field, grid);
            if (_constraints.Count == 0)
            {
                _terrain.ReportSolveResult(0, 0, skipped);
                return StageResult.Ok(0, 0, skipped);
            }

            EnsureScratch(grid.CellCount);

            // ③ 逐约束累加分子/分母（只遍历各自外接盒）
            _touched.Clear();
            var cols = grid.Columns;
            for (var i = 0; i < _constraints.Count; i++)
            {
                if (ctx.CancelRequested) return StageResult.CancelledResult();
                Accumulate(_constraints[i], grid, cols);
            }

            // ④ 单次融合 + 缓冲复位（复位必须在下一约束前完成，故放在同一循环内清）
            var heights = field.Heights;
            var freeW = _profile.FreeWeight;
            var touched = 0;
            for (var i = 0; i < _touched.Count; i++)
            {
                var idx = _touched[i];
                var den = _den[idx];
                var num = _num[idx];
                var target = _target[idx];           // 全权重处 = 目标平面高程
                var hard = _exact[idx] != 0;
                if (den <= 1e-8f)
                {
                    _num[idx] = 0f; _den[idx] = 0f; _target[idx] = 0f; _exact[idx] = 0;
                    continue;
                }

                // 硬覆盖（绝对高程）：_exact 已由 Accumulate 在"权重饱和到 con.Weight"的点置位，
                // 此处直接赋值。若再要求 den/(den+Wfree)>=1 则永不成立（Wfree=0.6 ⇒ 上限 0.94），
                // 绝对高程仍达不到目标值 —— 这是 M2 实测的第二个根因。
                // 过渡带内不置位 ⇒ 退回软混合 ⇒ 硬区与软区之间仍连续。
                heights[idx] = hard
                    ? target
                    : (heights[idx] * freeW + num) / (freeW + den);
                _num[idx] = 0f; _den[idx] = 0f; _target[idx] = 0f; _exact[idx] = 0;
                touched++;
            }

            _terrain.ReportSolveResult(_constraints.Count, touched, skipped);
            return StageResult.Ok(touched, _constraints.Count, skipped);
        }

        void Accumulate(in PlaneConstraint con, CityGrid grid, int cols)
        {
            var b = con.GetAffectedBounds();
            var radius = Mathf.Max(b.extents.x, b.extents.z);
            grid.CellRectForCircle(new Vector2(b.center.x, b.center.z), radius,
                                   out var minCx, out var minCz, out var maxCx, out var maxCz);

            for (var cz = minCz; cz <= maxCz; cz++)
            {
                var wz = grid.Origin.y + (cz + 0.5f) * grid.CellSize;
                var rowBase = cz * cols;
                for (var cx = minCx; cx <= maxCx; cx++)
                {
                    var wx = grid.Origin.x + (cx + 0.5f) * grid.CellSize;
                    var w = con.WeightAt(wx, wz);
                    if (w <= 0f) continue;

                    var idx = rowBase + cx;
                    var hTarget = con.HeightAt(wx, wz);
                    if (_den[idx] <= 0f) _touched.Add(idx);   // 首次进入：登记待复位
                    _num[idx] += w * hTarget;
                    _den[idx] += w;
                    // 硬覆盖标志只在权重饱和（=Weight）时生效，过渡带内退回软混合以保证连续
                    if (con.Exact && w >= con.Weight - 1e-5f) { _exact[idx] = 1; _target[idx] = hTarget; }
                }
            }
        }

        /// <summary>
        /// 约束来源（M2）：
        /// ① 自动 —— 覆盖城心的 N×N 试设地块（01 §2.2 的"先用人工试设地块验证约束"，M5 换真实 parcel）；
        /// ② 人工 —— <see cref="CityTerrain"/> 的显式圈定区域（存区域，平面每轮从基底重拟合）。
        /// 返回被跳过的约束数（样本不足 / 目标平面过陡）→ 计入 StageResult.Warnings。
        /// </summary>
        int CollectConstraints(PipelineContext ctx, TerrainField field, CityGrid grid)
        {
            var skipped = 0;
            var profile = _profile;
            var terrain = _terrain;

            // 自动试设地块：布局走 CityTerrain.EnumerateAutoTiles（与面板叠加、测试同源，避免重复实现）
            if (terrain.AutoFlattenAtCenter)
            {
                var localSkipped = 0;
                CityTerrain.EnumerateAutoTiles(terrain, profile, ctx.Project, grid, (c, half) =>
                {
                    if (!PlaneFitting.FitFromBase(field, c, half,
                            profile.MaxParcelSlope, profile.ConstraintInner,
                            profile.ConstraintExtend, 1f,
                            FlattenKind.Parcel, out var plane) || !TargetPlaneAllowed(plane, profile))
                    { localSkipped++; return; }
                    _constraints.Add(plane);
                });
                skipped += localSkipped;
            }

            var zones = terrain.ManualZones;
            for (var i = 0; i < zones.Count; i++)
            {
                var z = zones[i];
                if (z == null || !z.enabled) continue;

                var halfSize = new Vector2(Mathf.Max(4f, z.fullSize.x) * 0.5f,
                                           Mathf.Max(4f, z.fullSize.y) * 0.5f);

                PlaneConstraint plane;
                if (z.absoluteHeight > 0f)
                {
                    // 绝对高程 = <b>硬覆盖</b>（Exact）：核心处直接赋值，不走 Wfree 软混合。
                    // 软混合上限 w/(w+Wfree) 永远达不到用户输入的绝对值（M2 实测 6.72 vs 7.5）。
                    // M3 接 delta 层后改判为 Absolute 模式写 delta，使绝对效果在重算基底后仍成立（01 §2.3）。
                    plane = new PlaneConstraint(Vector3.up, z.absoluteHeight, z.center, halfSize,
                                               profile.ConstraintInner, z.extendMeters,
                                               grid.CellSize * 0.5f, z.rectMode,
                                               z.weight, FlattenKind.Plaza, exact: true);
                }
                else
                {
                    if (!PlaneFitting.FitFromBase(field, z.center, halfSize, profile.MaxParcelSlope,
                            profile.ConstraintInner, z.extendMeters, z.weight,
                            FlattenKind.Plaza, out plane))
                    { skipped++; continue; }
                    // weight 传 -1 = 保持 FitFromBase 已乘好的强度（此处若再传 z.weight 会把强度抹掉）
                    plane = plane.With(z.extendMeters, -1f, z.rectMode);
                }

                if (plane.Weight <= 0f) continue;
                if (!TargetPlaneAllowed(plane, profile)) { skipped++; continue; }
                _constraints.Add(plane);
            }
            return skipped;
        }

        /// <summary>
        /// 目标平面坡度守卫：拟合面自身过陡会使地块角部翘起、违反 A-1-3 容差 ⇒ 跳过并计 warning。
        /// <c>targetPlaneMaxSlope ≤ 0</c> 表示不检查。
        /// </summary>
        static bool TargetPlaneAllowed(in PlaneConstraint plane, TerrainProfile profile)
        {
            var limit = profile.TargetPlaneMaxSlope;
            return limit <= 0f || Vector3.Angle(Vector3.up, plane.Normal) <= limit;
        }

        void EnsureScratch(int cells)
        {
            if (_scratchCells == cells && _num != null && _den != null && _target != null && _exact != null)
                return;
            _num = new float[cells];
            _den = new float[cells];
            _target = new float[cells];
            _exact = new byte[cells];
            _scratchCells = cells;
        }
    }
}
