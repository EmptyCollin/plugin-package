# Modules/00 — 世界底座、确定性随机、管线框架、主面板

> 依赖：无（本包最底层）。被 01~07 全部模块消费。
> 对应里程碑：M1。状态：草案。

## 1. 职责边界
- 定义世界的空间表示（逻辑格、分块、坐标系与单位）；
- 提供**确定性随机与噪声内核**（全城唯一随机源）；
- 提供**阶段调度 + 脏标记传播**的执行框架（`PipelineRunner`）；
- 定义持久化总规约（资产 vs 派生）、撤销政策摘要、schema 迁移（反悔体系完整设计 = **Modules/08**）。
- 主面板 `CityWindow` 的骨架与阶段状态呈现。
不负责：任何具体生成算法（各地形/河流/路网/场域/建筑模块自持）。

## 2. 空间底座

### 2.1 坐标与单位
- 世界 XZ 为规划平面，Y 为上。**规划与所有几何计算只在 XZ 进行**，Y 是被求值的量（高度），不是自由度。
- 单位：距离 = 米；角度 = 度；四维值 = 0~100 整数（D-02）。
- 坐标转换一律经 `CityGrid`，禁止模块自己算 `worldPos / cellSize`（浮点误差会让同一格在两个模块里落到不同 index，是最难查的一类 bug）。
  ```csharp
  int   CellIndex(float x, float z);          // floor 语义，含边界钳制
  Vector3 CellCenter(int idx);                // 格中心世界坐标
  bool  TryCellIndex(float x, float z, out int idx);  // 越界返回 false
  ```

### 2.2 逻辑格与显示网格解耦（D-14；分辨率按**尺度档位**定，v6 决策③）
| 层 | 分辨率 | 用途 | 存储 |
|---|---|---|---|
| 逻辑格 grid | `cellSize`：**2km 档 = 2m**（Sample/基线）；**8km 档 = 4m**（D-26，实算 2m/16M格 三场 336MB 不可接受） | 高度采样、用途场、可建掩码、空间哈希 | 高度 `int16[]`、掩码 `ushort[]`、用途 `byte[]`（10 §2.3） |
| 显示网格 mesh | `displayStep` = 4×cell（2km→8m，8km→16m） | 地形 mesh 顶点 | 派生，不落盘 |
| 分块 chunk | `chunkSize` 默认 128m | 地形重建与可见性单位 | 索引，不落盘 |
| **region** | **1024m（=8×8 chunk）**，v6 新增 | **流式加载/投影物化/导出切块的单位**（10 §3） | 场数据按 region 分块落缓存 |

`displayStep` 必须是 `cellSize` 的整数倍（构造时断言）；4m 逻辑格下所有"以格为单位"的参数一律用**米**表达
（地块/退让/slot 间距已是米制；临街判定容差随之放宽到 ±2 格，见 10 §1 结论 1）。

解耦理由：把逻辑格直接做成 mesh 顶点会到 1024² 顶点（单块超大、重建极慢）；显示降采样 4× 后
视觉损失可用一次平滑消除，而规划精度不丢。`displayStep` 必须是 `cellSize` 的整数倍（构造时断言）。

### 2.3 世界尺寸与外环
`CityProject.worldSize`（默认 2000m）+ `CityProject.buildableInset`（默认 60m）。
外环带强制不可建，作用：① 让城边界自然渐隐成郊野/农田；② 避免边界格在 mesh 与噪声采样上出现半格问题。

### 2.4 掩码（每格 2 字节）
```csharp
[Flags] enum CellFlag : ushort {
    None=0,
    Buildable=1, Water=2, WaterBuffer=4, Road=8,
    Parcel=16, ManualLocked=32, OutOfBounds=128,
    OccupiedHard=256,   // adopt/手刷对象 footprint 硬占用：S2 河道/S3 细分/S6 落位 一律视为墙（08 H-2）
    NoRoad=512,         // 负约束：禁路（08 §4.3）
    NoBuild=1024,       // 负约束：禁建
    NoWater=2048,       // 负约束：禁水
    Interior=4096       // 过大地块内部不产 slot（04 §3.3 depthCap）
}
```
`ManualLocked`：被人工层显式钉住的格。`OccupiedHard` 由 **adopt 集合与 `CityBuildingTag` 反烘焙**得到——
这是"固化对象不挡自动生长"这类恶性 bug 的结构性防线（Modules/09 H-2）。
负约束（`NoRoad/NoBuild/NoWater`）由 `ConstraintAsset` 在 S0 烘焙进掩码，使"禁止"成为每格 O(1) 判定而非各自查询。
掩码由 S0/S2/S3/S4/08 分阶段写入，S6 只读。
**位预算纪律**：`ushort` 共 16 位，已用 12 位；新增位须登记于此并同步 00 校验（原 byte 8 位已用尽，Modules/09 H-3）。

## 3. 确定性随机与噪声内核

### 3.1 PRNG
- 整数基：`SplitMix64`（无状态、可跳步、分布足够）；不使用 `System.Random`（其内部实现跨版本不保证一致）。
- 种子派生树：
  ```
  rootSeed        = CityProject.seed
  stageSeed(s)    = mix(rootSeed, s)
  cellSeed(s,x,z) = mix(stageSeed(s), hash2(x,z))     // hash2 = splitmix(x*0x9E.. ^ z*0x85..)
  objectSeed(s,id)= mix(stageSeed(s), id)             // id = 节点/parcel/field 的稳定 id
  ```
- **铁律（D-10）**：任何一处随机必须能写成 `cellSeed/objectSeed` 的形式。遍历序、列表 index、字典枚举序**一律禁止**作为种子输入
  （字典/集合枚举序不保证稳定，会静默破坏 A-1）。
- 浮点 [0,1)：`NextDouble() = (NextUInt64() >> 11) * 2^-53`。

### 3.2 噪声
自带 value-noise + fbm（不依赖 Unity `Perlin`，其精度/跨平台一致性不可控）：
```
value2(ix,iz) = 位混列 → 平滑插值(6t⁵-15t⁴+10t³)
fbm(p, octaves, λ, gain=0.5, lacunarity=2) = Σ gain^k · value2(p/λ_k)
域扭曲 domainWarp(p) = p + (fbm(p+offA), fbm(p+offB)) · warpAmp   // 打破规则格感
```
参数（幅度 A、波长 λ、八度数、gain）全进 `TerrainProfile`，可保存可 diff。
**噪声函数必须是纯函数**（同输入同输出），且禁止内部缓存随会话变化的查找表——否则 A-1 在第二次生成时失败。

### 3.3 曲线与衰减的统一求值口径
场域衰减（05）与地形平整约束（01）共用同一个求值器：
```csharp
static float EvalFalloff(AnimationCurve c, float t)  // t∈[0,1]，0=内边界，1=外边界
```
约定：t 为**归一化半径进度**，输出为**从 innerValue 到 outerValue 的进度 s**；曲线越界一律 `Clamp01`。
两处各自定义"衰减"会导致同名参数在不同工具里手感不同，用户无法迁移经验。

## 4. 管线框架

### 4.1 阶段接口
```csharp
interface IPipelineStage {
    StageId Id { get; }
    DirtyBits DependsOn { get; }       // 哪些脏位会触发我
    StageResult Run(PipelineContext ctx, CancellationToken ct);
}
```
`PipelineContext` 持有 grid、掩码、各 Model 引用、派生层容器（`LandUseGrid`、`ParcelSet`、`RoadGraph` …）、stageSeeds。
**阶段之间只通过 Context 交换数据，禁止阶段互相持有引用**（否则脏传播会漏，出现"改了没刷新"的假死）。

### 4.2 脏标记与调度
```
[Flags] DirtyBits { Seed, World, Terrain, River, Road, Budget, Parcel, Field, Profile, Building, All }
```
- 每个工具改完数据 → `ctx.MarkDirty(bits, bounds)`（bounds = 影响的 AABB/圆环，用于局部重算）。
- `PipelineRunner` 每帧（或滑杆 change 事件）合并脏位 → 按 §3.2 总纲表求"最小脏集合" → 拓扑序执行 → 触发 View 重建。
- 合并窗口：同帧内多次 Mark 只跑一次（避免拖 handle 时每 tick 全量重算）。

### 4.3 执行档位（这是"实时"的关键）
| 档位 | 何时 | 范围 | 内容 |
|---|---|---|---|
| **Preview** | 拖拽过程中（每帧） | 仅脏 bounds | 低分辨率/仅 overlay/仅统计数字，不建 GameObject |
| **Commit** | 松手/滑杆 stop/显式点按钮 | 脏 chunk 或脏 parcel | 完整重建，落 GameObject，进 undo/保存 |

规则：**任何工具的拖拽期只允许 Preview，只有 Commit 才碰 Unity 对象**。
把 15000 个实例放进 undo 栈/每帧重建是编辑器假死的首要原因。

### 4.4 取消与事务
- 每阶段接收 `CancellationToken`；后台 Job 必须先 `Complete()` 再 Dispose，域重载钩子里强制取消
  （否则 `GCHandle is not from the intended target domain` 域重载崩溃）。
- Commit 以 chunk/parcel 为**事务单元**：失败则回滚该单元并记 warning，不整城中断（保证交付前状态一致）。

### 4.5 耗时与可观测
每阶段记录 `{stage, ms, mode, cells, outputs, warnings}` 到 `GenerationReport`，主面板逐行显示；
可导出到 `.tmp-inspect/citymap_report.json`（不入库，AGENTS §9）。
没有逐阶段耗时，性能预算（§8）就只能靠猜——R-04 的缓解手段之一。

## 5. 主面板 CityWindow（AGENTS §0.1 原则1）

### 5.1 布局
```
┌ 工具栏：[新建城…]（项目组，高危四重确认，08 §3.5）  [重新生成]（阶段工具栏）  [取消] ── 自动跟随☑ ── 视图：地形/水系/路网/用途/建筑 ┐
├ 左：阶段树（S0~S6，每节点显示脏位/耗时/警告数 + [重跑此阶段]）                      │
├ 中：参数区（当前选中阶段/工具的 Profile；ρ 滑杆、seed、urbanFlatness 等实时旋钮）   │
└ 底：状态条（格数 / 边数 / 地块数 / 建筑数 / 内存估算 / 上次生成模式）                │
┌ 右（浮动）：场域探针读数 + 警告列表（可点击定位到场景位置）                          │
```

### 5.2 硬性交互要求
1. **不存在"必须先点生成才看到效果"**：所有参数改动默认 `自动跟随=true` 走 Preview→Commit；
   `自动跟随=false` 时阶段树脏节点变黄并提示"有 N 项未生效"（关掉开关也要可见，否则是新的黑盒）。
2. 面板直接建在场景实例的 Inspector 上（对象级参数），全局参数才进 `CityWindow`；
   禁止"参数藏在资产文件里、要双击跳出去编辑"的模式（§0.1 原则1）。
3. 每个数值参数带**单位与推荐区间**（如 ρ 显示 `0.12~0.22 推荐`），超界给黄色警告三角而非静默。
4. 视图模式 = 单一 SceneView overlay 开关组，决定"当前能拖什么"，避免多工具抢占鼠标事件（见 §6.3）。

### 5.3 工具模式互斥
`ToolMode { Inspect, TerrainSculpt, RiverBrush, RoadGraph, FieldPlace, Build }` 单选。
- 同一时刻只有一个工具消费 SceneView 事件（用 `EditorTool` 激活机制，天然互斥）；
- 切换工具时保留各工具的临时状态（未提交的河流线草稿等），存 `SessionState`（不写资产、不入 undo）。

## 6. 持久化总规约

### 6.1 三类数据的处置
| 类 | 例 | 载体 | 入 git | 撤销 |
|---|---|---|---|---|
| Model | 7 个 SO | `.asset` | ✅ | ✅（资产级） |
| 人工层 | 雕刻 delta / 河流线 / 骨架路网 / 场域 / 已 adopt 建筑 | `.asset` + 场景组件 | ✅ | ✅（对象级） |
| 派生层 | LandUseGrid / parcel / slot / 自动路网 / mesh / 未 adopt 建筑实例 | 内存（调试导出 `.tmp-inspect/`） | ❌ | ❌（改参数重算；反悔走 **08** 的意图层四机制） |

### 6.2 场域为什么是场景组件而不是 SO 列表
用户心智 = "我在地图这个位置放了一个圆柱"。做成场景 GameObject 才能：拖动/多选/复制/与其它对象一起进场景存档、
被 Unity 原生 undo 覆盖、被其他包用 `FindObjectsByType` 发现。代价：场域 id 必须稳定（用组件的
`instanceId` 不行——重建会变，因此**每个场域持有一个 `stableId` 字符串字段**，创建时生成，
用于派生随机与增量 diff）。这是 §0.1 原则2 里"实例状态"的典型。

### 6.3 schema 版本与迁移
- 每个 SO/asset 带 `int schemaVersion`；`OnValidate`/自定义 importer 里做迁移，迁移器必须**幂等**。
- 迁移失败：拒绝加载 + 控制台 error（宁红不脏）。带版本的派生格式（如 delta 资产）同理。
- 版本变更需写入 `Docs/CHANGELOG.md` 与 ACCEPTANCE。

### 6.4 场景与资产的一致性
- 建筑实例与场域在场景里；参数 Model 在资产里。**保存钩子**（`EditorSceneManager.markSceneDirty` + 自定义保存前校验）
  保证：场景保存时检查引用的 SO 是否已保存（丢失引用是"下次打开全空"的根因）。
- 加载时：若 SO 缺失 → 面板红标 + 列出缺失项，不静默使用默认值（静默会用错参数生成整城）。

## 7. 模块内决策
| 编号 | 决策 | 备选 | 理由 |
|---|---|---|---|
| D-00-1 | 逻辑格/显示网格/chunk 三层分辨率解耦 | 单一分辨率 | 规划精度与 mesh 规模不可兼得，见 D-14 |
| D-00-2 | 每格一字节掩码承载全部禁止/属性位 | 每模块各存一张集合 | 单一真源，避免规则互相打架 |
| D-00-3 | 随机种子按空间/稳定 id 派生，集合枚举序禁用 | 顺序随机 | 局部稳定性是增量与 A-1 的前提 |
| D-00-4 | Preview（拖拽）/Commit（松手）两档执行模型 | 全实时 or 全手动 | 实时手感与编辑器不假死的唯一平衡点 |
| D-00-5 | 派生层不落盘，人工层落盘（D-13） | 全部落盘缓存 | 杜绝派生数据与真源不同步；资产体积可控 |
| D-00-6 | 场域用场景组件 + stableId | SO 列表 | 用户心智 + 原生 undo + 跨包可发现（§6.2） |
| D-00-7 | 衰减求值器全局唯一（§3.3） | 各模块各写 | 同名参数手感必须一致 |

## 8. 验收（模块局部，编号续总纲）
| # | 可观测事实 | 证据方式 |
|---|---|---|
| A-0-1 | 同一 `cellSeed(s,x,z)` 在进程重启、域重载、生成顺序变化后取值完全一致 | EditMode 测试断言 1000 格 |
| A-0-2 | 以遍历序作种子会被代码审查/测试拒绝（提供 `RandomApiGuard` 检查禁用 API 调用） | 静态扫描测试 |
| A-0-3 | 拖拽 handle 期间无 GameObject 创建（Profiler 无 Instantiate 调用），松手后才建 | Profiler 录制 |
| A-0-4 | 取消生成后场景无半成品 chunk（事务回滚），且后续重新生成结果与不取消一致 | 用例：中途取消→重跑→比对哈希 |
| A-0-5 | `自动跟随=false` 时脏阶段可见变黄，且提示未生效项数量 | 人工走查 |
| A-0-6 | schema 迁移幂等：同一资产连续迁移两次结果逐字节一致；缺字段版本拒载并报错 | 测试资产样本 |
| A-0-7 | `displayStep % cellSize == 0` 断言生效，非法参数拒绝应用并提示 | 面板输入非法值 |

## 9. 风险
- 域重载残留 Job/GCHandle → 所有后台 Job 必须在 `[RuntimeInitializeOnLoadMethod(SubsystemRegistration)]`/域重载钩子里强制取消（R-07 细化）。
- `CityWindow` 参数区若做成"一个大表单全平铺"，用户找不到当前该调什么 → 按阶段分组 + 只显示当前工具相关项。
- 掩码写入点分散在 S2/S3/S4 → 必须集中在 `MaskWriter`（唯一写口），否则权限位互相覆盖不可追溯。
