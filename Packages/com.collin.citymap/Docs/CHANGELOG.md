# CHANGELOG — com.collin.citymap

格式：Keep a Changelog（https://keepachangelog.com）；版本号：semver。

## [Unreleased]

### Added
- **Modules/11-BuildingRecord.md**：`BuildingRecord` 接口冻结定稿（32B/栋、小端、region 分块 payload、
  `slotKey` 唯一键与冲突协议、`heightValue` 双义消歧表、`intentVersion` 指纹输入清单、校验与迁移规则、
  旧键名对照表）。新发现并解决 **H-26/H-27/H-28**，新增验收 **A-45~A-51**。
  随附收敛：DESIGN/06/07/08/10 旧字段名统一到定稿键名，`CityBuildingTag` 收敛为仅 `slotKey` 反向指针；
  总纲升 v8；内存口径统一钉为 **MiB**（8km 全图量化 31.3MiB + 记录 3.05MiB ≈ 34.4MiB，A-34 的 ≤40MiB 余量约 14%）。
- M1：Modules/00 基础设施——格网（4m/2m 双档）、SplitMix64 确定性种子树、`ushort` 掩码、
  `PipelineRunner` 阶段调度与脏传播、`CityWindow` 面板壳；08 的 `manualVersion` / `UndoHistoryTracker` 骨架。

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
