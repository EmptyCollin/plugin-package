# 交接单 — com.collin.stealth

> 覆写式交接单（AGENTS §4）。最近更新：2026-11-04（会话 `stealth-nav-2026-11-04`）
> **包版本 0.4.0**（M5 演示视频仍未录，故不收 0.5.0）。
> 本轮响应用户两条实机反馈：**NPC 追击穿掩体**（要导航网格）、**地面投影闪得厉害**。
> 前者已实测闭环；后者根因确认并落码，**目视复核待用户确认**（见 U-6）。

## 1. 已完成（本轮）

用户反馈：①「NPC 抓人时直接穿模过去，需要导航网格」；②「地面那个警戒范围闪得厉害，要么改 shader，要么半透 + Z 略高于地面」。

- **可选导航子程序集 `com.collin.stealth.Navigation`**（+ `Navigation/Editor/`）：
  基于**内置 `com.unity.modules.ai` 的 legacy NavMesh** 实现 `IPathBridge`
  （`NavMesh.SamplePosition` 吸附起终点 + `NavMesh.CalculatePath` 出折线）。
  **没有引入 `com.unity.ai.navigation`**（D-06：付费 HAVOK，且 `NavMeshAgent` 按帧积分会摧毁「位姿是时间纯函数」的地基）。
  注册点自带在可选程序集内：Play 态 `RuntimeInitializeOnLoadMethod`，编辑态 `[InitializeOnLoad]`；
  **主包 Editor 不引用它 ⇒ 删掉 `Navigation/` 目录主包零影响**（可选性成立，验收 88）。
- **`IPathBridge` 扩为路径接口**：`TryFindPath(from, desired, List<Vector3> corners)` + 保留 `TrySnap`；
  写明 **`corners[0]` 必须是出发点**的锚点契约（违反即 NPC 瞬移）。`NullPathBridge` 同步实现。
- **绕行只驱动叠加层**（地基纪律，验收 89）：`GuardBrain` 反应位移改为沿折线逐段推进（`AdvanceAlongPath`），
  巡逻基线仍是纯函数结果。两条防回归细节：
  ① **重路由时追踪点钉在当前位置**（基线 + 现有偏移），否则每次重算都把 NPC 拽回路径起点 = 抖动/瞬移；
  ② 仅在目标移动 >0.6m 或超过 `_repathInterval` 时重路由（追击时玩家会跑、遮挡会变）。
  不可达 / 无桥时**退回直线叠加**，绝不原地停住（验收 90）。
- **地面投影专用着色器** `Resources/StealthGroundProjection.shader`：
  `Queue=Transparent` + `ZWrite Off` + `ZTest LEqual` + `Offset -1,-1` + 标准 alpha 混合，
  `LightMode` 用 `SRPDefaultUnlit`（在 `DrawObjectsPass.cs:87` 的 `shaderTagIds` 核实，非凭记忆，验收 92）。
  **根因**：原先复用 `URP/Unlit` 是 `Queue=Geometry` + `ZWrite On`，alpha<1 也进不了透明队列 ⇒ 与地面争深度；
  且多边形 y 与地面**完全共面**。修法按用户给的两条**一起做**：专用着色器 **加** 可配 `_clearance`（默认 0.02m）抬升。
  找不到着色器时兜底并**一次性警告**（不每帧刷）。
- **示例场景接入导航**：地面与三块掩体标 `isStatic`（烘焙只收静态），
  玩家/两哨兵/时钟/信标**递归取消**静态（`CreatePrimitive` 的子物体默认静态，不处理会把角色自己烘成障碍）。
  烘焙流程写进生成器：存盘 → `NavMeshBuilder.BuildNavMesh()` → 绕行校验 → **再存盘**；
  产物 `Sample/Scenes/StealthSample/NavMesh.asset` 随场景入库（验收 93）。
- **实测证据**：`[Nav] 起点=(0.0,0.1,-14.0) 终点=(0.0,0.1,14.0) 拐点=4 直线=28.00 路径=28.13 最大横向偏离=1.17`
  ⇒ 生成器日志「导航烘焙 完成并通过绕行校验」（验收 87）。
- **自查修正（重要）**：**校验判据本身原来是错的**（验收 94）。旧判据「路径总长 > 直线 + 0.3m」
  对「28m 直线绕 1.6m 宽的掩体」在几何上不可能成立（实测总长只增 0.13m），
  会把已经生效的导航误判成失败。改用「拐点相对直线的**最大横向偏离** > 0.4m」。
  同时给 5 个早退分支各补**诊断日志**——此前静默 return false 只能靠猜定位。
- **可选性守卫补成真的**（自查，验收 95）：asmdef 声明了 `STEALTH_NAVMESH_AVAILABLE` 但**代码零处 `#if` 引用**
  ⇒ 宏是虚设的，真缺内置 AI 模块时该程序集会整体编译失败，「删掉目录主包零影响」在「模块缺失」路径上不成立。
  现 `#if` 包住 `using UnityEngine.AI` 与整个 NavMesh 实现，`#else` 提供**同名降级实现**（一律 false ⇒ 等同未烘焙）。
- **门禁**（2026-11-04 18:13，宏改动之后复跑）：编译 `failed:false`；`console clear` 后重跑 recompile + test run
  → `consoleErrors:0 / consoleWarnings:0`；全量 **227/227**（CityMap 128 / **Stealth 99**）；校验器 **结论: 绿灯**。
  ⚠️ 期间出现过一次 `lock-stale` FAIL：是**本会话自己锁的心跳超时**（长测试导致），续期即转绿——
  跑长门禁时**中途记得续心跳**，否则校验器会以「锁超时」卡交锁。
- 文档同步：DESIGN §3.6 改写为已实装（含三条约束）、D-06/R-2/M3 三处更新；
  ACCEPTANCE 新增条目 87-95 与未通过项 **U-6**；CHANGELOG 记在 **Unreleased**（不收版本号）。
- 上一轮（会话 `stealth-fix-2026-11-04`，提交 `87a942e`）已完成：视角/模型角分离、近身察觉档、
  地面投影几何、撤离区与对局规则。详见 CHANGELOG，本轮未回退这些改动。

## 2. 实测得到的环境事实（新会话直接照做）

- **Play 模式下 `script eval_file` 会整体静默失败**（本轮两次复现：无 error、也无输出、控制台读不到）。
  ⇒ Play 态取证**不要依赖探针**，改走「代码内日志插桩 + 事后 `console get`」（本轮绕行证据就是这么拿到的）。
  另注：Play 模式跑 `test run` 会刷一批 `This cannot be used during play mode` error 且 `Summary.Total` 返回 **0**
  （看着像「没有测试」）。跑门禁前先 `editor status` 确认 `playMode: stopped`。
- **导航烘焙的正确姿势**：`UnityEditor.AI.NavMeshBuilder.BuildNavMesh()`（legacy 内置路径），
  产物落在 `Sample/Scenes/<场景名>/NavMesh.asset`，**必须与场景一起入库**，否则他人打开场景没有网格。
  烘焙只收 **static** 物体；角色/相机要**递归**取消 static（`CreatePrimitive` 的子物体默认带 static）。
- **`URP/Unlit` 不能当半透覆盖层用**：它 `Queue=Geometry` + `ZWrite On`，alpha 调小也进不了透明队列
  ⇒ 与共面地面争深度、剧烈闪烁。半透贴地必须 `Queue=Transparent` + `ZWrite Off` + `Offset` 或几何抬升。
- **URP 的 `LightMode` 只认 `SRPDefaultUnlit`**（对内置 UnlitPass 亦是）：查 `DrawObjectsPass.cs` 的
  `shaderTagIds` 首位可确认，**别凭记忆写 `UniversalUnlit`**。
- **探针度量锚点会算错**：外缘半径按世界原点量得到 22.08 的假值，按**脚底**量才得 13.98 吻合值。
  数值断言的锚点错了，等于测的不是产品。
- **绕行的几何直觉要核验**：中等大小障碍（1.6m 宽）对 28m 直线只带来 ~0.13m 路程增量。
  **用「总长变长」判导航是否生效是错的**，必须看拐点横向偏离。
- **Unity 不允许覆盖同路径的已打开场景，也不允许卸载最后一个已加载场景**：
  「删场景文件 → 向同一路径 SaveScene」会两头失败。正解：**打开目标场景 → 清空根物体 → 原地 `SaveScene(scene)`**。
- **编辑态访问 `renderer.material`** 即时报 error 并泄漏材质（破归零门禁）；上色用
  `new Material(sharedMaterials[0])` 写回 `sharedMaterials`，运行期绘制用 `Graphics.DrawMesh`。
- **EditMode 不执行 MonoBehaviour 生命周期**（本包已撞 4 次）：静态单例 `FindFirstObjectByType` 兜底、
  `GuardBrain.All` 剪枝+空则重建、注册表测试手动 `MarkDirty()`、组件引用在 `Tick` 内惰性 `GetComponent`。
- **跨包 id 必须非负**：`GetEntityId().GetHashCode()` 会为负，撞契约 `-1` 哨兵 ⇒ 过滤静默退化。一律 `StealthIds.Of`。
- **跨包单位枚举是全工程搜索**：断言用**实例化前后差值**，不用绝对值（还加载着别人的场景）。
- **Unity 6 API**：`GetInstanceID()` 报 error（CS0619）；`EntityId` 隐式转 int 同样废弃；
  `using System` + `using UnityEngine` 时裸 `Object.X` 触发 CS0104。
- **测试故意 `LogError` 会破归零门禁**，`LogAssert.Expect` **不回退计数**；
  用 `Debug.unityLogger.logEnabled=false` + `finally` 复位。本包全程零自造 error。
- **工具**：`test run --assembly X` 过滤被服务端忽略（按 `FullName` 前缀自筛）；
  `unity command` 输出是「JSON + 制表符表格」须 `raw_decode`（`console get` 用 `JSONDecoder().raw_decode`，
  整体 `json.loads` 会因尾部表格报 Extra data）；`script eval` 只回 success；
  `script eval_file` 内容被嵌进方法体（顶层不能声明类/函数，须纯语句 + `Debug.Log` 再从 `console get` 读）；
  `scene open` 只认 `Assets/` ⇒ 包内场景必须 `EditorSceneManager.OpenScene`；
  Game 视图非前台时 `capture screenshot --view game` **抓不到 IMGUI**，验 HUD 用
  `capture game_view --source screen`；改 package.json 后须 `package resolve`。
- **CLI 超时不取消服务端执行**：连发 `test run` 会串行叠加并把**所有**指令顶到超时。一次提交一次等待。
- **共享编辑器纪律**：禁 `NewScene(Single)`（丢他人现场）、禁 `DisplayDialog`（卡死指令通道，实测卡过）；
  切场景前查 `isDirty`；临时场景用 Additive + 用完 `CloseScene`。
- **自动录屏不可行**（已三次确认）：gdigrab 按 title 匹配失败（窗口最小化态）、抓 desktop 得到的是
  **用户正在使用的应用**画面、按 hwnd 抓得黑帧；`SetForegroundWindow` 被 Windows 前台锁挡掉
  ⇒ 鼠标注入会落到用户的应用上。**不要重试这条路。**
- **锁会被其他会话清掉**（已发生 5 次）：重新原子取锁即可；长测试期间自己心跳也会超时，续期即转绿。
- **并行实况**：另一会话持续在改 citymap（其编译错误多次让全项目 `failed=true`，均自行修好）。
  **不替他修、不因他人红灯停手。**

## 3. 未完成

- **U-6（本轮新增）**：地面色块「不再闪」的**目视复核**没有画面证据（Play 探针静默失败 + 录屏不可行）。
  根因、队列/深度配置、抬升量都已落码并通过编译与测试，但按「实测闭环」标准还差用户眼睛确认。
- **M5 收版项（唯一硬阻塞）**：`ShowcaseMedia/stealth_demo.mp4` 两段式实录视频（§6 硬性要求）
  + `Showcase.json.video` 填充 + 展板接入核验。**必须用户手动录屏**，我再用 ffmpeg 剪接入库，然后收 0.5.0。
- **M6**：冻结对外契约 `Docs/Modules/00-Contract.md`。
  ⚠️ 本轮 `IPathBridge` **签名已变**（新增 `TryFindPath`）——若 B 包已按旧签名消费，需在 M6 冻结前对齐。
- **待补证据**：编辑态回溯「拖滑块 NPC 连续移动」截图；50 NPC ≥ 55 FPS 性能验收；蹲伏态切换单独截图。
- **已知未接线**：`StealthWorldChangedChannel` / `StealthTimeIndexChannel` 已定义但**无资产实例、无 Raise 调用点**
  （`GuardBrain` 只用 `StealthDetectedChannel`）。B 包若依赖需先补。
- 搜索环绕阶段仍是水平面 `desired.y = 0`；导航折线只在**反应位移**里用，
  巡逻基线的**上下楼梯**未接（要接得先解决「基线必须是纯函数」的冲突，别直接塞 NavMeshAgent）。
- 撤离/被抓规则只在示例场景内（`StealthGameRule` 属演示层），未进 core 契约——
  B 包要显示胜负状态需先在 core 加接口（走 M6 冻结窗口）。
- 导航只在**烘焙过的场景**里有效。用户自己的场景需自行烘焙并标 static（包文档未写这条使用说明，M6 前补）。

## 4. 下一步（建议顺序）

1. **请用户实机确认两点**：①追击是否已绕开掩体（导航已烘焙，预期成立）；②地面色块是否还闪（U-6）。
   若仍闪，下一个可疑项是 `_clearance` 与地面共面的容差（可试 0.05m），**不要**先回去改队列配置。
2. **等用户录屏**（`屏幕录制 *.mp4` 丢在包根目录即可，源片不入库、只提交成片）。
   我这边负责：剪两段式（①怎么用 ②效果，含改前改后对照）→ 填 `video` → 收版 0.5.0。
3. **M6 契约冻结**（含 `IPathBridge` 新签名对齐）→ 交付 B 包消费；`ISafeIslandProvider` 反向等 B 实现。
4. B 包（设计辅助台）开工：安全岛、T 周期回溯面板、分层查看器、按玩家线路反推路点。`Showcase.json` order 拟 31。
5. 性能验收（50 NPC ≥ 55 FPS）与两项截图补采；包文档补「自有场景需烘焙导航」的使用说明。

## 5. 勿碰项

- **`Packages/com.collin.citymap/**`**：另一会话在迭代（其 `SculptBrushTool.cs`、`CityWindow.cs` 数次让全项目 `failed=true`）。
- **`Packages/com.collin.core/**`**：契约层红线（core DESIGN D-7）——只允许值类型/接口/通道子类，
  **禁止玩法逻辑与判定实现**、禁止签名出现 feature 包类型；core 版本保持 **0.2.0**（用户裁定）。
- **`Packages/manifest.json`、`ProjectSettings/**`**：§7 共享文件。本轮**没有**动它们
  （导航走内置 `com.unity.modules.ai`，无需新增依赖）。`ProjectSettings.asset` 的
  `activeInputHandler: 1`（仅新 Input System）**不要改回 Legacy**。
- **`Assets/Showcase/**`、`Assets/Scenes/SampleScene.unity`**：展板纯 UI，禁止为单插件改场景文件（§6）。
- **§8 钉死版本**：Unity 6000.6.7f1 / pipeline 0.9.1-exp.1 / ai.assistant 2.21.0-pre.2 / ai.inference 2.6.1。
- **不要引入 `com.unity.ai.navigation`**（D-06/R-2）：付费 HAVOK，且逐帧积分会摧毁「位姿为时间纯函数」地基。
  导航实现只走 `Navigation/` 可选子程序集 + 内置 legacy NavMesh。
- **`Sample/Scenes/StealthSample/NavMesh.asset` 勿手改、勿删**：由生成器烘焙产生，删了绕行立刻失效（表现为「又穿模了」）。
- **地基纪律勿破坏**：①基线位姿必须是纯函数，反应（含**导航绕行**）只走 `IPatrolModifier` 叠加；
  ②判定与投影**只有一条实现**（`GroundVisionProjection` / `VisionGeometry`），B 包禁止自研第二套；
  ③档位配色只有一份（`StealthStageVisual`）；④地面半透必须用 `Resources/StealthGroundProjection.shader`，
  别再复用 `URP/Unlit`。
- **哨兵单元三层结构（根 / RouteRoot / Body）勿合并**：路线根挂在被驱动本体上会形成位姿循环。
- **操控语义勿回退**：镜头角与模型角必须分离；相机位置与朝向共用同一阻尼（否则抖动回归）。
- **`GuardBrain` 重路由的两个坑勿回退**：追踪点必须钉在当前位置；必须有 `_repathInterval` 节流。
- **已定案勿重议**：D-01~D-13、Q-A1~Q-A5、方案 P、core 0.2.0、stealth 暂留 0.4.0（待视频收版）。
