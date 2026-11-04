# CHANGELOG — com.collin.stealth

格式：Keep a Changelog（https://keepachangelog.com）；版本号：semver。

## [Unreleased]

> **版本号故意仍为 0.4.0**：§6 演示视频未录制，该包演示按 §6 判不合格，提版本会谎报交付边界。
> 以下改动已入库，待视频补齐后一并收版。

### Added（M5 部分：预制件沉淀 + 拆箱即用验证）
- `Editor/StealthPrefabBaker.cs`：把示例场景的玩家与哨兵单元沉淀为
  `Sample/Prefabs/StealthPlayer.prefab` / `StealthGuard.prefab`，内置**引用完整性自检**
  （Body 三件套、路线根引用、路线/视野资产非空、T 周期 > 0、沿周期求值有位移）。
  菜单 `Tools ▸ Collin ▸ Stealth ▸ 沉淀示例预制件（玩家+哨兵）`。
- `Editor/StealthPrefabVerifier.cs`：**内存临时场景只放预制件 + 一个时钟**（不建地面、不建掩体、
  不手写资产）验证拆箱即用；检查 `PrefabInstanceStatus.Connected` 与跨包枚举**新增数量差值**；
  验完 `CloseScene` 丢弃、不落盘。菜单 `验证预制件自洽（内存临时场景）`。
- `Sample/README.md`：由 M0 规划改写为实际落位 + 三条使用路径。
- 实测：两个预制件已落盘（9393 B / 7766 B），GUID 级引用比对全通过，
  验证日志 `actors新增=1 cycle=28.00s 结论=通过`；门禁三项全绿（编译 0 错、
  归零后 `consoleErrors:0 / consoleWarnings:0`、全量 210/210、校验器绿灯）。
- **M5 余项（未做）**：`ShowcaseMedia` 两段式实录视频 + 展板接入核验。
  录屏管道已确认可用（ffmpeg 具 `gdigrab`、Unity 主窗口可按标题定位），但尚未拍摄。
- M6：冻结对外契约 `Docs/Modules/00-Contract.md`（逐签名核验 DTO 全部来自 core），交付给 B 包消费。
- 待补证据：性能验收（50 NPC ≥ 55 FPS）；编辑态回溯「拖滑块 NPC 连续移动」的截图（条目 71 部分通过）。

### Fixed（M5 实现轮自查，均为自造）
- **示例场景凭空消失**：原先 `DeleteAsset` 场景文件后再向同一路径 `SaveScene`。Unity 既不允许覆盖
  同路径的已打开场景、也不允许卸载最后一个已加载场景 ⇒ 旧文件被删、新文件没写出来
  （已入库场景一度需 `git checkout` 恢复）。改为**打开目标场景 → 清空根物体 → 原地 `SaveScene`**。
- **编辑态材质泄漏刷 3 条 error**（破 §1.6b 归零）：上色用 `renderer.material.color`；
  改为显式 `new Material(sharedMaterials[0])` 写回 `sharedMaterials`。
- **自检断言写错**：断言「哨兵预制件根上应有 `PatrolRouteRoot`」，而该组件按分层挂在 `RouteRoot`
  子物体上 ⇒ 必失败并报自造 error；改由专项 `VerifyBody` 负责。
- **计数断言方式错**：跨包枚举走全工程搜索，此刻还加载着其他会话的场景 ⇒ 绝对值既会误报也可能凑数假绿；
  改为实例化前后的**差值**。
- 生成器加两道保护：他人场景有未保存改动时中止并日志提示（不弹模态窗）；存盘失败输出明确 error。

### 环境事实（新增）
- 编辑器处于 Play 模式时执行 `test run`：TestRunner 因无法建/还原场景刷出一批
  `This cannot be used during play mode` error，**且 `Summary.Total` 返回 0**——不是「工程里没有测试」。
  跑门禁前先 `editor status` 确认 `playMode: stopped`。

### Changed
- 2026-11-03 用户拍板 Q-A1~Q-A5 后调整设计：新增 `com.unity.inputsystem` 依赖；路点改为相对坐标 + 预制件承载；**听觉/声音侦测移出本包范围**（最小可用子集），`StealthMask` 对应位改为预留位不启用；玩家移动控制器由「交给外部工程」改为「包内自带、拆箱即用」。

### Added（成品质量修正：用户三条实机反馈，session stealth-fix-2026-11-04）
- `ThirdPersonRig.cs`：操控数学纯函数层——`MoveDirection`（**相机相对**四向：A/D=侧移不是转身）、
  `SmoothAngle/SmoothValue`（帧率无关的 `1-e^(-k·dt)` 阻尼）、`ClampPitch`（上下限填反自动交换）。
- `StealthStageVisual.cs`：档位配色与中文名统一口径（HUD 与地面投影共用，杜绝「HUD 说红地上画黄」）。
- `GroundVisionProjection.cs`：**与判定同源地面投影**（纯函数）：外缘 = sqrt(maxDist²−Δh²)、
  内缘 = 俯仰盲区 Δh/tan(半垂直角)，近身豁免盖住盲区时收合成连续扇形；遮挡射线裁边。
- `StealthGroundProjection.cs`：Play 态半透明填充（`Graphics.DrawMesh`，不污染预制件层级、不碰 `.material`），
  随哨兵档位变色；着色器 URP→Sprites→内置兜底、失败只警告一次。
- `StealthExitZone.cs`：撤离区（纯几何圆判定 + 驻留计时，不依赖触发器/Rigidbody）。
- `StealthGameRule.cs`：对局规则——抵达撤离区=通关，察觉度顶格持续=被抓；R 重开；Manual 预览态不推进。
- `Tests/Editor/GroundProjectionTests.cs`：**17 条**修正守卫（投影半径与判定逐位吻合、
  盲区环口收合、贴脸可见且仍受遮挡、A/D 严格反向、平滑帧率无关、撤离区驻留/零驻留/高度窗）。

### Changed
- **玩家操控语义重写**：镜头角与模型角彻底分离（旧实现共用 `_yaw` ⇒ 按 A/D 变转镜头）；
  相机位置与朝向共用同一阻尼并移到 `LateUpdate`（旧实现朝向瞬时、位置单独 Lerp ⇒ 拖拽抖动）；
  新增 Shift 跑。`PlayerStealthController` 公开 `CameraYaw/MoveYaw`。
- **判定点从脚底改为身体中部**：`DetectionState.BodyPoint`（默认 0.95m、蹲伏下移），
  `GuardBrain` 感知、自身感知、安全岛掩码与投影全部改用同一口径。
- 示例场景：玩家出生点 (0,-16)→(0,-22)（实测出生 30 秒即被抓获，演示不公平）、地面扩到 60×60m、
  北侧 (0,16) 放撤离区 + 信标柱、哨兵 Body 挂投影组件、`GuardNormal.asset` 显式配 proximityRadius=1.8
  与遮挡层全开；HUD 增加撤离指引/被抓倒计时/胜负横幅与 R 重开。

### Fixed
- 重叠丧失目标（越近越瞎）、A/D 转镜头、拖拽抖动、投影边界与实际不符（旧实现把俯仰角当射程上限，
  14m 视距只画 ~4.9m——正是用户截图里的现象）、无通关点，共五条实机反馈全部闭环。
- 探针算法错误（外缘半径按世界原点量得 22.08 假值）→ 按脚底量后 13.98 与期望**逐位吻合**（V3 探针）。

### 门禁（末次实测 17:19）
编译 `failed:false`；`console clear` 后重跑全部验证动作 `consoleErrors:0 / consoleWarnings:0`；
全量 **227/227**（Stealth **99**）；校验器 **结论: 绿灯**。Play 探针实测：
`outerR=13.98=expectR`、`D=(1,0) A=(-1,0)`、`overlapFoot=True overlapBody=True`、
`exits=1 rules=1 projections=2 outcome=Playing`。

## [0.4.0] - 2026-11-04
### Added
- **M3 哨兵决策与反应层**：
  - `IPatrolModifier`：叠加层接口（`Tick(dt, baselineWorldPosition, timeIndex)` / `GetOverlay` / `ResetOverlay`）。
    设计要点——状态机的反应**只能作为基线之上的可关断偏移**，绝不回流进巡逻基线，
    否则辅助台的 T 周期回溯显示的就是「上一局玩过的痕迹」（DESIGN R-1）。
  - `GuardBrain`：四档状态机（巡逻→疑惑→警觉→搜索→巡逻）、可疑点记忆、驻留/环绕计时、
    同伴通报（Alerted 时把半径内同伴拉进疑惑并同步可疑点）、察觉事件经 core 的
    `StealthDetectedChannel` 广播（载荷含 `timeIndex`，供辅助台钉回时间轴）。
  - `IPathBridge` + `NullPathBridge`：寻路接缝（DESIGN D-06）。默认不注册 = 纯直线叠加；
    工程接入导航时由可选子程序集实现并注册，**本包不引用任何导航类型**。
  - `PatrolAgent` 成为唯一位姿合成点：`ApplyBaselineAt(t, dt)` 先驱动叠加器决策、
    再合成偏移与注视朝向、最后写 transform；`dt <= 0` 时**完全跳过叠加层**（编辑态预览语义）。
  - `StealthWorld.ResetAll()` 一并复原哨兵反应状态（档位 + 叠加偏移）。
- **M4 编辑器工具与可玩成品**：
  - `PatrolRouteRootEditor`：Scene 视图直接拖路点（世界显示、局部写回，D-09）、
    Inspector 逐点改停留/段速、追加/删除、从子物体烘焙路点、T 周期与朝向指示线。
  - `StealthScrubWindow`（Tools ▸ Collin ▸ Stealth ▸ 巡逻回溯窗口）：T 周期滑动条实时推送 NPC 位姿，
    自动播放 / 回到 t=0 / 恢复 Auto，并列出各单位在该时刻的世界坐标与朝向。
    **编辑态（非 Play）可用**——这是 M1「位姿为时间纯函数」换来的核心能力。
  - `StealthGizmos`：巡逻折线（路线根常显）+ 视野扇形（选中时绘制），与判定共用同一数据源。
  - `PlayerStealthController`（**拆箱即用**，D-13）：WASD/方向键移动、Ctrl 蹲伏（切换或按住两种模式）、
    第三人称跟随相机（右键环绕、防穿墙收距）、蹲伏自动改胶囊高度并向 L4 上报曝光度。
    输入后端按 `ENABLE_INPUT_SYSTEM` / `ENABLE_LEGACY_INPUT_MANAGER` 三档自动选择，
    两路皆不可用时输出一次性明确 warning。
  - `StealthHud`：IMGUI 显示察觉度条、各哨兵档位与可疑点、操作提示（零依赖、零场景配置）。
  - `StealthSampleSceneBuilder`（Tools ▸ Collin ▸ Stealth ▸ 生成可玩示例场景）：
    一键产出 `Sample/Scenes/StealthSample.unity` + 3 个资产（两条路线 + 一份视野参数）；
    场景含地面、3 个掩体、2 名哨兵、玩家、相机、HUD、时钟，**打开即可 Play**。
- `Tests/Editor/GuardBrainTests.cs`：**10 条** M3 用例（档位流转、回落链、纯预览不改状态、
  叠加不污染基线、事件广播、同伴通报、接缝生效、复原联动）。

### Fixed
- **玩家初始朝向写死 180°**：与场景摆放无关，导致打开示例后画面里看不到掩体与哨兵
  （第一张 Play 截图实证）。改为取 `transform.eulerAngles.y`。
- **视线遮挡判据的两难**：终点余量收 0.5m 会让贴身掩体被跳过（隔墙看得见），
  收 0.1m 又会让玩家自身胶囊被当成遮挡（哨兵永远看不见玩家）。
  正解 = 全长射线 + 按身份排除：`CanSee` / `CanSeeFrom` 新增 `targetOwner`，
  `DetectionState` 与 `GuardBrain` 均传入目标 transform。
- **`GuardBrain.All` 在编辑态恒空**：只靠 `OnEnable` 登记会让 HUD 与 `ResetAll` 静默失效
  （EditMode 不跑生命周期回调，本包第三次撞同一族坑）。改为剪枝 + 空则按场景重建。
- **回溯窗口三处失效写法**（自查发现）：`readonly` 字段被赋值、计时基准在 `OnGUI` 内被每次重绘重置
  （滑块永远不动）、编辑态提前 return（「不 Play 也能看」直接落空）。
- **共享编辑器纪律**：示例生成工具原用 `NewScene(Single)` + 模态对话框——前者会丢弃其他会话
  未保存的现场，后者会把指令通道卡死（本轮实测确实卡住一次）。
  改为 Additive 建内存场景 → 存盘 → 关闭，并把所有 `DisplayDialog` 换成日志。
- **状态机驻留计时被重复累加**：同一帧内 `_stageTime` 在两个分支各加一次，驻留时长实际减半且互相覆盖。
- **叠加偏移以当前 transform 为锚**：与巡逻基线构成循环依赖；改为以基线世界位置为锚收敛。

## [0.3.0] - 2026-11-03
### Added
- **M2 四层察觉管线**（Runtime 11 文件）：
  - `VisionProfile`（SO，共享 Model）：水平/垂直视野角（全角）、视距、视线高度、遮挡 LayerMask、
    察觉上升与回落速率、疑惑/警觉双阈值、光照敏感度、视线检测间隔帧数；
    配 `Set*` 写入面（§0.1 原则 2 的 Controller 入口），并保证疑惑阈值不会倒挂过警觉阈值。
  - `VisionGeometry`（静态纯函数）：L1 球检 → L2 水平锥 + 俯仰锥 → L3 视线委托注入；
    零向量/重合点等退化输入安全。**独立成纯函数的理由与 M1 同源**——辅助台要按 t 时刻的 NPC 位姿
    在编辑态问「这一点看不看得到」，绑在 transform 上就只剩「当前位置」一种问法。
  - `VisionVolume`（实例）：启用开关、视距倍率、视线高度/水平角覆盖（均为实例级差异）；
    L3 内部处理遮挡层过滤 + 自身与子节点排除 + 射线预算 + 错峰缓存；
    新增 `LineOfSightOverride` 静态注入点，使遮挡翻转与预算分支可在 EditMode 确定性断言。
  - `QueryOptions` / `StealthMask`：前者是四层可选掩码（值序与 core 的 `StealthQueryLayers` 对齐，有守卫用例）；
    后者是包内 L4 修正位（`SafeIsland=8` 消费辅助台数据，`4/16/32/64` 预留不启用）。
  - `AwarenessModel`（纯函数）：察觉度积分、档位导出、L4 曝光倍率合成（蹲伏 ×0.35、光照线性、安全岛归零）。
  - `DetectionState`（实例）：累积察觉度与档位；`EvaluateOnce(dt<=0)` 为**纯预览语义**——
    只更新可见性、绝不推进真实累积，故辅助台回溯 scrub 不会污染玩法状态。
  - `VisionRegistry` / `DetectionRegistry`：读时重建的保守失效注册表。
  - `StealthPerformance`：射线帧预算 + 按 actorId 取模错峰（帧号由外部显式报告，不自读 `Time.frameCount`）。
  - `StealthIds`：跨包单位 id 的**唯一算法**入口。
  - `StealthWorld` 补实现并注册 `IVisionWorld` / `IDetectionWorld`；`StealthClock.Update` 每帧复位射线预算。
- `Tests/Editor/VisionDetectionTests.cs`：**30 条** EditMode 用例（本包合计 72 条）。

### Fixed
- **察觉度永不回落**：未被看见时误用 `default` 阈值组（回落速率 0）⇒ 察觉度永久钉住、玩家无法脱身。
  改为取场上任一启用视野的 Profile 参数（ACCEPTANCE F-7）。
- **跨包 id 可为负**：`GetEntityId().GetHashCode()` 会为负，撞上契约 `-1`「不限定单位」哨兵，
  且巡逻侧与视野侧算法分叉会让 `CanSeeAt` **静默退回当前位姿**。统一走 `StealthIds.Of`（内部强制非负）
  （ACCEPTANCE F-8）。

## [0.2.0] - 2026-11-03
### Added
- **M0.5 跨包契约落地（方案 P 闭环）**：
  - `Runtime/StealthWorld.cs`：实现 core 的 `IPatrolWorld` + `IStealthScrubDriver` 并经 `Services` 注册；
    位姿/路点/轨迹/单位枚举全部以 core 的 DTO 出入（不外泄本包类型）；
    `ScrubTo(t)` **显式推送**全部位姿（编辑态无帧循环，B 包滑动条依赖此）；
    `RefreshPreview()` 供 Inspector 改完路点即时无帧刷新；`TrySetWaypoint` 是「按玩家线路反推巡逻路点」的落点。
    `IVisionWorld` / `IDetectionWorld` **故意不注册**（M2/M3 未完成，B 侧走优雅降级）。
  - `Editor/StealthEditorBootstrap.cs`：`[InitializeOnLoad]` 编辑态注册钩子 + 撤销回调触发预览刷新
    （补上「生命周期回调在编辑态不执行」的缺口，见 ACCEPTANCE F-5）。
  - `Tests/Editor/StealthContractConsumerTests.cs`：**消费方视角** 16 条用例——4 个 Fake 实现只用
    `Collin.Core.Stealth` 符号，证明 B 包不引用 A 也能实现与取用契约；含跨包镜像枚举值序一致性守卫。
- `Docs/ACCEPTANCE.md`：M0.5 增量条目 27-33 + 实现轮自查记录。

### Fixed
- `StealthWorld` 用 `Object.GetInstanceID()` 触发 Unity 6 的 CS0619（该 API 被当 error 报）；
  改用 `GetEntityId().GetHashCode()` 作 actorId，并同步契约注释声明「域重载后会变，消费方不得持久化」。
- `StealthWorld` 同时 using `System` 与 `UnityEngine` 时 `Object.FindObjectsByType` 二义性（CS0104）；改全限定写法。
- 适配层原以成员列表复用返回 `Actors` / 路点集合，会让消费方持有的旧引用被下次访问改写；改为每次新建容器。
- core 契约 `StealthPoseDto.HasLeg` 仅判 `legIndex >= 0`，而 `default` 的 `legIndex` 恰为 0
  ⇒「无数据」被误读成「位于第 0 段」；配合 core 新增 `valid` 位收紧判据。

## [0.1.0] - 2026-11-03
### Added
- **M1 巡逻求值层**（Runtime，5 个源文件）：
  - `PatrolRoute`（ScriptableObject，共享 Model）：路点存**相对路线根的局部坐标**（D-09）；
    每点可设停留秒数与段速度（0 表示沿用默认速度）；支持 Loop / PingPong；
    内部维护「分段累计长度表 + 布局哈希」，改路点后自动失效重建；
    `EvaluatePose(t)` 为**时间的纯函数**（D-01）——弧长定位 + 段内线性插值，不做逐帧积分；
    `BuildTrajectorySamples()` 提供等弧长采样（B 包分层绘制复用同一口径）；
    `WrapTime()` 把任意时间折算到 `[0, T)`。
  - `PatrolPose`（struct）：局部位姿 + 段索引 + 段内进度 + 停留标志 + 所属周期，结果自解释。
  - `PatrolRouteRoot`（MonoBehaviour）：路线根，提供局部↔世界换算、`ToWorld(pose)`、
    `BakeWaypointsFromWorld()`（从世界位置反推路点）、`BuildWorldTrajectory()`。
  - `PatrolAgent`（MonoBehaviour）：实例级差异数据（相位偏移、速度倍数、朝向跟随速度）；
    位置**硬贴**保持折线精确、朝向单独缓动（D-11）；`LateUpdate` 由 `StealthClock` 驱动。
  - `StealthClock`（MonoBehaviour）：`Auto` / `Manual` 双模式时间索引，`Manual` 是编辑态回溯的唯一入口。
- **Tests/Editor**：`com.collin.stealth.Tests` 程序集 + **24 条 EditMode 用例**。
- `Docs/ACCEPTANCE.md`：条目 1-17 全部补齐实测证据。

### Fixed
- `PatrolRoute` 段朝向把 `Mathf.Atan2` 的弧度乘了 `1f/Mathf.Rad2Deg`，朝向被压缩（+X 段应 90°、实得 0.027°）；改乘 `Mathf.Rad2Deg`（ACCEPTANCE F-4）。
- `StealthClock` 仅在 `Awake/OnEnable` 登记静态单例，**EditMode 下生命周期回调不执行 ⇒ `Instance` 恒 null**，会让 B 包的 T 周期回溯在 M4 才暴露走不通；`Instance` 增加 `FindFirstObjectByType` 兜底（ACCEPTANCE F-5）。
- `PatrolAgent.BuildWorldTrajectory` 重复调用根组件换算，导致世界坐标二次变换；改为单次取用。

## [0.0.1] - 2026-11-03
### Added
- 首个版本：**仅包骨架**，不含功能代码（AGENTS.md §4「DESIGN 先于代码」）。
- `package.json`（依赖 `com.collin.core@0.2.0` + `com.unity.inputsystem@1.20.1`，符合 §5 单向依赖）。
- `Runtime/com.collin.stealth.asmdef`（引用 core + Unity.InputSystem，`versionDefines` 探测输入包，`validateReferences: true`）。
- `Editor/com.collin.stealth.Editor.asmdef`（includePlatforms=Editor，引用 core + 本包 Runtime）。
- `Showcase.json`（category=玩法组件，order=30.0；`video` 暂空，M5 补 `ShowcaseMedia/stealth_demo.mp4`）。
- `Docs/DESIGN.md`（**已评审**，含 13 条决策与 Q-A1~Q-A5 决议表）、`Docs/ACCEPTANCE.md`、本文件。
- `Sample/`（README 规划预制件落位）、`ShowcaseMedia/` 占位目录。

### Changed
- 无（首版）。

### Removed
- 无（首版）。
