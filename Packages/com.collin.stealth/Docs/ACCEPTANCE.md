# ACCEPTANCE — com.collin.stealth

> 每条必须带可观测证据（谁 / 何时 / 用什么指令验证 / 结果）。只记事实，不记意图（AGENTS.md §4）。
> 当前状态：**M1 已交付**（路线/位姿/时钟/NPC 求值层 + 24 条 EditMode 用例）。M2 起为视野与察觉，未开始。

| # | 验收项（可观测行为） | 验证指令 / 方法 | 验证者（客户端+标识） | 日期时间 | 结果与证据 |
|---|---|---|---|---|---|
| 1 | 包骨架齐全：package.json / Runtime+Editor asmdef / Docs 三件 / Showcase.json / Sample/ | `python Tools/verify_project.py` | qianwen-work-assistant · stealth-m1-2026-11-03 | 2026-11-03 16:42 | **通过** — `[ ok ] pkg-layout: com.collin.stealth 结构齐全` |
| 2 | Runtime 程序集不引用 UnityEditor（§0.1 原则 3） | `python Tools/verify_project.py` → runtime-purity | qianwen-work-assistant · stealth-m1-2026-11-03 | 2026-11-03 16:42 | **通过** — `[ ok ] runtime-purity` |
| 3 | 包被 Unity 识别为内嵌包并纳入编译（新增 5 个 Runtime 脚本 + 1 个测试程序集） | `unity command "package resolve"`；`script recompile_status`；`unity command "package list"`；`unity command "test list"` | qianwen-work-assistant · stealth-m1-2026-11-03 | 2026-11-03 16:44 | **通过** — `package list` 显示 `com.collin.stealth, version:0.1.0, source:Embedded, isInstalled:true`；`recompile_status` = `failed:false, errors:[]`；`test list` 全工程 108 条（含本包 24 条） |
| 4 | `EvaluatePose` 确定性：同 (route,t) 连续 1000 次调用逐位一致 | EditMode `EvaluatePose_IsDeterministic_AcrossManyCalls` | qianwen-work-assistant · stealth-m1-2026-11-03 | 2026-11-03 16:40 | **通过** — 位置/朝向/段索引/停留标志四项均按 0 容差断言 |
| 5 | `TotalCycle` == Σ(段长/速度 + 停留)，误差 < 1e-4 | EditMode 4 条周期用例（默认速度、段速度覆盖、PingPong、改路点后重建） | qianwen-work-assistant · stealth-m1-2026-11-03 | 2026-11-03 16:40 | **通过** — 例：正方形 4×(10/2+1)=24；PingPong 三段=24 |
| 6 | 四相位（0/T4/T2/3T4）落点对应路点，偏差 < 0.05 | EditMode `EvaluatePose_LandsOnWaypointsAtQuarterPhases` | qianwen-work-assistant · stealth-m1-2026-11-03 | 2026-11-03 16:40 | **通过** |
| 7 | 停留窗口内零位移、段末抵达下一路点、负时间与超周期取模一致 | EditMode `…IsDwellingDuringStopWindow` / `…WrapsNegativeAndOvershootTime` | qianwen-work-assistant · stealth-m1-2026-11-03 | 2026-11-03 16:40 | **通过** |
| 8 | 退化路线（空/单点/零长段）不抛异常且可预测 | EditMode `…HandlesDegenerateRoutes` | qianwen-work-assistant · stealth-m1-2026-11-03 | 2026-11-03 16:40 | **通过** — 空路线 `HasLeg=false`、`TotalCycle=0` |
| 9 | 段朝向 = Atan2(dx,dz)（+X ⇒ 90°，+Z ⇒ 0°） | EditMode `…YawFollowsSegmentDirection` | qianwen-work-assistant · stealth-m1-2026-11-03 | 2026-11-03 16:40 | **通过**（首轮为 0.0274°，系实现把弧度当角度乘了 `1/Mathf.Rad2Deg`，已修，见 F-4） |
| 10 | **局部坐标 + 路线根语义**：只移根 ⇒ 整条轨迹平移，形状不变（D-09） | EditMode `MovingRoot_TranslatesWholeTrajectory_WithoutChangingShape`（40 个采样点逐个比对，1e-4） | qianwen-work-assistant · stealth-m1-2026-11-03 | 2026-11-03 16:40 | **通过** |
| 11 | 路线根旋转作用于轨迹与世界朝向 | EditMode `RootYaw_RotatesTrajectoryAroundRoot` | qianwen-work-assistant · stealth-m1-2026-11-03 | 2026-11-03 16:40 | **通过** — 根 (5,0,5)+yaw90，局部 (5,0,0) ⇒ 世界 (5,0,0)、朝向 180° |
| 12 | 「从当前世界位置反推路点」换算为局部坐标（D-09 直观性补偿） | EditMode `BakeWaypointsFromWorld_ConvertsToRelativeCoordinates` | qianwen-work-assistant · stealth-m1-2026-11-03 | 2026-11-03 16:40 | **通过**（数据层；Editor 按钮本体属 M4） |
| 13 | 等弧长轨迹采样满足间隔上限、时间单调、且与逐点求值同口径（B 包分层绘制依赖） | EditMode 3 条采样用例 | qianwen-work-assistant · stealth-m1-2026-11-03 | 2026-11-03 16:40 | **通过** — 0.7m 间隔下 26 个样本与 `EvaluatePose` 逐点吻合 1e-3 |
| 14 | 实例差异不写进配置：同资产两实例仅相位/倍数不同 ⇒ 结果不同且可复现（§0.1 原则 2） | EditMode `AgentPhaseOffset_…` / `AgentSpeedMultiplier_…` | qianwen-work-assistant · stealth-m1-2026-11-03 | 2026-11-03 16:40 | **通过** — b(t)=a(t+T/2)；2× 速度下 t=0.5 等价原 t=1 |
| 15 | **编辑态（非 Play）可回溯**：Manual 时钟驱动下扫描整周期，NPC 位置连续变化（B 包 T 周期滑动条的前提） | EditMode `ManualClock_ScrubsFullCycleWithoutPlayMode` / `ClockManualMode_…` | qianwen-work-assistant · stealth-m1-2026-11-03 | 2026-11-03 16:40 | **通过** — 100 步扫描中 ≥40 步发生位移；首轮失败因编辑态不执行 `Awake`，已加 `FindFirstObjectByType` 兜底（见 F-5） |
| 16 | Runtime asmdef 引用 Unity.InputSystem 后编译干净、runtime-purity 仍绿灯 | `script recompile_status` + `verify_project.py` | qianwen-work-assistant · stealth-m1-2026-11-03 | 2026-11-03 16:42 | **通过** |
| 17 | 交锁门禁三项 | `script recompile_status`；`console clear` → 重跑 recompile+test run → `console status`；`python Tools/verify_project.py` | qianwen-work-assistant · stealth-m1-2026-11-03 | 2026-11-03 16:46 | **本包三项全绿** — ①`status:up_to_date, failed:false, errors:[]` ②`groundTruth.consoleErrors=0 / consoleWarnings=0`（logs=12）③本包检查全 `ok`。**工程整体仍红灯**，来源是与本包无关的他包超时锁，见 U-1（用户已指示不予处理） |
| 18 | 锥内无遮挡 ⇒ CanSee=true；越视角边界 ⇒ false | EditMode + Play 观察 | — | — | 待验证（M2） |
| 19 | 插入 LineOfSight 层 Cube ⇒ CanSee 转 false，移开恢复 | Play 实测 | — | — | 待验证（M2） |
| 20 | 察觉累积越 alertThreshold；脱敏后衰减归零 | Play 实测 + 日志 | — | — | 待验证（M2） |
| 21 | Alerted 经 EC_Stealth_AlarmRaised 通知半径内 NPC 并转 Search | Play 实测 | — | — | 待验证（M3） |
| 22 | 编辑态 ManualTime 扫 0→T 的轨迹与 **Play 态**轨迹重合（误差 < 0.1） | 编辑器实测（同一路线两种模式采样对比） | — | — | 待验证（M4，数据层同口径已由 15 佐证） |
| 23 | 50 NPC Play 态 ≥ 55 FPS（记录采样帧数与设备） | `get_performance` / 帧率日志 | — | — | 待验证（M4） |
| 24 | **拆箱即用**：`StealthPlayer` 预制件拖进空场景按 Play，WASD 可移动、Ctrl 蹲伏，无需自建输入资产（D-13） | 空场景实测 | — | — | 待验证（M5） |
| 25 | **路线整体搬移**（场景内实测，非数据层） | 编辑器实测位移前后轨迹对比 | — | — | 待验证（M5；数据层等价性已由 10 通过） |
| 26 | **演示视频两点**：①怎么用（真实编辑器操作：Inspector 里的巡逻/视野/决策参数与层级结构）②用了之后的**效果**（地面警戒投影随档位变色、潜行绕行、抵达撤离圈弹结算卡片） | `ShowcaseMedia/stealth_demo.mp4`（用户实机录制，1616×880 / 44.7 s / 保留音轨）；展板播放由 `ShowcaseRuntime.ResolveVideoPath()` 核验 | 11-04 22:25 | **通过（收版 0.5.0）** — `Showcase.json.video` 已填充，展板运行时枚举自动接入。⚠️ **视频内容按用户明确指示不做抽帧验证**（「凡是我手录的视频内容不用验证，除非我提出了修改要求」），故本条只核验**交付与接入事实**（文件存在、时长与编码、路径可解析），不对画面内容作断言 |

## 本次会话已验证事实

- **F-1 全量测试结果**（2026-11-03 16:40 / 16:42 两次，指令 `unity command "test run" --mode editor`）：
  `Summary{Total:108, Passed:108, Failed:0}`，其中 `Collin.Stealth.Tests` **24/24 通过**。
- **F-2 控制台归零**（16:42，`console clear` → 重跑 recompile + test run → `console status`）：
  `groundTruth` = `compilationFailed:false, consoleErrors:0, consoleWarnings:0, consoleLogs:10`，
  `sampledUtc:"2026-11-03T08:42:24Z"`。归零后新产生的 error/warning 为 0，符合 §1.7 门禁语义。
- **F-3 首轮失败与修复轮**（16:36 首轮 `Failed:5`）：本包 5 条红，其中 **3 条是实现轮抓到的真问题**、
  2 条是测试自身预期算错。全部修复后 24/24 绿。
- **F-4 抓到缺陷（实现）**：`PatrolRoute.Rebuild` 把 `Mathf.Atan2` 的弧度结果乘了 `1f/Mathf.Rad2Deg`，
  导致段朝向被压缩（+X 段应为 90°，实得 0.027°）。修法：改乘 `Mathf.Rad2Deg`。
  该缺陷只在**朝向断言**里暴露——位置全对，故「只断言最显眼的那一维」会漏网。
- **F-5 抓到缺陷（实现，且直接决定 B 包可行性）**：`StealthClock` 靠 `Awake/OnEnable` 登记静态单例，
  而 **EditMode 下 MonoBehaviour 生命周期回调不执行** ⇒ `Instance` 恒为 null ⇒
  编辑态完全取不到时间索引，B 包的「T 周期滑动条回溯」会在 M4 才发现走不通。
  修法：`Instance` getter 增加 `FindFirstObjectByType<StealthClock>()` 兜底。
- **F-6 环境事实**：`unity command "test run" --mode editor --assembly com.collin.stealth.Tests`
  的 `--assembly` **过滤被服务端忽略**，实际仍跑全工程 108 条；筛本包结果须按 `FullName` 前缀自行过滤。
- **F-7 环境事实**：`test run` 输出是「JSON 后跟制表符表格」，`json.loads` 会因 Extra data 报错，
  须用 `json.JSONDecoder().raw_decode`。

## M0.5 增量（跨包契约层，2026-11-03，同验证者 · session stealth-m05-2026-11-03）

> A 侧新增 `Runtime/StealthWorld.cs`（契约实现）+ `Editor/StealthEditorBootstrap.cs`（编辑态注册钩子）
> + `Tests/Editor/StealthContractConsumerTests.cs`（消费方视角 18 条用例）。
> 末次门禁实测（2026-11-03 20:28）：全工程 **141/141 通过**（CityMap 99 / Stealth 42），
> `console clear` → 重跑 recompile + test run 后 `groundTruth` = `consoleErrors:1 / consoleWarnings:0`
> （该 1 条命中 §1.8 已批准豁免清单），`python Tools/verify_project.py` **结论: 绿灯**。

| # | 验收项（可观测行为） | 验证指令 / 方法 | 日期时间 | 结果与证据 |
|---|---|---|---|---|
| 27 | A 的能力可经 core 接口取用（`IPatrolWorld` / `IStealthScrubDriver` 已注册） | `Services.TryGet<T>` + 契约用例 | 2026-11-03 19:26 | **通过** — 注册往返用例绿；`IVisionWorld` / `IDetectionWorld` 故意**不注册**（M2/M3 未实现），B 取用时走降级 |
| 28 | **编辑态（非 Play）能拿到服务**——B 包滑动条的前提 | EditMode 用例 + `EditorBootstrap` 的 `[InitializeOnLoad]` | 2026-11-03 19:26 | **通过** — 与 F-5 同源问题：`RuntimeInitializeOnLoadMethod` 在编辑态不执行，故 A 自带 Editor 侧注册钩子，B 无需引用 A 即可拿到服务 |
| 29 | 路点可经契约写回（特性 3「按玩家线路反推巡逻路点」的落点） | EditMode `WaypointWriteback_SupportsReverseAdjustmentFlow` | 2026-11-03 19:26 | **通过** — 写回后越界索引/未知单位均返回 false；A 侧走既有 `SetWaypointPosition` 写入面，改完自动作废累计表 |
| 30 | `ScrubTo(t)` 后 NPC 位姿被**显式推送**（编辑态无帧循环） | 实现走 `PushAllPoses` → `ApplyPose(pose, 0f)` | 2026-11-03 19:26 | 代码路径已实装；**场景内实测待 M4**（验收 22 同源） |
| 31 | 全量回归：新增契约未破坏 M1 与既有包 | `unity command "test run" --mode editor` | 2026-11-03 20:28 | **通过** — 全工程 **141/141**（CityMap 99 / Stealth 42）；Stealth 由 24 条增至 **42** 条（M1 24 + 契约 18） |
| 32 | 契约 DTO 不出现 A 包类型（方案 P 前提） | 逐文件静态扫描 | 2026-11-03 19:10 | **通过** — 19 个类型全为接口/值类型/枚举/通道子类 |
| 33 | 本包源码编译干净 | `recompile_status` 错误列表 | 2026-11-03 19:26 | **通过** — 错误列表内**无一条**路径含 `com.collin.stealth`；`stealth.dll` / `stealth.Editor.dll` / `stealth.Tests.dll` 均已产出 |

### M0.5 期间自查发现并修正（实现轮）

- **契约 `HasLeg` 判据缺陷**：`StealthPoseDto` 原以 `legIndex >= 0` 判有效，而结构体 `default` 的
  `legIndex` 恰为 0 ⇒ 消费方拿到"无数据"会误读成"位于第 0 段"。修法：增加显式 `valid` 位，
  判据改 `valid && legIndex >= 0`，并补用例锁住（`ConsumerCanAssembleDto_…`）。
- **`Object.GetInstanceID()` 在 Unity 6 被当 error 报（CS0619）**：改用
  `GetEntityId().GetHashCode()` 作 actorId；契约注释同步声明「域重载后会变，消费方不得持久化」。
  该坑与 stretch 包记录的同源，本包首次撞上。
- **`System.Object` 与 `UnityEngine.Object` 二义性（CS0104）**：`StealthWorld.cs` 同时 using 两个命名空间，
  `Object.FindObjectsByType` 须全限定。core v0.1.0 也踩过同名坑（PackageInfo），属可预防的重复错误。
- **集合别名**：适配层原用成员列表复用返回 `Actors` / 路点，会让消费方持有的旧引用被下次访问改写；
  已改为每次新建容器。

## M2 增量（四层察觉管线 + 视野/察觉注册，2026-11-03，验证者 qianwen-work-assistant · stealth-m2-2026-11-03）

> Runtime 新增 9 文件：`VisionProfile`、`VisionVolume`、`VisionGeometry`、`QueryOptions`、`StealthMask`、
> `AwarenessModel`、`DetectionState`、`VisionRegistry`、`DetectionRegistry`、`StealthIds`、`StealthPerformance`；
> `StealthWorld` 补实现并注册 `IVisionWorld` / `IDetectionWorld`；`StealthClock.Update` 驱动射线预算复位。
> 测试新增 `VisionDetectionTests.cs`（30 条）。

| # | 验收项（可观测行为） | 验证指令 / 方法 | 日期时间 | 结果与证据 |
|---|---|---|---|---|
| 34 | L1 距离：球检早退，不消耗射线预算；眼高参与真实斜距 | EditMode `DistanceOnly_UsesSphereCheckNotRaycast` | 2026-11-03 21:47 | **通过** — 10m 视距下 9m 可见 / 11m 不可见 / 恰 10m 含入；`UsedThisFrame=0` |
| 35 | 视距倍率为实例级覆盖，不改共享资产 | EditMode `DistanceCheck_FoldsInstanceDistanceMultiplier` | 21:47 | **通过** — 同一 Profile，倍率 2× 后 15m 点由不可见转可见 |
| 36 | **验收 7**：锥内可见、越过视角边界即翻转 | EditMode `Cone_BoundaryFlipsWhenTargetRotatesOut` | 21:47 | **通过** — 100° 全角下 40° 可见 / 55° 不可见，且同点在 `DistanceOnly` 下可见（证明是锥在起作用） |
| 37 | 观察者转向扫过同一目标（旋转生效于判定） | EditMode `Cone_RotatingObserverSweepsSameTarget` | 21:47 | **通过** |
| 38 | 360° 视为全向；垂直角按俯仰差判定 | EditMode `Cone_ThreeSixtyIsOmnidirectional` / `VerticalCone_ChecksPitchAgainstEyeLine` | 21:47 | **通过** — 仰角 50° 被 60° 垂直角挡掉、俯角 8° 通过 |
| 39 | **验收 8**：遮挡插入 ⇒ 判不可见，撤除 ⇒ 恢复；缓存可复位 | EditMode `LineOfSight_OcclusionFlipsVerdictAndCacheResets`（经 `LineOfSightOverride` 注入） | 21:47 | **通过** — 三帧内 true→false→true |
| 40 | 无遮挡层时不发射线、不耗预算 | EditMode `LineOfSight_WithoutOccluderLayerDoesNotConsumeBudget` | 21:47 | **通过** |
| 41 | **预算耗尽不得翻转结论**（否则辅助台热力图随帧率抖动，把性能问题伪装成玩法问题） | EditMode `LineOfSight_BudgetExhaustedReusesLastVerdictInsteadOfFlipping` | 21:47 | **通过** — 预算=1 时后问者沿用上帧 true |
| 42 | 错峰：间隔 N 帧时每帧恰好 1/N 的单位实际检查 | EditMode `Staggering_SpreadsChecksAcrossFrames` | 21:47 | **通过** — 间隔 3、3 帧 × 3 单位 ⇒ 恰好 3 次检查 |
| 43 | 跳过的帧复用视线结论（不重复评估） | EditMode `CheckInterval_CachesVerdictOnSkippedFrames` | 21:47 | **通过** — 帧 0 评估 1 次、帧 1 复用、帧 2 再评估 |
| 44 | L4 只改累积速率、不改可见性；蹲伏/光照/安全岛的倍率合成 | EditMode `ExposureScale_CrouchLightAndSafeIsland` / `LineOfSight_CoverDoesNotChangeGeometricVerdict` | 21:47 | **通过** — 安全岛 ⇒ 倍率恒 0；蹲伏 ⇒ ×0.35 |
| 45 | **验收 9**：察觉累积越阈值升档，脱敏后**衰减归零**回 Patrol | EditMode `Suspicion_AccumulatesThenDecaysToZero` | 21:47 | **通过** — 1s→0.5(疑惑)、2s→1.0(警觉)、离场后 0.75→0 |
| 46 | **纯预览不改真实状态**（`dt<=0` 只更新可见性，编辑态 scrub 不污染累积） | EditMode `PurePreview_NeverMutatesSuspicion` | 21:47 | **通过** — 连续 10 次预览后察觉度仍为 0；真实推进一次即 0.5 |
| 47 | 安全岛经 core 反向注入生效；未注册时优雅降级不报错 | EditMode `SafeIsland_SuppressesAccumulationViaReverseProvider` | 21:47 | **通过** — 注册后累积为 0；`Services.Clear()` 后正常累积 |
| 48 | 注册表不泄漏已禁用物体 | EditMode `Registries_DoNotLeakDestroyedObjects` | 21:47 | **通过**（注意 EditMode 不调 `OnDisable`，须手动 `MarkDirty`） |
| 49 | `IVisionWorld` / `IDetectionWorld` **已注册**，视野快照字段齐全且 id 与哨兵同源 | EditMode `VisionWorld_IsRegisteredAndEnumeratesVolumes` | 21:47 | **通过** — 含 `originWorld.y=眼高`、`actorId==StealthIds.Of(哨兵)` |
| 50 | 禁用视野仍被枚举、由 `enabled` 标记（区分「关掉」与「不存在」） | EditMode `VisionWorld_DisabledVolumeIsFlaggedNotSilentlyDropped` | 21:47 | **通过** |
| 51 | core 层级掩码 ⇄ 包内 `QueryOptions` 值序一致；映射后分层结果符合预期 | EditMode `VisionWorld_CoreLayerMaskMapsToInternalOptions` / `AlertStage_MirrorValuesMatchCoreContract` | 21:47 | **通过** |
| 52 | **`CanSeeAt(t)` 用 t 时刻巡逻位姿，而非当前 transform**（辅助台按时刻预演的地基） | EditMode `VisionWorld_CanSeeAt_UsesPoseAtTimeNotCurrentTransform` | 21:47 | **通过** — t=2.5s 哨兵在 (10,0,0) 朝 +X 时正前方可见、背后不可见；而其 transform 仍在原点，两条对照断言证明路径确有区别 |
| 53 | `CountSeers` 与逐视野判定口径一致 | EditMode `CountSeers_AgreesWithPerVolumeVerdicts` | 21:47 | **通过**（首轮红，根因见 F-8） |
| 54 | `SuspicionOf`/`StageOf` 未知 id 返回 0/Patrol 不抛异常；`ResetAll` 清空全部累积 | EditMode `DetectionWorld_ReadsStateAndResetAllClearsEverything` | 21:47 | **通过** |
| 55 | 阈值写入不可倒挂（疑惑 ≤ 警觉）；`default` 阈值组解释为「该档未启用」 | EditMode `Thresholds_CannotInvertThroughWriter` / `StageOf_ZeroThresholdsMeansStageDisabled` | 21:47 | **通过** |
| 56 | 射线预算由时钟每帧复位（否则只增不减 ⇒ 判定实质失效） | `StealthClock.Update` 调 `StealthPerformance.BeginFrame(Time.frameCount)`；Play 态实测待 M4 | 21:47 | 代码路径已实装；数据层用显式帧号验证 |
| 57 | **全量回归 + 交锁门禁** | `script recompile_status`；`console clear` → 重跑 recompile + test run → `console status`；`verify_project.py` | 21:47 | **本包三项全绿** — `failed:false`；`consoleErrors:0 / consoleWarnings:0`；全量 **172/172**（CityMap 100 / Stealth 72）；校验器本包项全 ok。工程整体仍因 citymap 超时锁红灯，见 U-1 |

## M2 实现轮抓到并修复的缺陷

- **F-7 察觉度永不回落（玩法级 bug）**：`DetectionState` 在**未被看见**时用 `default(AwarenessTuning)`，
  其 `decayPerSecond = 0` ⇒ 察觉度永久钉住、玩家无法脱身。修法：回落与阈值改取「场上任一启用视野」的
  Profile 参数（`fallbackTuning`）。该 bug 由用例 45 的衰减断言逼出。
- **F-8 跨包 id 可为负，撞上契约的 -1 哨兵**：`GetEntityId().GetHashCode()` 会返回负数，
  而契约用 `-1` 表示「不限定单位」、`ShouldCheckThisFrame` 也按 id 取模做错峰。
  后果是**静默的**：`CountSeers` 按单位过滤会退化成全局计数（用例 53 首轮红即此因）；
  且巡逻侧（裸哈希）与视野侧（已非负化）算法分叉时，`CanSeeAt` 找不到 agent 会**悄悄退回当前位姿**，
  使「按 t 时刻判定」失效而不报错。修法：新增 `StealthIds.Of` 唯一入口，内部 `& int.MaxValue` 强制非负，
  `StealthWorld`/`DetectionState`/`VisionVolume`/`CountSeers` 全部改走该入口（用例 49/52/53 联合守卫）。
- **`LogAssert.Expect` 不回退控制台计数**（与 M0.5 同源，本轮再次确认）：本包测试全程用
  `Debug.unityLogger.logEnabled=false` + `finally` 复位策略，未产生任何自造 error。
- **测试前提写错两处（非实现缺陷）**：①距离用例忘了眼高把原点抬升，平面 10m 的真实斜距是 10.14m
  ⇒ 边界断言必然失败，改为 `eye=0` 并补一条眼高对照；②错峰缓存用例在同一帧号反复询问，
  永远进不到复用分支 ⇒ 改为显式传入可控 id（接口把 actorId 设为入参正是为此）并推进帧号。

## M3 + M4 增量（状态机 / 回溯窗口 / 可玩示例，2026-11-04，验证者 qianwen-work-assistant · stealth-m3m4-2026-11-03）

> Runtime 新增：`IPatrolModifier`、`GuardBrain`（四档状态机 + 同伴通报 + 事件广播）、
> `PlayerStealthController`（拆箱即用）、`StealthHud`、`StealthGizmos`、`IPathBridge` + `NullPathBridge`；
> Editor 新增：`PatrolRouteRootEditor`（Scene 拖路点 + Inspector 停留/段速）、
> `StealthScrubWindow`（T 周期回溯窗口）、`StealthSampleSceneBuilder`（一键生成可玩示例）；
> 测试新增 `GuardBrainTests.cs`（10 条）。

| # | 验收项（可观测行为） | 验证指令 / 方法 | 日期时间 | 结果与证据 |
|---|---|---|---|---|
| 58 | 察觉度越过疑惑阈值 ⇒ 升「疑惑」，并记住最后看见位置 | EditMode `SuspicionCrossesThreshold_EscalatesToSuspicious` | 2026-11-04 00:15 | **通过** |
| 59 | 越过警觉阈值 ⇒ 升「警觉」，叠加偏移指向可疑点并接管朝向 | EditMode `SuspicionCrossesAlert_EscalatesToAlerted_AndOverlayChases` | 00:15 | **通过** |
| 60 | 丢失目标后 警觉→(驻留)→搜索→(环绕)→巡逻 完整回落链 | EditMode `LostTarget_HoldsThenSearchesThenReturnsToPatrol` | 00:15 | **通过** |
| 61 | **纯预览不改档位**（`dt<=0` 只刷新感知读数） | EditMode `PurePreview_DoesNotAdvanceStageMachine` | 00:15 | **通过** |
| 62 | **叠加层不污染巡逻基线**（地基守卫）：`EvaluateWorld` 恒返回纯基线，只有 transform 被拉扯 | EditMode `OverlayMovesTransform_ButBaselineStaysPure` | 00:15 | **通过** — t=1s 基线 (2,0,0)、t=3.5s 基线 (7,0,0) 均不受叠加影响，而 transform 被拉向可疑点 |
| 63 | `ResetOverlay` / 跨包 `ResetAll` 连 NPC 反应状态一起复原 | EditMode `ResetOverlay_ClearsStageAndOffset` / `WorldResetAll_AlsoRestoresGuardReactions` | 00:15 | **通过** |
| 64 | 转入 Alerted 的瞬间经 `StealthDetectedChannel` 广播，载荷含 actorId/targetId/**timeIndex**/stage/suspicion；非跃迁帧不重复广播 | EditMode `Alert_BroadcastsPayload_OnDetectedChannel` | 00:15 | **通过** |
| 65 | 同伴通报：半径内同伴升疑惑并同步可疑点；半径外不受影响 | EditMode `Alert_PullsNearbyCompanionToSuspicious_NotFarOrAlreadyAlerted` | 00:15 | **通过** |
| 66 | `IPathBridge` 默认不注册（直线叠加）；注册后叠加目标被吸附改写 | EditMode `PathBridge_AbsentByDefault_PresentWhenRegistered` | 00:15 | **通过** |
| 67 | **实机 Play 验证「可以玩」**：NPC 真的发现玩家、脱离巡逻线追到出生点，随后脱敏归零 | 打开 `Sample/Scenes/StealthSample.unity` → Play 47.4s → 运行时探针 | 2026-11-04 00:06 | **通过** — 探针：`guards=2`、`Guard_Patrol Stage=Searching pos=(-4.5, 0.0, -15.7)`（玩家出生点 (0, 0.05, -16)，巡逻线只在 y≈±6 方框 ⇒ 该位置只能来自叠加追击）；`susp=0.000 seen=False` 证明衰减链同样在跑；`fps=589` |
| 68 | HUD 运行时显示察觉度、各哨兵档位与操作提示 | Game 视图屏幕源截图（`.tmp-inspect/play_hud.png`） | 00:12 | **通过** — 截图可见「未被发现 / 察觉度 0%」「Guard_Patrol 搜索 可疑点 (0,-18)」「Guard_Sweeper 巡逻」「WASD 移动 · Ctrl 蹲伏 · 右键拖拽转视角」 |
| 69 | 示例场景含地面/掩体/双哨兵/玩家/相机/HUD/时钟，可直接 Play | `scene list_open` → `rootCount=13`；截图核验 | 00:06 / 00:12 | **通过** — 两张截图分别证明掩体、红蓝胶囊、哨兵与玩家均在画面内 |
| 70 | **拆箱即用**（验收 24）：玩家预制件所需组件齐备，WASD 可动、Ctrl 蹲伏 | 示例场景实机 + 探针 `player=... crouch=False` | 00:06 | **部分通过** — 移动/蹲伏代码路径与后端选择已实装并经 Play 验证；蹲伏态切换的截图证据未单独采集 |
| 71 | 编辑态回溯窗口可用（T 量程、滑块推送、单位位姿表） | `StealthScrubWindow` 编译通过 + `Push()` 走 `ScrubTo` + `SceneView.RepaintAll()`；数据层等价由条目 15/52 佐证 | 00:15 | **部分通过** — 窗口与推送路径已交付并编译干净；「拖滑块时 Scene 视图 NPC 连续移动」的编辑态截图待补 |
| 72 | **交锁门禁三项** | `script recompile_status`；`console clear` → 重跑 recompile + test run → `console status`；`python Tools/verify_project.py` | 2026-11-04 00:24 | **本包三项全绿** — ①`failed:false, errors:0` ②`console clear` 后重跑 recompile + test run，`groundTruth` = **`consoleErrors:0 / consoleWarnings:0`**（logs=11，`sampledUtc:2026-11-03T16:24:14Z`）③校验器 **结论: 绿灯**（FAIL 0）。本包 **82/82 全绿**（M1 24 + 契约 18 + M2 30 + M3 10）。<br>全量 210 中有 **2 条失败均属他包**：`Collin.CityMap.Tests.SculptBrushTests.Degenerate_Radii_Return_Zero_Not_Nan` 与 `…Sub_Step_Movement_Accumulates_Across_Calls_Not_Dropped`（堆栈仅含 `com.collin.citymap`，是另一会话 00:20 前后新增的笔刷用例）。按 §2 包互斥与 §1.9「只回滚自己引入的错误」，本会话**未修改**该文件，如实上报 |

## M3+M4 实现轮抓到并修复的缺陷

- **玩家初始朝向写死 180°，导致「打开场景看不见关卡内容」**：控制器 `Awake` 里 `_yaw` 固定为 180°，
  与场景里摆好的朝向无关。第一张 Play 截图里只有玩家孤零零一颗胶囊、掩体与哨兵全部在画面外。
  修法：`_yaw = transform.eulerAngles.y`（所见即所得——在 Scene 里把玩家转向哪里，出生就朝哪里）。
  第二张截图即出现掩体 + 双哨兵 + HUD 全部要素。
- **状态机叠加以当前 transform 为锚会与基线循环依赖**：改为以 `PatrolAgent` 传入的
  **基线世界位置**为锚计算期望偏移，条目 62 专门守住「叠加永不回流基线」。
- **视线射线终点余量的两难**：余量收 0.5m 会让贴身掩体被跳过（隔墙看得见）；收 0.1m 又会让
  玩家自己的胶囊被当成遮挡（哨兵永远看不见玩家）。正解是**全长射线 + 按身份排除目标自身**
  （`CanSee/CanSeeFrom` 新增 `targetOwner` 参数，`DetectionState` 与 `GuardBrain` 都传入）。
- **`GuardBrain.All` 在编辑态恒空**：只靠 `OnEnable` 登记会让 HUD 与 `ResetAll` 静默失效
  （EditMode 不执行生命周期回调，本包第三次撞上同一族坑）。改为「剪枝 + 空则按场景重建」。
- **回溯窗口的三处失效写法**（自查发现）：`readonly` 字段被赋值（编译错）、
  计时基准在 `OnGUI` 里被每次重绘重置（滑块永远不动）、编辑态提前 return（「不 Play 也能看」直接落空）。
- **共享编辑器纪律**：示例生成工具原用 `NewScene(Single)` + 模态对话框——前者会丢弃其他会话未保存的现场，
  后者会把指令通道卡死（本轮实测确实卡住一次）。改为 Additive 建内存场景 → 存盘 → 关闭，
  并把所有 `DisplayDialog` 换成日志。
- **工具事实**：`script eval` 只回 `success`、拿不到返回值；`eval_file` 把内容嵌进方法体
  （顶层不能声明类/函数，须写纯语句）；`scene open` 只认 `Assets/` 下的路径，
  包内场景必须走 `EditorSceneManager.OpenScene`；`capture screenshot --view game` 在 Game 视图非前台时
  **抓不到 IMGUI 覆盖层**，HUD 需改用 `capture game_view --source screen`。

## M5 增量（预制件沉淀 + 拆箱即用验证，2026-11-04，验证者 qianwen-work-assistant · stealth-m5-2026-11-04）

> 新增 Editor 工具两个：`StealthPrefabBaker`（沉淀预制件 + 引用自检）、
> `StealthPrefabVerifier`（内存临时场景只放预制件验证自洽）；
> 示例生成器重写（原地存盘 + 消除材质泄漏）；产物 `Sample/Prefabs/StealthPlayer.prefab`、
> `StealthGuard.prefab` 已落盘。

| # | 验收项（可观测行为） | 验证指令 / 方法 | 日期时间 | 结果与证据 |
|---|---|---|---|---|
| 73 | 玩家单元可沉淀为预制件且保持实例连接 | 菜单 `Tools ▸ Collin ▸ Stealth ▸ 沉淀示例预制件` → `ls Sample/Prefabs/` | 2026-11-04 | **通过** — `StealthPlayer.prefab` 9393 B、`StealthGuard.prefab` 7766 B 均落盘；工具日志 `player=True guard=True` |
| 74 | **预制件自带全部组件与共享资产引用**（拆箱即用的前提） | 烘焙工具内置 `VerifyBody` 自检（Body 三件套 + 路线根引用 + 路线/视野资产非空） | 2026-11-04 | **通过** — 日志：`自检通过：StealthGuard.prefab 携带路线 WarehouseLoop 与视野 GuardNormal，可直接拖进任意空场景使用` |
| 75 | 引用完整性独立复核（不依赖工具自己的日志） | 静态解析两个 `.prefab` 的 `guid:` 引用，与本包 `.cs.meta` / `.asset.meta` GUID 逐一比对 | 2026-11-04 | **通过** — Player 含 `PlayerStealthController`/`DetectionState`/`StealthHud` 脚本引用；Guard 含 `PatrolAgent`/`VisionVolume`/`GuardBrain`/`PatrolRouteRoot` 四个脚本引用 + 路线与视野资产引用；唯一未匹配项 `0000…e000…` 为 Unity 内置资源 GUID，非丢失引用 |
| 76 | **拖进空场景即玩**（验收 24 的完整形态） | 菜单 `验证预制件自洽（内存临时场景）`：临时场景内**不建地面、不建掩体、不手写资产**，只 `InstantiatePrefab` + 一个时钟 | 2026-11-04 | **通过** — 探针日志：`actors新增=1 cycle=28.00s 结论=通过，预制件自洽、拆箱即用`；同时校验 `PrefabInstanceStatus.Connected`（实例未与预制件断开）与「沿 T 周期求值确有位移」 |
| 77 | 哨兵单元分层不形成位姿循环 | 结构约定：根 / `RouteRoot`（静态参考系）/ `Body`（被驱动本体）为兄弟；见 Sample/README 与生成器注释 | 2026-11-04 | **通过** — 静态结构核验；若路线根挂在被驱动本体上，本帧位姿会带动路线根、路线又反过来决定位姿（M3 立「反应不回流基线」时避开的同类循环） |
| 78 | 示例生成器幂等：重复执行不留残骸、不丢场景 | 连续执行菜单两次 + `ls Sample/Scenes/`；对照 git 状态 | 2026-11-04 | **通过（修复后）** — 首轮曾出现「旧场景被删、新场景没写出来」，根因见下方 F-9；改为原地存盘后产物正常 |
| 79 | **交锁门禁三项** | `script recompile_status`；`console clear` → 重跑 recompile + test run → `console status`；`python Tools/verify_project.py` | 2026-11-04 12:41 | **全绿** — ①`failed:false, errors:0` ②`console clear` 后重跑全部验证动作，`groundTruth` = **`consoleErrors:0 / consoleWarnings:0`**（`sampledUtc:2026-11-04T04:41:30Z`）③校验器 **结论: 绿灯**；全量 **210/210**（CityMap 128 / **Stealth 82**） |

## M5 实现轮抓到并修复的缺陷

- **F-9 示例场景凭空消失（自造，已修）**：生成器原先「先 `DeleteAsset` 场景文件 → 再向同一路径 `SaveScene`」。
  但 Unity 既不允许覆盖同路径的已打开场景，也不允许卸载最后一个已加载场景，
  于是**旧文件被删、新文件写不出来**，已入库的示例场景直接消失（曾需 `git checkout` 恢复）。
  正解：**打开目标场景 → 清空根物体 → 原地 `SaveScene(scene)`**，不做任何删除。
- **F-10 编辑态访问 `renderer.material` 报 error 并泄漏材质（自造，已修）**：上色原先写 `renderer.material.color`，
  一次执行刷出 3 条 `Instantiating material ... during edit mode` 的 error，直接破 §1.6b 归零门禁。
  改为显式 `new Material(sharedMaterials[0])` 写回 `sharedMaterials`，效果一致、零报错。
- **F-11 预制件自检断言写错（自造 1 条 error，已修）**：原先断言「哨兵预制件**根**上应有 `PatrolRouteRoot`」，
  而该组件按分层设计挂在 `RouteRoot` 子物体上 ⇒ 必失败并报 error。改由专项 `VerifyBody` 负责结构断言。
- **F-12 「数单位个数」是错的断言方式**：跨包单位枚举走全工程 `FindObjectsByType`，
  而此刻还加载着其他会话正在编辑的场景 ⇒ 绝对值断言既会误报失败也可能凑数假绿。
  改为**实例化前后的差值**（实测 `actors新增=1`）。
- **新环境事实**：若编辑器处于 Play 模式时执行 `test run`，TestRunner 会因无法建/还原场景刷出
  一批 `This cannot be used during play mode` 的 error，**且 `Summary.Total` 返回 0**——
  这不是「工程里没有测试」。跑门禁前先 `editor status` 确认 `playMode: stopped`。
- **新环境事实**：本机 ffmpeg（essentials 构建）具备 `gdigrab` 抓取设备，Unity 主窗口可用
  EnumWindows 按标题定位（`plugin-package - … - Unity 6.6 (6000.6.7f1) <DX12>`，2560×1407）
  ⇒ 自动录屏技术上可行；**本轮未录**，见 U-5。

## 成品质量修正（2026-11-04，用户三条实机反馈，验证者 qianwen-work-assistant · stealth-fix-2026-11-04）

> 用户反馈原文：①第三人称下 A/D 变成旋转镜头、右键拖拽视角抖动；②NPC 与玩家重叠后丧失目标、
> 没有通关点无法结束游戏；③警戒范围要在地面投影，否则看不出边界。

| # | 验收项（可观测行为） | 验证指令 / 方法 | 日期时间 | 结果与证据 |
|---|---|---|---|---|
| 80 | **A/D = 相机相对侧移，与镜头彻底解耦** | EditMode `MoveDirection_*` 3 条 + Play 探针 | 2026-11-04 17:15 | **通过** — 探针 `camYaw=0 D=(1.00,0.00) A=(-1.00,0.00)`（严格反向、不碰镜头）。根因：旧实现「移动朝向」与「镜头朝向」共用同一个 `_yaw`。新实现把镜头角/模型角分离，移动方向数学进 `ThirdPersonRig.MoveDirection`（纯函数） |
| 81 | **拖拽视角不抖**：相机位置与朝向共用同一阻尼、帧率无关平滑、相机移到 LateUpdate | EditMode `Smoothing_IsFrameRateIndependent_AndSnapsOnZeroDt`（2 帧 vs 200 帧收敛差 <6°） | 17:15 | **通过** — 根因是旧实现朝向瞬时赋值、位置单独 `Lerp(dt*10)` 两者脱节；`Lerp(a,b,dt·k)` 本身帧率敏感，换 `1-e^(-k·dt)` |
| 82 | **重叠不丧失目标**：近身档（跳锥不跳遮挡）+ 身体判定点（非脚底） | EditMode `Proximity_*` / `DetectionState_BodyPoint…` + Play 重叠探针 | 17:15 | **通过** — 探针 `overlapFoot=True overlapBody=True`。根因：判定点取脚底，贴脸时几乎在视线正下方，俯仰差 ~80° > 半垂直角 30° 被 L2 判瞎——**越近越瞎**。修复双保险：`BodyPoint`（0.95m）+ `proximityRadius=1.8` 近身档；`Proximity_StillRespectsOcclusion` 守住「贴脸不得穿透掩体看见」 |
| 83 | **有通关点、可结束、可重开**：撤离区驻留通关 + 察觉到底被抓 + R 重开 | EditMode `ExitZone_*` 3 条 + Play 探针 `exits=1 rules=1 outcome=Playing` | 17:15 | **通过** — `StealthExitZone`（纯几何圆判定，不依赖触发器）+ `StealthGameRule`（Escaped/Caught 双结局，Manual 预览态不推进）；HUD 显示撤离方向/距离/驻留进度与胜负横幅。**连带修正**：出生点原 (0,-16) 距巡逻路点仅 10m，站着不动 ~30s 即被抓（探针实测 outcome=Caught）——演示不公平，移至 (0,-22) 后 `playerToGuard=16.4 withinView=False outcome=Playing`；地面同步扩到 60×60m |
| 84 | **地面投影与判定同源**：外缘半径 = sqrt(maxDist²−Δh²)，盲区环口随近身档收合 | EditMode `Projection_*` 5 条 + Play 探针 | 17:15 | **通过** — 探针 `outerR=13.98 expectR=13.98` **逐位吻合**（旧 bug 正是截图里那种只画 ~4.9m：把俯仰角错用成射程上限）。遮挡裁边、近身圈补画、Gizmos 与运行期填充共用同一 `GroundVisionProjection.Build`。面积 187m² 含近身全向圆，符合预期（纯锥面理论值 94） |
| 85 | **运行期半透明填充真在画**：随档位变色、零材质泄漏 | Play 探针 `projections=2`；全程控制台 `error=0 warning=0` | 17:15 | **通过** — `Graphics.DrawMesh` 不污染预制件层级；着色器按 URP→Sprites→内置 兜底、失败只警告一次。**截图核验限制**：本会话自动录屏/抓屏因前台被占不可行（见上轮记录），画面效果由数值探针 + 代码审查佐证，最终以展板视频收版时补 |
| 86 | 全量回归 + 交锁门禁 | recompile → `console clear` → 重跑 recompile + test run → `console status` → `verify_project.py` | 17:19 | **全绿** — 编译 `failed:false`；归零后 `consoleErrors:0 / consoleWarnings:0`；**227/227**（CityMap 128 / **Stealth 99**，新增 `GroundProjectionTests` 17 条 + M3 断言同步 3 处）；校验器 **结论: 绿灯** |

## 导航与投影着色修正（2026-11-04，session stealth-nav-2026-11-04）

> 用户追加两条反馈：①NPC 抓人时穿模（要导航网格）；②地面投影「闪得厉害」（要么改 shader，要么半透 + Z 略高于地面）。

| # | 验收项（可观测行为） | 验证指令 / 方法 | 日期时间 | 结果与证据 |
|---|---|---|---|---|
| 87 | **追击走导航路径、绕开掩体** | 生成菜单内置绕行校验（烘焙后 `NavMesh.CalculatePath` 实测） | 2026-11-04 17:58 | **通过** — `[Nav] 起点=(0.0,0.1,-14.0) 终点=(0.0,0.1,14.0) 拐点=4 直线=28.00 路径=28.13 最大横向偏离=1.17` ⇒「导航烘焙 完成并通过绕行校验」。4 拐点 + 1.17m 横向偏离即掩体已成为障碍的直接证据 |
| 88 | **导航是"可选"的**（删掉子程序集主包不受影响） | `Navigation/` 与 `Navigation/Editor/` 两个独立 asmdef；主包 Editor **不引用**它们 | 18:00 | **通过** — 编译产物 `com.collin.stealth.Navigation.dll` 与 `.Navigation.Editor.dll` 独立存在；注册点自带在可选程序集内 |
| 89 | 绕行只作用于叠加层，**巡逻基线仍是纯函数** | 代码审查 + M3 地基用例 `OverlayMovesTransform_ButBaselineStaysPure` | 18:04 | **通过** — 该用例在导航改动后仍绿：`EvaluateWorld` 恒返回纯基线 |
| 90 | 不可达 / 无桥时**退回直线**，不原地停住 | EditMode `PathBridge_AbsentByDefault_PresentWhenRegistered` 第三段（桥判定不可达） | 18:04 | **通过** — 断言退回直线叠加（z=1.75、x=0） |
| 91 | **地面色块不再闪烁**（透明队列 + 关深度写入 + 深度偏移 + 贴地抬升） | 专用着色器 `Resources/StealthGroundProjection.shader`；`Queue=Transparent`/`ZWrite Off`/`Offset -1,-1`/`_clearance=0.02` | 18:02 | **代码与材质配置已实装并编译通过**。根因确认：复用 `URP/Unlit` 时 `Queue=Geometry`+`ZWrite On`，alpha<1 也不进透明队列 ⇒ 与地面争深度；且多边形与地面**完全共面**。**闪烁的目视复核受取证限制**（Play 模式 `script eval_file` 本轮两次静默失败、自动录屏不可行），需你实机确认 |
| 92 | 着色器 `LightMode` 标签非凭记忆 | 检索 `DrawObjectsPass.cs:87` 的 `shaderTagIds` | 17:50 | **通过** — 首位即 `SRPDefaultUnlit`，URP 与内置管线均会绘制 |
| 93 | 烘焙产物随场景入库 | `git ls-files` → `Sample/Scenes/StealthSample/NavMesh.asset` | 18:05 | **通过** — 场景内 `NavMesh` 字段 23 处；烘焙需静态标记（地面/掩体静态、角色递归取消静态）已写入生成器 |
| 94 | **校验判据自身曾是错的**（自查） | 旧判据「总长 > 直线 + 0.3m」对 28m 绕 1.6m 障碍在几何上不可能成立 | 17:58 | **已修** — 改用「拐点最大横向偏离 > 0.4m」；并为 5 个早退分支各补诊断日志（此前静默 false 只能靠猜） |
| 95 | **可选性守卫补成真的**（自查） | 全仓检索 `STEALTH_NAVMESH_AVAILABLE`，发现只出现在 asmdef、**代码零处 `#if` 引用** | 18:11 | **已修** — `#if` 包住 `using UnityEngine.AI` 与整个 NavMesh 实现，`#else` 提供**同名降级实现**（一律返回 false ⇒ 等同未烘焙，由调用方退回直线叠加）。此前宏是虚设的：真缺内置 AI 模块时该程序集整体编译失败，「删掉目录主包零影响」这条承诺在「模块缺失」路径上并不成立。两分支同名 ⇒ 装配点恒可编译 |
| 96 | 交锁门禁三项 | recompile → `console clear` → 重跑 recompile + test run → `console status` → `verify_project.py` | 18:13 | **全绿**（条目 95 改动**之后**复跑）— `failed:false`；`consoleErrors:0 / consoleWarnings:0`；**227/227**（Stealth 99）；校验器 **结论: 绿灯**。中途一次 `lock-stale` FAIL 是**本会话自身锁的心跳超时**（长测试导致），续期即转绿，非他包问题 |

## 成品质量修正（2026-11-04，session stealth-ux-2026-11-04）

> 用户实机反馈四条：①玩家一出场胶囊半截在地里；②给胜利/失败做简单 UI；③按 R 重开没效果；
> ④sample 稍复杂一点并加外侧围栏。每条都记「根因 → 修法 → 证据」。

| # | 验收项（可观测行为） | 验证指令 / 方法 | 日期时间 | 结果与证据 |
|---|---|---|---|---|
| 97 | **站立时视觉胶囊底边贴脚底，不埋地** | `GameRuleResetTests.CapsulePose_Standing_SitsOnFeetNotHalfBuried` + `Controller_AppliesCapsulePose_AndPreservesAuthorXZ` | 11-04 21:20 | **通过**。根因不是坐标摆错，而是 `PlayerStealthController.ApplyCapsuleShape()` 的公式：旧 `localPosition.y=(h−站高)×0.5` 在**站立时恰好算出 0** ⇒ 整根胶囊中心落到脚底。修法：`ThirdPersonRig.CapsuleVisualPose(h)` 返回 `(scale.y, 中心) = (h/2, h/2)`，底边 = 中心 − scale.y = 0。断言含 x/z 未被覆盖（作者摆的粗细要保留） |
| 98 | **旧公式被钉成反例**（防回退） | `CapsulePose_LegacyFormulaWasTheBug` | 11-04 21:20 | **通过**。用例断言旧公式在站立时**必须等于 0**、新公式必须更高。留这条是因为「中心压到脚底」看起来像合理的居中写法，容易被后人当正确实现改回去 |
| 99 | **蹲伏后仍贴脚底（不悬空不陷地）** | `CapsulePose_CrouchKeepsFeetPlanted` | 11-04 21:20 | **通过** h=1.0 ⇒ scale.y=中心=0.5，底边仍为 0 |
| 100 | **按 R 重开＝全量复位** | Play 探针 `verify_reset.cs`（`ResetRound()` 前后读数） | 11-04 21:10 | **通过**。复位前 玩家=(0,0.05,**24**) 时钟=42.5 → 复位后 玩家=(0,0.05,**−22**)=出生点、时钟=**0.0**、用时=0.00、三哨兵 stage 全 `Patrol`、两撤离区 `reached` 全 False、`frozen=False`、`outcome=Playing`。旧实现只清本组件计时，故观感为「没效果」 |
| 101 | 复位顺序与 teleport 陷阱 | `ResetToSpawn()` 实现 + `ResetRound_*` 5 条单测 | 11-04 21:20 | **通过**。两处必须按序：①写 transform 前**先关 `CharacterController`**（启用态改 transform 会被物理层按旧碰撞结果拉回）；②`Outcome` **最先**改回 Playing（按钮在绘制回调里调用本方法，否则同帧 Update 早退、残留状态清不掉）。相机阻尼基准作废（`_camInitialized=false`），否则镜头从死亡点飞回，看着像没复位 |
| 102 | **出生点不写死坐标**（保 D-13） | `SpawnPoint_DefaultsToAuthorPlacedPosition` + `SpawnPoint_ReRecordsOnLaterCall` | 11-04 21:20 | **通过**。生成器**不再**写 `_spawnOverride`：由 `Awake` 捕获场景摆好的位姿（所见即所得），并公开 `RecordSpawnPoint()` 供作者搬完位置重新定锚。写死坐标会让预制件拖到新场景仍在示例坐标出生 |
| 103 | **结算界面（胜利）实际弹出** | Play：把玩家放进北侧撤离圈 → `focus` 保持前台 → `game_view` 截图 | 11-04 21:12 | **通过（有图）**。画面为压暗遮罩 + 绿色强调条 + 标题「撤离成功」+ 四行归因（用时 0:04 / 察觉峰值 0% / 惊动哨兵 0 名 / 撤离点 已抵达）+ 按钮「再来一局（R）」「关闭本界面」+ 底部「R 重新开始 · Esc 关闭」 |
| 104 | 结算数据层（含失败分支文案） | `Result_*` 5 条单测 | 11-04 21:20 | **通过**。`Playing` 返回 null（进行中不弹卡片）；失败标题「行动失败」且**只有 3 行**（不显示撤离点归因）；`ClockOf` 覆盖 0 / 9.9 / 60 / 120 / 负数（整分进位、负数钳零，不允许出现 `-0:05`）；主提示须含 `R`、次提示须含 `Esc` |
| 105 | **HUD 不再画胜负横幅** | 代码复核 + 条目 103 截图 | 11-04 21:20 | **通过**。HUD 的横幅与 R 键处理已摘除：两处同帧响应重开会**重复触发复位**，且横幅与卡片同时出现自相矛盾。HUD 仅在「场景无 `StealthGameRule`」时提示，属缺件诊断而非胜负呈现 |
| 106 | **外侧围栏四面 + 参与烘焙** | 生成器日志 + `grep -c "m_Name: Fence_"` 场景文件 + Play 截图 | 11-04 21:12 | **通过**。场景内 4 个 `Fence_*`，54×54 内腔、高 4m；**全部标静态**——墙不烘焙则导航面照常铺到墙外，NPC 会穿墙追人（= 上一轮反馈换个位置复现）。东西墙长度让出转角厚度，避免转角两组立方体同空间产生 z-fighting |
| 107 | **场地硬边界（第二层）** | `Arena_*` 5 条单测 | 11-04 21:20 | **通过**。`Clamp` 只改 x/z、**y 原样保留**（纵向归重力）；以场地中心为参照而非世界原点；裕度 0.55m 停在墙内侧（避免贴墙摩擦抖动）。**默认关闭**（半宽 0 ⇒ 恒放行）：预制件拖进小场景不该凭空出现隐形墙 |
| 108 | **围栏之外再兜一层的理由** | 代码复核（`Update` 第⑤步） | 11-04 21:20 | **通过**。钳制走 `_cc.Move(back)` 而非直接写 transform——后者会让控制器内部碰撞状态与实际位置脱节，下一次 `Move` 可能把人挤过墙 |
| 109 | **布局复杂化后巡逻不穿模** | 生成器日志 `[Clearance]` | 11-04 21:02 | **通过**「三条路线与 20 个实体占位无交叉」。新增自动自检：路点连线按 0.5m 采样查是否落进实体 XZ 矩形。**巡逻基线完全不参与碰撞**（D-01 位姿纯函数），摆错位置不报错不卡住、只安静穿模，靠肉眼在 Play 里撞见成本极高 |
| 110 | PingPong 路线不检查闭合段 | `VerifyRouteClearance` 实现 | 11-04 21:02 | **通过**。`CrossSweep` 是原路往返，**没有**末点→首点线段；按 Loop 一并检查会把正常往返误报成穿墙（假阳性会掩盖真问题） |
| 111 | **绕行校验改按场景真实物体取点** | 生成器日志 `[Nav]` 两行 | 11-04 21:02 | **通过**。旧版硬编码起终点 (0,±14)，而出生点在 (0,−22)、撤离点在 (0,24)/(16,16) ⇒ **布局一改，校验量的是地图上不存在的位置却照样"通过"，等于没校验**。现逐撤离区取点：`ExitZone_North 拐点=3 直线=46.00 路径=46.06 横向偏离=1.17`、`ExitZone_Depot 拐点=4 直线=41.23 路径=41.30 横向偏离=0.72` |
| 112 | 绕行判据只对主点强制 | 同上 | 11-04 21:02 | **通过**。仓库点走东侧走廊、直线本就通畅，要求它绕行会把**好布局误报成失败**；可达性（`PathComplete`）则两点都强制 |
| 113 | 双撤离点 + 三哨兵 + 仓库 | 场景文件统计 + 生成日志 | 11-04 21:02 | **通过**。`StealthExitZone` ×2（北侧驻留 0.6s / 仓库内 1.2s）、`GuardBrain` ×3（新增 `DepotRing` 环绕仓库）、实体占位 18→20。单点会让路线只有一种解法，新增结构对玩法毫无影响 |
| 114 | **仓库门洞宽度** | 布局推演 + 条目 111 可达性实测 | 11-04 21:02 | **通过**。原设计净宽 1.4m，因 legacy 烘焙按角色半径**腐蚀可行走面**、窄缺口易被腐蚀成不连通（「看得见进不去」）而加宽到 3.8m。实测该点导航可达（拐点 4），门洞未被封死 |
| 115 | 撤离点与巡逻环线不重叠 | 布局推演 | 11-04 21:02 | **通过**。北侧点取 z=24 而非 22：`DepotRing` 上边在 z=21.5，22 会让撤离圈压在环线正下方，哨兵走到上边线时几乎贴脸站在玩家脸上，0.6s 驻留不可能完成；也不得更靠北（围栏内腔面 z=27，半径 2.4 的外缘会贴到导航面具边缘被腐蚀） |
| 116 | 哨兵本体偏移必须为 0 | 代码复核（`SpawnGuard` 调用处） | 11-04 21:02 | **通过**。本体位置 = 环线 + 偏移，给正偏移会把整条 `DepotRing` 平移到 x=33.5，直接穿出半宽 27 的围栏 ⇒ 哨兵穿过东墙 |
| 117 | 预制件同步本轮修复 | 菜单「沉淀示例预制件」+ 预制件文件核验 | 11-04 21:01 | **通过**。`StealthPlayer.prefab` 含 `StealthResultPanel` ×1、`_arenaHalfSize: {x: 27, y: 27}`、`_arenaMargin: 0.55`、`CapsuleVisual` 的 `m_LocalPosition: {x: 0, y: 0.9, z: 0}`；沉淀日志 `player=True guard=True`、自检通过 |
| 118 | 出生画面目视复核 | Play `game_view` 截图 | 11-04 21:04 | **通过**。蓝色胶囊**完整立于地面**（底部投影清晰，无半截入土），四周深色围栏入画，掩体群/仓库/青色撤离信标齐备 |
| 119 | 地面警戒投影在新布局下仍正常 | 同上截图 | 11-04 21:04 | **通过**。画面右侧可见绿色扇形边界，未因新增墙体错位或消失 |
| 120 | **交锁门禁三项** | recompile → `console clear` → 重跑 recompile + test run → `console status` → `verify_project.py` | 11-04 21:20 | **本包三项全绿**：编译 `failed:false`；`consoleErrors=0 / consoleWarnings=0`；全量 **250/250**（本包 121 = 99 + 新增 22，citymap 129，计数经逐文件 `grep -c "[Test]"` 实测）。校验器仍有一条 `assets-layout` FAIL，来源见 U-8（他包未跟踪文件，与本包改动无关） |

## 注册表缓存自愈修复（2026-11-05，session stealth-u8fix-2026-11-05，验证者 qianwen-work-assistant · stealth-u8fix-2026-11-05）

| # | 验收项（可观测行为） | 验证指令 / 方法 | 日期 | 结果与证据 |
|---|---|---|---|---|
| 121 | **编辑态切场景往返后 `IVisionWorld.Volumes` 不静默归 0**（U-8 收口，根因在 A 包注册表） | 探针 `diag_u8_switch.cs`：`OpenScene(StealthSample)→TerrainTest→StealthSample`（不触发域重载），读直接扫 vs `VisionRegistry.Volumes` vs `IVisionWorld.Volumes` | 11-05 | **通过** — 修复后 `[U8S] 切走前 Registry=3 \| 切到 TerrainTest 后 Registry=0 私有缓存条数=0 \| 切回 StealthSample 后 Registry=3 直接扫=3 **IVisionWorld=3**`（修复前同脚本末项 =0）。`Actors` 走现扫故一直=3，反证「单位正常、视野归 0」= 缓存滞留死引用 |
| 122 | **两张注册表缓存混入已销毁引用时读时自愈**（不依赖事件可靠性） | EditMode `VisionRegistry_StaleCacheAfterDestroy_SelfHeals` + `DetectionRegistry_StaleCacheAfterDestroy_SelfHeals`（**刻意不手动 MarkDirty/ResetForTests**，否则掩掉 bug） | 11-05 | **通过** — 销毁旧物体 + 新建活体后不置脏，`Volumes` 仍纳入新活体、上层 `IVisionWorld.Volumes` 枚举得到；两表对称。⚠️ 既有 `Registries_DoNotLeakDestroyedObjects` 因手动 `MarkDirty` 测不到本缺陷（保留，二者覆盖面不同） |
| 123 | **修复不得拖垮烘焙热路径**（自我纠正记录） | 走查 `CanSeeAt→FindVolume→VisionRegistry.Volumes`（B 包实测 1,970,540 次/烘焙） | 11-05 | **通过** — 第一版误写「非运行态每次读取全量重扫」= 每查询一次 `FindObjectsByType`，已收敛为「O(缓存长度) null 检查 + Editor 侧 `activeSceneChangedInEditMode` 事件 O(1) 置脏」，热路径成本与修复前同阶。事件名与委托签名 `UnityAction<Scene,Scene>` 均经反射核实（本版本无 `...InEditor`） |
| 124 | **交锁门禁** | recompile → `test run` 全量 → `console clear` 静默 70s → `console status` → `verify_project.py` | 11-05 | 本包相关**全绿**：编译 `failed=false`；静默后 `consoleErrors=0/consoleWarnings=0`；全量 **334/334**（本包 123 = 121 + 2，StealthPlanner 70、CityMap 141）。⚠️ 校验器整体 FAIL 4，全是另一会话新增 `channel-discipline` 揪出 citymap/stretch 的历史 `DisplayDialog`（§2.1.5），**属他包未代修**；本包 `runtime-purity`（钩子只加在 Editor 侧）等检查 ok |

## 未通过项

| # | 现象 | 复现步骤 | 状态 |
|---|---|---|---|
| U-1 | 工程整体校验器红灯：`lock-stale` 来自 `com.collin.core.lock` 等**他包**超时锁（holder=qianwen-work-assistant 的历史会话残留） | `python Tools/verify_project.py` | 已如实上报，**用户指示不予处理**（2026-11-03：「别管锁，那不是你的事」）。本包相关检查（pkg-layout / runtime-purity / assets-layout / console / dep-direction）全绿 |
| U-2 | M0 时记录的「本包骨架未取得绿灯证据」 | — | **已由 F-1/F-2 与条目 1-3 闭环，作废** |
| U-3 | 交锁门禁 §1.6b「控制台归零」一度无法达成：`recompile_status` = `failed:true`，`groundTruth` = `consoleErrors:15 / consoleWarnings:3`（2026-11-03 19:26 读数） | `unity command "script recompile_status"`；`unity command "console status"` | **已作废（20:28 复测转绿）** — 当时 15 条 error 全部来自 `com.collin.citymap/Runtime/View/` 两个**未被版本库跟踪**的进行中文件（另一会话写入），按 §2 包互斥与 §1.9 本会话未代其修复，该会话随后自行修好。末次实测：`failed:false, errors:0`、`consoleErrors:1 / consoleWarnings:0`（命中 §1.8 已批准豁免）、校验器 **结论: 绿灯**、全量 141/141 |
| U-4 | 本会话自造 1 条 error：契约的「重复注册」用例故意触发 core 冲突报错，原先用 `LogAssert.Expect` 声明预期 | 该用例运行时 `console status` 计数 | **已修复** — 实测 `LogAssert.Expect` 只让测试不失败、**不回退 `consoleErrors` 计数**，仍破 §1.6b 门禁。改为断言期间 `Debug.unityLogger.logEnabled=false` + `finally` 复位（与 citymap 交接单记录的正解一致） |
| U-5 | **§6 演示视频未交付**：`Showcase.json` 的 `video` 曾为空串、`ShowcaseMedia/` 曾为空目录 | `cat Packages/com.collin.stealth/Showcase.json` | **已闭环（11-04 22:25，收版 0.5.0）** — 用户手动实录 44.7 s 完整演示并交付，本包转码为 `ShowcaseMedia/stealth_demo.mp4`（1616×880，保留音轨）入库、`video` 字段已填充，展板经运行时枚举自动接入。**按用户明确指示：手录视频的内容不作抽帧验证**（仅交付方提出修改要求时才复核），故本条闭环的是「素材已到位 + 展板可加载」，不含画面内容断言（见条目 26）。自动录屏不可行的旧结论不变（已四次确认），录屏始终由用户完成 |
| U-6 | **地面色块闪烁的目视复核未完成**（对应条目 91）：根因与修复方案均已确定并落码，但「不再闪」这一观感结论**尚无画面证据** | 打开 `Sample/Scenes/StealthSample.unity` 按 Play，观察两哨兵脚下扇形是否随视角/档位切换闪烁 | **部分闭环（11-04 21:04）** — 条目 118/119 的 Play 截图已确认投影在新布局下正常渲染、边界清晰无破碎，静态观感成立。**残留**：单张截图无法证明「拖动视角与切档时不闪」这一动态结论，仍待用户实机确认；若仍闪，下一个可疑项是 `_clearance` 与地面共面的容差，而非队列/深度写入配置 |
| U-7 | **失败（被抓）结算卡片缺目视证据**：条目 104 只覆盖数据层文案，「卡片实际弹出」未取到图 | 把玩家放到哨兵视锥行进线上，等察觉顶格 + 持续注视 1.2s | **未完成** — 受阻于共享编辑器的环境事实（见 U-9）：被抓需要数秒时间累积，而窗口一失焦 Play 循环就冻结。与胜利分支共用同一段绘制代码（仅 `Accent`/`Title`/行数不同），胜利卡片已实测弹出（条目 103），故判定风险较低，但**不据此声称已验证** |
| U-8 | 校验器 `assets-layout` FAIL：`Assets/Scenes/testscene.unity` 不在 §0 允许位置 | `python Tools/verify_project.py` | **非本包问题，未代修** — 该文件无提交记录（`git log` 空）、属另一会话的未跟踪产物，且 `com.collin.citymap.lock` 当时仍被 `qianwen-work-assistant` 持有（心跳 43s 前）。按 §2 包互斥与 §1.9 只如实上报，请该会话自行归位 |
| U-9 | **共享编辑器后台不推进 Play 循环**（取证环境限制） | 间隔 4s 两次取样 `Time.frameCount` / `Time.time` / `StealthClock.Current` | **事实记录** — 窗口失焦时三值**完全冻结**（实测均为 13229 / 41.1 / 42.5）；`set_auto_tick` 只保活编辑器 UI，**不驱动 Play 循环**；`focus` 能推进（13229→13602）但每次 CLI 调用都会把焦点抢回终端。⇒ 凡依赖时间累积的玩法（驻留通关、察觉顶格）取证必须「探针 + focus + 立即截图」连做，且不能并行发其他指令 |
