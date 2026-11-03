# 交接单 — com.collin.stealth

> 覆写式交接单（AGENTS §4）。最近更新：2026-11-03（会话 `stealth-m2-2026-11-03`）
> 代码阶段：本会话按 §3 取锁开工，按 §1.6 三项门禁交锁。**未 push。**

## 1. 已完成（M0 + M1 + M0.5 + M2，包版本 0.3.0）

- **M1 巡逻求值层**（提交 932cadc）：`PatrolRoute`（弧长累计表、路点为相对路线根**局部坐标**、
  布局哈希自动失效重建）、`PatrolPose`、`PatrolRouteRoot`、`PatrolAgent`（实例级相位/倍数）、
  `StealthClock`（Auto|Manual）。**核心承诺：位姿是时间的纯函数**，编辑态与 Play 态同源求值。
- **M0.5 跨包契约**（提交 2784017）：core 新增 `Runtime/StealthContracts/`（3 文件 19 类型，只增不改）；
  A 侧 `StealthWorld` 实现 `IPatrolWorld` + `IStealthScrubDriver`；`Editor/StealthEditorBootstrap`
  用 `[InitializeOnLoad]` 补**编辑态注册**（Play 态的 `RuntimeInitializeOnLoadMethod` 在编辑态不执行）。
  core 版本按用户裁定**保持 0.2.0**（升版会让三包写死的依赖声明解析失败）。
- **M2 四层察觉管线**（本次提交）：11 个 Runtime 文件——
  `VisionProfile`（SO，双阈值 + 遮挡层 + 光照敏感度 + 检测间隔，含 `Set*` 写入面且禁止阈值倒挂）、
  `VisionGeometry`（**纯函数** L1 球检→L2 水平锥+俯仰锥→L3 委托，退化输入安全）、
  `VisionVolume`（实例覆盖：启用/视距倍率/眼高/水平角；L3 处理遮挡层、自身与子节点排除、
  射线预算、错峰缓存；`LineOfSightOverride` 注入点使遮挡与预算分支可在 EditMode 断言）、
  `QueryOptions`/`StealthMask`（包内掩码，**不进 core**）、`AwarenessModel`（积分/导出档位/曝光倍率）、
  `DetectionState`（`dt<=0` 为**纯预览语义**：只更新可见性不推进累积）、
  `VisionRegistry`/`DetectionRegistry`、`StealthPerformance`、`StealthIds`。
  `StealthWorld` 补实现并注册 `IVisionWorld` / `IDetectionWorld`；`StealthClock.Update` 每帧复位射线预算。
- **门禁实测**（2026-11-03 21:47）：`recompile_status` `failed:false, errors:0`；
  `console clear` → 重跑 recompile + test run → `console status` `groundTruth` =
  **`consoleErrors:0 / consoleWarnings:0`**；全量 **172/172**（CityMap 100 / **Stealth 72**）；
  `verify_project.py` 本包项全 ok。

## 2. 实测得到的环境事实（新会话直接照做）

- **跨包 id 必须非负**：`GetEntityId().GetHashCode()` 会返回**负数**，而契约用 `-1` 作
  「不限定单位」哨兵、错峰也按 id 取模。负 id 的后果是**静默的**：`CountSeers` 的单位过滤退化成全局计数；
  巡逻侧与视野侧算法分叉时 `CanSeeAt` 找不到 agent、会悄悄退回当前位姿（「按 t 时刻判定」失效且不报错）。
  ⇒ 一律走 `StealthIds.Of`（内部 `& int.MaxValue`）。
- **`GetInstanceID()` 在 Unity 6 被当 error 报（CS0619）**，`EntityId` 隐式转 int 同样废弃；
  同时 `using System` 与 `using UnityEngine` 时裸写 `Object.X` 触发 CS0104 二义性。
- **EditMode 不执行 MonoBehaviour 的 Awake/OnEnable/OnUpdate**：静态单例要 `FindFirstObjectByType` 兜底；
  注册表测试须**手动 `MarkDirty()`**；帧号必须由外部显式传入（测试里传可控 id + `BeginFrame(n)`），
  不要依赖 `Time.frameCount`。
- **测试故意触发的 error 会破 §1.6b 归零门禁**，`LogAssert.Expect` **只让测试不失败、不回退计数**；
  正解是断言期间 `Debug.unityLogger.logEnabled=false` + `finally` 复位。本包全程零自造 error。
- **`test run --assembly X` 的过滤被服务端忽略**（仍跑全工程），须按 `FullName` 前缀自行筛；
  `test status` 在异步作业下可能读到 `total=0`（结果未落盘），同步 `--timeout 200` 才稳。
- **CLI 侧超时不会取消服务端执行**：连发 `test run` 会在执行队列串行叠加，期间**所有**编辑器指令超时。
  一次提交一次等待。
- **锁会被其他会话清掉**（本会话已发生两次）：重新原子取锁即可，不要因此停手。
  `unity status` 现查现用（本轮 7801 / PID 3512，AGENTS 写的 7800 已过时）。

## 3. 未完成

- **M3**：四档状态机（Patrol→Suspicious→Alerted→Search）+ 警戒广播
  （core 的 `StealthDetectedChannel` / `StealthTimeIndexChannel` / `StealthWorldChangedChannel`
  已定义，**尚无资产实例、尚无 Raise 调用点**）+ `IPathBridge` 寻路接缝。
  当前 `DetectionState` 只算「看见与否 + 察觉度 + 档位」，**没有 NPC 行为响应**。
- **M4**：Editor 侧 Inspector/Handles/Gizmos（世界系路点手柄、「从当前世界位置反推路点」按钮本体）；
  `StealthWorld.ScrubTo` / `RefreshPreview` 的**场景内实测**（数据层与推送路径已实装并有用例，
  但「拖滑动条 NPC 真的在 Scene 视图动」缺截图证据，ACCEPTANCE 条目 30/56 已标注为部分完成）。
- **M5**：`PlayerStealthController` + `StealthPlayer`/`StealthGuard` 预制件 + `Sample/` 白盒场景 +
  实录演示视频（`Showcase.json` 的 `video` 仍为空，`ShowcaseMedia/` 空目录）。
- **M6**：冻结对外契约 `Docs/Modules/00-Contract.md`。
- 性能验收（50 NPC ≥ 55 FPS，条目 12/23）未做，属 M4 之后。

## 4. 下一步（M3 建议顺序）

1. `StealthGuardBehaviour`（或把状态机并入 `PatrolAgent`）：读 `DetectionState.Stage` 驱动
   走向最后可见点 / 搜索邻域。**行为响应必须仍走 M1 的纯函数求值**（可用 `IPatrolModifier` 叠加偏移），
   不要改成 transform 积分——会摧毁编辑态预览。
2. 察觉事件：状态跃迁时 `StealthDetectedChannel.Raise(payload)`，payload 带 `timeIndex`
   （辅助台要靠它把事件钉回回溯时间轴）。Sample 里建通道资产。
3. 警戒广播：Alerted 时通知半径内其他 NPC 转 Search。
4. `IPathBridge` + `NullPathBridge`（默认实现），保持不引用导航包（D-06）。
5. 断言继续写在数据层；每条配 EditMode 用例。

## 5. 勿碰项

- **`Packages/com.collin.citymap/**`**：另一会话在迭代（TerrainView/CityTerrainView 刚写过并报错过）。
- **`Packages/com.collin.core/**`**：本轮未改（M0.5 已提交）。core 契约层红线（DESIGN D-7）：
  只允许值类型/接口/通道子类，**禁止玩法逻辑与判定实现**，禁止签名出现 feature 包类型。
- **`Assets/Showcase/**`**：展板纯 UI，禁止为单个插件改场景文件（§6）。
- **`Packages/manifest.json`、`ProjectSettings/**`**：§7 共享文件。`ProjectSettings.asset:898`
  = `activeInputHandler: 1`（仅新 Input System），**不要改回 Legacy**。
- **§8 钉死版本**：Unity 6000.6.7f1 / pipeline 0.9.1-exp.1 / ai.assistant 2.21.0-pre.2 / ai.inference 2.6.1。
- **不要引入 `com.unity.ai.navigation`**（D-06/R-2）：逐帧积分会摧毁「位姿为时间纯函数」这一地基，
  进而使 B 包的 T 周期回溯不可用。
- **已定案勿重议**：D-01 纯函数、D-09 局部坐标+预制件、D-10 方案 P、D-11 朝向单独缓动、
  D-12 不做听觉、D-13 拆箱即用控制器；Q-A1~Q-A5；core 版本保持 0.2.0（用户 2026-11-03 裁定）。
