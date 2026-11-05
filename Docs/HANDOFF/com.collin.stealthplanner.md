# HANDOFF — com.collin.stealthplanner

> 覆写式交接单（非追加日志），每次交锁前整体重写五部分（AGENTS.md §4）。
> 只写可观测事实：碰了哪些文件、跑了什么指令、结果如何；不写意图与代码行为解释。

最后交接：qianwen-work-assistant · `planner-m3-2026-11-05` · 本次提交（M3 相位可行性求解 + 无解 Top-K + 边判据单一真源 + 63 条用例）
> 前序：`78a103a`（骨架+DESIGN）→ `bd0d8b4`（门禁补记）→ `4334661`（DESIGN 定稿）→ `e1e957b`（M1 表+烘焙）→ `fe44abe`（M2 时间轴+传播+热区）→ `4535b3a`（M2 收口轮 表版本1→2）

## 1. 已完成（本轮 = M3，静态设计 2「有无解」+ 静态设计 3「无解 Top-K」）

**Runtime**（纯 CPU、只读表、无 UnityEditor 引用，可后台/引擎侧复用）：
- `PhaseSolver.cs`（新增）：
  - `Solve(table, settings, startWorld, goalWorld, walkSpeed, out failure)`（面板入口，内部算 horizon=N×BucketsPerPhase + stepCells + FallbackTopK）。
  - `Solve(table, startCell, goalCell, horizonBuckets, stepCells, topK, out failure)`（核心入口，测试直接走这条）。
  - 输出 `Result`：`Feasible` / `ArrivalBucket` / `EarliestArrivalSeconds` / `HorizonBuckets` / `CellsReached` / `FallbackTopK(List<Candidate>)`。
  - 可行性一律复用 `PhaseReachability.Compute`（带目标早退重载），**不自建第二套安全/邻居判据**（见 D-24）。
  - 无解 Top-K：从 goal 做「只穿非墙格」多源 BFS 距离场；候选须 `EarliestSeconds` 有限 **且** 距离场有限，按(绕墙格步数, 最早到达)升序、截断到 topK（topK<1 按 1）。墙隔断时**如实返回空清单**（排序键用绕墙距离，不用直线距离——后者会谎报「就差一格」）。
  - 视界硬上限 `MaxHorizonBuckets=4096`；`Horizon()` 超限返 -1 + 可读原因。
- `PhaseReachability.cs`（改）：
  ① 抽出**单一真源公开 API**：`BuildBlockedMask(table,t,noData,mask)`（危险或无数据，全工程唯一「某桶哪些格不能站/过」入口）、`TryBuildNoData`、`ExpandSuccessors(table,from,stepCells,maskHere,maskNext,outBuf)`（合法后继含原地等待，去重，D-18 三条语义）。BFS 主循环改为调这三者（原 `ExpandFrom`/`MergeNoData` 私有实现被公开入口取代）。
  ② 新增 `Compute(...,goalCell, out failure, out goalArrivalBucket)` **带目标早退**重载；原 5 参入口转调它（goal=-1）。注释写死：早退时场只覆盖到命中层，未命中时跑满 horizon 场完整。

**Editor**：`StealthTimelineWindow.cs`（改）加「可行性求解（静态设计 2/3）」区块：`_goalWorld` 输入 + 「求解有无可行解」按钮（无表禁用）+ `_solveSummary` 文本。**不驱动逐桶热区**（求解用完整 horizon，与滑条 `_buckets` 是两个量）。

**测试**：本包 **54→63**（新增 `PhaseSolverTests` **9 条**）。全量 **325/325**，
按 `FullName` 命名空间：CityMap 141 + Stealth 121 + StealthPlanner 63（自洽）。

**实测读数**：
- 假表（`PhaseSolverTests`）：无障碍 5 格步长1⇒第4层(=4s)；目标桶0/1/2危险⇒**第3层冲入**（等待生效）；起点==目标⇒桶0；topK=1⇒仅最近；步长0⇒候选仅起点；墙隔断⇒不可达**且** Top-K 空；入参越界各回专属原因；`Horizon` 默认480、N=64/Δt=0.05 时报「视界过大」返 -1。
- 真实烘焙表（3 哨兵，脚本模式烘，视野体=3）：`片3 活动格11,660/11,664 永久安全4,482 位图252.0KB 查询1,970,540 预筛省1,586,206 实打384,334 主循环0.66s 跳过0 零可见0`；表 `Version=2 足迹=True 无数据格=4`（与 M1/M2 逐位一致 ⇒ 抬版本+重构未改变判定）。
- 端到端（horizon=480）：起点(-26.75,-26.75)→对角撤离点(26.75,26.75) ⇒ **✅ 有解 t=27.5s（桶 55）**、途中可达 11,660 格（直线最少约 50 桶 ⇒ 55 桶说明绕开巡逻危险区）。
- 端到端（horizon=1 造无解）：`Feasible=False CellsReached=1 TopK数=1 cell=0 距goal=107 最早=0`（108×108 网格切比雪夫对角=107 步，吻合）。

**文档**：DESIGN 里程碑 M3 标达成 + 新增 **D-24**（本阶段用带早退的层序传播替代手搓 A*，真 A*/四剪枝/k-shortest 推 M4）；
ACCEPTANCE 状态头转 M3、C 组三行标取证状态（k-shortest 那条如实标「部分/属 M4」）、新增条目 **29~32**、U-2/U-4/U-6 推进；
CHANGELOG 新增 Added(M3) 段、版本说明转 M3；HANDOFF 覆写五节。

**门禁（本轮末次跑，最终代码状态）**：`console clear` → `"script recompile"` → `test run`(全量) → 重放 `m3e2e.cs` 端到端 → `"console status"` → `verify_project.py`。
三项全绿：`compilationFailed=false`；`consoleErrors=0 / consoleWarnings=0`；校验器 **FAIL 0**（WARN 1=本会话自有锁）。还原场景后再次 `console status` error=0/warn=0。

## 2. 未完成

- **M4~M9 全部未动**。下一站 M4：真 A* 可采纳启发 + §6.2 四剪枝（**剪枝①现在才该消费 `IsPermanentlySafe`**，M1 条目 7 已独立验证该层可信）+ §6.3 k-shortest（静态4 前 M 条短解）+ §6.7 建议参考相位时长（U-2）+ JSON/SO 数据集导出注入弹窗（静态4/动态1）。
- 静态3 已交付候选筛选，但**未接进 SceneView 绘制**（当前只在窗口出文本）。若 M4 要把 Top-K 点画进场景，注意复用 `HeatmapGrid`/`Handles`，别在 `duringSceneGui` 里用 `Gizmos.*`（见 §3）。
- **增量重烘未实现**（U-3）；D-11 只兑现「分帧+可中断+订阅失效」，**对外不得声称支持增量**。首烘 0.66~0.88s 使该优化暂不紧迫。
- 本包无自己的 `Sample/` 演示场景，取证复用 A 包 `StealthSample`；M9 两段式演示视频未落（**录屏只能用户手动录**，不得自行生成或标"示意视频"）。
- `.gitignore` 仍未加表资产忽略行（工具报 ACL 拒改，未绕过）；靠逐文件精确 `git add` 排除 `Sample/VisibilityTable.asset*`。**长期规则需用户拍板。**

## 3. 已知坏

- **本包无红灯**：门禁三项末次全绿（读数见上）。
- ⚠️ **A 包编辑态注册时机问题（属他包，本会话未代修）**：实测「切场景后**同一帧立刻**烘焙」得 `视野体=0 巡逻单位=3` ⇒ 烘出永久安全格=全场的全瞎表。本包只让它**不再静默**（D-23 三处出口已生效：日志/表/面板）。**取证纪律：切场景后先过一帧（等 ≥5s）再烘**，M3 端到端就是靠 step1 预热 + sleep 3 才拿到 `视野体=3` 的可信表。
- **本会话自造失误（均当场纠正，无遗留红灯）**：
  ① 求解器初稿留了一处 `|| true` 恒真垃圾判断和一个未用参数 `startCell` —— 写完自检删净；
  ② 还原场景探针 `restore.cs` 只判 `name=="StealthSample"`，漏了空场景分支 ⇒ 第一次显示"无需还原"没真切；补 `restore2.cs` 用 `name==""||name=="StealthSample"` 才还原成 TerrainTest（非脏）。**教训：切场景还原的判断分支要覆盖"空场景"。**
  ③ 文档数字两次写错（本包 53→实为 54、54→实为 63；分包 130+…→实测 141+121+63），一律从 `test run` 输出回填后才落笔。
- **反复 Single 模式切场景的风险**：本轮为端到端取证多次 `OpenScene(Single)` 切 StealthSample↔TerrainTest。TerrainTest 是 citymap 会话现场，**每次切前都确认其 `dirty=False`**，末态已还原为 TerrainTest(非脏)。若 citymap 会话正在其中未保存操作，Single 切换会打断它——下个会话做同类取证应优先 additive 或先探脏（本轮已尽量规避，如实记此风险）。
- 环境噪声 `KeyNotFoundException: 'services-gateway'` 仍在 `Tools/console_allowlist.json` 在册；本轮未新增豁免、未代清他会话日志。

## 4. 下一步（M4 建议顺序）

1. 跑 `python Tools/verify_project.py` + `unity status` + 取包锁确认基线（本轮 citymap 已自修其编译错误、超时锁也已由对方处理，开工基线应是 FAIL 0）。
2. **剪枝①先做**（成本最低、收益最直接）：求解/传播消费 `VisibilityTable.IsPermanentlySafe`（永久安全格不必展开时间维）。这是 §6.2 里唯一现成数据支撑的剪枝。
3. §6.3 k-shortest（带禁止边迭代 BFS/A*）产出前 M 条 → 接静态4 导出（JSON schema + SO 双形态，D-07）→ 弹窗确认注入动态1。
4. §6.7 参考相位时长（U-2）：`lcm(round(c_i/Δt))·Δt`，互质爆炸兜底 `max(c_i)×2`+Warning，面板**只读展示、绝不回写手配 P**（D-13）。
5. ⚠️ 任何新增求解/剪枝入口**不得在主线程现算射线**（D-01 全部意义）；`PhaseSolver`/`PhaseReachability`/`VisibilityTable` 均无 UnityEditor 依赖，可只读并发。

## 5. 勿碰项

- **`Sample/VisibilityTable.asset(.meta)` 不入库**（派生产物；本轮重烘已升到 version=2）；`PlannerBakeSettings.asset` 是源、入库。
- **单一口径纪律**（破了必被用例判红）：
  `PhaseReachability.BuildBlockedMask` = 「某桶哪些格不能站/过」**唯一入口**（BFS/A*/热区共用）；
  `PhaseReachability.ExpandSuccessors` = **唯一**邻居展开（含 D-18 三语义）；
  `PhaseReachability.StepCellsFor` = 步长唯一口径；
  `VisibilityTable.BucketOf` = 填/取桶唯一口径；`BuildDangerMask` 逐格走 `IsDangerousAt`（**不许自己按桶扫位图**）；
  `VisibilityBakeService.EnumerateWalkableCells` = footprint 唯一实现；`PlannerBakeSettings.MaxAlignedBuckets=7200` 仅一处。
- **判定永远转调玩法包契约**（`IVisionWorld.CanSeeAt`）；本包源码出现任何 A 包具体类型即破方案 P（`dep-direction` 守）。
- **表结构再加字段必须抬 `LayoutVersion`**（D-22，本轮 1→2 已实践）。
- **SceneView 回调禁用 `Gizmos.*`**（必抛、每次重绘一条 error 顶爆门禁）；辅助标记用 `Handles.*`。
- **窗口视图态不得写静态字段**；测试清理凡涉 ScriptableObject/EditorWindow 必须先判 `EditorUtility.IsPersistent` 或只回收自建实例（本轮条目 27 实测踩坑）。
- `com.collin.core`（契约签名改动=同时打断 A/B，走 ADR）；`com.collin.citymap`、`com.collin.stealth` **任何文件**——工作区里 citymap 的 `CityWindow.cs`/`CityTerrain.cs`/`SculptFilterAsset.cs`/`TerrainProfile.asset`/`TerrainTest.unity`、`Assets/` 下 `CityProject.asset`/被删的 `test.asset`、`README.md`、`packages-lock.json` **都是他会话现场，本轮未动、不要提交**。
- `.locks/com.collin.stealthplanner.lock`：本会话持有、**随本次提交释放**；长动作序列（test run/烘焙/多次采样，累计十几分钟）里**每 60s 续心跳**（本轮已多次续，最险一次是 m3e2e 端到端跑完才续，中间未超时）。
