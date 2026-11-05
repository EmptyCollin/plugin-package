namespace Collin.CityMap
{
    /// <summary>管线阶段（总纲 §3.2）。S1 拆 a/b 以打破 S1↔S4 的循环依赖（09 D-12 / R-12）。</summary>
    public enum StageId
    {
        S0_Assemble      = 0,
        S1a_TerrainBase  = 1,
        S2_River         = 2,
        S3_Road          = 3,
        S4_Parcel        = 4,
        S1b_Flatten      = 5,
        S5_LandUse       = 6,
        S6_BuildingFill  = 7,
        StageCount       = 8,
    }

    /// <summary>执行档位（00 §4.3 D-00-4）：拖拽期只允许 Preview，只有 Commit 才碰 Unity 对象。</summary>
    public enum RunMode
    {
        /// <summary>拖拽中：仅脏 bounds、仅 overlay/统计，禁止 Instantiate/Destroy、禁止写资产。</summary>
        Preview,
        /// <summary>松手：完整重建，落 GameObject，进 undo/保存。</summary>
        Commit,
        /// <summary>导出/显式全量重生成：全区纯数据重算，允许长耗时、必须可取消（10 §5.4 H-20）。</summary>
        WholeMap,
    }

    /// <summary>
    /// 脏位（08 §5.1 映射表的值域）。用枚举位域而非 bool 标记，保证"最小脏集合"可求交可并。
    /// <para><see cref="DirtyBits.Terrain"/> 与 <see cref="DirtyBits.Flatten"/> 必须分开：
    /// 前者 = S1a 噪声基底重算（贵、全城），后者 = S1b 平整约束回写（局部）。
    /// 合一位会让"改一条河"顺带重跑噪声，摧毁 01 承诺的"不重跑 S1a"（Modules/09 H-29）。</para>
    /// <para>同理 <see cref="DirtyBits.Occupancy"/>（结构性占用变化：adopt/手刷/锁区/负约束）与
    /// <see cref="DirtyBits.OccupiedReloc"/>（仅挪动已固化对象）分家，
    /// 否则挪一栋楼牵连水系重算（08 表末行）。</para>
    /// </summary>
    [System.Flags]
    public enum DirtyBits
    {
        None          = 0,
        Seed          = 1 << 0,
        World         = 1 << 1,
        /// <summary>S1a 噪声基底（幅度/波长/八度/warp/档位/worldSize）。</summary>
        Terrain       = 1 << 2,
        /// <summary>S1b 平整约束回写（依赖地块与道路的几何结果）。</summary>
        Flatten       = 1 << 3,
        River         = 1 << 4,
        Road          = 1 << 5,
        Budget        = 1 << 6,
        Parcel        = 1 << 7,
        Field         = 1 << 8,
        Profile       = 1 << 9,
        Building      = 1 << 10,
        /// <summary>占用结构变化：adopt / 手刷 / 锁区 / 负约束（09 H-2 → 08 D-08-11）。</summary>
        Occupancy     = 1 << 11,
        /// <summary>
        /// 已固化对象被挪动：**终端位**——只让 S0 重烘焙掩码、S3/S6 借 DependsOn 复检禁入与叠放，
        /// 自身不向任何下游传播。
        /// <para>为何必须是终端位：若用 <see cref="Road"/> 表达"复检路网"，Road 的下游规则会把
        /// Parcel/Field 一起拖进来，于是"挪一栋已固化的楼"触发地块重算——08 §5.1 表末行明令禁止。
        /// 测试 H29 抓到的正是这个组合性缺陷。</para>
        /// </summary>
        OccupiedReloc = 1 << 12,
        /// <summary>城心锚点变更：近全链、高危，必过影响预览（09 H-7 / 08 §6.2）。</summary>
        Center        = 1 << 13,
        // 14..15 保留。

        All = Seed | World | Terrain | Flatten | River | Road | Budget | Parcel | Field
            | Profile | Building | Occupancy | OccupiedReloc | Center,
    }

    /// <summary>
    /// 变化类型（08 §5.1 映射表的键域）。Tracker 用它把"哪个对象变了"翻译成"哪些阶段脏了"。
    /// <para>必须是 [Flags]：同一对象在一帧内可能发生多种变化（如 CityProject 同时改 seed 与城心锚点）。
    /// 单值枚举会让后写者覆盖先写者 ⇒ 漏传播。M1 自测抓到这一点：<c>CityProject.Kind</c> 恒为
    /// <see cref="WorldParam"/>，于是「设为城心」被误映射成种子变更（走 All 而非 Center 链），
    /// 面板显示的脏位也是错的。改为按次累积的 Flags 后修正。</para>
    /// </summary>
    [System.Flags]
    public enum IntentKind
    {
        None          = 0,
        Field         = 1 << 0,   ///<summary>LandUseField（05）</summary>
        RiverStroke   = 1 << 1,   ///<summary>河笔画（02）</summary>
        RoadNode      = 1 << 2,   ///<summary>pinned 节点（03）</summary>
        RoadEdge      = 1 << 3,   ///<summary>pinned 边（03）</summary>
        RoadBudget    = 1 << 4,   ///<summary>ρ 等预算参数（03）</summary>
        TerrainModel  = 1 << 5,   ///<summary>TerrainProfile（01）</summary>
        ParcelModel   = 1 << 6,   ///<summary>ParcelProfile（04）</summary>
        BuildingRow   = 1 << 7,   ///<summary>表行（06）</summary>
        WorldParam    = 1 << 8,   ///<summary>seed / 尺寸 / 格档位（00）</summary>
        CityCenter    = 1 << 9,   ///<summary>城心锚点（01 §4.4）</summary>
        Occupancy     = 1 << 10,  ///<summary>adopt / 手刷 / 锁区 / 负约束（08 §4）</summary>
        OccupiedReloc = 1 << 11,  ///<summary>已固化建筑挪动（终端位，不向下游传播，08 表末行）</summary>
        DeltaStroke   = 1 << 12,  ///<summary>山水笔刷雕刻（01 §5）。用户 2026-11-04 裁定 **D-31** 推翻旧「仅脏 S6」：
                                  /// 雕刻除脏 S6 落位外，<b>必须触发 S1b 重算</b>（周边地块坡度与平整目标
                                  /// 要跟着新地形走）。仍不脏 Terrain ⇒ 绝不重跑噪声基底（A-2 局部性）。</summary>
    }

    /// <summary>
    /// 「对象类型 × 变化 → 脏集合」映射表（08 §5.1 定稿表格的代码化，唯一实现处）。
    /// 纯函数、无副作用，供 EditMode 测试直接断言（A-17/A-19/A-27 的判定基础）。
    /// </summary>
    public static class IntentChangePlanner
    {
        /// <summary>
        /// 直接脏位：该意图变化本身意味着什么（不含下游传播）。
        /// <para>逐位求并集，因此支持组合值（如 <c>WorldParam|CityCenter</c>）。
        /// 若沿用 switch-on-single-value，组合值会落到 <c>default</c> 返回 None ⇒
        /// **静默丢脏**（"改了但没重算"，比报错更难查）。</para>
        /// </summary>
        public static DirtyBits DirtyOf(IntentKind kind)
        {
            var d = DirtyBits.None;
            for (var i = 0; i < _kinds.Length; i++)
                if ((kind & _kinds[i]) != 0)
                    d |= _bits[i];
            return d;
        }

        // 表序与 IntentKind 的位序一一对应；新增变化类型必须同时加两行，否则编译期/测试期即红
        private static readonly IntentKind[] _kinds =
        {
            IntentKind.WorldParam, IntentKind.TerrainModel, IntentKind.RiverStroke,
            IntentKind.RoadNode, IntentKind.RoadEdge, IntentKind.RoadBudget,
            IntentKind.ParcelModel, IntentKind.Field, IntentKind.BuildingRow,
            IntentKind.CityCenter, IntentKind.Occupancy, IntentKind.OccupiedReloc,
            IntentKind.DeltaStroke,
        };

        private static readonly DirtyBits[] _bits =
        {
            DirtyBits.Seed | DirtyBits.World,   // WorldParam
            DirtyBits.Terrain,                  // TerrainModel
            DirtyBits.River,                    // RiverStroke
            DirtyBits.Road,                     // RoadNode
            DirtyBits.Road,                     // RoadEdge
            DirtyBits.Budget,                   // RoadBudget
            DirtyBits.Parcel,                   // ParcelModel
            DirtyBits.Field,                    // Field
            DirtyBits.Profile,                  // BuildingRow
            DirtyBits.Center,                   // CityCenter
            DirtyBits.Occupancy,                // Occupancy
            DirtyBits.OccupiedReloc,            // OccupiedReloc：终端位，Expand 不传播，仅由 S0/S3/S6 的 DependsOn 认领
            // DeltaStroke（用户 2026-11-04 裁定，登记为 DESIGN **D-31**）：脏 Flatten + Building。
            //   · Flatten ⇒ S1b 重算（周边地块的坡度判定与平整目标必须跟着雕出来的新地形走）
            //   · Building ⇒ S6 落位重排
            //   · **刻意不含 Terrain** ⇒ 绝不重跑噪声基底（A-2 局部性、D-10 逐格种子稳定性）
            //   · 不含 Parcel/Road ⇒ 不重切地块、不重排路网（那是 S4/S3 的意图，雕刻不改它们）
            DirtyBits.Flatten | DirtyBits.Building,
        };

        /// <summary>
        /// 一步传播规则（真源 = 总纲 §3.2 脏传播表 + 08 §5.1 映射表）。
        /// <see cref="Expand"/> 求到不动点，因此这里只写"直接下游"。
        /// </summary>
        private static DirtyBits Next(DirtyBits d)
        {
            var r = d;

            // 种子 / 世界尺寸 ⇒ 一切几何失效（00）
            if ((d & (DirtyBits.Seed | DirtyBits.World)) != 0)
                r |= DirtyBits.All;

            // 城心锚点：压幅中心 + 高度衰减中心都变 ⇒ 近全链（H-7 的代价，不隐藏）
            if ((d & DirtyBits.Center) != 0)
                r |= DirtyBits.Terrain | DirtyBits.Flatten | DirtyBits.River | DirtyBits.Road
                   | DirtyBits.Parcel | DirtyBits.Field | DirtyBits.Building | DirtyBits.Occupancy;

            // S1a 噪声 ⇒ 平整回写 + 下切 + 用途 + 建筑（噪声是重放次序第 1 层，01 §3.2）
            if ((d & DirtyBits.Terrain) != 0)
                r |= DirtyBits.Flatten | DirtyBits.River | DirtyBits.Field | DirtyBits.Building;

            // 河 ⇒ 桥(路) + 地块边界 + 临水场 + 建筑 + 平整回写。
            // 【不置 Terrain】：改河不得重跑噪声基底（01 承诺、A-2-2 局部性与 A-27 的前提）
            if ((d & DirtyBits.River) != 0)
                r |= DirtyBits.Road | DirtyBits.Parcel | DirtyBits.Field | DirtyBits.Building | DirtyBits.Flatten;

            // 路 / ρ ⇒ 地块细分 + 可达场 + 建筑
            if ((d & (DirtyBits.Road | DirtyBits.Budget)) != 0)
                r |= DirtyBits.Parcel | DirtyBits.Field | DirtyBits.Building;

            // 地块参数 ⇒ 用途字段 + 建筑 + 平整约束（目标平面来自地块）
            if ((d & DirtyBits.Parcel) != 0)
                r |= DirtyBits.Field | DirtyBits.Building | DirtyBits.Flatten;

            // 道路几何同样提供平整约束（带状压平，01 §3.4）⇒ 需 S1b 回写
            if ((d & (DirtyBits.Road | DirtyBits.Budget)) != 0)
                r |= DirtyBits.Flatten;

            // 场域 ⇒ 用途 + 建筑（A-27 断言：绝不含 Terrain/Flatten）
            if ((d & DirtyBits.Field) != 0)
                r |= DirtyBits.Building;

            // 建筑表 ⇒ 仅 S6
            if ((d & DirtyBits.Profile) != 0)
                r |= DirtyBits.Building;

            // 结构性占用 ⇒ 河道/细分/地块/落位都要复检禁入（09 H-2）
            if ((d & DirtyBits.Occupancy) != 0)
                r |= DirtyBits.River | DirtyBits.Road | DirtyBits.Parcel | DirtyBits.Building;

            // OccupiedReloc 是【终端位】：此处刻意不写任何传播规则。
            // 复检由 S0/S3/S6 各自的 DependsOn 认领；一旦在此并上 Road/Building，
            // Road 的下游会把 Parcel、Field、Flatten 全拖进来 → 挪一栋楼等于重算地块（08 §5.1 表末行禁止）。

            return r;
        }

        /// <summary>
        /// 把"直接脏位"展开为完整最小脏集合 = 传播规则的**不动点闭包**。
        /// 闭包保证幂等：<c>Expand(Expand(x)) == Expand(x)</c>（A-0-3 判定对象）。
        /// 上限 16 轮；脏位有限必然收敛，不收敛视为规则表写错（抛，不静默死循环）。
        /// </summary>
        public static DirtyBits Expand(DirtyBits raw)
        {
            var acc = raw;
            for (var i = 0; i < 16; i++)
            {
                var next = Next(acc);
                if (next == acc) return acc;
                acc = next;
            }
            throw new System.InvalidOperationException(
                $"{PackageIdentity.LogPrefix} 脏传播未收敛（规则表存在环且不自限），检查 IntentChangePlanner.Next");
        }

        /// <summary>展开结果是否含"必须走 Commit 档"的重活（Preview 档只允许 Field/Building 局部）。</summary>
        public static bool RequiresCommit(DirtyBits expanded)
            => (expanded & ~(DirtyBits.Field | DirtyBits.Building)) != 0;
    }

    /// <summary>
    /// 「阶段 → 认领哪些脏位」的**单一真源**（总纲 §3.2 脏传播表的另一半）。
    /// <para>存在理由：Runner 用 <c>IPipelineStage.DependsOn</c> 决定是否执行某阶段，
    /// 而面板（CityWindow）需要同一份信息来显示"哪个阶段脏了"。若两处各写一份，
    /// 必然分叉成"面板说脏、Runner 不跑"或反之（00 §4.1 禁止阶段互相持有引用的同一类问题）。
    /// 因此：真实阶段注册时应以此表为 DependsOn 的基线，面板只读此表渲染。</para>
    /// <para>与 <see cref="IntentChangePlanner.Expand"/> 的分工：Expand 负责"位→位"的下游传播，
    /// 本表负责"位→阶段"的认领；二者合起来才是完整的脏传播语义。</para>
    /// </summary>
    public static class StageClaims
    {
        /// <summary>某阶段认领的脏位集合。</summary>
        public static DirtyBits Of(StageId id)
        {
            switch (id)
            {
                case StageId.S0_Assemble:
                    // 占用/挪动都要重烘焙掩码（OccupiedHard 来自记录，10 §4）
                    return DirtyBits.World | DirtyBits.Seed | DirtyBits.Occupancy | DirtyBits.OccupiedReloc;
                case StageId.S1a_TerrainBase:
                    return DirtyBits.Terrain | DirtyBits.World | DirtyBits.Seed | DirtyBits.Center;
                case StageId.S2_River:
                    // +Flatten（02 改版）：水界由<b>最终生效地表</b>决定（用户裁定③"所见即所得"）。
                    // 雕刻走 DeltaStroke ⇒ Flatten、烘焙与开关滤镜走 ParcelModel ⇒ Flatten，
                    // 因此认领 Flatten 就让"水跟地形走"自动成立，且<b>不需要新增脏位、不改传播表、
                    // 也不违反 D-31</b>（Flatten 本就在 D-31 允许的集合内）。
                    // 反面对比：若改 bump River 位，Expand(River) 会带出 Road|Parcel ⇒
                    // "雕一笔就重切地块"，直接破坏 D-31 与 M5 之后的性能预算。
                    // 代价实测（8km/400 万格）：清位 2ms + 洪泛 2~8ms + 缓冲 3~21ms ≈ 每笔 7~30ms，
                    // 且<b>没有水体种子时几乎为零</b>（Run 里早退）⇒ 不画水完全不影响手感。
                    return DirtyBits.River | DirtyBits.Terrain | DirtyBits.Occupancy | DirtyBits.Flatten;
                case StageId.S3_Road:
                    // OccupiedReloc：挪动的楼可能压到路 ⇒ 只复检，不重建地块
                    return DirtyBits.Road | DirtyBits.Budget | DirtyBits.River | DirtyBits.Occupancy | DirtyBits.OccupiedReloc;
                case StageId.S4_Parcel:
                    // 刻意不含 OccupiedReloc：挪一栋固化楼不得触发地块重算（08 §5.1 表末行）
                    return DirtyBits.Parcel | DirtyBits.Road | DirtyBits.Budget | DirtyBits.River | DirtyBits.Occupancy;
                case StageId.S1b_Flatten:
                    return DirtyBits.Flatten;
                case StageId.S5_LandUse:
                    return DirtyBits.Field | DirtyBits.River | DirtyBits.Road | DirtyBits.Terrain | DirtyBits.Center;
                case StageId.S6_BuildingFill:
                    return DirtyBits.Building | DirtyBits.Profile | DirtyBits.Field | DirtyBits.OccupiedReloc | DirtyBits.Occupancy;
                default:
                    return DirtyBits.None;
            }
        }

        /// <summary>该阶段本轮是否需要执行。</summary>
        public static bool IsClaimed(StageId id, DirtyBits expanded)
            => (expanded & Of(id)) != 0;
    }
}
