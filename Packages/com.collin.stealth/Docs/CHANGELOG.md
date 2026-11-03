# CHANGELOG — com.collin.stealth

格式：Keep a Changelog（https://keepachangelog.com）；版本号：semver。

## [Unreleased]

### Added
- M2：`VisionProfile` / `VisionVolume` / 四层察觉管线 / `StealthMask` / `DetectionState`。
- M3：察觉状态机 + 警戒广播 + `IPathBridge` 寻路接缝。
- M4：Editor 侧 Inspector/Handles/Gizmos（世界系路点手柄 + 「从当前世界位置反推路点」按钮本体）+ 编辑态回溯预览的场景内核验。
- M5：`PlayerStealthController`（新 Input System 优先、条件编译回退旧后端）+ `StealthPlayer` / `StealthGuard` 预制件 + `Sample/Scenes/StealthSample.unity` 白盒场景 + 实录演示视频 + 展板接入。
- M6：冻结对外契约 `Docs/Modules/00-Contract.md`（逐签名核验 DTO 全部来自 core），交付给 B 包消费。

### Changed
- 2026-11-03 用户拍板 Q-A1~Q-A5 后调整设计：新增 `com.unity.inputsystem` 依赖；路点改为相对坐标 + 预制件承载；**听觉/声音侦测移出本包范围**（最小可用子集），`StealthMask` 对应位改为预留位不启用；玩家移动控制器由「交给外部工程」改为「包内自带、拆箱即用」。

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
