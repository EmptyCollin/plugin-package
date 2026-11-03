# CHANGELOG — com.collin.citymap

格式：Keep a Changelog（https://keepachangelog.com）；版本号：semver。

## [Unreleased]

### Added
- **M2（Modules/01 数据层：S1a 基底噪声 + S1b 平整约束）已实现并自测通过**（`test run --mode editor` Total 84 / Failed 0，
  新增 `Tests/Editor/TerrainDataTests.cs` 28 条地形断言）：
  - `Runtime/Core/CityNoise.cs`：纯 C# 确定性噪声内核（value noise + fbm + domain warp，splitmix64 派生子种子），
    **不依赖 Unity PerlinNoise**（后者跨平台/版本不保证逐位一致，会击穿"同种子同图"地基）；全纯函数、无静态可变态。
  - `Runtime/Core/HeightField.cs`：`TerrainField`（Heights/BaseHeights + 量化往返）、`ConstraintStrength`
    （**Parcel=8 / RoadCrown=4 / WaterBuffer=1** 分级 + 核心贴合度）、`PlaneConstraint`（`InsetMeters`/`ExtendMeters`/
    `Exact` 硬覆盖，`MaxInsetFor/ComputeCoreHalf` 单一算法真源）、`PlaneFitting.FitFromBase`
    （**坡向取最小二乘、水平取中位数**以抗离群）。
  - `Runtime/Core/TerrainBaseStage.cs`：S1a。`TerrainProfile` SO（噪声参数 + 城内平整度旋钮 + 约束求解设置），
    `ResolveUrbanCenter` 回退图幅中心，压幅公式 `ampFactor*(amp*baseN + detailA*detail)`。
  - `Runtime/Core/FlattenSolveStage.cs`：S1b。加权融合 `h=(h·Wfree + Σ wᵢ·planeᵢ)/(Wfree + Σ wᵢ)` + 硬覆盖模式，
    scratch 缓冲改**实例级**（避免跨上下文污染）。
  - `Runtime/Core/CityTerrain.cs`：场景组件（手动平整区 + 自动地块开关），`EnumerateAutoTiles` 按 `pitch` **留街道间隙**。
- **实现轮抓到并修复 6 处空洞**（详见 Modules/09 第六轮）：
  - **H-31**：`urbanCenter` 写死 `(1000,1000)` → 小图幅城心落图外、`urbanFlatness` 旋钮完全失效。改哨兵默认回退图幅中心。
  - **H-32**：拟合截距是 LSQ 面在**样本均值点**的高程，被当成区域中心高程用 → 目标面整体偏移实测 1.0m。坡向偏移改解析式。
  - **H-33**：中位数抗离群的设计意图被 LSQ **旁路**（水平仍取均值口径）→ +395m 单点离群致目标高程偏 0.816m。改水平取中位数。
  - **H-34**：缺 `constraintInner`（核心=整块矩形）→ 相邻地块共享边界同时全权重、两独立平面被平均成 **4.79°→65.48° 陡坎**。
    增 `InsetMeters`（上限=较短半边 40%）。
  - **H-35**：自动地块无街道间隙 → 内切后仍 27.92°（两独立平面高差须在有限距离内消化，几何无解）。按 `pitch` 留街隙。
  - **H-37**：压幅只乘低频层、细节层完全绕过 `urbanFlatness` → `flatness=0` 城内标准差 **0.236m 违反 A-1-1 ≤0.2m**；
    且被我把断言阈值写成 `0.6f`（凭直觉放宽而非用文档数值）掩盖。**修公式为压整条噪声和后 → 0m**。
    纪律：**文档给绝对数值，断言就用那个数值**；另补 `flatness=0.5` 中点线性度断言防"中间段不跟手"。
  - 另记流程坑 **H-36**：`recompile_status=up_to_date` 不等于"新代码已编译"——dll mtime 可能早于源码，测试跑旧程序集
    表现为"改了码失败数值一字不差重复"。判别法：比 dll/源 mtime 或反射探新成员；改码后必先确认 mtime 再跑测试。
- 交锁三项门禁（H-37 修复后最终复跑）：`script recompile_status` `failed=False`；`console clear`→重跑全量+校验器→
  `console status` **`consoleErrors=0/consoleWarnings=0/seeded=True`**；`verify_project.py` 本包检查项全 `[ ok ]`
  （整体红灯仅因他包 `core/stretch/stealth` 锁心跳超时，非本包改动，已按 §3 如实上报待用户裁定）。

  `slotKey` 唯一键与冲突协议、`heightValue` 双义消歧表、`intentVersion` 指纹输入清单、校验与迁移规则、
  旧键名对照表）。新发现并解决 **H-26/H-27/H-28**，新增验收 **A-45~A-51**。
  **字段方案裁定（用户 2026-11-03，总纲 D-30）：`heightValue` 保留双义，不拆 `repeat`+`scale`**；
  读取强制单一入口 `RecordGeometry.Resolve`，§13 把该风险列为代码审查打回项、A-51 为回归必测。
  随附收敛：DESIGN/06/07/08/10 旧字段名统一到定稿键名，`CityBuildingTag` 收敛为仅 `slotKey` 反向指针；
  总纲升 v8；内存口径统一钉为 **MiB**（8km 全图量化 31.3MiB + 记录 3.05MiB ≈ 34.4MiB，A-34 的 ≤40MiB 余量约 14%）。
- **M1（Modules/00 基础设施 + 08 反悔骨架）已实现并自测通过**（56 个 EditMode 用例，`test run --mode editor` Total 56 / Failed 0）：
  - `Runtime/Core/CityGrid.cs`：四层分辨率解耦（逻辑格 / 显示 / chunk / region）、2km 与 8km 双档、
    floor 语义格换算与越界安全、圆外接盒裁剪、`RecordQuant` 定点标度（0.01m / 1/4 度）。
  - `Runtime/Core/DeterministicRandom.cs`：SplitMix64 混合与种子树（stage/cell/object）、可重放 `RngStream`、
    `IntentHash`（乱序输入直接抛，守 00 §3.1 铁律）。
  - `Runtime/Core/CellFlag.cs`：`ushort` 13 位掩码 + `MaskWriter` 唯一写口与禁建合集判定。
  - `Runtime/Core/PipelineTypes.cs`：`DirtyBits`（**Terrain 与 Flatten 分家**、`OccupiedReloc` 为终端位）、
    `IntentChangePlanner`（不动点闭包 `Expand`，幂等）、**`StageClaims` 阶段认领单一真源**（面板与阶段共用，已删除面板私有副本）。
  - `Runtime/Core/Pipeline.cs`：`PipelineContext`（槽位/脏区域并集/取消）、`PipelineRunner`（同帧合并、
    固定执行序 S1a→…→S4→S1b→S5→S6、失败与取消即中止）、`GenerationReport` 逐阶段耗时。
  - `Runtime/Core/Falloff.cs`：全工程唯一衰减求值器（内平台→曲线过渡→外截止；四预设单调、越界钳制）。
  - `Runtime/Core/BuildingRecord.cs`：**已冻结结构落地**（32B/栋 + `ValidateLayout` 偏移自证）、
    `BuildingFlags` 位域、`RecordGeometry.Resolve` 作为 `heightValue` 双义的唯一读取入口、`SlotKey` 命名空间。
  - `Runtime/Core/CityProject.cs`：项目根 Model、`CityCenterAnchor`（H-7：城心为独立意图锚点，非实时读场域）、
    世界参数与城心的显式提交入口（非法参数先校验后落字段）。
  - `Runtime/Core/AssembleStage.cs`：S0 装配（外环带位 + `ConstraintAsset` 负约束烘焙，先清位再置位）。
  - `Editor/UndoHistoryTracker.cs`：意图层→派生层一致性桥（首帧只建基线、双检、250ms 合并窗口）。
  - `Editor/CityWindow.cs`：主面板壳（阶段树呈现为**信息而非门禁**、自动跟随开关、工具模式互斥、非法参数提示）。
  - `Tests/Editor/`：EditMode 测试程序集（`UNITY_INCLUDE_TESTS` 约束）+ 56 条用例。
- **实现轮新发现并修复 2 处空洞**（详见 Modules/09 第五轮）：
  - **H-29**：把"复检 S3"直译成"置 `Road` 位" ⇒ `Road` 下游把 Parcel/Field/Flatten 全拖进来 ⇒
    **挪一栋已固化的楼会重算地块**（正是 08 表末行禁止的事）。改为 `OccupiedReloc` 终端位 + `StageClaims` 认领。
    纪律：文档里「→Sx 复检」不等于"置 Sx 的触发位"。
  - **H-30**：意图对象用"单一 Kind + 单一版本"记账 ⇒ 「设为城心」被读成 `WorldParam` ⇒ Expand 成 `All`
    ⇒ **H-7 的修复被这条接线静默抵消**（城心又变回一动就全城重算），且面板脏位显示错误。
    改为 **`IntentChangeLedger` 按通道（对象 × IntentKind）记账**、`IntentKind` 升 `[Flags]`、
    `DirtyOf` 改逐位求并集（否则组合值落 default = 静默丢脏）；序列号必须是 `[SerializeField]`，
    否则 Unity Undo 还原后 Tracker 看不见（H-9 漏检形态）。

### Planned
- M2~M11 见总纲 §10 里程碑表。
- **M0 后、M7 前须冻结 `BuildingRecord` 结构**（Modules/10 §2.2）——D-25 已改变 07/08 数据底座，
  M11 再改会波及认回 / journal / 检查点，返工面大。

## [0.0.1] - 2026-11-03

首个版本：**M0 包骨架**（无功能代码，纯结构与文档）。

### Added
- `package.json`：内嵌包身份，唯一依赖 `com.collin.core@0.2.0`（AGENTS §4 依赖唯一真源）。
- `Runtime/com.collin.citymap.asmdef`：仅引用 core，`validateReferences: true`，命名空间 `Collin.CityMap`。
- `Runtime/PackageIdentity.cs`：包名/版本常量（版本真源仍是 `package.json`，禁用于逻辑分支）。
  存在意义是让 Runtime 程序集非空，使 `runtime-purity` 与编译门禁在 M0 即有真实检查对象。
- `Editor/com.collin.citymap.Editor.asmdef`：引用 core + 本包 Runtime，`includePlatforms: ["Editor"]`。
- `Editor/CityMapMenus.cs`：`Collin/CityMap/Package Info` 菜单自检入口（打印包身份与四件套齐备情况）。
  **必要而非可选**：Editor 目录若只有 asmdef 没有 `.cs`，Unity 会产出 error
  `Assembly for Assembly Definition File ... will not be compiled`，直接破坏 §1.6b 控制台归零门禁
  （且堆栈含本包路径，不适用 §1.8 环境噪声豁免）。详见 ACCEPTANCE §9。
- `Showcase.json`：展板条目五字段（`order=20.0` 排在 core=0 / stretch=10 之后；`video` 暂空，M10 回填）。
- `Sample/README.md`：包内试用演示目录占位与用途说明（AGENTS §0：showcase = 包内演示，展板不引用插件代码）。
- `Docs/DESIGN.md`：**已评审 v7.1**——总纲 + `Docs/Modules/00~10` 共 11 份设计细则（约 3000 行）。
  含四轮操作序列走查发现的 25 处跨模块空洞（H-1~H-25，7 处阻断级）及全部回写裁决：
  - C1~C6 能力定义（地形 / 河流 / 路网 / 地块 / 场域 / 建筑 + 导出）；
  - 七阶段管线 S0~S6（S1 拆 S1a/S1b 以打破与地块的循环依赖）、人工层与自动层分域、派生层不落盘；
  - 场域 = 同心双圆柱（类型 / 内外径 / 内外值 / 衰减曲线），烘焙四维强度场；
  - 建筑生成 = 全图统一"主导维数值硬过滤 → softmax 加权随机"，拉伸高度由场域强度驱动；
  - 编辑管线：L1~L4 反悔分层、锁区 / 负约束 / adopt 三种局部反悔机制、上游改动冲突矩阵；
  - 大尺度：`BuildingRecord` 数据态真源 + region 工作集流式 + `CitySnapshot` 三形态导出（scene 切块 / JSON / 2D tile）；
  - 决策 D-01~D-29、验收 A-1~A-44。
- `Docs/ACCEPTANCE.md`：M0 结构 / 依赖 / 纯净性 / 编译 / 控制台归零条目与证据栏。

### Changed
- 无（首个版本）。

### Removed
- 无。

### 未做（明确记录，避免误解为遗漏）
- **新建内嵌包的发现步骤（本项目环境实测，后续会话照此执行）**：
  `Assets/Refresh` 菜单与 `AssetDatabase.Refresh(ForceUpdate)` **都无法**让编辑器发现新内嵌包
  （`script recompile` 会返回 `up_to_date` 且 0 个 `.meta` 生成）；
  必须执行 `unity command "package resolve"`，之后 `.meta` 生成、`package list` 出现该包、两侧 dll 产出。
- 未触碰 `Packages/manifest.json`：内嵌包由 Unity 自动发现，无需声明（AGENTS §7 共享文件不申报）。
- 未修改 `ProjectSettings/**`、`Assets/Showcase/`、`com.collin.core`、`com.collin.stretch`。
- 未引入任何第三方 UPM 依赖（Unity Recorder 仅在 M10 为 Sample 录制单独申报）。
- 无功能代码：M0 的交付判据是"校验绿灯 + 编译干净 + 控制台归零"，不是任何生成能力。
