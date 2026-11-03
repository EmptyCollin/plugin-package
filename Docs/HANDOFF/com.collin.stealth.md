# 交接单 — com.collin.stealth

> 覆写式交接单（AGENTS §4）。最近更新：2026-11-03（会话 `stealth-m05-2026-11-03`）
> 代码阶段：本会话按 §3 取锁开工（stealth + core 两包），按 §1.6 三项门禁交锁。**未 push。**

## 1. 已完成（M0 + M1 + M0.5）

- **M1 巡逻求值层**（见上一轮，已提交 932cadc）：`PatrolRoute`（弧长累计表、路点为相对路线根局部坐标、
  布局哈希自动失效重建）、`PatrolPose`、`PatrolRouteRoot`、`PatrolAgent`（实例级相位/倍数）、`StealthClock`（Auto|Manual）。
- **M0.5 跨包契约（方案 P 闭环）**：
  - **core 侧新增** `Runtime/StealthContracts/`（3 文件 / 19 个公开类型，**只增不改**，未触碰既有 4 个源文件）：
    `StealthDtos.cs`（位姿含 `valid` 位、路点、轨迹采样、单位、视野快照、时钟、察觉载荷
    + `StealthLoopMode`/`StealthAlertStage`/`StealthQueryLayers` 三组镜像枚举）、
    `StealthWorldInterfaces.cs`（`IPatrolWorld`/`IVisionWorld`/`IDetectionWorld`/`ISafeIslandProvider`/`IStealthScrubDriver`）、
    `StealthChannels.cs`（3 个 `EventChannel<T>` 具体子类）。
  - **A 侧实现** `Runtime/StealthWorld.cs`：实现 `IPatrolWorld` + `IStealthScrubDriver` 并经 `Services` 注册；
    `ScrubTo(t)` **显式推送**位姿（编辑态无帧循环，B 包滑动条的前提）；`RefreshPreview()` 供 Inspector 改路点即时刷新；
    `TrySetWaypoint` 是「按玩家线路反推巡逻路点」的落点。
  - **A 侧 Editor 钩子** `Editor/StealthEditorBootstrap.cs`：`[InitializeOnLoad]` 编辑态注册
    + 撤销回调触发预览刷新（**不改时钟模式、不改 t**）。
  - **消费方视角测试** `Tests/Editor/StealthContractConsumerTests.cs`（16→18 条）：
    4 个 Fake 实现**只用 `Collin.Core.Stealth` 符号**，证明 B 不引用 A 也能实现与取用契约；
    含跨包镜像枚举值序一致性守卫。
- **门禁达成（本会话末次实测）**：`recompile_status` → `failed:false, errors:0`；
  `console clear` → 重跑 recompile + test run → `console status` `groundTruth` =
  `compilationFailed:false, consoleErrors:1, consoleWarnings:0`（该 1 条命中 §1.8 已批准豁免清单）；
  `python Tools/verify_project.py` → **结论: 绿灯**（FAIL 0 / WARN 4，均为锁 active 提示与已豁免噪声）。
  全量测试 **141/141 通过**（CityMap 99 / Stealth 42）。

## 2. 实测得到的环境事实（新会话直接照做）

- **`Object.GetInstanceID()` 在 Unity 6 被编译器当 error 报（CS0619）**；`EntityId` 隐式转 int 同样废弃
  ⇒ 作字典键用 `GetEntityId().GetHashCode()`（stretch 包早已记录此坑，本包首次撞上）。
- **同时 `using System;` 与 `using UnityEngine;` 时，裸写 `Object.XXX` 触发 CS0104 二义性**，须全限定。
  core v0.1.0 的 `PackageInfo` 同名坑同源，属可预防的重复错误。
- **测试故意触发的 error 会计入 `consoleErrors` 破 §1.6b 归零门禁**；`LogAssert.Expect` **只让测试不失败、
  不回退计数**（citymap 交接单已记，本轮实测复现）。正解：断言期间 `Debug.unityLogger.logEnabled=false` + `finally` 复位。
- **`test run --assembly X` 的过滤被服务端忽略**（仍跑全工程），筛本包结果须按 `FullName` 前缀自行过滤。
- **`test run` 是异步指令**：`--detach` 后 `job wait` 会返回 `completed` 但 `test status` 可能读到 `total=0`
  （结果尚未落盘）；改回同步调用（`--timeout 150`）可拿到完整 Summary。
- **CLI 侧超时不会取消服务端执行**：连发 3 次同步 `test run` 会让三次在全工程执行队列里串行叠加，
  期间**所有**编辑器指令（含 `console status`、`editor status`）都超时。单次提交 + 等待即可。
- **`unity command` 输出是「JSON + 制表符表格」**，须 `json.JSONDecoder().raw_decode`。
- **编辑器端口/PID 现查现用**：本轮 `unity status` = 7801 / PID 3512（AGENTS.md 写的 7800 已过时）。
- **锁会被其他会话清理**：本会话取得的 core/stealth 锁中途消失过一次，重新原子取得即可；
  另一会话正在并行迭代 citymap（其 `m3-view` 锁 active），它的编译错误一度污染全项目 `failed=true`，
  随后由该会话自行修好——**不要替他修，也不要因此停手**。

## 3. 未完成

- **M2~M6 全部功能**：视野（`VisionProfile`/`VisionVolume`）、四层察觉管线、`StealthMask`、`DetectionState`、
  `AwarenessModel`、四档状态机、警戒广播、`IPathBridge`。
- **契约的 `IVisionWorld` / `IDetectionWorld` 已定义但故意未注册**（提供方未实现）——
  B 包现在取用它们会走优雅降级；M2/M3 完成后由 `StealthWorld` 补注册。
- **`ScrubTo` 的场景内实测待 M4**：数据层与推送路径已实装，但「滑动条拖动时 NPC 在 Scene 视图真的动」
  尚无截图/实测证据（ACCEPTANCE 条目 30 明确标注为部分完成）。
- Editor 侧 Inspector/Handles/Gizmos 未做（`Editor/` 目前只有注册钩子）。
- `Sample/` 只有 README；预制件与白盒场景在 M5；`Showcase.json` 的 `video` 仍为空。
- core 版本号仍为 0.2.0（详见 core CHANGELOG 的 Notes：升版需同一次改动里更新三个包的依赖声明，属 §7 申报范畴）。

## 4. 下一步（M2 建议顺序）

1. `VisionProfile`（SO）+ `VisionVolume`（实例覆盖）：水平视角、视距、视线高度、俯仰、遮挡 LayerMask、光照敏感度。
2. `StealthMask`（A 内部 flags，**不进 core**；跨包只用 `StealthQueryLayers` 三档）。
3. 四层管线，**必须可分层取结果**；L3 走射线预算（`raycastBudgetPerFrame` + 每 NPC 每 N 帧 + 错峰）；
   命中过滤 `ownerGameObject` 及子节点。
4. `DetectionState` + `AwarenessModel`（累积/衰减），察觉断言写在数据层。
5. 在 `StealthWorld` 补注册 `IVisionWorld`/`IDetectionWorld`，并让 `BuildTrajectory` 那套
   「单一求值路径」纪律同样适用于视野（**B 禁止自研第二套判定**）。
6. 补 `Editor/` 的 Inspector/Handles/Gizmos，并做 M4 的 `ScrubTo` 场景内实测（截图 + 与 Play 态轨迹对比）。

## 5. 勿碰项

- **`Packages/com.collin.citymap/**`**：另一会话正在迭代（`m3-view`，TerrainView/CityTerrainView 刚写完）。
- **`Assets/Showcase/**`**：展板纯 UI，禁止为单个插件改场景文件（§6）。
- **`Packages/manifest.json`、`ProjectSettings/**`**：§7 共享文件。`ProjectSettings.asset:898` =
  `activeInputHandler: 1`（仅新 Input System），**不要改回 Legacy**。
- **core 契约层的红线**（DESIGN D-7）：只允许值类型/接口/通道子类，**禁止玩法逻辑与判定实现**，
  禁止签名出现 feature 包类型——一旦违反，方案 P（feature 互不引用）当场失效。
- **§8 钉死版本**：Unity 6000.6.7f1 / pipeline 0.9.1-exp.1 / ai.assistant 2.21.0-pre.2 / ai.inference 2.6.1。
- **不要引入 `com.unity.ai.navigation`**（D-06/R-2）：逐帧积分会摧毁「位姿为时间纯函数」这一地基。
- **DESIGN 已定案勿重议**：D-01~D-13、Q-A1~Q-A5、§4.3「M0.5 实装版」签名（与草案的 3 处差异已注明）。
