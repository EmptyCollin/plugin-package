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

        /// <summary>自然化用的整图积分图（(cols+1)×(rows+1)）。<b>实例级</b>同 scratch 纪律：
        /// 静态缓冲会被第二个城市上下文串用。</summary>
        float[] _natPrefix;

        void EnsurePrefix(int cols, int rows)
        {
            var need = (cols + 1) * (rows + 1);
            if (_natPrefix == null || _natPrefix.Length < need) _natPrefix = new float[need];
        }

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

            // ①b 人工雕刻层（01 §3.2 第 3 步：delta 在平整**之前**，人工优先于自动平整）
            //     复位会抹掉上一轮的平整结果与 delta，故每轮在此重新叠加 —— 缺了这一步，
            //     雕刻只活一轮，下一次 S1b 就把它冲平（正反馈的另一面：欠反馈）。
            //     叠完之后工作数组 = "自然 + 人工、尚未平整"的<b>意图地表</b>，
            //     ② 的目标平面拟合就读它（M4-c 用户裁定：雕刻必须波及地块坡度/平整目标）。
            var d = _terrain.Delta;
            if (d != null) d.ApplyTo(field);

            // ①c 阶段开关（12 §2 三步工作流）。**语义在 M4-o 改版**（用户裁定）：
            //     平整不再是"实时求解的开关"，而是**烘焙出来的滤镜结果层**——点「烘焙平整」才计算，
            //     结果以**独立层**（`SculptFilterAsset`）叠加在意图地表之上，**绝不写回 delta 意图层**。
            //     关＝只看"噪声基底 + 手绘雕刻"的原始意图地表；开＝在其上叠一层已烘焙的自然化修正。
            //     因此本方法不再做任何求解：只做"复位 → 叠 delta → 叠滤镜"的重放（三次数组操作），
            //     开关切换的成本与烘焙解耦，改手绘也不会自动重算滤镜（用户裁定"不实时算"）。
            if (!_terrain.FlattenEnabled)
            {
                // 关：意图地表原样交出（cells/outputs=0 ⇒ 一格未改，不虚报改动数）
                _terrain.ReportSolveResult(0, 0, 0);
                return StageResult.Ok(0, 0);
            }

            // 开：只叠加**已烘焙**的滤镜层；没烘焙过就不假装整理（并给面板可读原因）
            var fl = _terrain.FlattenFilter;
            if (fl == null || fl.IsEmpty)
            {
                _terrain.ReportSolveResult(0, 0, 0);
                _terrain.ReportFlattenHint("尚未烘焙平整：点「烘焙平整」生成自然化滤镜层（当前显示=手绘意图地表）");
                return StageResult.Ok(0, 0);
            }
            fl.ApplyTo(field);
            {
                var fn = fl.CountFilledCells();
                _terrain.ReportSolveResult(0, fn, 0);
                _terrain.ReportFlattenHint(null);
                return StageResult.Ok(fn, 1);
            }
        }

        /// <summary>
        /// 真正的平整求解：<b>只被「烘焙平整」按钮调用</b>（不再挂在管线里随参数自动跑）。
        /// <para>产物写进独立的滤镜资产，<b>不碰 <see cref="SculptDeltaAsset"/></b>（D-01-1 同源纪律：
        /// 自动层一旦混进人工层，用户改噪声参数就会把自己的设计意图毁掉，且再也撤不回）。</para>
        /// <para>重放次序仍是 01 §3.2 的 <c>base → delta → flatten</c>：烘焙时同样先复位+叠 delta
        /// 再拟合求解，所以"在定稿的意图地表之上整理"这条 D-33 语义不变，变的只是<b>时机</b>。</para>
        /// </summary>
        public StageResult Bake(PipelineContext ctx, SculptFilterAsset outFilter)
        {
            if (ctx == null) return StageResult.Fail("ctx 空");
            if (outFilter == null) return StageResult.Fail("缺滤镜资产");
            if (_profile == null) return StageResult.Fail("缺 TerrainProfile");
            if (_terrain == null) return StageResult.Fail("缺 CityTerrain");

            var field = TerrainBaseStage.EnsureField(ctx);
            var grid = ctx.Grid;
            var cols = grid.Columns; var rows = grid.Rows;

            // 滤镜以"最后一次烘焙"为准：先清空，否则同一资产被反复烘焙会把修正越叠越大
            // （视觉表现＝每按一次烘焙地形就更平一分，且无从回溯）。
            outFilter.ClearAll();
            field.CommitBaseAsWorking();
            var d0 = _terrain.Delta;
            if (d0 != null) d0.ApplyTo(field);

            // ① 自然化（M4-o / D-41，用户原话「消除明显的手绘痕迹」）：
            //    <b>只软化人工层 (Heights − BaseHeights)，绝不碰 S1a 自然噪声</b>。
            //    理由：自然碎纹是"地形本来该有的样子"，把它一起抹平用户看到的就是"整张图糊了"；
            //    而笔刷折痕、光圈边缘、反复叠加留下的硬边才是该被化掉的东西。
            //    做法：a_soft = 半径窗内人工层的均值（积分图精确、O(1)/格），
            //          H' = Base + (1−t)·a + t·a_soft。t=0 原样、t=1 人工层完全贴合其邻域趋势。
            //    窗对称 ⇒ 人工层里"已经做好的斜面"不被推歪（只吃残差与折痕）。
            var natCells = 0;
            var tNat = _profile.NaturalizeAmount;
            if (tNat > 1e-3f)
            {
                var cell = grid.CellSize;
                var half = Mathf.Max(1, Mathf.RoundToInt(_profile.NaturalizeRadius / cell));
                EnsurePrefix(cols, rows);
                var hs = field.Heights; var bs = field.BaseHeights;
                SculptBrush.BuildLocalPrefix((gx, gz) => hs[gz * cols + gx] - bs[gz * cols + gx],
                                             cols, rows, _natPrefix);
                for (var cz = 0; cz < rows; cz++)
                {
                    var rowBase = cz * cols;
                    for (var cx = 0; cx < cols; cx++)
                    {
                        var i = rowBase + cx;
                        var a = hs[i] - bs[i];
                        var soft = SculptBrush.LocalBoxMean(_natPrefix, cols, rows,
                                                            cx - half, cz - half, cx + half, cz + half);
                        // 跳过判据必须是「本格与邻域都近乎没有人工层」，<b>不能只看本格</b>：
                        // 雕刻边界外那圈 a≈0 但 soft≠0 的格正是"把硬边抹开"要动的地方，
                        // 只按 a≈0 跳过会在笔刷轮廓上留一圈锐利硬环 = 用户要消除的手绘痕迹本身。
                        if (Mathf.Abs(a) <= 1e-4f && Mathf.Abs(soft) <= 1e-4f) continue;
                        var nh = hs[i] - tNat * (a - soft);
                        var dfilt = nh - hs[i];
                        hs[i] = nh;
                        if (dfilt != 0f) { outFilter.AddDelta(grid, cx, cz, dfilt); natCells++; }
                    }
                    if ((cz & 255) == 0 && ctx.CancelRequested) return StageResult.CancelledResult();
                }
            }

            // ② 收集约束（可平整对象）。<b>无对象时自然化仍然生效</b>——这正是用户"开了没变化"的场景：
            //    滤镜里只有软化成分、没有地块压平，画面照样变（而不是空跑一遍再解释为什么没变）。
            _constraints.Clear();
            var skipped = CollectConstraints(ctx, field, grid);

            EnsureScratch(_constraints.Count > 0 ? grid.CellCount : 0);

            // ③ 逐约束累加分子/分母（只遍历各自外接盒）
            _touched.Clear();
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
                var before = heights[idx];
                heights[idx] = hard
                    ? target
                    : (before * freeW + num) / (freeW + den);
                _num[idx] = 0f; _den[idx] = 0f; _target[idx] = 0f; _exact[idx] = 0;
                touched++;

                // ★ 烘焙产物 = **独立滤镜层的差值**（融合后 − 意图地表），绝不出现在 delta 里。
                //   <b>必须无条件记录</b>：画面改了而滤镜没记，重放（Run 只叠滤镜）就复现不出烘焙结果
                //   —— 表现是"烘焙一次后画面回不去/开关拨不动"。容差只能有一个真源：
                //   落不落格由 `SculptFilterAsset.AddDelta` 内部的 ZeroEps 决定（与 delta 层同口径），
                //   求解器不得再自行设第二道阈值（M4-o 自查：我第一版在此写了 `>0.005f`，
                //   被 A1_2 幂等断言抓出，最大偏差恰好等于该阈值）。
                outFilter.AddDelta(grid, idx % cols, idx / cols, heights[idx] - before);
            }

            // 收口：全零条目一次性丢弃（剪枝放这里而非每次写入，理由见 Compact 注释 ——
            // 每写都判空曾让 8km 全图烘焙花掉 49.6 秒，M4-o 端到端实测）。
            outFilter.Compact();

            // cells 读数 = 自然化 + 压平融合的合计改动格（两者都是"这一层滤镜改了多少"，
            // 只报融合数会让"没有可平整对象但有手绘"的场景虚报 0 改动，面板看起来又像没生效）。
            var changed = touched + natCells;
            _terrain.ReportSolveResult(_constraints.Count, changed, skipped);
            return StageResult.Ok(changed, _constraints.Count, skipped);
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
                            FlattenKind.Parcel, out var plane,
                            fromIntentSurface: true) || !TargetPlaneAllowed(plane, profile))
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
                            FlattenKind.Plaza, out plane, fromIntentSurface: true))
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
