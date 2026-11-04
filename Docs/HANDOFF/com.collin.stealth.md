# 交接单 — com.collin.stealth

> 覆写式交接单（AGENTS §4）。最近更新：2026-11-04（会话 `stealth-fix-2026-11-04`）
> **包版本 0.4.0**（M5 演示视频仍未录，故不收 0.5.0）。
> 本轮响应用户三条实机反馈做成品质量修正，全部**实测闭环**（含 Play 态数值探针）。

## 1. 已完成（本轮）

用户反馈：①第三人称下 A/D 变成旋转镜头、右键拖拽视角抖动；②NPC 与玩家重叠后丧失目标、没有通关点；③警戒范围要在地面投影。

- **操控层重写**：新增 `ThirdPersonRig`（纯函数：相机相对 `MoveDirection`、帧率无关 `SmoothAngle/SmoothValue`、
  `ClampPitch` 自动交换倒挂上下限）。`PlayerStealthController` 改为
  **镜头角 `_camYaw` 与模型角 `_moveYaw` 分离**（旧实现共用 `_yaw` ⇒ 按 A/D 就转镜头）、
  相机**位置与朝向共用同一阻尼**并移到 `LateUpdate`（旧实现朝向瞬时 + 位置单独 Lerp ⇒ 拖拽抖动）、
  新增 Shift 跑、公开 `CameraYaw/MoveYaw`。
- **近身察觉**：`VisionProfile` 加 `proximityRadius`（跳 L2 锥、**不跳 L3 遮挡**）；
  `DetectionState.BodyPoint`（身体判定点 0.95m，蹲伏下移）统一被 `GuardBrain`、自身感知、安全岛掩码与投影使用。
  旧 bug 根因：判定点取脚底，贴脸时方向几乎正下、俯仰差 ~80° > 半垂直角 30° ⇒ **越近越瞎**。
- **地面投影**：`GroundVisionProjection`（纯函数，**与判定同源**）：外缘 sqrt(maxDist²−Δh²)、
  内缘 Δh/tan(半垂直角)、近身豁免覆盖盲区时内缘收 0（连续扇形）、遮挡射线裁边；
  `StealthGroundProjection`（Play 态 `Graphics.DrawMesh` 半透明填充，随档位变色，不碰 `.material`）；
  `StealthGizmos` 改为调用同一 Build（编辑态与运行期一套几何）。
  旧 bug：把俯仰角当**射程上限**，14m 视距只画 ~4.9m（正是用户截图的现象）。
- **通关闭环**：`StealthExitZone`（纯几何圆 + 驻留计时，不依赖触发器/Rigidbody）
  + `StealthGameRule`（Escaped/Caught 双结局，Manual 预览态不推进）+ HUD 撤离指引/被抓倒计时/胜负横幅/R 重开。
- **示例场景连带修正**：出生点 (0,-16)→(0,-22)（实测原地站 30s 即被抓，演示不公平）、地面 40→60m、
  北侧 (0,16) 撤离区 + 信标柱、`GuardNormal.asset` 显式配 proximityRadius=1.8 + 遮挡层全开、哨兵 Body 挂投影。
- **测试**：新增 `GroundProjectionTests` **17 条**（投影半径逐位吻合、盲区收合、贴脸仍受遮挡、
  A/D 严格反向、平滑帧率无关、撤离区三态）；同步 M3 三处身体判定点断言。
- **门禁**（2026-11-04 17:19）：编译 `failed:false`；归零后 `consoleErrors:0 / consoleWarnings:0`；
  全量 **227/227**（CityMap 128 / **Stealth 99**）；校验器 **结论: 绿灯**。
- **Play 探针实测**（V3）：`outerR=13.98 == expectR 13.98`（逐位吻合）、
  `D=(1.00,0.00) A=(-1.00,0.00)`、`overlapFoot=True overlapBody=True`、
  `playerToGuard=16.4 withinView=False`、`exits=1 rules=1 projections=2 outcome=Playing`。
- 此前各里程碑（M1/M0.5/M2/M3/M4 + M5 预制件部分）见 CHANGELOG，均已提交；
  本轮之前最近提交 `6121312`（M5 预制件沉淀）。

## 2. 实测得到的环境事实（新会话直接照做）

- **探针自身也会算错**：外缘半径若按世界原点量会得到 22.08 的假值（虚惊一场）；必须按**脚底**量。
  数值断言的锚点错了，等于测的不是产品。
- **Play 模式下执行 `script eval` 的脚本会报「不允许立即执行」**：须 `editor.playmode stop` → 等域重载完成
  → 再 `script recompile`（别用固定 sleep 猜时间）。
- Play 模式下跑 `test run`：刷一批 `This cannot be used during play mode` error，
  且 `Summary.Total` 返回 **0**（看着像「没有测试」）。跑门禁前先 `editor status` 确认 `playMode: stopped`。
- **Unity 不允许覆盖同路径的已打开场景，也不允许卸载最后一个已加载场景**：
  「删场景文件 → 向同一路径 SaveScene」会两头失败（旧文件已删、新文件没写出）。
  正解：**打开目标场景 → 清空根物体 → 原地 `SaveScene(scene)`**。
- **编辑态访问 `renderer.material`** 即时报 error 并泄漏材质（破归零门禁）；上色用
  `new Material(sharedMaterials[0])` 写回 `sharedMaterials`，运行期绘制用 `Graphics.DrawMesh`。
- **EditMode 不执行 MonoBehaviour 生命周期**（本包已撞 4 次）：静态单例 `FindFirstObjectByType` 兜底、
  `GuardBrain.All` 剪枝+空则重建、注册表测试手动 `MarkDirty()`、
  组件引用在 `Tick` 内惰性 `GetComponent`（`_volume == null` 早退要在惰性取之后）。
- **跨包 id 必须非负**：`GetEntityId().GetHashCode()` 会为负，撞契约 `-1` 哨兵 ⇒ 过滤静默退化、
  反查失败静默退回默认分支。一律 `StealthIds.Of`。
- **跨包单位枚举是全工程搜索**：断言用**实例化前后差值**，不用绝对值（还加载着别人的场景）。
- **Unity 6 API**：`GetInstanceID()` 报 error（CS0619）；`EntityId` 隐式转 int 同样废弃；
  `using System` + `using UnityEngine` 时裸 `Object.X` 触发 CS0104。
- **测试故意 `LogError` 会破归零门禁**，`LogAssert.Expect` **不回退计数**；
  用 `Debug.unityLogger.logEnabled=false` + `finally` 复位。本包全程零自造 error（修复后）。
- **工具**：`test run --assembly X` 过滤被服务端忽略（按 `FullName` 前缀自筛）；
  `unity command` 输出是「JSON + 制表符表格」须 `raw_decode`；`script eval` 只回 success；
  `script eval_file` 内容被嵌进方法体（顶层不能声明类/函数，须纯语句 + `Debug.Log` 再从 `console get` 读）；
  `scene open` 只认 `Assets/` ⇒ 包内场景必须 `EditorSceneManager.OpenScene`；
  Game 视图非前台时 `capture screenshot --view game` **抓不到 IMGUI**，验 HUD 用
  `capture game_view --source screen`（base64 在 `data.result.base64`）；改 package.json 后须 `package resolve`。
- **CLI 超时不取消服务端执行**：连发 `test run` 会串行叠加并把**所有**指令顶到超时。一次提交一次等待。
- **共享编辑器纪律**：禁 `NewScene(Single)`（丢他人现场）、禁 `DisplayDialog`（卡死指令通道，实测卡过）；
  切场景前查 `isDirty`；临时场景用 Additive + 用完 `CloseScene`。
- **自动录屏不可行**（本轮再次确认）：gdigrab 按 title 匹配失败（窗口最小化态）、
  抓 desktop 得到的是**用户正在使用的应用**画面、按 hwnd 抓得黑帧（窗口不在前台时 gdigrab 拿不到内容）；
  `SetForegroundWindow` 被 Windows 前台锁挡掉 ⇒ 鼠标注入会落到用户的应用上。**不要重试这条路。**
- **锁会被其他会话清掉**（已发生 5 次）：重新原子取锁即可；长测试期间自己心跳也会超时，续期即转绿。
- **并行实况**：另一会话持续在改 citymap（其编译错误多次让全项目 `failed=true`，均自行修好）。
  **不替他修、不因他人红灯停手。**

## 3. 未完成

- **M5 收版项（唯一硬阻塞）**：`ShowcaseMedia/stealth_demo.mp4` 两段式实录视频（§6 硬性要求）
  + `Showcase.json.video` 填充 + 展板接入核验。**必须用户手动录屏**（见上条），我再用 ffmpeg 剪接入库，
  然后版本号收 0.5.0。画面效果目前只有数值探针 + 代码审查佐证（ACCEPTANCE 条目 85 已如实标注该限制）。
- **M6**：冻结对外契约 `Docs/Modules/00-Contract.md`。
- **待补证据**：编辑态回溯「拖滑块 NPC 连续移动」截图；50 NPC ≥ 55 FPS 性能验收；蹲伏态切换单独截图。
- **已知未接线**：`StealthWorldChangedChannel` / `StealthTimeIndexChannel` 已定义但**无资产实例、无 Raise 调用点**
  （`GuardBrain` 只用 `StealthDetectedChannel`，示例场景也没建通道资产）。B 包若依赖需先补。
- 状态机搜索环绕只在水平面（`desired.y = 0`）；垂直移动留给未接线的 `IPathBridge`。
- 撤离/被抓规则目前只在示例场景内（`StealthGameRule` 属演示层），未进 core 契约——
  若 B 包要显示胜负状态，需先在 core 加接口（走 M6 冻结窗口）。

## 4. 下一步（建议顺序）

1. **等用户录屏**（`屏幕录制 *.mp4` 丢在包根目录即可，参照 stretch 做法：源片不入库、只提交成片）。
   我这边负责：剪两段式（①怎么用 ②效果，含改前改后对照）→ 填 `video` → 收版 0.5.0。
2. **M6 契约冻结** → 交付 B 包（辅助台）消费；`ISafeIslandProvider` 反向等 B 实现（A 侧降级已写好）。
3. B 包（设计辅助台）开工：安全岛、T 周期回溯面板、分层查看器、按玩家线路反推路点。`Showcase.json` order 拟 31。
4. 性能验收（50 NPC ≥ 55 FPS）与两项截图补采。

## 5. 勿碰项

- **`Packages/com.collin.citymap/**`**：另一会话在迭代（本轮其 `SculptBrushTool.cs`、`CityWindow.cs` 数次让全项目 `failed=true`）。
- **`Packages/com.collin.core/**`**：契约层红线（core DESIGN D-7）——只允许值类型/接口/通道子类，
  **禁止玩法逻辑与判定实现**、禁止签名出现 feature 包类型；core 版本保持 **0.2.0**（用户裁定）。
- **`Assets/Showcase/**`、`Assets/Scenes/SampleScene.unity`**：展板纯 UI，禁止为单插件改场景文件（§6）；
  模板场景不要塞潜行内容。
- **`Packages/manifest.json`、`ProjectSettings/**`**：§7 共享文件。`ProjectSettings.asset:898`
  = `activeInputHandler: 1`（仅新 Input System），**不要改回 Legacy**（玩家控制器按新后端 + 条件编译写）。
- **§8 钉死版本**：Unity 6000.6.7f1 / pipeline 0.9.1-exp.1 / ai.assistant 2.21.0-pre.2 / ai.inference 2.6.1。
- **不要引入 `com.unity.ai.navigation`**（D-06/R-2）：逐帧积分会摧毁「位姿为时间纯函数」地基。
- **地基纪律勿破坏**：①基线位姿必须是纯函数，反应只走 `IPatrolModifier` 叠加；
  ②判定与投影**只有一条实现**（`GroundVisionProjection` / `VisionGeometry`），B 包禁止自研第二套；
  ③档位配色只有一份（`StealthStageVisual`）。
- **哨兵单元三层结构（根 / RouteRoot / Body）勿合并**：路线根挂在被驱动本体上会形成位姿循环。
- **操控语义勿回退**：镜头角与模型角必须分离；相机位置与朝向共用同一阻尼（否则抖动回归）。
- **已定案勿重议**：D-01~D-13、Q-A1~Q-A5、方案 P、core 0.2.0、stealth 暂留 0.4.0（待视频收版）。
