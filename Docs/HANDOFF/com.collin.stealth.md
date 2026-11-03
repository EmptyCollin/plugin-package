# 交接单 — com.collin.stealth

> 覆写式交接单（AGENTS §4）。最近更新：2026-11-03（会话 `stealth-m1-2026-11-03`）
> 代码阶段：本会话按 §3 原子取锁开工，按 §1.6 三项门禁交锁。**未 commit、未 push。**

## 1. 已完成（M0 + M1：包骨架 + 巡逻求值层）

- **包骨架**（`Packages/com.collin.stealth/`）：`package.json`（v0.1.0，依赖 core@0.2.0 + inputsystem@1.20.1）、
  Runtime/Editor 两个 asmdef、`Showcase.json`（category=玩法组件，order=30）、Docs 四件套、`Sample/`、`ShowcaseMedia/`。
- **设计定稿**：DESIGN 状态由草案改为**已评审**。Q-A1~Q-A5 全部折入正文（决议表在 DESIGN §9）：
  方案 P（B→A 经 core 接口，asmdef 不互引）／路点局部坐标 + 预制件承载／位置精确折线 + 朝向单独缓动／
  **听觉不做**／**包内自带拆箱即用玩家控制器**。
- **M1 求值层**（Runtime 5 文件）：
  - `PatrolRoute`：SO 共享 Model，路点为**相对路线根的局部坐标**；每点停留 + 段速度（0 走默认）；
    Loop / PingPong；内部「累计长度表 + 布局哈希」自动失效重建；
    **`EvaluatePose(t)` 是时间的纯函数**（弧长定位 + 段内线性插值，不做逐帧积分，不依赖物理/导航）；
    `BuildTrajectorySamples()` 等弧长采样（与逐点求值同口径，供 B 包分层绘制）；`WrapTime()` 折算到 [0,T)。
  - `PatrolPose`：局部位姿 + 段索引 + 段内进度 + 停留标志 + 所属周期。
  - `PatrolRouteRoot`：局部↔世界换算、`ToWorld(pose)`、`BakeWaypointsFromWorld()`（世界→局部反推路点）、`BuildWorldTrajectory()`。
  - `PatrolAgent`：**实例级差异数据**（相位、速度倍数、朝向跟随速度）；位置硬贴保精确、朝向单独缓动（D-11）；`LateUpdate` 由时钟驱动。
  - `StealthClock`：`Auto` / `Manual`；`Manual` 是编辑态回溯的唯一入口。
- **Tests/Editor**：`com.collin.stealth.Tests` + **24 条 EditMode 用例**。
- **交锁门禁**（本包三项全绿）：`recompile_status` → `failed:false, errors:[]`；
  `console clear` → 重跑 recompile + test run → `console status` 的 `groundTruth` =
  `consoleErrors:0 / consoleWarnings:0 / consoleLogs:10`（2026-11-03T08:42:24Z）；
  校验器中本包相关项（pkg-layout / runtime-purity / assets-layout / console / dep-direction）全 `ok`。

## 2. 未完成

- **M2~M6 全部功能**：视野（`VisionProfile`/`VisionVolume`）、四层察觉管线（L1 距离→L2 视角→L3 视线→L4 遮蔽修正）、
  `StealthMask`、`DetectionState`、`AwarenessModel`、四档状态机、警戒广播、`IPathBridge`、`StealthWorldBootstrap`。
- **M0.5（阻塞 B 包，不阻塞本包）**：向 core 追加 `IPatrolWorld`/`IVisionWorld`/`IDetectionWorld` + DTO。
  **须先与用户确认 core 的改动窗口**（core 当前有他包锁）。约束：接口签名里**不得出现本包具体类型**
  （`PatrolPose` 须转成 core 里的 DTO），否则 B 编译仍躲不开引用本包。
- **Editor 程序集目前为空**：Inspector / Handles / Gizmos 全在 M4；`Sample/` 只有 README，预制件与场景在 M5。
- **Showcase.json 的 `video` 为空**，`ShowcaseMedia/` 是空目录（M5 补实录，否则按 §6 判该包演示不合格）。
- 未 commit：本会话全部改动为工作区新增文件（`Packages/com.collin.stealth/**`、`Docs/HANDOFF/com.collin.stealth.md`）。

## 3. 已知坏 / 已抓到的坑（实现轮发现，勿重复踩）

- **朝向弧度 bug（已修）**：`Atan2` 返回弧度，原代码乘了 `1f/Mathf.Rad2Deg` ⇒ +X 段朝向实得 0.027°。
  教训：**只断言位置会漏网，朝向必须单独断言**。
- **编辑态拿不到时钟（已修，且是 B 包可行性关键）**：`StealthClock` 原来只在 `Awake/OnEnable` 登记静态单例，
  而 **EditMode 不执行 MonoBehaviour 生命周期回调** ⇒ `Instance` 恒 null ⇒
  「T 周期滑动条回溯」会在 M4 才发现整条链路不通。现 `Instance` getter 带 `FindFirstObjectByType` 兜底。
- **`test run --assembly` 过滤被服务端忽略**（实测）：传了仍跑全工程 108 条。
  筛本包结果须按 `FullName` 前缀自行过滤，否则会误判「本包测试没跑」。
- **`unity command` 输出是「JSON + 制表符表格」**（实测）：`json.loads` 直接报 Extra data，
  须用 `json.JSONDecoder().raw_decode`；两段式命令名须整体加引号（`"test run"` / `"console status"`），
  否则 Command Not Found。
- 工程整体校验器仍红灯：`lock-stale` 来自**他包**超时锁（core/citymap/stretch，holder 为历史会话残留）。
  用户 2026-11-03 明确「别管锁」，故本轮未处理，也不再重复上报为待办。

## 4. 下一步（M2 建议顺序）

1. `VisionProfile`（SO）+ `VisionVolume`（实例覆盖）：水平视角、视距、视线高度、俯仰角、遮挡 LayerMask、光照敏感度。
2. `StealthMask`（flags：`LineOfSight=1`、`CoverOrCrouch=2`、`SafeIsland=8`；`4/16/32/64` 预留不启用）。
3. 四层管线 `QueryOptions`（`DistanceOnly`/`ConeOnly`/`LineOfSight` 掩码）——**必须可分层取结果**，
   B 包分层查看器直接调本函数，**禁止 B 自研第二套判定**（两套逻辑必然算出不同结果）。
4. L3 射线预算制：`raycastBudgetPerFrame` + 每 NPC 每 N 帧 + 错峰；命中过滤 `ownerGameObject` 及子节点。
5. `DetectionState` + `AwarenessModel`（累积/衰减曲线）；察觉度断言写在数据层，不写 UI。
6. 每条补 EditMode 用例；测试若故意触发 error 会计入 `consoleErrors` 破坏归零门禁 ⇒
   断言期间用 `Debug.unityLogger.logEnabled=false` + `finally` 复位（citymap 已验证此法有效）。

## 5. 勿碰项

- **`Assets/Showcase/**`**：展板是纯 UI，运行时枚举 `Showcase.json`；**禁止为单个插件改展板场景文件**（§6）。
- **`Packages/com.collin.core/**`、`citymap/**`、`stretch/**`**：均有他包锁；core 只允许在用户批准的 M0.5 窗口内「只增不改」。
- **`Packages/manifest.json`、`ProjectSettings/**`**：§7 共享文件，改动须先向用户申报。
  注意 `ProjectSettings.asset:898` 是 `activeInputHandler: 1`（仅新 Input System）——
  **不要改回 Legacy**，本包玩家控制器按新后端实现。
- **§8 钉死版本**：Unity 6000.6.7f1 / pipeline 0.9.1-exp.1 / ai.assistant 2.21.0-pre.2 / ai.inference 2.6.1，
  升级须走 ADR + 用户批准。
- **不要引入 `com.unity.ai.navigation`**（DESIGN D-06/R-2）：NavMeshAgent 逐帧积分会摧毁「位姿为时间纯函数」这一地基，
  进而使 B 包回溯预览不可用。寻路只走 `IPathBridge` 接缝。
- **DESIGN 已定案项勿回头重议**：D-01 纯函数、D-09 局部坐标+预制件、D-11 朝向缓动、
  D-12 不做听觉、方案 P、13 条决策与 Q-A1~Q-A5 决议表。
- `.locks/` 目录内的非锁残留文件（`UrlCmp.cs`、`patch_fix.py`、`profiles_backup` 等）属他会话产物，未获授权不得清理。
