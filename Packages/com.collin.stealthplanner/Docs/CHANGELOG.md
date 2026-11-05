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
维持 **0.0.1**：M2 已交付但 M3~M9 未开工，本包尚无 `Showcase.json` 演示视频与包内演示场景，不满足收版条件。

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
