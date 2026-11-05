# CHANGELOG — com.collin.stealthplanner

格式：Keep a Changelog（https://keepachangelog.com）；版本号：semver。

## [Unreleased]

### Added（M1：时空可见性表 + 烘焙，session planner-m1-2026-11-05）
**Runtime**（无 UnityEditor 依赖，引擎侧可复用）
- `VisibilityTable.cs`：表结构本体。按哨兵**分片**、时间索引用 `t mod c_i`（D-02，不物化全局 LCM）；
  每片是「桶 × 格」的位图（`byte[]` 手工打包）；派生**永久安全层**（跨片 AND，供求解器剪枝①不展开时间维）；
  查询 `IsDangerousAt(cell,t)` 复杂度 = 哨兵数（与格数、相位窗长度无关）；越界与未烘一律**保守判危险**。
  `BucketOf` 定为填桶/取桶的**唯一口径**（公开静态纯函数，跨程序集可回归钉住）。
- `VisibilityBakeRunner.cs`：烘焙驱动（算法在 Runtime 而非 Editor 的两条理由：引擎可复用同一填桶口径、
  且桶数进位/越界钳制/降级排除必须能在 EditMode 用假世界断言）。**分帧按「查询预算」而非毫秒**
  ——预算切分是确定性的（同输入同分片、可断言），`DateTime` 卡时间片则每帧分点不同。
- `VisibilityTableAsset.cs`：持久化载体（SO）。带 `LayoutVersion` 校验（读旧资产一律拒绝并要求重烘，
  不静默给错数据）+ 烘焙参数快照 + `TryQuery` 降级出口（未烘/版本不符 ⇒ 保守判危险并给原因文本）。

**Editor**
- `ContractVisibilitySource.cs`：把 core 契约适配成 Runtime 的可见性源，**判定一律转调玩法包**（不自建第二套）。
  加**距离预筛**：每桶一次纯数学水平距离比较，超视距即跳过契约调用。方向保守正确
  （3D 距离 ≥ 水平距离 ⇒ **只会少问、绝不漏问**）。
- `VisibilityBakeService.cs`：`VisibilityBakeSession`（可跨帧续跑的会话，持有 footprint 与驱动状态，
  避免「每帧重枚举一次 footprint」）+ 服务化的**可活动 footprint 枚举唯一实现**（向下验地面 + 向上验站高，
  两条缺一不可：只查地面会把掩体内部当可走格）。
- `VisibilityBakeWindow.cs`：交互窗口（分帧泵 + 进度条 + 可中断，禁模态弹窗）与**脚本模式菜单**
  （一次跑完并打读数——本工程实测失焦时编辑器不推进泵，自动化取证必须有不靠帧回调的路径）。
- `Sample/PlannerBakeSettings.asset`：**入库**（是源）。
- `Tests/Editor/VisibilityTableBakeTests.cs`：**15 条**（本包 7 → 22）。

### Added（M2：相位时间轴 + 可达传播 + 热区，session planner-m2-2026-11-05）
**Runtime**（纯 CPU、不引 UnityEditor，引擎侧可直接复用同一传播与查询）
- `PhaseReachability.cs`：**相位可达传播**（静态设计 1 的「玩家可达位置基于上一轴节点」，亦是 M3 求解器地基）。
  按桶 BFS 推进，`Field` 输出逐桶可达位图 + 每格最早到达秒 + 整窗并集。三条语义见 DESIGN **D-18**
  （桶内不插值 / 终点只查下一桶 / 允许原地等待）；一步跨多格用整数超行栅格化检查**严格中间格**
  （只查起止格会一步跳过掩体）；步长 `floor(v·Δt/格长)` 且**允许为 0**（兜成 1 等于虚构移动力）；
  每桶判据 = 表危险 **或** 无数据格（**D-19**），与热区渲染共用同一份合成掩码。
  `Field.FillReachableUpTo(bucket, mask)`（**D-20**）：「截至当前桶」的前缀并集，专供时间轴；
  与「整窗并集 = 可达上限」严格分成两个 API。
- `HeatmapGrid.cs`：热区网格构建器，把**同行相邻同状态格合并成横带**（单条 4 顶点 2 三角，绕序法线朝上）。
  11664 格 → 单次采样 354~786 三角形（**D-21**，逐格画 gizmo 会把 SceneView 拖死）。
- `VisibilityTable.cs` 扩展（全部走既有单一口径，不开第二套判定）：`BuildDangerMask`（逐格转调
  `IsDangerousAt`）、`VisibleCellCountInBucket` + 新增 `VisibleCellCountInSliceBucket`（按**片内桶号**
  精确取，供泳道画一整条周期；两者共用同一条位扫描实现）、**足迹三件套**
  `MarkEvaluated` / `IsEvaluated` / `HasFootprint` / `BuildNoDataMask`（**D-19**，`byte/格`）、
  表级 `SetUnknownGuardNames` / `UnknownGuardNames`（跳过的哨兵 = 其视野在表里不存在 ⇒ 面板必须警告"安全偏乐观"）。
- `VisibilityBakeRunner.cs`：收尾登记足迹 + 写入缺失哨兵名单（**先标足迹再重建永久安全层**）。

**Editor**
- `StealthTimelineWindow.cs`：**相位时间轴窗口**（菜单 `Tools ▸ Collin ▸ Stealth Planner ▸ 相位时间轴`）。
  拖 t 时：① 经 `IStealthScrubDriver.ScrubTo` 推 A 包 NPC 位姿（**B 不自算折线**，同源同步）
  ②危险热区 + 本桶可达 + 累计可达三层 ③**哨兵泳道**：每名哨兵一行，横条深浅 = 该桶可见格数、
  白竖线 = 当前 t 落在其自身周期的哪一桶（走 `BucketOf`，周期不整除 Δt 时不会错位）、
  行尾 = 此刻世界坐标 / 朝向 / 是否落在网格外（取 `IPatrolWorld.TryPoseAtWorld`）。
  视图态全部是窗口实例字段 + `[SerializeField]`，**无静态**（不跨窗口串台，为 M8 多窗口预留）。
  ⚠️ SceneView 回调里起点标记必须用 `Handles.DrawWireDisc`——`Gizmos.*` 在 `duringSceneGui` 会抛
  `ArgumentException`，每次重绘一条 error，直接顶爆 §1.6b 的 `consoleErrors == 0` 门禁（实测踩过）。
- `PlannerEditorInternals.cs`：`InternalsVisibleTo("com.collin.stealthplanner.Tests")`。
  为的是让 U-1 的降级断言打在**真实的 internal 入口**上，而不是为了测试把 `ContractVisibilitySource`
  / `VisibilityBakeSession` 提成 public（那等于把内部实现固化成对外 API）。
- `ContractVisibilitySource.cs` / `VisibilityBakeService.cs`：**本轮未改动**（`WarmUpPoses`、
  距离预筛、`EnumerateWalkableCells` 唯一 footprint 实现、`RunOnce` 脚本模式都是 **M1 已交付**内容，
  见上一节。此处点名是因为时间轴与采样取证依赖它们，**不是本轮的产出**，勿据此误记归属）。
- `Tests/Editor/`：新增 `PhaseReachabilityTests` **18 条**、`GracefulDegradationTests` **4 条**（收口 U-1）、
  `TimelineRecomputeTests` **4 条**（窗口级回归，§0.1 所见即所得），
  + 既有 22 条 = **本包 48 条**；`Tests` asmdef 增引用 `com.collin.stealthplanner.Editor`。
  ⚠️ 新用例一律用 `VisibilityTableAsset.SetBaked` **现场注入假表**，不载库里的
  `Sample/VisibilityTable.asset`——那是派生产物、不入库，依赖它会随现场状态时红时绿。

### Fixed（M2 实测发现的三处缺陷 ⇒ 均已补回归，见 ACCEPTANCE 条目 20~22）
- **累计热区拖不动**：窗口拷的是 `Field.EverReachable`（整次传播的并集），8 个采样桶读数**恒为同一数**，
  时间轴看着像坏了。修法：Runtime 新增前缀并集 API，窗口改调用；测试同时钉「中途桶必须**严格小于**
  整窗并集」与「末桶必须**恰好等于**并集」。**教训：「可达上限」与「推进量」是两个语义，混用不报错、只静默失效。**
- **改「可达起点」输入框毫无反应**：`_startCell` 只在第一次算出后缓存（`if (_startCell < 0 && …)`），
  之后改世界坐标被完全忽略 ⇒ 面板死报「出发格在 t=0 已被看见」。违反 AGENTS.md §0.1 第 1 条。
  修法：每次 `Recompute` 都按 `_startWorld` 重算格号，越界置 -1 并报「起点落在网格外」。
  回归断言**只改坐标、不点任何按钮**，并额外测「移出再移回必须恢复」防单向卡死。
- **表失效/传播失败后残留上一次热区**：`Reload` 的无表分支只改状态文本，`_meshDanger` 仍持有
  上一份表的网格对象、`_dangerMask` 仍满长度 ⇒ 状态行**继续谎报旧格数**且对象泄漏；
  传播失败时 `Compute` 返回的 `Buckets=0` 空场留在 `_field` 会被读成含糊的「0 格可达」。
  修法：无表分支补 `ReleaseGfx()` + 清三张掩码与泳道缓存；失败分支补 `_field = null`。
  （这条由我自己写的断言首次运行即失败暴露——**断言先于实现生效**，正是它该有的样子。）

### Changed（M2 收口轮：U-5 定案，session planner-m2fix-2026-11-05）
- ⚠️ **破坏性变更（用户 2026-11-05 拍板）：`VisibilityTableAsset.LayoutVersion` 1 → 2**。
  v1 的表资产**一律判过期、拒绝使用**，面板与查询都会给出「表结构版本 1 ≠ 当前 2，请重烘」。
  原因：足迹栏（D-19）是 v2 才有的字段，v1 缺这一栏 ⇒ 墙与掩体内部会被读成「安全可走」，
  热区把墙涂绿、求解器可规划穿墙路线，且**全程不报错**。只增字段不抬版本 = 新加的保护区静默失效。
  **已有表请重新烘焙**（菜单 `Tools ▸ Collin ▸ Stealth Planner ▸ 烘焙可见性表`，实测 0.82~0.88s；
  表本就是场景派生产物、不入库，故新克隆环境不受影响）。
- **D-17 第二道防线补上三处出口**（新增决策 **D-23**）：`ScanZeroVisibility()` 此前只在 runner 内存里
  记名单、**无人可读**，所以「三个哨兵全瞎」的表能被下游当可信数据。现出口为：
  ① `VisibilityBakeSession.ZeroVisibilityGuards` 对外暴露；② 脚本模式日志追加 `零可见=N（名字…）`；
  ③ **写进表**——与被跳过的哨兵合并登记进 `VisibilityTable.UnknownGuardNames`（二者表侧语义相同：
  该哨兵的视野在这张表里等于不存在 ⇒ 其视野区会被画成安全），时间轴窗口据此出警告横幅。
  回归 `ZeroVisibilityGuard_IsReported_AndPersistedOnTable` 断言名单**必须落到表上**。
- 新增决策 **D-22**（版本随字段新增必须抬升）、验收条目 25~28、`VisibilityTableAssetVersionTests` **5 条**
  → 本包 **54 条**（明细：`PlannerBakeSettingsTests` 7 + `VisibilityTableBakeTests` 16 +
  `PhaseReachabilityTests` 18 + `GracefulDegradationTests` 4 + `TimelineRecomputeTests` 4 +
  `VisibilityTableAssetVersionTests` 5）。全量 **316/316**
  （CityMap 141 + Stealth 121 + StealthPlanner 54，按 `FullName` 命名空间统计）。

### Fixed（M2 收口轮实测发现的两处缺陷）
- 🐛 **测试清理越界，差点删掉库内真实资产**：`Resources.FindObjectsOfTypeAll<T>()` 会把
  **磁盘上的真实资产**一并列出（本包 `Sample/VisibilityTable.asset` 正在其中），
  `TearDown` 里直接 `DestroyImmediate` 被 Unity 拦下并打 error，**5 条用例断言全对却全红**。
  修法：清理前判 `EditorUtility.IsPersistent(obj)`，为 true（磁盘资产）一律不碰。
  同类隐患一并修掉：`TimelineRecomputeTests` 原本扫全部窗口销毁，会连带销毁
  **用户/其他 AI 会话正开着的时间轴窗口**（动他人现场，违反 AGENTS.md §2/§9），
  改为 `Created` 清单只回收**本用例自己**创建的实例。
- 🐛 **默认起点被如实拒绝，但探针写法会误导取证**：A 包出生点 `(0,-22)` 在可信表上 **t=0 即被看见**，
  工具报「出发格在 t=0 已被看见…换个起点」——这是**正确的保守拒绝**，不是缺陷。
  教训记进 ACCEPTANCE 条目 28：验成功路径必须**从表里筛合格格**
  （`IsEvaluated(c) && !IsDangerousAt(c, 0f)`）做起点，别拿猜来的坐标当"工具坏了"。
- 🐛 **切场景同一帧立刻烘焙会产出全瞎表**（`视野体=0 巡逻单位=3` ⇒ 永久安全格=全部格）。
  根因在 A 包编辑态的服务/注册表时机，**属他包、本会话未代修**；本包的处置是上面那条 D-23 出口，
  使这类数据**不再静默**（日志、表、面板三处都会留痕）。取证纪律：切场景后先过一帧再烘。

### Added（M4 第一增量：多条短解 + 面板接入，session planner-m4-2026-11-05）
**Runtime**（纯 CPU、只读表，判据仍全部走 `PhaseReachability` 公开入口，无第二套安全口径）
- `PhaseKShortest.cs`：**静态设计 4「有解时输出前 M 条短解」的算法地基**。在按桶分层的 DAG 上
  逐轮产出**边不相交**的真实动线：每轮做一次「按层递增、逐层重建前沿」的单位代价分层 BFS，
  首个把撤离点纳入前沿的层即最早到达（`Route.CellsPerLayer` 按层展开站位，等待=相邻层同格）；
  然后把该路线用到的全部转移（含等待边）`(from,layer)→to` 锁进禁止集重搜下一条；穷尽如实返回少于 M 条。
  节点必须按 `(cell, layer)` 建模——**同格可在不同层各算一个节点**（折返/等待变体的前提）。
  ⚠️ 口径 = **非严格 Yen**（新增决策 **D-25**，理由与升级路径见该条）。
  规模护栏：`(cell,layer)` 节点数上限 `MaxLayerCells=24M`，超限回可读失败不 OOM；
  入参非法（表空/越界/起点被看见/规模超限）不返回半条路线。
- `PhaseReachability.cs`：无行为改动——k-shortest 直接复用 `#5` 抽出的 `ExpandSuccessors`/`BuildBlockedMask`。

**Editor**
- `StealthTimelineWindow.cs`：可行性求解区块升级为「静态设计 2/3/4」，新增「求前 M 条短解」按钮 +
  `M` 输入（1~16）+ 结果文本（每条给到达时刻/移动跳/等待拍/末站坐标 + 口径行 + 耗时）。
  复用同一张表/人速/视界（`PhaseSolver.Horizon`），首条路线与「求解有无可行解」同 horizon ⇒ 可比对。

**测试**：本包 **63→70**（`PhaseKShortestTests` **7 条**：首条到达层与 PhaseSolver 逐位一致 + 逐轮非降、
禁边后**折返**仍在解空间、全安全走廊的等待变体两两边不相交、等待展开成站位序列、
起点==终点、墙隔断⇒0 条、非法入参各回专属原因且不崩）。全量 **332/332**
（CityMap 141 + Stealth 121 + StealthPlanner 70）。

**实测**：真实烘焙表（StealthSample 3 哨兵、horizon=480、M=5）⇒ 5 条路线：首条到达桶 55
与可行性解**逐位一致**，路线 2~5 桶 57 且首步岔向不同邻格（结构互异），5 条重搜共 2.92s；
UI 接线因编辑态视野体注册恒 0（见 Fixed 段）改用注入内存表验证 ⇒ 窗口正确复用视界/步长/格换算，
8×8 表出 3 条短解（<M=4 如实穷尽，含一条"移动14+等待1"）。

### Changed（M4 范围与性能，用户 2026-11-05 拍板，session planner-m4perf-2026-11-05）
- **slack（§6.5 最脆弱一步容差）移出 M4 ⇒ 挪到 M5/M7**。理由（我提出、用户选①）：slack 要"这一步距被抓还差多少秒"，
  需要**每格的察觉累积速率**；M1 表只存"该格该桶可不被看见"的位图，没有速率，A 包的 `LastSeerGain` 是运行时状态、
  编辑态拿不到。硬造要么扩表翻倍成本、要么给几何假容差——都不如到实跑阶段（M5）用真跑的察觉度算，更诚实。
- **性能归因（先量后改，推翻我上一轮的口头建议）**：同会话带 JIT 预热探针 `perf_ab`/`perf_fill` 实测——
  | 开销项 | 实测 | 结论 |
  |---|---|---|
  | 全量预计算逐桶掩码 `L480` | 545ms | 本次优化对象 |
  | 按需实际建到层 `L60` | 58ms | **净省 ≈487ms（≈17%）** |
  | `parent` 每轮 `Fill(-1)`×5 | 15ms | 我原以为是主因，**实测证伪** |
  | 剩余前沿洪水式扩散 | ~2.3s | **真瓶颈** |
  ⚠️ **上一轮我建议"M4 先做剪枝①（永久安全格）"是错的**：`IsDangerousAt` 早已短路永久安全格，剪枝①在此不对症。
  真瓶颈是 BFS 为取一条最短路把 108×108 全图逐层灌满 ⇒ 对症的是 **§6.2 剪枝②（可采纳启发式引导搜索）**，
  那是更大的手术（要自建优先队列与 g/发现表、动"每跳须过单一真源判据"的一致性），**未纳入本次增量**，留 M4 后续。
- 本次落地的安全小优化：`PhaseKShortest` 的逐桶阻断掩码从「一次性预计算全 horizon 层」改为
  **按需构建 + 缓存**（`EnsureLayer`，配独立 `built[]` 标记——全安全层内容也是全 0，只靠"缓存非零"会误判未建而反复重算）。
  结果与改前逐位一致（首层仍=55、路线数=5、每跳仍过 `ExpandSuccessors` 复核），仅成本下降。本包用例仍 **70**、结果不变。

### Added（M4 数据集增量：`PlayerRouteDataset` 双形态 + 非模态导出，session planner-m4data-2026-11-05）
**Runtime**（纯数据 + 纯函数，不引 UnityEditor ⇒ 引擎侧可加载同一份 JSON）
- `PlayerRouteDataset.cs`：**静态设计 4 的产物，亦是动态设计 1 注入器的输入**（D-07 JSON + SO 双形态）。
  - **canonical 表示 = 每层站位时间线**（`cellsPerLayer`，下标=全局时间桶、值=格索引；相邻相等=原地等待）——
    新增决策 **D-26**。理由：折叠成稀疏路点会让「在第几层等了多久」由注入端用速度反推 ⇒ **第二套时间口径**
    （D-22/D-24/D-25 同族教训），且与 `PhaseKShortest`「每边恰一桶」漂移。要稀疏路点时经
    `MovementWaypoints()` 派生（只是视图，不是真相）。
  - 随存**网格快照**（origin/格长/Δt/维度）：格索引换一张表就失去含义 ⇒ 注入端 `GridMatches` 不过就**拒绝**，
    而不是拿错网格硬解出乱坐标。
  - `SchemaVersion`（当前 1）不匹配一律拒载——`JsonUtility` 对**改名字段静默补零不报错**，
    不钉版本与字段名就会拿到「看着合法其实全 0」的动线（本包认定的"谎报安全"同类）。
  - `BuildFromRoutes` / `ToJson` / `FromJson` 全部**失败给可读原因且不留半张**。
- `StealthTimelineWindow.cs`：加「导出为路点数据集（JSON + SO）」按钮（无表或无路线时禁用）。
  ⚠️ **刻意非模态**：DESIGN §3.2 原写「弹窗确认」，与 AGENTS.md §2.1.5 禁 `DisplayDialog`（阻塞主线程 =
  卡死所有会话共用的指令通道）直接冲突，且校验器 `channel-discipline` 正把 citymap/stretch 的 4 处该调用判红
  ⇒ 照原文实现等于自造红灯。改为**三处非模态确认**：固定包内路径 + 按钮下方文本 + 日志读数；
  反悔成本只是删掉产物，比弹窗安全。已在 DESIGN §3.2 就地标注 v0.2 修正。
  SO 资产从**刚落盘的 JSON** 读回（不是再复制内存对象）⇒ 两形态必然同源，且顺带证明导出件可被解析。
- `Tests/Editor/PlayerRouteDatasetTests.cs` **8 条**：逐层往返无损、**手写线上 JSON 字面量钉字段名契约**、
  版本不符拒载、空/坏/无路线各回专属原因、网格不匹配判拒、折叠视图与时间线自洽、`BuildFromRoutes` 守 null/空、
  `SchemaVersion` 单源。→ 本包 **70→78**；全量 **367/367**（本包 78 + CityMap 166 + Stealth 123）。
- 真实烘焙表端到端（U-8 修复后终于可用真场景，不必再拿注入内存表兜底）：导出 5 条、
  JSON 7454 字节 + SO 5 段、每条时间线不变量（层数=到达桶+1、首=start、末=goal）全通过，首条到达桶 55
  与 M3/M4 先前在同一张表上的可行性解数值相同。⚠️ 探针 `[EXP]` 日志这次取不到（重编译后缓冲重置）
  ⇒ 证据改为**落盘产物 + 解析**，未直接看到的部分在 ACCEPTANCE 条目 38 标为推论，没冒充实测。
- ⚠️ 派生产物处置：`Sample/WaypointDataset.json(.meta)` 与 `Sample/PlayerRouteDataset.asset(.meta)`
  **同表资产一类、不入库**（内容是探针随手选的端点参数，入库=把取证参数伪装成设计成果；**格式与类才是源**）。
- **静态设计 4 的导出侧就此收口**；「注入器沿数据集实跑并复现同一条动线」（组 E 后半）随 M5 复现器落地。

### Fixed / 已知问题登记（M4 期间）
- 🐛 **编辑态视野体注册间歇恒 0（属 A 包，本包未代修，已挂 ACCEPTANCE U-8）**：本轮多次
  `OpenScene(StealthSample)` + `EnsureRegistered` 后 `IVisionWorld.Volumes.Count=0`（巡逻单位=3 正常），
  连强制同名重载也无效——同一路径在 M2/M3 取证时可复现"过一帧后=3"。疑似 A 包注册表跨域重载残留
  （`GlobalServices` 物体查无）。**本包处置**：真实表端到端沿用本轮早期成功现场的读数，UI 层取证改注入内存表；
  **M5 实跑前必须解决**（已在 HANDOFF §2 标红、复现线索随 U-8 移交）。
- 🐛 **反射创建的窗口实例残留挂 `duringSceneGui`**：取证脚本销毁窗口的分支没覆盖"同名场景已加载"等
  边缘态，残留实例在无 StealthClock 的场景持续 `Recompute` 刷出 2 条 A 包警告 ⇒ 归零门禁被污染。
  处置：销毁全部遗留实例 + `console clear` 后静默 70s 复采 0/0。**教训沿用条目 27：测试/探针创建的
  编辑器对象必须枚举彻底回收，别按"当前活动场景"条件化销毁。**
- 🐛 **本会话自造红灯一次**：`PhaseKShortest.cs` 首版把"格级全局 arrived"当访问标志 ⇒ 连首条路线都
  找不到（4 条用例红），且与 M3"判可行"自相矛盾。改为 `parent[(cell,layer)]` 每轮 `Fill(-1)` 重建后
  332/332 全绿。**注**：`Array.Clear` 会把"未发现"哨兵 -1 填成 0（0 是合法格号）——这类
  "哨兵值初始化"错误编译不报、只把可行场景判成 0 条，`ReentryAfterForbidden` 等用例是它的捕手。
- 🐛 **文档更新踩点两处小事故**：ACCEPTANCE 状态头改写后行号偏移导致一次编辑越界拒收（重读后修正）；
  `StealthTimelineWindow` 一次替换吞掉相邻方法的文档注释一行、另留一个未使用局部变量（会打 CS0219
  警告顶爆门禁）——均在提交前 diff 核对时清掉。**沿用纪律：改长文档只锚单行、改完必扫删除行。**

### Added（M3：相位可行性求解 + 无解 Top-K，session planner-m3-2026-11-05）
**Runtime**（纯 CPU、只读表、无 UnityEditor 依赖，可后台/引擎侧复用）
- `PhaseSolver.cs`：**静态设计 2「有无可行解」+ 静态设计 3「无解给最近撤离点 Top-K」**。
  `Solve(table, settings, startWorld, goalWorld, walkSpeed, out failure)` 面板入口 +
  `Solve(table, startCell, goalCell, horizonBuckets, stepCells, topK, out failure)` 核心入口。
  输出 `Result`（`Feasible` / `ArrivalBucket` / `EarliestArrivalSeconds` / `HorizonBuckets` /
  `CellsReached` / `FallbackTopK`）。**⚠️ 不写第二套可达判据**：可行性与最早到达一律复用
  `PhaseReachability.Compute`（见 D-24）。无解 Top-K 走「从撤离点做只穿非墙格的多源 BFS 距离场」，
  候选须**同时满足**「玩家视界内到过」(`EarliestSeconds` 有限) 与「绕墙连得到撤离点」(距离场有限)，
  按(绕墙格步数, 最早到达)升序、截断到 K。排序键用**绕墙距离而非直线距离**——被墙隔开的点
  看着近其实到不了，直线距离会谎报「就差一格」；墙彻底隔断时如实返回**空清单**。
  视界硬上限 `MaxHorizonBuckets=4096`（`Horizon()` 超限返回 -1 + 可读原因，不返回会算爆内存的视界）。
- `PhaseReachability.cs` 扩展：
  ① 抽 **#5 单一真源公开 API**——`BuildBlockedMask`（危险 或 无数据，全工程唯一「某桶哪些格不能站」入口，
     BFS/A*/热区三处共用）、`TryBuildNoData`、`ExpandSuccessors`（合法后继格含原地等待，去重写缓冲，
     M2 的 BFS 与 M3 复用同一份邻居/安全判据，杜绝两处各写必分叉）；
  ② `Compute(..., goalCell, out failure, out goalArrivalBucket)` **带目标早退**重载——每步恰一桶、传播层序，
     第 b 层首见目标即最早到达可安全停止（起点==目标算第 0 层命中）。⚠️ 早退时场只覆盖到命中层
     （有解不需要尾部场），未命中时跑满 horizon、场完整（无解才要 Top-K），语义在注释写死。

**Editor**
- `StealthTimelineWindow.cs` 加「可行性求解（静态设计 2/3）」区块：撤离点世界坐标输入 +
  「求解有无可行解」按钮（无表时禁用）+ `_solveSummary` 文本展示。复用同一张表与面板人速，
  但结果**只出文本、不驱动逐桶热区**——求解用 `N×BucketsPerPhase` 的完整 horizon，与滑条 `_buckets`
  是两个量，混用会让热区口径分裂。

**决策与设计口径**
- 新增 **D-24**：M3 用带目标早退的层序传播（unit-cost + earliest-arrival + 等待恒可选 ⇒ 等价于 A* 的答案）
  实现，**不手搓 A\***；真 A* 可采纳启发、§6.2 四剪枝、§6.3 k-shortest **一律推 M4**。
  理由：此时手搓启发纯属增加可采纳性 bug 的风险面、不改变答案。**下个会话勿据 §6.1/§6.3 字面以为 M3 已含 A\*。**
- 新增 `PhaseSolverTests` **9 条**（假表：距离↔层数、目标先险后安须等待、起点==目标、墙隔断⇒Top-K空、
  topK 截断、步长0 仅到过、入参越界各回专属原因、视界超限返 -1）→ 本包 **54→63 条**。
- 端到端（真实烘焙表 3 哨兵，horizon=480）：角到对角撤离点 **✅ 有解 t=27.5s（桶 55）**——直线最少约 50 桶，
  55>50 说明绕开了巡逻危险区（潜行求解该有的结果，非直线穿越）；horizon=1 造无解⇒候选仅起点、绕墙距 107 格吻合。
- 全量 **325/325**（CityMap 141 + Stealth 121 + StealthPlanner 63，按 `FullName` 命名空间统计）。


### Fixed（M1 实测发现的严重缺陷 ⇒ DESIGN 新增 **D-17**）
- **冷启动首烘静默给出荒谬数据**：重开场景后第 1 次烘焙把永久安全格算成 **11664 = 全部格**
  （等于「三个哨兵全是瞎子」），第 2 次才回落正确的 4482，**全程不报错**。
  根因两条：① 玩法包弧长查找表**惰性构建** + PhysX 未同步 ⇒ 首烘读到未初始化位姿；
  ② `CanSeeAt` 在视野体按 actorId 取不到时**静默返回 false** ⇒ 整张表被烘成全场安全。
  修法：烘焙前 `Physics.SyncTransforms()` + `WarmUpPoses()`（每单位把 `PoseAt`/`TryPoseAtWorld` 各探一遍逼表建起来）；
  并加第二道防线 `ZeroVisibilityGuards`（收尾扫「整片零可见」⇒ 上报为可疑，绝不当可信读数）。
- **窗口/脚本模式只 Load 不创建资产** ⇒ 新克隆工程永远没有输出资产、「开始烘焙」按钮永久灰死。
  改为共用 `EnsureAsset`（只补最后一级目录，父目录不存在则告警返回 null，不递归误建路径）。

### 实测（M1，示例场景 3 哨兵）
| 读数 | 值 |
|---|---|
| 活动格 / 矩形格 | 11,660 / 11,664 |
| 永久安全格 | 4,482（剪枝①可省掉的时间扩展节点） |
| 表体积 | 252.0 KB（DESIGN §7 目标 < 4MB，余量充足）|
| 查询数 | 1,970,540 |
| 预筛省下的射线 | 1,586,206（**80.5%**）｜实打 384,334 |
| 首烘主循环 | **0.82s**（§7 目标 <30s）｜footprint 枚举 0.01s |
| 表值 vs 直调契约 | 随机 400 个 (格,t) **零不一致**；永久安全格 1,250 次抽验 **零违例** |
| 确定性 | 三轮「重开场景→烘焙→逐片快照」SHA256 **完全一致** |
| 测试 | 本包 **22/22**，全量 **279/279**；门禁三项全绿、校验器 FAIL 0 |

> ⚠️ 取证方法两条教训（已写进 ACCEPTANCE）：
> ① `AssetDatabase` 返回**同一个被缓存的实例**，第二次烘焙会覆写第一次的表 ⇒
>    验确定性**必须先拷位图快照**，否则「首=次」是同一实例自比的假信号（我第一版探针就踩了）。
> ② 我上一版验确定性用的探针里 `a1`/`a2` 是同一实例 ⇒ 那句「首=次?True」当时就是假读数，
>    真结论来自条目 8 的快照法。

### 版本
维持 **0.0.1**：M3 已交付 + **M4 三个增量**（多条短解 / 掩码按需建层 / 数据集双形态导出）已交付；
M4 剩余（剪枝②引导搜索、规模化基准）与 M5~M9（含注入器、slack）未开工，
本包尚无 `Showcase.json` 演示视频与包内演示场景，不满足收版条件。

### Changed（DESIGN 定稿，session planner-design-2026-11-05）
- 用户 2026-11-05 拍板原 §10 五个开放问题 ⇒ **DESIGN 状态 v0.1 草案 → v0.2 已评审（定稿），M1 可开工**：
  ① 短名维持 `com.collin.stealthplanner`；② 相位窗 **用户手配秒数** + 面板给**只读**「建议参考时长」
  （`lcm(round(c_i/Δt))·Δt`，互质爆炸时兜底 `max(c_i)×2` 并 Warning，绝不改写手配值）；
  ③ 目标**仅「抵达撤离点」**，多前置任务不排期；④ 实跑**先单窗口顺序**，UMPM 多窗口留作后续迭代（M8 不提前）；
  ⑤ 格长 / Δt / 相位窗 P **三项全部面板可配**，默认 `0.5m / 0.5s`。
- 新增决策 **D-12~D-16**（对应上述五条）、算法章 **§6.7**（参考相位时长口径 + 正确性声明：P 不改变
  「是否存在 ≤N 相位可行解」的答案，漏解由 N 决定）、**§6.8**（参数敏感性实时预览估算，超预算警示）；
  §7 预算与 D-08 措辞同步为「默认可配 / 并行属后续迭代」。
- `ACCEPTANCE.md`：状态头转定稿；新增**验收草案组 G**（6 条，逐项把上述决议变成可观测判据——
  含「改 P 后解的存在性不变」「参考值不自动写入 P」「互质退化路径不卡死」「无多目标谓词」）。
- 🐛 **修复骨架期遗留红灯**：M0 只放了 asmdef、没有脚本，Unity 对空程序集**必打 warning**
  （`Assembly for ... will not be compiled, because it has no scripts`，实测本包两条）。
  该 warning 会在交锁时破 §1.6b 归零门禁——M0 那次读数恰好被 `console clear` 时机掩盖，本轮才暴露。
  解法是落两个**属于骨架本身**的最小文件（不是提前写功能）：
  `Runtime/PlannerBakeSettings.cs`（决议 D-13/D-16 的可配参数载体 SO：格长 0.5m、Δt 0.5s、
  相位窗 P 手配、N/M/Top-K，含 `MaxAlignedBuckets=7200` 与 `BucketsPerPhase`；**不含任何判定或求解逻辑**）
  与 `Editor/PlannerBakeSettingsEditor.cs`（§6.8 参数敏感性检视：即时显示桶数/轮数/输出规模并对越界给
  Warning；活动格数因需场景 footprint 而**如实标注「待 M1」**，不留假数字）。
  修后本包 warning 归零。
- **新增测试程序集与 7 条守卫用例** `Tests/Editor/`（asmdef + `PlannerBakeSettingsTests.cs`），
  把一次性探针升级成可重复回归：默认值即拍板值、三项真可配、`BucketsPerPhase` 向上进位
  （10/3 必须得 4，截成 3 会漏掉末尾一段时间）、P<Δt 病态配置桶数不为 0、越界钳制**不牵连 P**、
  改 Δt **不静默改写**手配的 P、`7200` 赋值形态全包只允许一处。
  ⚠️ 该用例判据连错两轮才修对，教训记进 ACCEPTANCE 已验证事实 5：
  ①首版扫全包 + 数裸数字，把注释说明、用例自己的期望值、搜索词全算成重复定义（报「6 次」假阳性）；
  ②二版用 `Assert.Ignore` 想"记一笔"，结果命中定义处即中止、统计变死代码，测试判「跳过」而非通过
  ——**跳过不等于通过，等于没守卫**。终版收紧为「只扫 Runtime/Editor + 跳注释 + 只匹配 `= 7200`
  赋值形态 + 定义文件豁免」，并加**「必须真扫到定义文件」前置断言**堵掉"路径拼错→命中 0 文件→虚假通过"。
- 实测：**本包 7/7 绿，全量 258/258**（按 `FullName` 分包统计：CityMap 130 + Stealth 121 + StealthPlanner 7，合计核对自洽）；
  门禁 `compilationFailed=false`、`consoleWarnings=0`、`consoleErrors=1` 全部命中 §1.8 已批准豁免
  （`services-gateway`），校验器 **FAIL 0 / 结论绿灯**。
- 除上述骨架配套与参数守卫外**仍无烘焙/求解实现代码**（DESIGN 先于代码），无 `Sample/`，
  `Showcase.json.video` 留空。

## [0.0.1] - 2026-11-05
### Added
- 包骨架（DESIGN 先于代码，AGENTS.md §4）：`package.json`（依赖仅 `com.collin.core`）、
  `Runtime/` 与 `Editor/` 两套 asmdef（`validateReferences: true`，Runtime 不引 UnityEditor）、
  `Showcase.json`（`order=31`，沿用 stealth DESIGN §4.4 为设计工具预留的序号位）。
- `Docs/DESIGN.md` v0.1 草案：潜行关卡设计辅助台的完整方案与决策记录，覆盖用户提出的
  静态设计 1~4 与动态设计 1~3。核心架构是「时空可见性查找表」——把玩法包的主线程射线判定
  烘焙成离线数据资产，之后求解/仿真全部退化为纯 CPU 查表，以同时满足「可后台并行」与
  「编辑器/引擎双端共用」两条硬约束。
- `Docs/ACCEPTANCE.md` 验收草案（逐条待实现后取证，当前无一条通过）。

> 本版仅骨架 + 设计，无任何 Runtime/Editor 实现代码，故不进入可玩状态、`Showcase.json.video` 留空。
