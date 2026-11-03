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
| 26 | **演示视频两点**：①怎么用（真实编辑器操作：挂组件、Inspector 拖路点、拖动倍数滑条，可见控件与鼠标动作）②用了之后的**效果**（Scene 视图即时响应 + 改前/改后对照） | `ShowcaseMedia/stealth_demo.mp4` 实机录屏，展板播放核验 | — | — | 待验证（M5）；**若受环境限制无法实录，须在本行标注「示意（非实录）」并说明原因，否则按 §6 视为该包演示不合格** |

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

## 未通过项

| # | 现象 | 复现步骤 | 状态 |
|---|---|---|---|
| U-1 | 工程整体校验器红灯：`lock-stale` 来自 `com.collin.core.lock` 等**他包**超时锁（holder=qianwen-work-assistant 的历史会话残留） | `python Tools/verify_project.py` | 已如实上报，**用户指示不予处理**（2026-11-03：「别管锁，那不是你的事」）。本包相关检查（pkg-layout / runtime-purity / assets-layout / console / dep-direction）全绿 |
| U-2 | M0 时记录的「本包骨架未取得绿灯证据」 | — | **已由 F-1/F-2 与条目 1-3 闭环，作废** |
| U-3 | 交锁门禁 §1.6b「控制台归零」一度无法达成：`recompile_status` = `failed:true`，`groundTruth` = `consoleErrors:15 / consoleWarnings:3`（2026-11-03 19:26 读数） | `unity command "script recompile_status"`；`unity command "console status"` | **已作废（20:28 复测转绿）** — 当时 15 条 error 全部来自 `com.collin.citymap/Runtime/View/` 两个**未被版本库跟踪**的进行中文件（另一会话写入），按 §2 包互斥与 §1.9 本会话未代其修复，该会话随后自行修好。末次实测：`failed:false, errors:0`、`consoleErrors:1 / consoleWarnings:0`（命中 §1.8 已批准豁免）、校验器 **结论: 绿灯**、全量 141/141 |
| U-4 | 本会话自造 1 条 error：契约的「重复注册」用例故意触发 core 冲突报错，原先用 `LogAssert.Expect` 声明预期 | 该用例运行时 `console status` 计数 | **已修复** — 实测 `LogAssert.Expect` 只让测试不失败、**不回退 `consoleErrors` 计数**，仍破 §1.6b 门禁。改为断言期间 `Debug.unityLogger.logEnabled=false` + `finally` 复位（与 citymap 交接单记录的正解一致） |
