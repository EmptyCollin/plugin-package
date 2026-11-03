# 交接单 — com.collin.stealth

> 覆写式交接单（AGENTS §4）。最近更新：2026-11-04（会话 `stealth-m3m4-2026-11-03`）
> 代码阶段：本会话按 §3 取锁开工，按 §1.6 三项门禁交锁。**未 push。**
> **包版本 0.4.0，已完成 M0 / M1 / M0.5 / M2 / M3 / M4 + M5 大部分：示例场景打开即可 Play。**

## 1. 已完成

- **M1 巡逻求值层**（932cadc）：`PatrolRoute` 弧长累计表 + **位姿为时间的纯函数**；
  路点为相对路线根**局部坐标**（D-09）；`PatrolAgent` 实例级相位/倍数；`StealthClock` Auto|Manual。
- **M0.5 跨包契约**（2784017）：core 新增 `Runtime/StealthContracts/`（3 文件 19 类型，只增不改）；
  A 侧 `StealthWorld` 实现并注册；`StealthEditorBootstrap` 用 `[InitializeOnLoad]` 补编辑态注册。
  core 版本按用户裁定**保持 0.2.0**。
- **M2 四层察觉管线**（477fa3c）：`VisionProfile`/`VisionGeometry`（纯函数）/`VisionVolume`/
  `QueryOptions`/`StealthMask`/`AwarenessModel`/`DetectionState`/两个注册表/`StealthPerformance`/`StealthIds`；
  `IVisionWorld`+`IDetectionWorld` 已注册。
- **M3 哨兵决策**：`IPatrolModifier`（叠加层，**反应永不回流基线**）、`GuardBrain`
  （四档状态机 + 可疑点记忆 + 驻留/环绕计时 + 同伴通报 + 经 core 通道广播察觉事件）、
  `IPathBridge`+`NullPathBridge`（默认不注册）；`PatrolAgent.ApplyBaselineAt(t, dt)` 成为唯一位姿合成点。
- **M4 编辑器工具**：`PatrolRouteRootEditor`（Scene 拖路点、逐点停留/段速、子物体烘焙、朝向指示）、
  `StealthScrubWindow`（**编辑态** T 周期滑块实时推送 + 强制 `SceneView.RepaintAll`）、
  `StealthGizmos`（巡逻折线常显 + 视野扇形选中绘制）。
- **M5 大部分**：`PlayerStealthController`（WASD + Ctrl 蹲伏 + 第三人称相机，拆箱即用）、
  `StealthHud`、`StealthSampleSceneBuilder`（菜单一键生成 `Sample/Scenes/StealthSample.unity`
  + 2 条路线 + 1 份视野参数；场景含地面/3 掩体/2 哨兵/玩家/相机/HUD/时钟）。
- **实机可玩证据**：Play 47.4s 探针——`Guard_Patrol Stage=Searching pos=(-4.5, 0.0, -15.7)`
  （玩家出生点 (0, 0.05, -16)，而巡逻线只在 y≈±6 的方框上 ⇒ 该位置只能来自叠加追击），
  `susp=0.000 seen=False` 证明脱敏衰减同样在跑；屏幕源截图可见 HUD 全部要素与掩体/双哨兵。
- **门禁实测**（2026-11-04 00:24，改版本号触发 `package resolve` 后复跑）：`failed:false, errors:0`；
  `console clear` → 重跑 recompile + test run → `groundTruth` = **`consoleErrors:0 / consoleWarnings:0`**；
  本包 **82/82**（M1 24 + 契约 18 + M2 30 + M3 10）；`verify_project.py` **结论: 绿灯**（FAIL 0）。
  全量 210 中另有 **2 条失败属 citymap**（`SculptBrushTests` 两条，另一会话新增，堆栈不含本包程序集），
  按 §2/§1.9 本会话未代其修复，已记入 ACCEPTANCE 条目 72。

## 2. 实测得到的环境事实（新会话直接照做）

- **EditMode 不执行 MonoBehaviour 生命周期**（本包已撞 3 次）：静态单例要 `FindFirstObjectByType` 兜底、
  `GuardBrain.All` 要「剪枝 + 空则按场景重建」、注册表测试须手动 `MarkDirty()`、
  测试驱动状态机必须显式传 `dt` 与可控 actorId。
- **跨包 id 必须非负**：`GetEntityId().GetHashCode()` 会为负，撞契约 `-1` 哨兵 ⇒ 过滤静默退化、
  按 id 反查失败静默退回默认分支。一律走 `StealthIds.Of`。
- **Unity 6 API**：`GetInstanceID()` 被当 **error** 报（CS0619）；`EntityId` 隐式转 int 同样废弃；
  同时 `using System` + `using UnityEngine` 时裸 `Object.X` 触发 CS0104。
- **测试故意 `LogError` 会破归零门禁**，`LogAssert.Expect` **不回退计数**；
  正解 `Debug.unityLogger.logEnabled=false` + `finally` 复位。本包全程零自造 error。
- **工具事实**：`test run --assembly X` 过滤被服务端忽略（按 `FullName` 前缀自行筛）；
  `unity command` 输出是「JSON + 制表符表格」须 `raw_decode`；`script eval` 只回 success 拿不到返回值；
  `script eval_file` 把内容嵌进方法体（**顶层不能声明类/函数**，须写纯语句 + `Debug.Log` 再读控制台）；
  `scene open` 只认 `Assets/` 下路径 ⇒ **包内场景必须走 `EditorSceneManager.OpenScene`**；
  `capture screenshot --view game` 在 Game 视图非前台时**抓不到 IMGUI 覆盖层**
  ⇒ 验 HUD 要用 `capture game_view --source screen`（base64 在 `data.result.base64`）。
- **CLI 超时不取消服务端执行**：连发 `test run` 会在执行队列串行叠加并把**所有**编辑器指令顶到超时。
  一次提交一次等待。
- **共享编辑器纪律**：禁用 `NewScene(Single)` 与 `EditorUtility.DisplayDialog`
  （前者丢弃他人未保存现场，后者把指令通道卡死——本轮实测卡住一次）。
  建场景用 Additive → 存盘 → `CloseScene` → 还原活动场景。
- **锁会被其他会话清掉**（本会话发生 3 次）：重新原子取锁即可；长测试期间自己的心跳也会超时，
  续期后校验器即转绿。**不要因他人红灯停手**。

## 3. 未完成

- **M5 余项**：把示例里的玩家与哨兵沉淀成 `Sample/Prefabs/StealthPlayer.prefab` /
  `StealthGuard.prefab`（现为场景内直接搭建，功能等价但不可拖拽复用）；
  `ShowcaseMedia/stealth_demo.mp4` 实录视频（§6 两段硬性要求，`Showcase.json` 的 `video` 仍为空）；
  展板接入核验。
- **M6**：冻结对外契约 `Docs/Modules/00-Contract.md`（逐签名核验 DTO 全部来自 core）。
- **待补证据**：编辑态回溯「拖滑块 NPC 连续移动」的截图（条目 71 标注部分通过）；
  50 NPC ≥ 55 FPS 性能验收（条目 12/23）；蹲伏态切换的单独截图（条目 70 部分通过）。
- **已知未实现**：`StealthWorldChangedChannel` / `StealthTimeIndexChannel` 已定义但**尚无资产实例、
  尚无 Raise 调用点**（`GuardBrain` 只用了 `StealthDetectedChannel`，且示例场景没建通道资产）。
  辅助台若依赖这两个通道，需先补资产与广播点。
- 状态机的搜索环绕目前只在水平面（`desired.y = 0`），垂直移动交给未接线的 `IPathBridge`。

## 4. 下一步

1. **M5 收尾**（推荐先做，成本最低、交付感最强）：`prefab create` 把场景里的
   `Player` / `Guard_Patrol` 存成预制件；改 `StealthSampleSceneBuilder` 改为 `prefab instantiate` 搭建；
   录两段式演示视频（①Inspector 拖路点/改停留段速/回溯窗口拖滑块 ②Play 里被发现与脱敏前后对照）填入 `video`。
2. **M6 契约冻结**：核验 `IPatrolWorld`/`IVisionWorld`/`IDetectionWorld` 签名里无 A 类型，写 `00-Contract.md`。
3. **然后可以启动 B 包（设计辅助台）**：A 的四个接口已全部注册可用，安全岛反向接口（`ISafeIslandProvider`）
   等 B 实现——A 侧已按「未注册即降级」写好消费代码。
4. 性能验收：50 NPC（10 路线 × 5 实例）Play 态 ≥ 55 FPS。

## 5. 勿碰项

- **`Packages/com.collin.citymap/**`**：另一会话在迭代（本轮其 `SculptDeltaAsset.cs` CS0136 一度让全项目
  `failed=true` 并卡住测试注册，随后该会话自行修好）。
- **`Packages/com.collin.core/**`**：契约层红线（core DESIGN D-7）——只允许值类型/接口/通道子类，
  **禁止玩法逻辑与判定实现**，禁止签名出现 feature 包类型。
- **`Assets/Showcase/**`**：展板纯 UI，禁止为单个插件改场景文件（§6）。
- **`Packages/manifest.json`、`ProjectSettings/**`**：§7 共享文件。`ProjectSettings.asset:898`
  = `activeInputHandler: 1`（仅新 Input System），**不要改回 Legacy**。
- **§8 钉死版本**：Unity 6000.6.7f1 / pipeline 0.9.1-exp.1 / ai.assistant 2.21.0-pre.2 / ai.inference 2.6.1。
- **不要引入 `com.unity.ai.navigation`**（D-06/R-2）：逐帧积分会摧毁「位姿为时间纯函数」这一地基。
- **两条地基纪律勿破坏**：①基线位姿必须是纯函数，状态机反应只能走 `IPatrolModifier` 叠加；
  ②判定只有一条实现，B 包禁止自研第二套（两套口径必然算出不同结果）。
- **已定案勿重议**：D-01~D-13、Q-A1~Q-A5、方案 P、core 版本保持 0.2.0（用户 2026-11-03 裁定）。
