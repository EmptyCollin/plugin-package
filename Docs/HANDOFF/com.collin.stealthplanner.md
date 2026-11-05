# HANDOFF — com.collin.stealthplanner

> 覆写式交接单（非追加日志），每次交锁前整体重写五部分（AGENTS.md §4）。
> 只写可观测事实：碰了哪些文件、跑了什么指令、结果如何；不写意图与代码行为解释。

最后交接：qianwen-work-assistant · `planner-m2-2026-11-05` · 本次提交（M2 时间轴 + 可达传播 + 热区 + 48 条用例，随提交交锁）
> 前序：`78a103a`（骨架+DESIGN）→ `bd0d8b4`（门禁补记）→ `4334661`（DESIGN 定稿）→ `e1e957b`（M1 表+烘焙）

## 1. 已完成（本轮 = M2，静态设计 1 的可视化与地基）

**Runtime**（纯 CPU，不引 UnityEditor）：
- `PhaseReachability.cs`：相位可达传播。桶推进 BFS；`Field` 出逐桶可达位图 + 每格最早到达秒 + 整窗并集
  + `FillReachableUpTo`（**前缀并集**，时间轴专用，D-20）。步长 `floor(v·Δt/格长)` 可为 0；
  欧氏半径；一步跨多格用整数超行栅格化查**严格中间格**（本桶），**终点只查下一桶**（D-18）。
  每桶判据 = 表危险 **或** 无数据格（D-19，`MergeNoData`）。失败返回空场 + 中文原因。
- `HeatmapGrid.cs`：同行相邻同状态格合并横带（4 顶点/2 三角，法线朝上）。
- `VisibilityTable.cs`：`BuildDangerMask`（逐格转调 `IsDangerousAt`）、
  `VisibleCellCountInBucket` + `VisibleCellCountInSliceBucket`（**片内桶号**口径，两者共用一条位扫描）、
  足迹三件套 `MarkEvaluated`/`IsEvaluated`/`HasFootprint`/`BuildNoDataMask`、
  表级 `SetUnknownGuardNames`/`UnknownGuardNames`。
- `VisibilityBakeRunner.cs`：收尾**先登记足迹**再重建永久安全层，并写入缺失哨兵名单。

**Editor**：
- `StealthTimelineWindow.cs`：相位时间轴窗口（菜单 `Tools/Collin/Stealth Planner/相位时间轴`）。
  拖 t → `ScrubTo` 推 NPC → 三层热区（危险/本桶可达/累计可达）→ **哨兵泳道**（每行=一名哨兵整条周期的
  逐桶可见格数 + 当前 t 白线 + 行尾此刻世界坐标/朝向/是否格外，坐标取 `TryPoseAtWorld`）。
  视图态全是实例字段 + `[SerializeField]`，**无静态**。
- `PlannerEditorInternals.cs`：`InternalsVisibleTo` 测试程序集（降级断言打真实 internal 入口）。
- ⚠️ 更正归属：`VisibilityBakeService.cs`（`EnumerateWalkableCells` 唯一 footprint 实现 + `RunOnce`）
  与 `ContractVisibilitySource.cs`（`WarmUpPoses` = D-17 落地）**本轮零改动**，两者都是
  **M1（`e1e957b`）已交付内容**。时间轴与采样取证依赖它们，但**不得记成本轮产出**
  （复核：`git status` 显示 Editor 目录本轮只新增 `StealthTimelineWindow.cs` 与 `PlannerEditorInternals.cs`）。

**实测读数（StealthSample，3 哨兵，重烘后）**：
11664 格 · 足迹=True · **无数据格=4** · 逐桶采样 8 桶耗时 **217ms** ·
危险格数随 t 变化 `2130→2136→2000→1716→1428→1594→1844→1378` ·
本桶可达 `1→199→481→871→1369→1975→2685→3507` · 累计同向单调（整窗并集上限 4111）·
热区三角形 **354~786**（对照 11664 格）· NPC 位置随桶 `(-6.0,-6.0)…(6.0,-1.0)` ·
泳道逐桶口径比对 **169 桶 0 不符**（三片 `53/26.5s/峰718`、`56/28.0s/峰712`、`60/29.9s/峰718 均564`）·
统计构建 **5ms** · 真实窗口（菜单打开）重绘 8 次 `consoleErrors=0 consoleWarnings=0`。

**测试**：本包 **22→48**（`PhaseReachabilityTests` 18 + `GracefulDegradationTests` 4 收口 U-1
+ `TimelineRecomputeTests` 4 + 既有 22）。**全量 306/306**，按 `FullName` 命名空间分包统计：
**CityMap 137 + Stealth 121 + StealthPlanner 48 = 306**（自洽，非估算）。

**修掉三处本轮实测发现的缺陷**（详见 ACCEPTANCE 条目 20~22，均已补回归）：
1. 累计热区拷成整窗并集 ⇒ 拖 t **恒定不动**（语义混用，不报错只静默失效）。
2. `_startCell` 缓存 ⇒ **改起点输入框毫无反应**，违反 §0.1 所见即所得。
3. 表失效/传播失败**残留上一次热区**（旧网格对象泄漏 + 状态行谎报旧格数）。

## 2. 未完成

- **M3~M9 全部未动**。下一站 M3（相位图 A\* + 四剪枝 + 无解 Top-K，静态2/3）。
  `Field.EarliestSeconds` 已是 M3 的直接可复用产物；D-18 的判据与 §6.1 的边定义刻意保持一致。
- 🚨 **U-5（本轮新发现，M3 前须与用户定方案）**：**缺足迹的旧表资产不会被自动判为过期**。
  实测打开窗口时读到 `格=11664 足迹=False`——M1 时代烘的资产 `LayoutVersion` 仍是 1、与当前代码版本相符，
  于是窗口照常画热区，但 **D-19 的穿墙保护不生效**、面板不报错。
  两条修法代价不同：① `LayoutVersion` 提到 2（一刀切，所有已烘资产作废须重烘）；
  ② 加「足迹代次」标记 + 窗口状态行显式警告「本表缺足迹，墙格按可走处理」（不作废，但保护仍可选）。
  **本轮未擅自改版本号**（会让用户机器上的表全部作废，属需批准的高影响变更）。
  缓解：表资产不入库，新克隆本来就必须重烘。
- **本包没有自己的 `Sample/` 演示场景**，取证与演示都复用 A 包 `Packages/com.collin.stealth/Sample/Scenes/StealthSample.unity`。
  M9 的 `Showcase.json` + 两段式演示视频（§6：先"怎么用"再"用了之后的效果"）未落；
  **录屏只能用户手动录**，本包不得自行生成或标注"示意视频"。
- **增量重烘未实现**（U-3）：每次全量重算。D-11 目前只兑现「分帧+可中断+订阅失效」，
  **对外不得声称支持增量**。首烘 0.82s 使该优化暂不紧迫。
- §6.7「建议参考相位时长」算法未实现（U-2，与 M3 同期）。
- `.gitignore` 仍未加「表资产」忽略行：**工具报 `Cannot preserve Windows file access permissions` 拒改，
  未绕过**；靠**逐文件精确 add**（不含 `Sample/VisibilityTable.asset*`）保证不入库。**长期规则需用户拍板。**

## 3. 已知坏

- **本包无红灯**：门禁三项读数见 ACCEPTANCE「M2 交锁门禁」行与本次提交说明。
- ⚠️ **本会话自造过两次编译红灯并已当场修复**（§1.9）：`CS0841`（窗口 OnGUI 里 `bucket` 先用后声明）、
  `CS0246`（新测试漏 `using …EditorTooling`）。第一次发生期间编辑器进入 Safe Mode、实时通道断开过一次——
  **留红灯的真实代价是冻结全项目通道，不是抽象风险。**
- **取证方法的三条硬教训（M3 起必守）**：
  ① `AssetDatabase.LoadAssetAtPath` 返回**同一缓存实例**，重烘会覆写上一份 ⇒ 比较两次烘焙必须**先拷位图快照**；
  ② 反射**直接调 `OnGUI()`** 会因无 GUI 上下文在 `GUILayoutUtility.DoGetRect` 抛 NRE ⇒
     **探针假信号，不作验收依据**；界面验证必须走菜单开窗口 + `Repaint()`；
  ③ `unity list` 的列序是「动作 组」**反写**：真名是 `console status` / `test status` / `test run` /
     `recompile_status`；两段式命令名**必须整体加引号**（`"script recompile"`），
     写错的失败请求**本身会写进控制台**、污染归零门禁读数。
- `KeyNotFoundException: 'services-gateway'` 与 `Color primaries`（视频解码）两类环境噪声仍在
  `Tools/console_allowlist.json` / 历史 ACCEPTANCE 在册；本会话未新增豁免、未代清他会话日志。

## 4. 下一步（M3 建议顺序）

1. **先拍 U-5**（旧表资产无保护的问题）：问用户选「版本提到 2 强制重烘」还是「加代次标记 + 面板警告」。
   不定这个就写求解器，等于让求解器吃一张**可能含穿墙格**的表。
2. 相位图 A\*（§6.1）+ 四剪枝（§6.2，**剪枝①现在就该消费 `IsPermanentlySafe`**，M1 已独立验证该层可信）。
   边的判据**直接复用 `PhaseReachability` 的中间格/终点分桶语义**，别在求解器里再写一套。
3. N 相位封顶判有无解（静态2）→ 无解走 Top-K + 最早到达时间（静态3，D-04）。
4. §6.7 参考相位时长（U-2）与面板只读展示 + 一键填入（D-13：绝不自动改写手配 P）。
5. ⚠️ 求解器**必须能后台/多线程**（D-01 的全部意义）：`VisibilityTable` 与 `PhaseReachability`
   都无 UnityEditor 依赖、可只读并发；**任何新增的求解入口不得在主线程现算射线**。

## 5. 勿碰项

- **`Sample/VisibilityTable.asset(.meta)` 不入库**（派生产物）；`PlannerBakeSettings.asset` 是源、**入库**。
- **单一口径纪律**（破了必被测试判红，别绕）：`VisibilityTable.BucketOf` = 填桶/取桶唯一口径；
  `VisibilityBakeService.EnumerateWalkableCells` = footprint 唯一实现；
  `VisibilityTable.BuildDangerMask` 逐格走 `IsDangerousAt`（**不许自己按桶扫位图**）；
  阈值 `7200` 只在 `PlannerBakeSettings.cs` 一处；步长只走 `PhaseReachability.StepCellsFor`。
- **判定永远转调玩法包契约**（`IVisionWorld.CanSeeAt`）；本包源码出现任何 A 包具体类型即破方案 P
  （`dep-direction` 守，asmdef 层 `com.collin.core` 是唯一对外依赖）。
- **SceneView 回调里禁用 `Gizmos.*`**（见 §3）；起点标记用 `Handles.*`。
- **窗口视图态不得写静态字段**（DESIGN §0.1 与 M8 多窗口前提）；`duringSceneGui`/`update` 必须
  OnEnable/OnDisable 成对摘除，测试里创建的窗口必须 `DestroyImmediate` 否则污染后续用例重绘。
- `com.collin.core`（契约签名改动 = 同时打断 A/B，走 ADR）；`com.collin.citymap`、`com.collin.stealth`
  任何文件（工作区里 citymap 的 `TerrainProfile.asset`/`TerrainTest.unity` 改动与 `Assets/` 下的
  `CityProject.asset`、被删的 `Assets/test.asset` **都是他会话现场，本轮未动、不要提交**）；
  `Packages/manifest.json`、`ProjectSettings/**`、`.gitignore`、`README.md`、
  `Packages/packages-lock.json`（机器生成，本包条目由 Unity 自动写入，**不手工解冲突、本轮未提交**）。
- `.locks/com.collin.stealthplanner.lock`：本会话持有，TTL 300s；跑 `test run`/烘焙等长动作**中途续心跳**。
