# 交接单 — com.collin.stealth

> 覆写式交接单（AGENTS §4）。最近更新：2026-11-04（会话 `stealth-m5-2026-11-04`）
> 代码阶段：本会话按 §3 取锁开工，按 §1.6 三项门禁交锁。**未 push。**
> **包版本 0.4.0（M5 未收版，见 §3）。已完成 M0 / M1 / M0.5 / M2 / M3 / M4 + M5 的预制件部分；
> 示例场景打开即可 Play，预制件可拖进空场景即用。**

## 1. 已完成

- **M1 巡逻求值层**（932cadc）：`PatrolRoute` 弧长累计表 + **位姿为时间的纯函数**；
  路点为相对路线根**局部坐标**（D-09）；`PatrolAgent` 实例级相位/倍数；`StealthClock` Auto|Manual。
- **M0.5 跨包契约**（2784017）：core 新增 `Runtime/StealthContracts/`（3 文件 19 类型，只增不改）；
  A 侧 `StealthWorld` 注册实现；`StealthEditorBootstrap` 用 `[InitializeOnLoad]` 补编辑态注册。
  core 版本按用户裁定**保持 0.2.0**。
- **M2 四层察觉管线**（477fa3c）：`VisionProfile` / `VisionGeometry`（纯函数）/ `VisionVolume` /
  `QueryOptions` / `StealthMask` / `AwarenessModel` / `DetectionState` / 两注册表 / `StealthPerformance` / `StealthIds`；
  `IVisionWorld` + `IDetectionWorld` 已注册。
- **M3 哨兵决策**（74bc6af）：`IPatrolModifier`（叠加层，**反应永不回流基线**）+ `GuardBrain`
  （四档状态机 / 可疑点记忆 / 驻留环绕 / 同伴通报 / 通道广播）+ `IPathBridge`+`NullPathBridge`；
  `PatrolAgent.ApplyBaselineAt(t, dt)` 成唯一位姿合成点。
- **M4 编辑器工具**（74bc6af）：`PatrolRouteRootEditor`（Scene 拖路点 + 逐点停留/段速 + 子物体烘焙）、
  `StealthScrubWindow`（**编辑态** T 周期滑块 + 强制 `SceneView.RepaintAll`）、`StealthGizmos`。
- **M5 已交付部分**（本会话，未收版）：
  - `PlayerStealthController`（WASD + Ctrl 蹲伏 + 第三人称相机，三档输入后端自动选择）、`StealthHud`、
    `StealthSampleSceneBuilder`（菜单一键生成示例，**已重写**：打开目标场景原地存盘）。
  - `StealthPrefabBaker`：沉淀 `Sample/Prefabs/StealthPlayer.prefab`（9393 B）/
    `StealthGuard.prefab`（7766 B），内置引用完整性自检。
  - `StealthPrefabVerifier`：内存临时场景只放预制件 + 时钟，实测 `actors新增=1 cycle=28.00s 结论=通过`。
  - 静态 GUID 级引用比对独立复核通过（Guard 自带 4 个脚本引用 + 路线/视野资产引用）。
- **实机可玩证据**（M3+M4 轮）：Play 47.4s 探针 `Guard_Patrol Stage=Searching pos=(-4.5,0,-15.7)`
  （玩家出生点 (0,0.05,-16)、巡逻线仅 y≈±6 ⇒ 只能来自叠加追击），`susp=0.000 seen=False` 证明衰减链在跑；
  屏幕源截图含 HUD 全要素。
- **门禁实测（本会话末次，2026-11-04 12:41）**：`failed:false, errors:0`；
  `console clear` → 重跑 recompile + test run → `console status` `groundTruth` =
  **`consoleErrors:0 / consoleWarnings:0`**；全量 **210/210**（CityMap 128 / **Stealth 82**）；
  `verify_project.py` **结论: 绿灯**。

## 2. 实测得到的环境事实（新会话直接照做）

- **Play 模式下跑 `test run`**：TestRunner 刷出一批 `This cannot be used during play mode` error，
  且 **`Summary.Total` 返回 0**（看着像「工程里没有测试」）。跑门禁前先 `editor status` 确认 `playMode: stopped`。
- **Unity 不允许覆盖同路径的已打开场景，也不允许卸载最后一个已加载场景**：
  「删场景文件 → 向同一路径 SaveScene」会导致旧文件被删、新文件写不出来。
  正解 = **打开目标场景 → 清空根物体 → 原地 `SaveScene(scene)`**。
- **编辑态访问 `renderer.material`**：即时报 error 并泄漏材质（破归零门禁）。上色用
  `new Material(sharedMaterials[0])` 写回 `sharedMaterials`。
- **EditMode 不执行 MonoBehaviour 生命周期**（本包已撞 4 次）：静态单例要 `FindFirstObjectByType` 兜底、
  `GuardBrain.All` 要「剪枝 + 空则重建」、注册表测试须手动 `MarkDirty()`、组件引用要惰性 `GetComponent`。
- **跨包 id 必须非负**：`GetEntityId().GetHashCode()` 会为负，撞契约 `-1` 哨兵 ⇒ 过滤静默退化、
  反查失败静默退回默认分支。一律走 `StealthIds.Of`。
- **跨包单位枚举是全工程搜索**：验证/断言要用**实例化前后差值**，不能用绝对值
  （当前还加载着其他会话正在编辑的场景，绝对值既会误报也可能凑数假绿）。
- **Unity 6 API**：`GetInstanceID()` 被当 error（CS0619）；`EntityId` 隐式转 int 同样废弃；
  `using System` + `using UnityEngine` 时裸 `Object.X` 触发 CS0104。
- **测试故意 `LogError` 会破归零门禁**，`LogAssert.Expect` **不回退计数**；
  正解 `Debug.unityLogger.logEnabled=false` + `finally` 复位。本包全程零自造 error（修复后）。
- **工具事实**：`test run --assembly X` 过滤被服务端忽略（按 `FullName` 前缀自筛）；
  `unity command` 输出是「JSON + 制表符表格」须 `raw_decode`；`script eval` 只回 success 无返回值；
  `script eval_file` 把内容嵌进方法体（顶层不能声明类/函数，须纯语句 + `Debug.Log` 再读控制台）；
  `scene open` 只认 `Assets/` 路径 ⇒ 包内场景必须 `EditorSceneManager.OpenScene`；
  Game 视图非前台时 `capture screenshot --view game` **抓不到 IMGUI**，验 HUD 用
  `capture game_view --source screen`（base64 在 `data.result.base64`）；改 package.json 版本号后须 `package resolve`。
- **CLI 超时不取消服务端执行**：连发 `test run` 会在执行队列串行叠加并把**所有**编辑器指令顶到超时。
- **共享编辑器纪律**：禁 `NewScene(Single)`（丢他人未保存现场）、禁 `EditorUtility.DisplayDialog`
  （卡死指令通道，本轮实测卡住一次）。切场景前先查 `isDirty`；建临时场景用 Additive + 用完 CloseScene。
- **锁会被其他会话清掉**（已发生 4 次）：重新原子取锁即可；长测试期间自己心跳也会超时，续期即转绿。
- **本机 ffmpeg（essentials）具备 `gdigrab`**，Unity 主窗口可用 `EnumWindows` 按标题定位
  （`plugin-package - … - Unity 6.6 (6000.6.7f1) <DX12>`，2560×1407）⇒ 自动录屏技术可行。
- **并行实况**：另一会话在持续迭代 citymap（其编译错误多次污染全项目 `failed=true`，均自行修好）。
  **不要替他修，也不要因他人红灯停手。**

## 3. 未完成

- **M5 余项（阻塞该包演示合格）**：
  1. `ShowcaseMedia/stealth_demo.mp4` **两段式实录视频**（①怎么用：挂组件、Inspector 拖路点、
     拉视野滑条、回溯窗口拖滑块，须看到控件与鼠标动作；②用了之后的效果：Scene 即时响应 + 改前改后对照）。
     参照 stretch 做法：用户手动录屏 → 包内工具剪辑入库（stretch 源片未入库、成片 55.6s）。
     **未录前本包按 §6 判演示不合格，版本号仍留 0.4.0**。
  2. `Showcase.json` 的 `video` 仍为空串；展板接入核验未做（校验器在 `video` 为空时不报 FAIL）。
- **M6**：冻结对外契约 `Docs/Modules/00-Contract.md`（逐签名核验 DTO 全部来自 core）。
- **待补证据**：编辑态回溯「拖滑块 NPC 连续移动」的截图（条目 71 部分通过）；
  50 NPC ≥ 55 FPS 性能验收；蹲伏态切换的单独截图（条目 70 部分通过）。
- **已知未接线**：`StealthWorldChangedChannel` / `StealthTimeIndexChannel` 已定义但
  **无资产实例、无 Raise 调用点**（`GuardBrain` 只用了 `StealthDetectedChannel`，且示例场景未建通道资产）。
  B 包若依赖这两个通道需先补。
- 搜索环绕只在水平面（`desired.y = 0`）；垂直移动留给未接线的 `IPathBridge`。

## 4. 下一步（建议顺序）

1. **补演示视频**（唯一阻塞 M5 收版项）：请用户手动录一段编辑器操作 + Play 效果，
   我这边用 ffmpeg 剪成两段式并填 `Showcase.json.video`，然后版本号收 0.5.0。
2. **M6 契约冻结** → 交付 B 包（设计辅助台）消费：四个接口已全部注册可用，
   `ISafeIslandProvider` 反向等 B 实现（A 侧「未注册即降级」已写好）。
3. 若先做 B 包：`Showcase.json` order 拟 31（core=0 / stretch=10 / citymap=20 / stealth=30）。
4. 性能验收（50 NPC ≥ 55 FPS）与两项截图补采。

## 5. 勿碰项

- **`Packages/com.collin.citymap/**`**：另一会话在迭代（本轮其 `SculptBrushTool.cs`、`CityWindow.cs`
  数次让全项目 `failed=true`，均自行修好）。
- **`Packages/com.collin.core/**`**：契约层红线（core DESIGN D-7）——只允许值类型/接口/通道子类，
  **禁止玩法逻辑与判定实现**，禁止签名出现 feature 包类型；core 版本保持 0.2.0。
- **`Assets/Showcase/**`、`Assets/Scenes/SampleScene.unity`**：展板是纯 UI，禁止为单个插件改场景文件（§6）；
  `SampleScene` 是模板默认场景，**不要往里塞潜行内容**。
- **`Packages/manifest.json`、`ProjectSettings/**`**：§7 共享文件。`ProjectSettings.asset:898`
  = `activeInputHandler: 1`（仅新 Input System），**不要改回 Legacy**。
- **§8 钉死版本**：Unity 6000.6.7f1 / pipeline 0.9.1-exp.1 / ai.assistant 2.21.0-pre.2 / ai.inference 2.6.1。
- **不要引入 `com.unity.ai.navigation`**（D-06/R-2）：逐帧积分会摧毁「位姿为时间纯函数」这一地基。
- **两条地基纪律勿破坏**：①基线位姿必须是纯函数，状态机反应只走 `IPatrolModifier` 叠加；
  ②判定只有一条实现，B 包禁止自研第二套。
- **哨兵单元三层结构（根 / RouteRoot / Body）勿合并**：路线根挂在被驱动本体上会形成位姿循环。
- **已定案勿重议**：D-01~D-13、Q-A1~Q-A5、方案 P、core 版本 0.2.0、stealth 版本暂留 0.4.0。
