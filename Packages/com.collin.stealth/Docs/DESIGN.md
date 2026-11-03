# DESIGN — com.collin.stealth

> 状态：**已评审**（2026-11-03 用户拍板 Q-A1~Q-A5，决议见 §9；本包 M0 只落骨架与本文档，未写任何功能代码）。
> 定位：A 包 = **最小潜行玩法**。设计辅助工具（安全岛 / T 周期回溯 / 分层查看 / 路线反推）
> 属于独立的 B 包，B 依赖 A 的对外契约（§4），A 不依赖 B、可独立交付。

## 1. 目标与非目标

### 1.1 解决什么问题
提供开箱可跑的潜行玩法最小集，让关卡/玩法设计师拿到场景就能验证「NPC 看得见谁、什么时候被看见」，
并且这套数据天然**可被设计工具消费**（B 包靠它做预览与反推）。

1. **路线**：一条巡逻路线 = 有序路点 + 每段速度 + 到点停留 + 朝向特性 + 循环模式。
2. **路点控制 NPC 活动**：单个 NPC 沿路线行进、到点停顿、循环/往复；支持起步相位与速度倍数（实例级差异）。
3. **可配置视野**：水平视角、视距、视线高度、俯仰角、遮挡层；参数为共享资产，实例可覆盖。
4. **察觉判定**：视野锥 + 遮挡射线 → 玩家被看见 → 察觉度累积 → 状态流转（巡逻→疑惑→警觉→搜索）。
5. **玩家移动控制器（拆箱即用，D-13）**：包内直接提供 `PlayerStealthController` + 预制件，
   拖进场景即可用（WASD 走、Ctrl 蹲伏），不需用户自建输入资产。
6. **示例场景**：包内 `Sample/` 提供白盒保底演示（用户要求的「1 的示例保底方案」）。

### 1.2 明确不做什么
- **不做**场景设计辅助功能（安全岛、分层查看、时间回溯、反推路线）——全部属于 B 包。
- **不做**通用 AI（行为树/GOAP/Utility）、寻路求解（只沿既有路线走，不自寻路）、战斗、掩体系统、尸体搬运。
- **不做**听觉/声音侦测（D-12）：本包是最小可用子集，声源半径与 distraction 实体移出范围，
  掩码位 `DistractionIgnore` 保留为预留位不启用。
- **不做**玩家视角以外的玩家能力（攀爬、掩体判定、道具、UI 面板）。
- **不含** NavMesh 烘焙工具（Unity 自带）。
- **不依赖** `com.unity.ai.navigation`：实测工程 manifest 未安装该包；其底层为 HAVOK 付费方案，
  且逐帧积分会破坏 §3.1 的确定性要求。
- **不依赖** B 包：A 必须能脱离任何设计工具独立运行、独立交付。
- **不提供**运行时存档/回放系统。

## 2. 约束
- Unity 6000.6.7f1；URP 17.6.0；只用内置模块（physics / jsonserialize）+ `com.unity.inputsystem`（玩家控制器需要，已进 package.json 与 Runtime asmdef）。
- **输入后端实测约束**：本工程 `ProjectSettings/ProjectSettings.asset:898` 为 `activeInputHandler: 1`
  （仅新 Input System）。旧 `UnityEngine.Input.*` 在本工程运行时不可用，
  移动控制器必须走 `com.unity.inputsystem`（已在 manifest，1.20.1）的 `Keyboard.current` 轮询；
  为兼容外部工程另用 `ENABLE_INPUT_SYSTEM / ENABLE_LEGACY_INPUT_MANAGER` 条件编译双路（D-13）。
- 依赖方向：`com.collin.stealth` → `com.collin.core`（§5；asmdef 与 package.json 均已声明）。
- Runtime 程序集禁止引用 UnityEditor（校验器 `runtime-purity` 强制）。
- 编辑器增强全部走 `com.collin.stealth.Editor.asmdef`；`validateReferences: true` 不得关闭。
- 遵守 §0.1 三原则：所见即所得 / MVC 解耦 / 双侧友好。
- 性能预算（设计点 50 NPC × 60 FPS）：位置求值 O(1)/NPC；视野判定走 §3.3 四层管线 + 逐层帧预算，
  禁止每帧对全部 NPC 做全向射线。

## 3. 方案概述

### 3.1 最高杠杆的决定：**NPC 位姿是「时间的纯函数」**

```
PatrolPose EvaluatePose(PatrolRouteData route, float t)   // 确定性：同 (route, t) ⇒ 同 (位置, 朝向)
```

实现：路线预计算**分段累计长度表**，`t → 弧长 → 定位到段 → 段内线性插值 + 朝向缓动`。
不使用 NavMeshAgent、不做 transform 积分。

为什么（这一条决定两个包能否解耦）：
1. **B 包的「T 周期滑动条」必须在编辑态（非 Play）实时求值任意 t 的 NPC 位置**。
   若位置来自逐帧积分，Play 外根本不会动、且带帧率噪声不可复现，回溯预览直接不可用。
2. A 包运行时复用同一条求值路径（由 `StealthClock` 驱动 `t`），做到**编辑态预览与 Play 态行为同源**
   （§0.1 原则 3：引擎侧无需重复配置）。
3. 验收友好：ACCEPTANCE 可断言「给定 t=3.7 时 NPC 必在 P 点」。

代价（见 §6 R-1）：纯函数不含「被玩家挡路时绕行」这类非确定性行为，由 §3.6 的事件叠加层处理。

### 3.2 组件与程序集

| 组件 | 程序集 | 类型 | 职责 |
|---|---|---|---|
| `PatrolRoute` | Runtime | ScriptableObject（**Model**） | 路点数组（**相对路线根的局部坐标**，D-09）、段速度/停留/朝向权重、循环模式、累计长度表；可被 N 个 NPC 复用 |
| `PatrolRouteRoot` | Runtime | MonoBehaviour | 路线根：持有一个 `PatrolRoute` 引用 + 自身 transform 提供世界系；移动根即整条路线平移（D-09） |
| `PatrolPose` | Runtime | struct | `EvaluatePose` 输出：局部位姿、世界位姿、当前段索引、段内进度、是否处于停留 |
| `PatrolAgent` | Runtime | MonoBehaviour（实例） | 引用 `PatrolRouteRoot` + **实例级差异数据**（相位偏移、速度倍数、当前状态）——§0.1 原则 2 |
| `VisionProfile` | Runtime | ScriptableObject（**Model**） | 视野参数：水平视角、视距、视线高度、俯仰角、遮挡 LayerMask、光照敏感度（听觉参数按 D-12 移出） |
| `VisionVolume` | Runtime | MonoBehaviour（实例） | 引用 `VisionProfile` + 实例级覆盖（如高哨兵视距加倍） |
| `PlayerStealthController` | Runtime | MonoBehaviour（实例） | 拆箱即用的玩家移动（WASD + Ctrl 蹲伏）与 `isCrouching` 上报（D-13） |
| `StealthPlayer` | 预制件 | prefab（Sample 资产） | Capsule + CharacterController + `PlayerStealthController` + `DetectionState`，拖进场景即可用 |
| `StealthGuard` | 预制件 | prefab（Sample 资产） | Capsule + `PatrolAgent` + `VisionVolume` + 可见身体；携带自己的路线根，与玩家预制件成对提供 |
| `DetectionState` | Runtime | MonoBehaviour（挂被侦测目标） | 察觉深度、累积值、`OnDetected` 事件 |
| `AwarenessModel` | Runtime | 类 | 察觉度的累积/衰减曲线（可配置无威胁时回落速度） |
| `StealthMask` | Runtime | flags enum | §3.3 判定层掩码位分配 |
| `StealthClock` | Runtime | MonoBehaviour | 全局时间索引；`Auto`（每帧自增）/ `Manual`（外部写 t）两种模式 |
| `DetectorRegistry` | Runtime | ScriptableObject | 场景内 `VisionVolume` / `PatrolAgent` / `DetectionState` 注册表（供查询与 B 包枚举） |

Editor 程序集（A 自用最小编辑手段，**不含设计辅助功能**）：
`PatrolRouteInspector`（Inspector 上直接拖路点、加删点、改速度，改动即时生效）·
`Handles` 绘制（路线折线 + 路点手柄 + 视野扇形）· `Gizmos`（视野锥半透明）· `Context Menu`（一键挂 NPC 组件）。

### 3.3 察觉判定：四层管线，逐层可关

| 层 | 判据 | 成本 | 备注 |
|---|---|---|---|
| L1 距离 | 球检（`sqrMagnitude < range²`） | 极廉 | 每帧全量 |
| L2 视角 | 水平夹角 < 视场/2（+ 俯仰锥） | 廉（2 次点积） | 每帧全量 |
| L3 视线 | `RaycastNonAlloc` + `StealthMask` 过滤 | 高 | **预算制**：`raycastBudgetPerFrame`、每 NPC 每 N 帧一次、错峰 |
| L4 遮蔽修正 | 蹲伏/阴影/掩体 | 廉 | 只改累积速率，不改可见性判定（声音类注意力转移按 D-12 移出，保留预留位） |

**四层必须可分层取结果**（`QueryOptions` 掩码：`DistanceOnly` / `ConeOnly` / `LineOfSight`）——
B 包的「N1 NPC 视野可达区域」分层渲染要按层可视化，
且 **B 不自己实现第二套判定逻辑**，只调 A 的函数（同源原则，避免两套逻辑算出不同结果）。

`StealthMask` 位分配（A 定义，B 消费）：
`None=0` · `LineOfSight=1`（几何遮挡）· `CoverOrCrouch=2`（遮蔽）·
`SafeIsland=8`（安全区，数据由 B 提供、A 只消费）·
`DistractionIgnore=4` 与 `16/32/64` 为**预留位不启用**（D-12 已把听觉移出最小集，留位是为避免将来加听觉时重排位序）。

### 3.4 察觉状态机（保底玩法闭环）

`Patrol → Suspicious → Alerted → Search → Patrol`
- Suspicious：察觉度越过 `suspicionThreshold`，走向最后可见点。
- Alerted：越过 `alertThreshold`，通知半径内其他 NPC（走 core 的 `EventChannel`）。
- Search：按 `PatrolRoute` 派生的搜索点遍历（保底实现 = 原路线 + 可疑点邻域；不做寻路）。
- 衰减：L4 判定 + 脱离视野后按 `AwarenessModel` 回落。

玩家反馈：察觉度 UI（由示例场景/B 的演示层承担，不进 Runtime 逻辑）· 警戒事件广播 · 视野锥颜色（绿/黄/红）。

### 3.5 数据流（示例场景 = 保底方案）

```
Sample/Scenes/StealthSample.unity
 └─ 关卡根（地面/掩体白盒 + Layer 标注）
    ├─ StealthGuard 预制件实例 ×N
    │    ├─ PatrolRouteRoot（局部坐标 → 世界系；移动它=整条路线一起动，D-09）
    │    │    └─ PatrolRoute "WarehouseLoop" (SO，共享 Model)
    │    └─ PatrolAgent（实例差异：phaseOffset / speedMul）+ VisionVolume（→ VisionProfile SO）
    ├─ DetectorRegistry (SO)
    └─ StealthPlayer 预制件实例（PlayerStealthController + DetectionState）

每帧：StealthClock(t) → PatrolAgent 取 PatrolPose（局部求值 → 经根转世界）→ 写 transform
      → VisionVolume 逐层判定(L1→L2→L3→L4) → AwarenessModel → DetectionState
      → 状态机 → EventChannel.Raise(...)
```

**路点坐标约定（D-09 定案）**：`PatrolRoute` 存**相对路线根的局部坐标**，
`PatrolRouteRoot` 的 transform 提供世界系。这样 SO 资产可跨场景复用、
路线整体搬移只需拖动一个根物体（直观性由 Editor 手柄保证：拖路点手柄时按世界系显示、写回时换算为局部）。
Editor 另提供「从当前世界位置反推路点」按钮（把摆放好的路线烘焙回局部坐标）。

示例场景刻意保持**白盒**（Cube 掩体 + `StealthPlayer` 预制件 + 若干 `StealthGuard` 预制件）：
它的作用是证明「A 包独立可跑」，不承担美术演示。
**拆箱即用（D-13）**：`Sample/Prefabs/StealthPlayer.prefab` 内含 Capsule + CharacterController +
`PlayerStealthController` + `DetectionState`；用户把它拖进任意场景即可用 WASD 控制、Ctrl 蹲伏，
无需自建 InputAction 资产（控制器内部优先轮询 `Keyboard.current`，
并在 `ENABLE_LEGACY_INPUT_MANAGER` 下回退 `Input.*`，两路都不可用时给出一次明确 warning 而非静默失灵）。

演示视频（§6 两段硬性要求，顺序固定）：
①怎么用——真实编辑器操作：把 `PatrolAgent` 加到场景物体、Inspector 里拖路点划边界、拖动速度/视角滑条，
画面须能看到被操作的控件与鼠标动作；
②用了之后的效果——Scene 视图对参数改动的即时响应，含改前/改后对照（含玩家走进视野锥被察觉的前后对比）。

### 3.6 与寻路引擎的接缝（可选，默认关）

定义 `IPathBridge { bool TrySnap(Vector3 target, out Vector3 navPoint); }`，
默认实现 `NullPathBridge`（走原始位置）。工程若接入导航方案，另建可选 asmdef
`com.collin.stealth.Navigation` 实现该接口并注册到 `Services`。
**A 包本体不引用任何导航类型**——避免与「HAVOK 付费 + 帧噪声」耦合。
事件叠加（被玩家撞到需绕行、上下楼梯）通过 `IPatrolModifier`（`t → Vector3 delta`）挂到求值结果上。

## 4. 跨包接口（A 对外暴露的全部面，B 只能通过这些消费）

### 4.1 查询函数（纯函数，编辑态/运行态同一条路径）
- `PatrolPose PatrolRoute.EvaluatePose(float t)`（局部位姿）；`PatrolRouteRoot.ToWorld(PatrolPose)`（D-09）
- `float PatrolRoute.TotalCycle`（= T 周期，B 滑动条量程）
- `bool VisionVolume.CanSee(Vector3 worldPoint, QueryOptions opt)`
- `VisionFrustum VisionVolume.EvaluateFrustum(float t)`（B 绘制「N1 可达区域」）
- `DetectorRegistry.Count / GetActor(i)`（B 枚举场景内 NPC）

### 4.2 EventChannel（core 的 `EventChannel<TPayload>`）
- `EC_Stealth_Detected`（payload：察觉事件 DTO）
- `EC_Stealth_AlarmRaised`
- `EC_Stealth_TimeIndex`（`StealthClock` 广播 t；B 用它对齐自己的 scrub）
- `EC_Stealth_Scrub`（B → A：`ManualTime` 模式下由 B 驱动 NPC 位置，用于编辑态回溯预览）

### 4.3 Services 接口（A 注册实现，B 按接口取用）——**M0.5 实装版**
契约落在 `com.collin.core` 的 `Runtime/StealthContracts/`，A 侧实现为 `Runtime/StealthWorld.cs`：
```csharp
// 已注册（A 提供）
IPatrolWorld        { IReadOnlyList<StealthActorInfo> Actors; float MaxCycleTime; StealthClockDto ClockState;
                      StealthPoseDto PoseAt(int actorId, float t); bool TryPoseAtWorld(...);
                      void BuildTrajectory(int actorId, float spacing, List<StealthTrajectorySampleDto>);
                      bool TryGetWaypoints(...); bool TrySetWaypoint(...); bool TrySetRouteTuning(...); }
IStealthScrubDriver { void ScrubTo(float t); void ResumeAuto(); bool IsManual; event Action Changed; }

// 已定义、暂未注册（M2/M3 实现后由 A 注册；B 现在取用会走降级路径）
IVisionWorld        { IReadOnlyList<StealthVisionInfo> Volumes; bool CanSee(...); bool CanSeeAt(...); int CountSeers(...); }
IDetectionWorld     { float SuspicionOf(int targetId); StealthAlertStage StageOf(int targetId); void ResetAll(); }

// 方向相反：实现方 = B（设计辅助台），消费方 = A（判定 L4 时查询安全岛）
ISafeIslandProvider { int Count; bool TryGetBounds(int i, out Bounds, out string); bool Contains(Vector3); }
```
注册入口两处（缺一即失效）：Play 态走 `StealthWorld.RegisterOnPlay()`（`RuntimeInitializeOnLoadMethod`），
**编辑态走 `Editor/StealthEditorBootstrap.cs` 的 `[InitializeOnLoad]`**——
因为生命周期回调在编辑态不执行（实测，见 ACCEPTANCE F-5），若只有前者，B 包的滑动条在编辑态取不到服务。

与草案的三处定稿差异（均为实现轮自查修正）：
1. `PoseAt` 返回 core 的 `StealthPoseDto` 而非 A 的 `PatrolPose`（草案签名会让 B 必须引用 A，方案 P 当场失效）；
2. `StealthPoseDto` 增加 `valid` 位：`default` 的 `legIndex` 恰为 0，只判 `legIndex >= 0`
   会把「无数据」误读成「位于第 0 段」；
3. `StealthMask` / `QueryOptions` 不进 core，跨包只用 `StealthQueryLayers`（三档），
   A 内部的遮蔽修正与预留位不外露，避免 B 依赖 A 的内部枚举。

### 4.4 B→A 的依赖形式（**已定案：方案 P**，2026-11-03 用户拍板 Q-A1）
AGENTS.md §5 规定「feature 包之间禁止引用（asmdef 层面切断）」，B（feature）不得在 asmdef 层引用 A。
定案执行方式：
- A 持有全部**数据与求值实现**（`PatrolRoute.EvaluatePose` / `VisionVolume.CanSee` / 四层管线）；
- core 只新增**接口与纯数据 DTO**（`IPatrolWorld` / `IVisionWorld` / `IDetectionWorld` +
  `ActorInfo` / `VisionInfo` / `PatrolPoseDto` 等），**不放任何玩法逻辑**；
- B 的 asmdef 只引用 `com.collin.core`，通过 `Services.Get<IPatrolWorld>()` 取能力；
- A 在 `StealthWorldBootstrap` 注册实现；B 在取不到服务时优雅降级（面板显示「未接入玩法包」）。

为此 **core 需要一次小改动**（新增上述接口与 DTO 文件）。core 当前由他人/他会话持锁，
本包实现 M1 前需与用户确认 core 的改动窗口；改动性质为「只增不改」，不影响既有包。

DTO 归属的边界：接口方法签名里出现的类型必须全部来自 core（不能出现 A 的具体类型），
否则 B 仍需引用 A 才能编译——这是 P 方案的硬约束，`00-Contract.md`（M6）须逐签名核验。

### 4.5 Showcase.json
`name`=Collin Stealth 潜行玩法最小集 · `category`=玩法组件 · `order`=30（citymap=20，B 包拟 order=31）
· `video`=`ShowcaseMedia/stealth_demo.mp4`（M5 前补齐，两段式要求见 §3.5）。
## 5. 决策记录

| 编号 | 决策 | 备选 | 理由 |
|---|---|---|---|
| D-01 | NPC 位姿为时间纯函数（弧长查找表） | NavMeshAgent 逐帧移动 / transform 积分 | 编辑态可预览 + 可复现 + B 包 T 周期滑动条依赖此；工程未装导航包 |
| D-02 | 路线=SO(Model)，Agent=场景组件(实例) | 全写在场景组件里 | §0.1 原则 2：同资产复用时实例差异（相位/倍数）禁止进配置 |
| D-03 | 四层判定管线 + 掩码 + 帧预算错峰 | 每帧全量射线 | 50 NPC × 60 FPS 预算；B 需要分层可视化 |
| D-04 | 视野锥用「扇形 + 高度」解析几何近似 | 真实视锥网格 | 玩法够用且便宜；B 侧要精确网格可自行细分 |
| D-05 | 察觉分四档 + 连续累积值 | 只有「看见/没看见」布尔 | 潜行手感靠累积与衰减；B 可按档分层显示 |
| D-06 | `IPathBridge` 默认 Null 实现 | A 直接依赖导航包 | 避免付费依赖；保留后续接入接缝 |
| D-07 | A 的 Editor 只含 Inspector/Handles/Gizmos | 把设计辅助也放 A | 用户特性 2 要求解耦；A 可独立交付，B 可被替换 |
| D-08 | 示例场景白盒、不用真实美术资产 | 用 Poly Haven CC0 资产 | 示例只证明「独立可跑」；真实资产会掩盖玩法问题（stretch 曾因此暴露 Builder 缺陷） |
| D-09 | **路点存相对 `PatrolRouteRoot` 的局部坐标，路线根做成预制件**（Q-A2 定案） | 直接存世界坐标 | 用户要求「打成预制件，内部相对坐标，可以一起动、也保证直观性」：SO 可跨场景复用、拖一个根即整条路线搬移；直观性由 Editor 世界系手柄 + 「反推路点」按钮补回 |
| D-10 | **B 依赖 A 走方案 P**：接口/DTO 上提 core，B 的 asmdef 不引用 A（Q-A1 定案） | 修订 §5 允许同族引用 | 用户拍板；保住 feature 零耦合，且 A 缺席时 B 可降级 |
| D-11 | **位置走精确折线、朝向单独缓动**（Q-A3 定案） | 位置与朝向一同 SmoothDamp | 位置精确使 B 的路线反推可解算、轨迹可预测；朝向缓动保留观感 |
| D-12 | **不做听觉/声音侦测**（Q-A4 定案） | 纳入声源半径 + distraction 实体 | 用户判定本包是最小可用子集；`StealthMask` 对应位保留为预留位不启用 |
| D-13 | **包内提供玩家移动控制器与预制件，拆箱即用**（Q-A5 定案，推翻原「只留 DetectionState」草案） | 移动交给外部工程 | 用户要求 A 包开箱可玩；控制器优先 `Keyboard.current`，条件编译回退 `Input.*`，两路皆不可用时一次性 warning |

## 6. 风险

| 编号 | 风险 | 影响 | 缓解 |
|---|---|---|---|
| R-1 | 纯函数位置 vs 实时寻路（绕开玩家/上下台阶）语义冲突 | 演示不自然 | 基线走纯函数，事件走 `IPatrolModifier` 叠加偏移；B 只预览基线，ACCEPTANCE 写明边界 |
| R-2 | 无 NavMesh 导致 NPC 穿模/悬空 | 示例观感 | 示例场景设计成全平白盒；接缝 D-06 |
| R-3 | 编辑态与 Play 态两套判定结果不一致 | 设计工具失真 | 判定函数只有一条实现（Runtime 内），B 全部走函数调用，禁止 B 自研 |
| R-4 | 每帧 N 条射线在大场景掉帧 | 性能 | 错峰预算 + 距离早退 + L3 可按 `VisionVolume` 粒度开关 |
| R-5 | `Services` 域重载后清空 | B 取不到服务 | A 初始化入口注册 + B 面板对 null 优雅降级 |
| R-6 | 视线射线命中 NPC 自身/己方单位 | 误判 | 命中过滤：`ownerGameObject` 及其子节点、Layer 黑名单 |
| R-7 | ~~路点 SO 存世界坐标导致换场景即错~~ | — | **已由 D-09 消除**：路线存相对路线根的局部坐标，SO 与场景解耦 |
| R-10 | 本工程 `activeInputHandler: 1`（仅新输入系统），若控制器只写 `Input.*` 会静默失灵 | 拆箱即用变成拆箱即坏 | 优先 `Keyboard.current` + 条件编译双路；不可用时输出一次性明确 warning（实测依据见 §2） |
| R-11 | 方案 P 要求 core 新增接口/DTO，而 core 正被他人持锁 | M1 开工受阻 | 与用户确认 core 改动窗口；改动为「只增不改」；DTO 必须自成闭环（接口签名不得出现 A 的具体类型，M6 逐签名核验） |
| R-8 | 演示视频若标为示意而非实录，按 §6 视为该包不合格 | 交付卡门禁 | M5 实机录屏；受环境限制则在 ACCEPTANCE 说明原因 |
| R-9 | 新建内嵌包需 `package resolve` 才被包管理器识别 | 开工首日踩空 | M0 完成后立即执行并复查校验器 |

## 7. 验收草案（正式条目见 Docs/ACCEPTANCE.md，每条须带证据）
1. `EvaluatePose` 对同一 `(route, t)` 连续 1000 次调用结果逐位一致。
2. `TotalCycle` == 各段（长度/速度 + 停留）之和，误差 < 1e-4。
3. `t = 0 / T/4 / T/2 / 3T/4` 四相位下 NPC 位置与设计路线关键点偏差 < 0.05。
4. 目标在锥内且无遮挡 ⇒ `CanSee=true`；旋转目标越过视角边界 ⇒ 变 `false`。
5. 在目标与 NPC 间放置 Cube（Layer=LineOfSight）⇒ `CanSee` 转 `false`，移开恢复。
6. 持续可见 n 秒后越过 `alertThreshold`；脱离视野后 m 秒内衰减归零。
7. 一个 NPC 进入 Alerted ⇒ 半径内另一 NPC 收到 `EC_Stealth_AlarmRaised` 并转 Search。
8. **编辑态预览自证**：非 Play 状态下以 `ManualTime` 把 t 从 0 扫到 T，
   Scene 视图 NPC 预览标记连续移动，且与 Play 态轨迹重合（误差 < 0.1）。
9. `runtime-purity` 绿灯（Runtime 无 UnityEditor 引用）。
10. 交锁门禁三项：`recompile_status.failed=false`；`console clear` 后重跑本会话全部验证动作，
    `console status` 的 `groundTruth` 满足 `consoleErrors==0 且 consoleWarnings==0`；`python Tools/verify_project.py` 绿灯。
11. 50 NPC（10 路线 × 5 实例、L3 开启、默认错峰预算）Play 态 ≥ 55 FPS（记录采样帧数与设备）。
12. `Showcase.json` 被展板枚举到，category/order 生效；演示视频两段齐全。
13. 拖入 `StealthPlayer` 预制件到**空场景**按 Play：WASD 可移动、Ctrl 蹲伏（不添加任何输入资产/场景配置）——拆箱即用（D-13）。
14. 只移动 `PatrolRouteRoot` 一个物体，整条路线（全部路点 + N 个 NPC 轨迹）一起平移，位移前后轨迹形状一致（D-09）。

## 8. 里程碑

| | 内容 | 门禁 |
|---|---|---|
| M0 | 建包骨架：`package.json`、Runtime/Editor asmdef、Docs 四件套、`Showcase.json`、`Sample/`；取锁；`package resolve` | 校验器包结构检查通过（**已达成**） |
| M0.5 | 与用户确认 core 改动窗口，向 core 追加 `IPatrolWorld`/`IVisionWorld`/`IDetectionWorld` + DTO（只增不改，R-11） | **已达成**：core 新增 `Runtime/StealthContracts/` 3 文件；A 侧实现 `StealthWorld`（`IPatrolWorld` + `IStealthScrubDriver`）并经 `Services` 注册；消费方视角 16 条契约用例全绿 |
| M1 | `PatrolRoute`（局部坐标）+ `PatrolRouteRoot` + `PatrolPose.EvaluatePose` + `StealthClock`；EditMode 测试（验收 1-3） | **已达成**：24 条 EditMode 用例全绿，全工程 108/108 通过；交锁门禁本包三项全绿 |
| M2 | `VisionProfile` + `VisionVolume` + 四层管线 + `StealthMask` + `DetectionState`（验收 4-6） | 同上 |
| M3 | 状态机 + 警戒广播 + `IPathBridge`（验收 7） | 同上 |
| M4 | Editor 侧 Inspector/Handles/Gizmos（世界系手柄 + 反推路点）+ `ManualTime` 编辑态预览（验收 8-9、11） | 同上 |
| M5 | `PlayerStealthController` + `StealthPlayer`/`StealthGuard` 预制件（D-09/D-13）+ `Sample/Scenes/StealthSample.unity` 白盒场景 + `ShowcaseMedia` 实录视频 + 展板接入（验收 12-14） | 三项门禁全绿 |
| M6 | 冻结 §4 对外契约并写入 `Docs/Modules/00-Contract.md`（逐签名核验 DTO 归属），交付给 B 包消费 | 契约冻结需用户确认 |

## 9. 决议（2026-11-03 用户拍板，已折入正文）

| 编号 | 问题 | 定案 | 落点 |
|---|---|---|---|
| Q-A1 | B 依赖 A 的形式 | **方案 P**：接口/DTO 上提 core，B 的 asmdef 不引用 A | §4.4、D-10、R-11、M0.5 |
| Q-A2 | 路点坐标与直观性 | **打成预制件，内部用相对坐标**——可整体搬移、又直观 | §3.5 坐标约定、D-09、验收 14 |
| Q-A3 | 转向方式 | **位置精确折线 + 朝向单独缓动** | D-11 |
| Q-A4 | 是否纳入听觉 | **不纳入**（最小可用子集），掩码位保留为预留位 | §1.2、§3.3、D-12 |
| Q-A5 | 玩家移动控制器 | **包内提供，拆箱即用**（`PlayerStealthController` + `StealthPlayer` 预制件） | §1.1-5、§3.5、D-13、R-10、验收 13 |
