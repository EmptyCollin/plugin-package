# DESIGN — com.collin.citymap（城市地图编辑器）· 总纲

> 状态：**已评审（v7.1，用户 2026-11-03 确认）** — 设计阶段收口，可进入 M0 包骨架
> 修订记录：
> - v1（2026-11-02）初稿：整体思路 + 决策 D-01~D-10 + 开放问题 Q-1~Q-6。
> - v2（2026-11-02）用户裁决落地：**Q-1 ✅ 内径/内径值两参数**；**Q-2 ✅ 生成规则改为"全图统一：先硬过滤 → 再加权随机"**（§4.8 重写）；**Q-3 ✅ 复用 com.collin.stretch**（走 §5 合规桥接，D-07 修订、新增 §6）。
> 同时文档拆分为「总纲 + 9 份模块细则」：每模块的技术路线、操作交互、数据编辑与持久化单独成文。
> - v3（2026-11-02）新增 **Modules/08 编辑管线**：操作顺序、L1~L4 反悔体系、局部反悔（锁区/负约束/adopt）、
>   上游改动 × 下游固化对象的冲突矩阵。此前各模块零散提及的撤销约定，一律以 08 为真源。
> - **v4（2026-11-02）新增 Modules/09 操作序列推演**：用 T1~T4 真实工作流横推 00~08，发现 9 处跨模块空洞
>   （H-1~H-9，其中 4 处阻断级），裁决已全部回写：城心独立锚点（D-08-10）、adopt=硬占用墙（D-08-11）、
>   Tracker 双检+orphan（D-08-12）、掩码升 ushort、分裂栈失效规则、过小块并入、删自动路三选一。
>   **后续每轮推演（R2/R3/R4）继续追加到 09，修订继续回写各模块。**
> - **v5（2026-11-02）09 第二轮推演 R2~R4 完成**：新增 7 处空洞 H-10~H-16（其中 H-10 跨会话重复叠楼、H-15 删表行生无主物为阻断级）。
>   回写要点：**认回(reclaim)以 `CityBuildingTag` 为唯一依据、prev 降为优化**（D-22）、
>   `重新生成` 与 `新建城` 两动作分家（D-23）、表行失效同转 orphan（D-24）、
>   桥接改**两段式合并 + 惰性解析 + 批量接口 + 注册唯一性**（06 §4.2/§4.3/§4.5）、「冻结全城」入口（08 §4.4）。
>   三轮推演累计 **16 处空洞 / 5 处阻断级**，M0 前置条件更新为 H-1~H-16 全部回写完毕（已完成）。
> - **v6（2026-11-02）用户四项决策落地，新增 Modules/10（大尺度流式与导出层）**：
>   ① 消费形态 = **可选导出**（scene 切块 / 引擎无关 JSON / 2D tile），非仅编辑器场景；
>   ② 批准 ADR-0001 但 **core 接口推迟到 M9 一起做** → M8~M9 全程走降级拉伸；
>   ③ 尺度 = **8km+ / 10 万栋以上**（推翻原 2km/15k 预算）；④ 允许 Unity Recorder 仅用于 Sample 演示。
>   决策 ①+③ 触发本包最大架构调整（D-25~D-27）：建筑真源由"场景 GameObject"改为 **`BuildingRecord` 数据层**，
>   GameObject 降为视口投影。推演新增空洞 **H-17~H-23**（H-17/H-18 阻断级，均经实算验证，见 10 §1）。
> - **v7（2026-11-03）Q-11/Q-12 裁决落地（纯文档，设计阶段不动锁）**：
>   Q-11 → **基准格式先行、对接时补 `ExportAdapter`**（禁止改基准通道语义，每适配器带往返用例，A-44）；
>   Q-12 → **切块尺寸 `chunkSizeM` 可自定义、默认 1024m**，落地时新发现 **H-25**
>   （可配值暴露了原隐式归属规则：地形跨 region 拼接 + 长条对象跨块消失），已处置并澄清
>   `region`（工作集）≠ `scene chunk`（交付）概念正交性。累计空洞 **H-1~H-25 / 阻断级 7 处**。
> - **v7.1（2026-11-03）评审通过**：用户确认设计无问题，状态转「已评审」。
>   同时**提前**建立包根 `Showcase.json`（§6 五字段，`order=20.0` 排在 core=0 / stretch=10 之后，
>   `video` 暂为空串——校验器对空串跳过文件存在性检查，M10 补录视频后回填路径）。
>   ⇒ M0 清单相应减少一项（见 §10）。
> - **v8（2026-11-03）M0 已落地并提交（`ebb6f4b`，0.0.1）；本轮产出 Modules/11 接口冻结文档**：
>   `BuildingRecord` 定为 **32 字节/栋**、小端、按 region 分块 + `blockIndex`、**禁用自动序列化器**。
>   相对 v6 草案：删冗余 `id`/`landUseDominant`/`thresholdUsed`（纯派生）、增 `landF/I/R/A` 与 `footWQ/DQ`。
>   定稿时新发现 **H-26**（footprint 覆盖不持久化→跨会话叠楼）、**H-27**（`slotKey` 冲突在 n=10⁵/2³¹ 下期望约 2.3 次，
>   非边角情况）、**H-28**（`List<struct>` 走 SO 会退回 YAML → H-17 换皮回归）。
>   活文档（DESIGN/06/07/08/10）的旧字段名已按 11 §9 对照表收敛；09 的历史推演行文不回改。
>   **状态：结构定稿待用户确认冻结**（确认后任何字段增删须先登记 + 用户批准，见 11 §1/§11）。

## 0. 文档地图

| 文档 | 内容 | 关键类/资产 |
|---|---|---|
| Modules/00-WorldFoundation.md | 格网/坐标/分块、确定性随机、管线与脏标记框架、持久化与撤销总规约、主面板 | `CityProject`、`PipelineRunner`、`CityWindow` |
| Modules/01-Terrain.md | 地形生成（基底噪声+平整约束）、雕刻工具、叠加层数据 | `TerrainProfile`、`SculptDeltaAsset` |
| Modules/02-River.md | 河流笔刷、剖面下切、自动水系、下游耦合 | `RiverProfile`、`RiverNetworkAsset` |
| Modules/03-RoadNetwork.md | 路网点线编辑、细分式补全、道路占比系数、桥 | `RoadProfile`、`RoadGraphAsset` |
| Modules/04-ParcelSlot.md | 地块划分、临街边与建筑位、上下文哈希增量重算 | `Parcel`/`Slot`（派生） |
| Modules/05-LandUseField.md | 场域定义/求值/烘焙/可视化/工具 | `LandUseField`、`LandUseGrid` |
| Modules/06-BuildingProfile.md | 建筑配置表、四维值标度、stretch 桥接接口 | `BuildingProfile`、`IStretchService` |
| Modules/07-BuildingFill.md | 准入过滤→加权随机→落位→拉伸驱动高度 | `BuildFillCommand`、`CityBuildingTag` |
| **Modules/08-EditingWorkflow.md** | **编辑管线：操作顺序、L1~L4 反悔体系、锁区/负约束/adopt 局部反悔、冲突矩阵、历史面板** | `UndoHistoryTracker`、`LockRegionAsset`、`CityCheckpoint` |
| **Modules/09-Workthrough.md** | **操作序列推演（设计验证）：四轮走查，空洞 H-1~H-25 与回写裁决** | `CityCenterAnchor`、orphan 状态 |
| **Modules/10-StreamingExport.md** | **v6 新增：8km+ 大尺度的数据态真源、工作集流式、三形态导出（scene 切块/JSON/tile）** | `BuildingRecord`、`ViewportRealizer`、`CitySnapshot` |
| **Modules/11-BuildingRecord.md** | **接口冻结：建筑落位记录字段/偏移/量化/位域/序列化布局/指纹输入/校验与迁移（H-26~H-28）** | `BuildingRecord`、`intentVersion`、`RecordGeometry.Resolve` |

---

## 1. 目标与非目标

### 1.1 解决的问题
提供**参数驱动、可人工干预的城市地图编辑器**：人工用笔刷与点线给出"意图"（河、主干道骨架、场域），
程序按预算与规则补全其余细节（等高地形、支路网、地块、用途分配、建筑填充），整条管线可增量重跑且**人工劳动永不被自动生成摧毁**。

| # | 能力 | 人工输入 | 程序自动补全 | 模块文档 |
|---|---|---|---|---|
| C1 | 地形 | 雕刻笔刷 | 低频噪声 + 平整约束（城市相对平整、局部起伏） | 01 |
| C2 | 河流 | 中心线笔刷 + 宽度 | 河床下切、D8 支流、下游补全 | 02 |
| C3 | 路网 | **点与线**（节点+边） | 按道路占比系数 ρ 细分补全支路 | 03 |
| C4 | 建筑 | 配置表（路径/可拉伸/四维值） | 全图"硬过滤→加权随机"填充 + 拉伸驱动高度 | 06/07 |
| C5 | 场域 | 同心圆柱工具对象 | 烘焙四维强度场，驱动 C3 修正与 C4 规则 | 05 |
| **C6** | **导出** | 选择形态与切块/分辨率 | 由统一 `CitySnapshot` 投影为 scene 切块 / `citydata.json` / 2D tile | **10** |

**场域是设计枢纽**：把"想要什么样的城市"翻译成"每格的四维强度"，C4 的规则因此统一为采样+过滤+加权，不写死启发式。
**消费形态（v6 决策）**：城市是**可多形态导出的产物**，不是只能活在编辑器场景里——
同一份数据投给 Unity 场景、外部引擎数据格式、或 2D 瓦片。因此 C6 与 C1~C5 同级，不是收尾脚本。

### 1.2 非目标
- 不做运行时 PCG **游戏逻辑**（本包定位是编辑器侧生产工具 + 导出器）。
  但**允许运行时消费导出的数据**：`CityPipeline` 内核保持 Runtime 化、无 Editor 依赖，
  外部/运行时若要按 Model+seed 现算，走同一内核即可（v6 决策 ① 的落地方式，见 10 §2.1）。
- 不做真实 GIS 精度、水文仿真、交通/人口仿真（四维值只做生成门槛与权重，不是经济模型）。
- 不做建筑外观/材质生成：建筑**只用预制件**，几何变化仅"拉伸"（复用 `com.collin.stretch`，§6）。
- 不做城市美化（树/路灯/行人）——预留 core 事件通道，属后续独立包。
- 不修改展板场景（§0/§6 AGENTS）；演示走包内 `Sample/` + `Showcase.json`。
- **不自造外部引擎/tile 规范**：`citydata.json` 与 tile 通道是本包内部一致的格式；
  若对接具体外部瓦片体系/中间件，需另行提供规格并新增 `ExportAdapter`（属新需求，非默认范围，10 §9）。

---

## 2. 约束

| 项 | 内容 |
|---|---|
| 引擎 | Unity 6000.6.7f1（钉死，AGENTS §8） |
| 包形态 | 内嵌包 `Packages/com.collin.citymap/`；代码禁入 `Assets/` |
| 程序集 | `com.collin.citymap`（Runtime，引用仅 `com.collin.core`）+ `com.collin.citymap.Editor`；`validateReferences: true` |
| 依赖 | **asmdef 层面禁止引用 `com.collin.stretch`**（§5 feature→feature FAIL）。stretch 能力经 core 接口 + `Services` 桥接消费（§6，ADR-0001） |
| Runtime 纯净 | 禁 `using UnityEditor`（校验器 runtime-purity） |
| 外部依赖 | 零 UPM 第三方包；随机/噪声/几何算法自带，保证确定性 |
| 预算 | **v6 档位（用户决策）：8km×8km、逻辑格 4m、建筑 ≥10 万栋**；场数据量化 + region 流式工作集（详见 Modules/10）。旧 2km/15k 档保留为 `Sample` 演示与回归基线 |

---

## 3. 总体架构

### 3.1 MVC 分层（AGENTS §0.1 原则2 判据：实例差异禁止进共享配置）

```
Model（ScriptableObject，共享）   CityProject / TerrainProfile / RiverProfile / RoadProfile+RoadBudget
                                  BuildingProfile（★配置表）/ FieldPreset / FalloffCurveAsset（共享曲线）
Instance（场景 MonoBehaviour）     CityGraph（路网+地块+建筑根）/ LandUseField（★场域）/ CityTerrain
                                  CityBuildingTag（落位记录）/ 人工层资产引用（雕刻/河流/骨架路）
Derived（可重算，默认不落盘）      LandUseGrid（场域烘焙）/ Accessibility / WaterProximity
                                  规划面网格（可建掩码/红线/水缓冲）/ Parcel+Slot / 自动路网层 / mesh
View（只读渲染）                  chunk 地形 mesh / 道路条带 mesh / 建筑 prefab 实例 / SceneView overlay
Controller（Editor）              CityWindow / PipelineRunner / RiverBrush·RoadGraph·Field·Sculpt 工具
                                  BuildFillCommand / GenerateCommand / 保存钩子
                                  UndoHistoryTracker / HistoryPanel / LockRegion·Constraint 工具（08）
```

派生层原则：**能从（Model+Instance+seed）重算的一律不持久化**——省资产体积、杜绝"派生数据与真源不同步"这类脏。
例外：重算代价 > 保存代价且需人工微调时，落**人工层**（雕刻 delta、骨架路网、河流线、场域组件）——这四类正是"人工意图"的载体。

### 3.2 管线阶段与脏传播

```
S0 装配 → S1a 地形基底 → S2 河流 → S3 路网 → S4 地块 → S1b 平整约束回解 → S5 用途烘焙 → S6 建筑落位
```
S1 拆两步的原因：平整约束（地块/道路压平）需要地块多边形与道路几何，而地块又建立在基底地形上——
S1a 先出自然起伏，S4 后 S1b 做一次**非迭代**的约束融合（迭代松弛会连锁破坏局部稳定，D-04）。

脏传播（模块级，详见各模块文档）：

| 改动 | 最小脏集合 |
|---|---|
| seed / 世界尺寸 | 全部 |
| TerrainProfile 噪声 | S1a, S2(汇流), S1b, S5, S6(脏chunk) |
| 雕刻笔刷（manualDelta） | 仅 S6（脏 chunk 内地块的落位校正）——**不触发重生成** |
| 河流线 | S2, S1a?, S3(桥), S4, S5(临水场), S6 |
| 路网点线 / ρ | S3(自动层), S4, S5(可达场), S6 |
| 场域 | S5(脏圆环), S6(脏地块) |
| BuildingProfile 表 | S6 |

### 3.3 确定性契约（第一工程属性）
- 全部随机来自可复现 PRNG（splitmix64），子种子 = `mix(seed, stageId, gridX, gridZ)`——**按空间位置派生，禁止按遍历顺序**，
  保证增删一个地块/场域**不改变邻居的随机结果**（局部稳定性，增量重算的前提，D-10）。
- 禁用 API：`UnityEngine.Random`、无种子 `System.Random`、时间/帧序依赖。
- 契约：同一 (Model, 人工层, seed) ⇒ 逐位一致产物。验收 A-1。细则见 Modules/00 §3。

### 3.4 人工层与自动层隔离（D-08，本工具"可用性"的底线）
四类人工层：**雕刻 delta / 河流线 / 骨架路网(pinned) / 场域**。
"全量重生成"只重算自动层，人工层原样叠回（分层次序固定：基底 → 下切 → delta → 约束）。
自动产物可被用户**「固化」为人工层**（右键：adopt），固化项此后不再被自动流程改动。

### 3.5 编辑/引擎双侧一致（AGENTS §0.1 原则3）
生成内核（纯数据进/纯数据出的 `CityPipeline`）放 **Runtime**；工具/面板/handle 全放 **Editor**；
编辑态与 Play 态共用同一 `BuildGeometry()` 路径。落盘产物走 `PrefabUtility` 实例 + 保存钩子，不手写场景 YAML。

---

## 4. 核心概念速览（细节在各模块文档）

### 4.1 场域（Modules/05）
`LandUseField` = 同心双圆柱：`type(Fin/Ind/Res/Agr)`、`innerRadius`、`outerRadius`、`innerValue`、`outerValue`、`falloff`、`weight`、`enabled`。
**Q-1 裁决：内径（米）与内径值（0~100）是两个独立参数**。求值：

```
r ≤ innerR            → v = innerValue                          （平台区）
innerR < r ≤ outerR   → t=(r-innerR)/(outerR-innerR); v = lerp(innerValue, outerValue, curve.Evaluate(t))
r > outerR            → v = 0
```

多场域同维度聚合默认 **Max**（保持"这是金融区"的语义纯净，D-03）。烘焙成 `LandUseGrid`（每格 4 维 float）。

### 4.2 建筑配置表（Modules/06）
每行：`prefabPath`（字符串路径，兼容 `prefabRef` 强引用）、`stretchable`（是否支持几何拉伸）、
`finance/industrial/residential/agricultural`（**0~100 整数**，与场域值同标度，D-02）。
可选增强项（占地覆盖、临水偏好、高度区间、tags）都有默认值——**必填只有用户点名的 6 项**，保证"5 分钟填表就能出城"。

### 4.3 建筑生成规则（Modules/07）—— **Q-2 裁决后的定稿语义**
两步法适用于**全地图每个 slot**，不只内径范围：

```
① 硬过滤（筛掉不合格）：D = argmax(场域四维强度 F(c))；门槛 Th = F_D(c)
   候选集 C = { B ∈ 表 | B.v_D ≥ Th }            ← 只看数值门槛，不看建筑"类别"
   ⇒ 金融区里"生活类但金融值足够高的咖啡馆"合格入选（用户案例语义）
   C 为空 → 门槛 ×0.7 放宽（≤3 轮）→ 仍空取该维最高分行 + warning 计数
② 加权随机：w = softmax(β · score)；score = 四维余弦相似 + 临街/临水/尺寸适配 − 重复惩罚 + 聚簇修正
   抽样种子 = mix(seed, parcelId, slotIndex)      ← 局部稳定
```

过渡带（内径~外径）门槛随曲线自然下降，**同一条规则连续覆盖全城**，无需"区内/区外"分支逻辑。

### 4.4 路网（Modules/03）
人工侧 = **图编辑器**（点=路口、线=路段，边上按下自动插 T 字口，吸附正交）；
自动侧 = **地块细分式生长**：递归切分最大区域、端点吸附成 T、`ρ = Σ(红线宽×长)/可建设用地` 收敛目标+硬上限。
ρ 滑杆实时驱动支路增减（本包最核心的"所见即所得"交互）。

### 4.5 地形（Modules/01）与河流（Modules/02）
地形 = 低频 fbm + 城市区径向压幅（`urbanFlatness` 旋钮）→ S1b 用**地块/道路平整约束的加权融合**一次求解
（"越靠近对象中心越贴目标平面"），兼得整体起伏与局部平整。河流 = 中心线笔刷 + 剖面下切层 + D8 自动支流（人工主干优先），
临水带进可建掩码；死胡同自动补下游，交付前并查集校验。

---

## 5. 全局数据模型与持久化总规约（细则见 Modules/00 §7）

| 数据 | 载体 | 落盘 | 理由 |
|---|---|---|---|
| 参数 Model（7 个 SO） | 资产 | ✅ 入 git | 真源，可 diff |
| 人工层：雕刻 | `SculptDeltaAsset`（按 chunk 平面） | ✅ | 不可重算 |
| 人工层：河流 | `RiverNetworkAsset`（折线+宽） | ✅ | 真源；下切剖面**派生不落盘** |
| 人工层：骨架路网 | `RoadGraphAsset`（存原始点线） | ✅ | 真源；**平面化结果派生** |
| 人工层：场域 | 场景内 `LandUseField` 组件 | ✅ 随场景 | 用户直觉 = 摆在地上 |
| 局部反悔层：锁区/负约束 | `LockRegionAsset`/`ConstraintAsset` | ✅ | 它们是意图层（08 §4） |
| **建筑真源（v6/定稿）** | **`BuildingRecordAsset`（32B/栋，SO 元数据 + `byte[] payload`）** | ✅ | 仅当存在人工改动/adopted 时必落盘；纯派生城可重算（D-25、Modules/11） |
| 建筑表现（v6） | 工作集内的 prefab 实例 + 反向指针 `CityBuildingTag.slotKey` | ❌ | **投影，按需物化/回收，不是真源**（10 §2.1/§3.1、11 §5） |
| 场数据（高度/掩码/用途） | 按 region 的分块二进制（Library 缓存或结果层） | ❌ | 可重算；int16/byte 量化（D-26） |
| 检查点 | `CityCheckpoint.asset`（命名）/ Library 自动快照 | 命名✅／自动❌ | 08 §8；v6 起含记录层，快照单位即 region |
| 派生层：LandUseGrid/地块/slot/自动路网/网格 | 内存（调试可导出 `.tmp-inspect/`） | ❌ | 可重算，D-13 |

撤销政策（真源在 **Modules/08**，此处仅摘要）：反悔的唯一入口是**意图层**（L1 对象级 Unity Undo → L2 journal →
L3 区域回滚 → L4 检查点四层）；派生层不可撤销、随 `UndoHistoryTracker` 版本差自动重算；
自动产物的"反悔"表达为 **adopt/锁区/负约束** 三种机制，不做对象级撤销栈。版本化：每个 SO 带 `schemaVersion`，
`OnValidate` 内做迁移；迁移失败红灯拒载。

---

## 6. 跨包接口与 stretch 桥接（Q-3 裁决落地方案）

### 6.1 为什么不能直接引用
§5 明文"feature 包之间禁止引用（asmdef 层面切断）"，校验器 `dep-direction` 对 `citymap→stretch` 直接判 **FAIL**。
用户意图（用 stretch 的拉伸能力）与规则并不矛盾——§5 同时给出了合法通道：**跨包通信走 core 的接口/事件/信号**。

### 6.2 桥接设计（ADR-0001，实施前置条件）
1. `com.collin.core` 新增通用接口（不含任何 stretch/citymap 专有类型）：

```csharp
namespace Collin.Core {
    public interface IStretchService {
        bool TryBind(GameObject instance);              // 为实例启用拉伸（未就绪返回 false）
        int  GetRepeatableSegmentCount(GameObject go);
        float GetSegmentSpan(GameObject go, int seg);   // 米
        bool  SetRepeat(GameObject go, int seg, int n); // 整数复制份数
        float GetStretchedHeight(GameObject go);        // 当前拉伸后总高
        void  Unbind(GameObject go);
    }
}
```

2. `com.collin.stretch` 在其初始化入口把实现 `Services.Register<IStretchService>()`；
3. `com.collin.citymap` 仅引用 core，经 `Services.TryGet<IStretchService>()` 取用；
   **取不到时自动降级**为 Transform.Y 等比缩放（结果标记 `approximated`，面板黄标）。
4. 语义映射：配置表 `stretchable=true` 的行，落位时把"目标高度"换算成中段 `SetRepeat(n)`；
   `stretchable=false` 的行绝不改动几何（验收 A-9 不变）。

> 效果 = "就用 com.collin.stretch"，且 uv 不拉伸、接缝分段等 stretch 特性原样继承；
> 代价 = 多一层薄接口。备选（修改 AGENTS §5 开特例）不推荐：规则变更属高风险且校验器需同步改。

### 6.3 本包对外（经 core）
- `Services`：`ICityGenerator`（Generate/RebuildDirty/Cancel）、`ILandUseSampler`（四维采样+门槛）、`ITerrainSampler`（高度/可建查询）。
- `EventChannel`：`CityGenerated`、`LandUseFieldChanged`、`RoadGraphChanged`、`TerrainDirty(chunkId)`。
- 展板：`Showcase.json`（`order=20`：core=0 < stretch=10 < citymap），`Sample/` 预生成小城 + 演示视频。

### 6.4 冲突与合并策略（AGENTS §2，第二轮推演 H-12 修订）
桥接实施采用**两段式合并**（细则见 Modules/06 §4.5）：
- 段 1 只向 core 新增接口文件（含批量成员），不碰 stretch/citymap → 编译风险≈0、两包不阻塞、可单文件回滚；
- 段 2 待 stretch 空闲取锁后写实现与消费接线；失败只回滚两包，core 空接口留着无害（自动走已验证的降级路径）。
取锁前一律以 `python Tools/verify_project.py` 实时复核锁状态并向用户申报（AGENTS §2）。
（历史：v2~v4 期间 stretch 被 `ui-fix` 会话持锁；v5 时该锁已释放，但**开工前仍需实时确认**，不写死结论。）

---

## 7. 决策记录（v2 修订汇总；模块内局部决策见各模块 §决策）

| 编号 | 决策 | 变更 |
|---|---|---|
| D-01 | 规则格网为统一空间底座（逻辑 2m，显示 mesh 可降采样） | — |
| D-02 | 四维值与场域值统一 0~100 整数标度 | — |
| D-03 | 场域求值 = 内平台+曲线过渡+外截止；跨场域聚合默认 Max | — |
| D-04 | 平整 = 一次加权融合，迭代上限 1 | — |
| D-05 | 路网补全 = 半边界面细分 + 端点吸附；ρ 为收敛目标+硬上限 | — |
| D-06 | footprint 自动测量 prefab，表内默认不手填 | — |
| **D-07** | ~~本包自带轻量拉伸~~ → **复用 com.collin.stretch，经 core `IStretchService` + Services 桥接；缺包时降级等比缩放** | ✅ Q-3 |
| D-08 | 人工层/自动层分域；人工层永不被重生成摧毁；自动产物可 adopt | — |
| D-09 | 生成内核 Runtime、工具 Editor，同一构建路径 | — |
| D-10 | 空间位置派生 PRNG，禁遍历序随机 | — |
| **D-11** | **建筑选择 = 全图统一两步：主导维硬过滤（只看数值达标，不看类别）→ softmax 加权随机**；过渡带门槛随曲线连续下降 | ✅ Q-2 |
| **D-12** | 场域 `innerRadius`（米）与 `innerValue`（0~100）解耦为两参数 | ✅ Q-1 |
| D-13 | 派生层默认不持久化；人工层才落盘 | v2 新增 |
| D-14 | 显示网格分辨率与逻辑格解耦（默认 4×），控制 mesh 规模 | v2 新增 |
| D-15 | 骨架路网存**原始点线**，平面化（交叉口打断等）每次重建派生 | v2 新增 |
| **D-16** | **反悔 = 只回滚意图层；派生层经 `UndoHistoryTracker` 版本差自动重算**（08 §5.1） | v3 新增 |
| **D-17** | **局部反悔三机制：adopt（要它）/ 锁区（保住一片）/ 负约束（不要它）；删除自动物自动转负约束**（08 §4） | v3 新增 |
| **D-18** | **操作顺序 = 推荐 checklist（Route1/Route2 双路线）而非门禁；上游提交产生的固化对象冲突转待裁决条目，永不自动删挪**（08 §3.4/§3.3） | v3 新增 |
| **D-19** | **城心 = 独立意图锚点 `CityCenterAnchor`**；S1a 压幅与 07 高度衰减只读锚点，场域经「设为城心」显式提交（09 H-7→08 D-08-10） | v4 推演新增 |
| **D-20** | **adopt/手刷对象 footprint 烘焙为 `OccupiedHard` 掩码位**，对 S2 河道/S3 细分/S4 地块/S6 落位全体现为硬禁入墙（09 H-2→08 D-08-11） | v4 推演新增 |
| **D-21** | **slot 失效的固化建筑转 `orphan` 保留（仍占位 + info 条目三选一）；Tracker 用集合哈希+版本差双检**（09 H-4/H-9→08 D-08-12） | v4 推演新增 |
| **D-22** | ~~跨会话认回以 `CityBuildingTag` 为唯一依据~~ → **v6 由 D-25 修订**：主依据改为 `BuildingRecord` 资产 + `intentVersion` 指纹，tag 反查退为首次兜底；Tracker 首帧只建基线不触发重算（09 H-10/H-10b→08 D-08-14→10 §4） | v5 推演新增，v6 修订 |
| **D-23** | **`重新生成`(保留人工层，免确认) 与 `新建城`(清空意图层，四重高危确认) 语义与按钮彻底分家；造城后显示"0 固化对象"+「冻结全城」入口**（09 H-16/H-14→08 §3.5） | v5 推演新增 |
| **D-24** | **表行失效与 slot 失效同等对待 → 转 orphan 保留并占位；`rowId`/`slotKey` 永不复用；桥接采用两段式合并 + 惰性解析 + 批量接口 + 注册唯一性检查**（09 H-15/H-11/H-12/H-13/H-14b；键名统一见 Modules/11） | v5 推演新增，定稿收敛 |
| **D-25** | **建筑真源 = `BuildingRecord` 数据层（≤32B/栋），GameObject 降为视口投影**；认回主依据从 tag 改为记录 + `intentVersion` 指纹（10 §2/§4，H-17/D-10-1/D-10-5） | v6 决策①③ |
| **D-26** | **8km 档：逻辑格 4m + 高度 `int16` 量化 + 用途场 `byte` + region(1024m) 流式工作集**（常驻 <40MB；2m/float 口径实算 336MB 不可接受，H-18/D-10-2/D-10-3） | v6 决策③ |
| **D-27** | **三形态导出（scene 切块 / citydata.json / 2D tile）共享单一 `CitySnapshot`；导出前强制全区一致性 `EnsureWholeMapConsistent()`，error 条目阻断导出**（10 §5，D-10-6/H-20/H-21/H-22） | v6 决策① |
| **D-28** | **ADR-0001 批准但 core 改动推迟至 M9**：M8~M9 全程降级拉伸；M9 后提供「重建降级建筑高度」一次性迁移，`approximated` 与 `repeat` 互斥（10 §6，H-23） | v6 决策② |
| **D-29** | **Unity Recorder 允许引入，仅限 `Sample/` 演示录制**，不得进入生成内核与 Runtime 依赖（v6 决策④，见 §11 关闭 Q-10） | v6 决策④ |
| **D-30** | **`BuildingRecord.heightValue` 保留双义字段**（`stretchable × approximated` 二维表消歧：repeat / 缩放百分数 / 非拉伸哨兵），**不拆成 `repeat`+`scale` 两字段**；所有读取强制经 `RecordGeometry.Resolve` 单一入口 | v8 用户裁定 2026-11-03（11 §4.3/§13） |

## 8. 性能预算（v6 按 8km 档重写；收口验证见 §9）

| 项 | 预算（8km / 4m 格 / 10万栋） | 兜底手段 |
|---|---|---|
| 常驻内存 | **≤40MB**（工作集 2 region + 全区记录 3.2MB + 索引） | int16 高度 / byte 用途场 / region 惰性加载（10 §3.3）|
| 打开场景→可交互 | ≤10s（与工作集相关、与全图无关） | 记录优先认回，不遍历场景（10 §4） |
| 保存 | ≤15s | 只写脏 region + 记录增量 |
| 投影实例峰值 | ≤4096 栋（LRU ≤8 region） | 视口驱动物化/回收（10 §3.1） |
| 场域烘焙 | ≤500 场域、脏环内 <100ms（Job） | 场域 ≤8 主线程快路径 |
| ρ 滑杆单步补全 | ≤150ms | 面优先队列缓存 |
| 工作集内建筑重算 | ≤300ms/region | 地块上下文哈希 diff（04 §5） |
| **全区记录重算（纯数据）** | ≤90s，分帧可取消 | **仅在导出前 / 显式全量重生成时执行**，编辑期绝不做（10 §9） |
| 拉伸重建（10 万栋） | 计入全区预算 | `BeginBatch/EndBatch`；不支持则分帧 8ms（09 H-13） |
| 跨会话打开 | 零重算（无 Instantiate 净增） | 记录 + `intentVersion`；Tracker 首帧建基线（10 §4、09 H-10b） |
| 导出（形态 A/B/C） | 全区一致性检查后 ≤60s/形态 | `CitySnapshot` 单源三投影（10 §5.4） |
| **旧 2km 档** | 保留为 `Sample` 与回归基线（21MB/15k 栋） | 与 8km 档共用同一代码路径，仅参数不同 |

## 9. 验收索引（全表在各模块 §验收；根门禁 = AGENTS §1.6 三项）

A-1 确定性一致（**v6 起口径 = 量化后一致：≤0.01m / ≤1 量化步**，见 10 §9） ｜ A-2 场域改动只影响圆环内 ｜ A-3 **全图**门槛硬过滤语义（含咖啡馆用例）
A-4 ρ 单调收敛+地块下限 ｜ A-5 道路穿河必有桥 ｜ A-6 无死胡同水道 ｜ A-7 人工层重生成后完整保留
A-8 建筑零重叠、不越红线/水缓冲 ｜ A-9 非拉伸行几何恒等 ｜ A-10 无"必须手点生成"黑盒 ｜ A-11/A-12 校验绿灯+控制台归零
新增：A-13 stretch 桥接——Services 不可用时降级缩放且黄标，可用时走 stretch 分段且 UV 不形变 ｜ A-14 自动层 adopt 固化后重生成不回归
**v3（Modules/08 编辑管线）**：A-17 undo 后派生层自动一致 ｜ A-18 删自动物→负约束、undo→复活 ｜ A-19 锁区生效/解锁一致
A-20 冲突转条目且四裁决可执行 ｜ A-21 检查点恢复逐字段一致 ｜ A-22 批量动作=1 条 journal 可整条回滚
A-23 影响预览只读+阈值生效 ｜ A-24 任意阶段全工具可用（顺序非阻塞）｜ A-25 高危操作前自动快照
**v4（09 推演回写）**：A-26 固化对象=硬墙（自动路/河不穿 adopted） ｜ A-27 场域拖动不触发 S1a（城心=锚点）
A-28 orphan 保留+占位+三选一 ｜ A-4-9 过小块并入而非留空
**v5（第二轮推演回写）**：A-29 删表行→实例转 orphan ｜ A-30 认回幂等（重开→保存零新增实例）
A-31 Route2「冻结全城」保全入口 ｜ A-32 `新建城` 四重确认而 `重新生成` 免确认 ｜ A-33 打开即静默+装配顺序反转不误判降级
A-6-9 注册唯一性恰 1 处 ｜ A-6-10 批量拉伸计时达标
**v6（大尺度/导出，Modules/10）**：A-30 口径改记（重开→保存后**记录资产**逐字节一致、投影无净增）
A-34 常驻 ≤40MB/打开 ≤10s ｜ A-35 穿越 16 region 投影峰值 ≤4096 且记录零丢失 ｜ A-36 工作集外全区记录同步
A-37 影响预览与条目覆盖全图（未加载区可拉入） ｜ A-38 scene 切块归属唯一 ｜ A-39 citydata 无 Unity 类型
A-40 tile↔记录 双向一致（2D/3D 同源） ｜ A-41 导出前强制全区一致性、error 阻断 ｜ A-42 降级高度迁移可回滚
**v7（Q-11/Q-12 落地）**：A-43 `chunkSizeM` 四档导出地形无跨块拼接缝、非法倍数值被拒 ｜ A-44 基准 tile 格式冻结后
适配器改动零侵入基准通道，每适配器带字段对照表 + 往返像素一致用例（无用例不合并）
**v8（Modules/11 接口冻结）**：A-45 `sizeof==32` 与偏移逐项断言 ｜ A-46 同输入两次 payload 逐字节一致（A-30 可判定形式）
A-47 `slotKey` 冲突按 salt 协议消解、重复键 error 阻断保存（H-27）｜ A-48 footprint 覆盖跨会话存活无叠楼（H-26）
A-49 10 万条时资产 YAML 行数 <500、打开保存不进分钟级（H-28）｜ A-50 面板门槛=派生值、无独立 threshold 字段
A-51 `heightValue` 四种情形逐一验证、非法组合被校验阻断

## 10. 里程碑（v2 重排；一次交锁 = 一个可编译增量）

| M | 内容 | 依赖 |
|---|---|---|
| M0 | 包骨架：package.json/双 asmdef/四件套/Sample 空目录（**Showcase.json 已于 v7.1 提前建立**；前置：09 的 H-1~H-9 修订已回写完毕） | — |
| M1 | Modules/00 基础设施：格网/PRNG/管线调度/脏框架/CityWindow 壳 **+ 08 的 manualVersion/`UndoHistoryTracker` 骨架** | M0 |
| M2 | Modules/01 S1a 基底 + 显示 mesh + S1b 平整（先用人工试设地块验证约束） | M1 |
| M3 | Modules/01 雕刻工具 + delta 层；Modules/02 河流全套 | M2 |
| M4 | Modules/03 点线编辑（人工层闭环） | M1 |
| M5 | Modules/03 细分补全 + ρ + 桥；Modules/04 地块/slot | M3,M4 |
| M6 | Modules/05 场域全套（数据/烘焙/工具/可视化） | M1（与路网并行可） |
| M7 | Modules/06 配置表 + 编辑器 | M0 |
| M8 | Modules/07 落位引擎（降级缩放路径先行）→ A-3 全图语义验收 **+ 08 的 adopt/负约束/锁区（局部反悔三机制）与冲突条目** | M5,M6,M7 |
| M9 | **ADR-0001 桥接（用户批准：与 M9 同期做）**：core 加接口（含批量成员）→ stretch 注册 → citymap 消费 + 「重建降级建筑高度」迁移（H-23）；取锁前实时复核 | M8 |
| M10 | Services/事件装配、Sample 小城、**Unity Recorder 录演示视频（v6 批准的 Sample-only 外部依赖）**、Showcase 收口、性能预算实测 **+ 08 历史面板/检查点/影响预览收口** | M9 |
| **M11（v6 新增）** | **10 号模块：`BuildingRecord` 数据层 + `ViewportRealizer` 工作集 + `CitySnapshot` 三形态导出（scene 切块/JSON/tile）+ 8km 档量化与 region 流式**。依赖 M8（记录语义）与 M1（region 索引）；**8km 预算达标是本模块的验收线** | M8,M10 |

## 11. 遗留开放问题（剩余默认见各模块）

### 11.1 已由用户决策关闭（v6）
- ~~Q-5 城市尺度~~ → **8km+ / 10 万栋以上**（触发 D-25/D-26/D-27，新增 Modules/10）。
- ~~Q-10 演示视频外部包~~ → **允许 Unity Recorder，仅 Sample 用途**（D-29，禁入生成内核与 Runtime 依赖）。
- ~~消费形态~~ → **可选导出**：scene 切块 / 引擎无关数据 / 2D tile 三形态（D-27，10 §5）；core 接口改动推迟到 M9（D-28）。

### 11.2 已关闭（v7，用户裁决）
- ~~**Q-11**~~ → **「对接时补适配器」**：本包内部通道方案定性为**基准（canonical）格式**并先行冻结；外部瓦片规范
  （Tiled/LDtk/自研 tileset）出现后只在 `ExportAdapter` 层做映射，**禁止改基准通道语义与编号**；
  每个适配器须自带字段对照表 + 往返用例（外部格式解析回索引图与基准逐像素一致），无用例不合并。
  代价（一次返工）已由用户接受，**不阻塞 M11**。（A-44）
- ~~**Q-12**~~ → **切块粒度支持自定义，默认 1024m**（`chunkSizeM`，可选 256/512/1024/2048，须为 `cellSize` 整数倍）。
  落地时新发现 **H-25**：把写死的值改成可配，必须重新回答它原先隐式承担的全部归属规则——
  `chunkSizeM < region` 时一块地形来自多个 region 拼接；横贯全城的河线/主干路会整条消失在其他块。
  处置（10 §5.1）：导出地形**按切块边界独立重切 mesh**（不复用 region 视口 mesh）+ 导出报告列"贴边界 ±2m 对象清单"
  + `splitLongStrokes`（默认关）供按块裁断交付。并澄清概念正交：`region`=编辑期工作集单位，
  `scene chunk`=交付期单位，**默认值下二者重合纯属巧合**。（A-38 改写、A-43 新增）

### 11.3 保持默认即可（可调、无需拍板）
- Q-7 门槛放宽轮数 3、系数 0.7；Q-8 农业覆盖率 15%；Q-9 场域高度带首版无限（R-08）。

## 12. 风险（v2 增量；模块内风险见各文档）

| 编号 | 风险 | 状态/缓解 |
|---|---|---|
| R-01 | ~~与 stretch 的依赖方向冲突~~ | **已解**：D-07 桥接方案；接口签名最终冻结于 ADR-0001 |
| R-11 | 桥接实施触碰 core+stretch，stretch 正被 `ui-fix` 持锁 | §6.4 申报流程；M9 排在功能闭环后，风险窗口可控 |
| R-12 | S1a↔S4 的阶段循环依赖 | 已解：S1a/S1b 拆分（§3.2），非迭代单次约束 |
| R-13 | IStretchService 语义不匹配（stretch 的段模型 vs citymap 的目标高度） | 接口含 `GetSegmentSpan/SetRepeat`，换算放 citymap 侧；M9 联调时允许扩接口（core 变更走同 ADR） |
| R-14 | undo 后派生层滞后（Unity undo 只回滚意图层） | 08 §5.1 `UndoHistoryTracker` 版本差感知 + 250ms 合并窗口；A-17 断言 |
| R-15 | 上游改动波及固化对象（自动删挪会毁掉用户确认的成果） | 08 §3.4 冲突矩阵：永不自动删挪、转待裁决条目；冲突未清零阻断导出 |
| R-02~R-10 | 见 v1 清单（掏空/断链/超时/死循环/Safe Mode/域重载/调参/展板视频） | 各模块文档已带对应缓解 |
