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

        /// <summary>
        /// 逐格「禁止平整」原因码（0 = 允许压平；1 陡坡 / 2 手绘 / 3 水体或缓冲带）。
        /// <b>只在 Bake 里构建</b>，Run（重放）用不到。
        /// <para>存在理由（用户 2026-11-06 反馈③「平整滤镜简直是灾难」）。取证确认的三个
        /// "文档写了、代码一处落点都没有"的缺口，在这里一次补上：</para>
        /// <para>① <b>陡坡退让</b>（Modules/12 §3／DESIGN <b>D-32</b> 的 hardRetreatSlope）：
        /// 原始地面陡于阈值的格不参与压平。缺它时默认参数把雕出来的山<b>削顶填谷成水平台阶</b>
        /// ——拟合面坡度超过 maxParcelSlope 就退化成水平中位数面，而地块核心贴合度实测 93%。</para>
        /// <para>② <b>手绘豁免</b>（01 §3.4 的 ManualLocked）：被笔刷碰过的格不许被自动层覆盖。
        /// D-01-1 反复强调"自动层不得毁掉人工层"，此前只靠"两个资产分开存"这条屏障 ——
        /// 但结构分离只防"写坏数据"，防不了"叠加显示时把手绘磨平"，那才是用户看到的现象。</para>
        /// <para>③ <b>水感知</b>：水域与缓冲带格禁压禁填。S2 免费交出掩码，此前烘焙与它零协商，
        /// 能把刚点出来的河道直接填掉。</para>
        /// <para>判据优先级 水 → 手绘 → 陡坡（互斥归因，统计才不会重复扣）。</para>
        /// <para>⚠️ 本位图**只 gate 约束压平**。自然化（软化人工层）只对水体码避让：
        /// "消除手绘痕迹"本来就是对手绘层动手，若把手绘格一起豁免，该功能会整块失效
        /// ——那不是保护，是把用户明确要的旋钮关掉。</para>
        /// </summary>
        byte[] _forbid;

        /// <summary>禁止平整的原因码（互斥，便于把"保住了多少"归因到具体保护）。</summary>
        static class Forbid
        {
            public const byte None = 0, Steep = 1, Hand = 2, Water = 3;
        }

        /// <summary>本次烘焙因保护而被跳过的格数（按原因分列，仅统计确实落在约束权重内的格）。</summary>
        int _skipSteep, _skipHand, _skipWater;

        /// <summary>本次烘焙的最大绝对高差（米）——"这次削了多少山"必须报出来（用户反馈③）。</summary>
        float _maxDelta;

        /// <summary>最近一次烘焙的保护统计（面板读它呈现，不重算）。</summary>
        public (int steep, int hand, int water) LastProtectStats => (_skipSteep, _skipHand, _skipWater);
        /// <summary>最近一次烘焙的最大削平幅度（米）。</summary>
        public float LastMaxDelta => _maxDelta;

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

            _skipSteep = _skipHand = _skipWater = 0;
            _maxDelta = 0f;
            BuildForbidMap(field, grid, ctx.Mask);

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
                        // 只避开<b>水体</b>格：见 _forbid 注释 —— 软化本来就是对人工层动手，
                        // 若连手绘格一起避开，这个旋钮会整块失效（那是把用户要的功能关掉，不是保护）。
                        if (_forbid != null && _forbid[i] == Forbid.Water) continue;
                        var nh = hs[i] - tNat * (a - soft);
                        var dfilt = nh - hs[i];
                        hs[i] = nh;
                        if (dfilt != 0f)
                        {
                            outFilter.AddDelta(grid, cx, cz, dfilt);
                            natCells++;
                            var ad = dfilt < 0f ? -dfilt : dfilt;
                            if (ad > _maxDelta) _maxDelta = ad;
                        }
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
                var dfuse = heights[idx] - before;
                outFilter.AddDelta(grid, idx % cols, idx / cols, dfuse);
                var adf = dfuse < 0f ? -dfuse : dfuse;
                if (adf > _maxDelta) _maxDelta = adf;      // "这次削了多少山"（用户反馈③要求必须报数）
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
                    // ★ 保护闸门：被禁的格<b>连权重都不累加</b>，于是融合式里它保持原始高程，
                    //   且过渡带会自然绕过去（不是"改了再改回来"）。放在权重判定之后：
                    //   落在所有约束之外的格本来就不动，不计入保护数，统计才不会被全图格数灌爆。
                    if (_forbid != null)
                    {
                        switch (_forbid[idx])
                        {
                            case Forbid.Steep: _skipSteep++; continue;
                            case Forbid.Hand: _skipHand++; continue;
                            case Forbid.Water: _skipWater++; continue;
                        }
                    }
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
        /// 构建逐格禁止平整的原因码。<b>三遍都是 O(图幅格数) 或更低</b>：
        /// 手绘标记走稀疏条目（成本与实际覆盖格数成正比，见 <see cref="SculptDeltaAsset.MarkContentInto"/>），
        /// 坡度与掩码各一遍线性扫。实测数据记在 ACCEPTANCE。
        /// <para><b>坡度必须读"意图地表"</b>（此刻 <c>field.Heights</c> = base+delta，滤镜尚未叠）：
        /// 用户刚抬起来的那座山也得算山。读 BaseHeights 会让雕刻出的陡坡被当成平地削掉（D-31 同口径）。</para>
        /// <para>阈值 0 = 关闭陡坡保护；但<b>手绘豁免与水感知始终生效</b> —— 它们不是"可调强度"，
        /// 而是"不许毁掉用户输入"的底线。</para>
        /// </summary>
        void BuildForbidMap(TerrainField field, CityGrid grid, ushort[] mask)
        {
            var cells = grid.CellCount;
            if (_forbid == null || _forbid.Length != cells) _forbid = new byte[cells];
            System.Array.Clear(_forbid, 0, cells);
            if (cells <= 0) return;

            var cols = grid.Columns; var rows = grid.Rows;

            // ① 手绘标记：只扫被写过的稀疏条目
            _handScratch ??= new bool[cells];
            if (_handScratch.Length != cells) _handScratch = new bool[cells];
            System.Array.Clear(_handScratch, 0, cells);
            var hand = _terrain.ProtectHandCarved ? _terrain.Delta : null;
            if (hand != null) hand.MarkContentInto(_handScratch, grid);

            var steepLimit = _profile.SteepProtectSlope;
            var waterBit = (ushort)(CellFlag.Water | CellFlag.WaterBuffer);

            for (var cz = 0; cz < rows; cz++)
            {
                var rowBase = cz * cols;
                for (var cx = 0; cx < cols; cx++)
                {
                    var i = rowBase + cx;

                    // 优先级：水 → 手绘 → 陡坡（先保住"绝不能动"的，再谈"用户画的"，最后"太陡的"）
                    if ((mask[i] & waterBit) != 0) { _forbid[i] = Forbid.Water; continue; }
                    if (_handScratch[i]) { _forbid[i] = Forbid.Hand; continue; }
                    if (steepLimit > 0f)
                    {
                        var w = grid.CellCenterWorld(cx, cz);
                        if (field.GetSlopeDeg(w.x, w.y) >= steepLimit) _forbid[i] = Forbid.Steep;
                    }
                }
            }
        }

        /// <summary>手绘标记的复用位图（实例级，同 scratch 纪律：静态会串两个城市上下文）。</summary>
        bool[] _handScratch;

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
                // ★ 作用域裁剪（用户反馈③的第二个"削山"主因）：取证实测默认档位下
                //   n=8 ⇒ 街距 900m、单块 846×846m，8×8 覆盖 7200×7200m ≈ <b>整张 8km 图</b>，
                //   而布局只拿城区半径算街距、<b>从不按半径裁剪</b> —— 于是"城外也全是城市地块"。
                //   现在中心超出「城区半径 + 一个地块半尺寸」的直接不收集：宁少压一片，也不毁掉郊野。
                var urban = profile.ResolveUrbanRadius(ctx.Project, grid);
                var urbanCenter = profile.ResolveUrbanCenter(ctx.Project, grid);
                CityTerrain.EnumerateAutoTiles(terrain, profile, ctx.Project, grid, (c, half) =>
                {
                    var reach = urban + Mathf.Max(half.x, half.y);
                    if (Mathf.Max(Mathf.Abs(c.x - urbanCenter.x), Mathf.Abs(c.y - urbanCenter.y)) > reach)
                    { localSkipped++; return; }
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
