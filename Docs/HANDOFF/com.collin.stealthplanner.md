# HANDOFF — com.collin.stealthplanner

> 覆写式交接单（非追加日志），每次交锁前整体重写五部分（AGENTS.md §4）。
> 只写可观测事实：碰了哪些文件、跑了什么指令、结果如何；不写意图与代码行为解释。

最后交接：qianwen-work-assistant · `planner-m4perf-2026-11-05` · 本次提交（M4 性能增量：逐桶掩码按需建层 + slack 经用户拍板移出 M4；含一次"先量后改推翻上轮建议"的归因）
> 前序：`78a103a`（骨架+DESIGN）→ `bd0d8b4`（门禁补记）→ `4334661`（DESIGN 定稿）→ `e1e957b`（M1 表+烘焙）→ `fe44abe`（M2 时间轴+传播+热区）→ `4535b3a`（M2 收口轮 表版本1→2）→ `eabaca3`（M3 可行性+Top-K）→ `5952f49`（M4 第一增量 多条短解+面板）→ `3da5586`+`b8680b6`（U-8 修复在 A 包 + B 包登记）

## 1. 已完成（本轮 = M4 性能增量，无新增功能、结果不变）

**Runtime**（`PhaseKShortest.cs`，改一处、判据与邻居逻辑不动）：
- 逐桶阻断掩码从「一次性预计算全 horizon 层」改为 **`EnsureLayer` 按需构建 + 缓存**：
  新增 `built[]` 标记（⚠️ 不能只靠"缓存非零"判已建——全安全层内容也是全 0，会被误判未建而每层反复重算）。
  起点检查改为只在第 0 层验一次（掩码构建失败只源于长度不匹配，对同一表逐层恒同）。
- 结果与改前**逐位一致**：`FindRoutes(M=5)` 仍 5 条、首层仍 =55（=`PhaseSolver` 可行性解），每跳仍过
  `ExpandSuccessors`/`BuildBlockedMask` 单一真源复核。**本包用例未增减，仍 70**。

**归因实测（先量后改；同会话带 JIT 预热探针，避免跨会话旧数误导）**：
| 开销项 | 探针 | 实测 | 定性 |
|---|---|---|---|
| 全量预计算掩码 L480 | perf_ab | 545ms | 本次优化对象 |
| 按需建到层 L60 | perf_ab | 58ms | **净省 ≈487ms（17%）** |
| parent 每轮 Fill(-1)×5 | perf_fill | 15ms | 我上轮猜的主因，**证伪** |
| 剩余前沿洪水式扩散 | 残差 | ~2.3s | **真瓶颈（~81%）** |
→ **推翻我上一轮的口头建议「M4 先做剪枝①」**：`IsDangerousAt` 早已短路永久安全格，剪枝①在查表/掩码层基本已实现、
剩余空间小；对症的是 **§6.2 剪枝②（可采纳启发式引导搜索）**。已写进 DESIGN §6.2 优先级校正，防下个会话再照字面顺序做。
（注：剪枝①的"时间维合并"形态能否进一步削减洪水**未单独测**，不下"一定不省"的定论。）

**范围调整（用户 2026-11-05 拍板「1」）**：**slack（§6.5）移出 M4 ⇒ 挪 M5/M7**。
原因：slack 需"每格察觉累积速率"，M1 表只存可见性位图、A 包 gain 是运行时态，编辑态拿不到，硬造不诚实。

**门禁（最终代码状态）**：`console clear` → `"script recompile"`（`compilationFailed=false`、本包 0 错误）
→ `test run` 全量 **359/359**（本包 **70**：CityMap 166 + Stealth 123 + StealthPlanner 70；
⚠️ 前两者数目增长来自他会话本轮新增用例，**与本包无关**，本次我没动测试数）→ 归因探针只读取表 → `verify_project.py`。
GATE-B 归零后 error=0/warn=0。⚠️ 校验器整体 FAIL 全来自他包（citymap 超时锁 + 另一会话新加的 channel-discipline
揪出 citymap/stretch 历史 DisplayDialog），**本会话未代修、未碰其文件**（用户明确「别管别人的超时锁」）。

**文档**：DESIGN §6.2 加优先级校正 + 里程碑 M4 行更新（后续增量清单/按需建层已落地/slack 移出）；
ACCEPTANCE 新增条目 36（性能归因）、U-6 更新；CHANGELOG 新增 Changed（M4 范围与性能）段。

## 2. 未完成

- **M4 剩余（下一轮，按实测优先级）**：
  ① **§6.2 剪枝②：可采纳启发式引导搜索**（对症那 81% 的洪水；`h(cell)=网格最短距离/speed_max` 保最优。
     ⚠️ 大手术：要自建优先队列与 (cell,layer) 的 g/发现表，**不得绕开 `ExpandSuccessors` 真源**，
     否则丢"每跳合法 + 首层与可行性一致"这两条一致性网；改完必须复跑 `PhaseKShortestTests` 7 条）。
  ② **JSON/SO 数据集导出 + 注入弹窗**（静态4→动态1，组 E round-trip）——静态4 真正的收口条件，
     `Route.CellsPerLayer`（站位序列）就是导出载体，D-07 已定双形态。
  ③ 规模化耗时基准 N∈{4,8,16}。
- §6.7 参考相位时长（U-2）、增量重烘（U-3）、实跑复现器 M5、反推路点 M6、安全岛端到端 M7、
  UMPM M8、`Showcase.json`+两段式演示视频 M9（**录屏只能用户手动录**）。
- `.gitignore` 表资产忽略行仍未加（工具报 ACL 拒改，未绕过）；靠逐文件精确 add 排除。

## 3. 已知坏

- **本包无红灯**。
- **上一轮"M4 先做剪枝①"的建议被我自己的实测推翻**（见 §1 表）：教训=**别凭结构直觉排性能优先级，先归因再动手**；
  已把校正写进 DESIGN 防复发。本轮另两次"猜测"也被探针证伪（掩码非 88% 主因、parent Fill 非主因）。
- **取证纪律（本轮再验证）**：跨会话的旧耗时数不可直接比（JIT/内联态不同）——同会话带预热重测后，按需建层
  真实收益是 17%（不是我最初按 55/292 估的 44%）。归因探针务必带一轮预热。
- citymap 会话本轮活跃（用例数随其增删变动，门禁终值实测 166；`WaterViewTests.cs` 一度编译报错卡全项目，随后自修）；
  我没代修、没碰其文件。全量计数以"本包类明细"为准，别把他包用例算进本包成绩。
- 环境噪声豁免清单不变（`services-gateway` 等）；未新增。

## 4. 下一步（建议顺序）

1. 先做 **剪枝②引导搜索**（收益最大且对症），保持 `PhaseKShortestTests` 作回归网：改完必须仍满足
   "首层=可行性解、每跳过 ExpandSuccessors、折返可达、墙阻断、非法入参可读失败"。
2. 再做 **数据集导出注入**（组 E），这是静态4 收口条件；顺路补 §6.7 参考相位时长。
3. 任何求解侧新代码：判据只准调 `PhaseReachability.ExpandSuccessors/BuildBlockedMask/TryBuildNoData`，
   视界只走 `PhaseSolver.Horizon`，步长只走 `StepCellsFor`；入口一律"可读失败 + 不返回半结果"。
4. 门禁老三样 + **静默 70s 复采**；探针创建的窗口/资产对象必须在同次调用里彻底回收。

## 5. 勿碰项

- **`Sample/VisibilityTable.asset(.meta)` 不入库**（派生产物；现为 version=2）；`PlannerBakeSettings.asset` 是源、入库。
- **单一口径纪律**：`BuildBlockedMask`=唯一"某桶不能站"；`ExpandSuccessors`=唯一邻居展开；
  `StepCellsFor`=唯一步长；`PhaseSolver.Horizon`=唯一视界；`BucketOf`=唯一填/取桶；
  `BuildDangerMask` 逐格走 `IsDangerousAt`；`EnumerateWalkableCells`=唯一 footprint；`7200` 仅一处。
  ⚠️ 引导搜索若引入优先队列/g 表，**只许加"搜索策略"，不得新增一套安全或邻居判定**。
- **表结构加"缺省即危险"字段必须抬 `LayoutVersion`**（D-22）；防线必须有可观测出口（D-23）；
  短解口径以 **D-25**（边不相交≠严格 Yen）为准；M3 可行性以 **D-24**（层序传播≠手搓 A*）为准。
- `com.collin.core`（契约签名改动走 ADR）；`com.collin.citymap`、`com.collin.stealth` **任何文件**——
  工作区里 citymap 的 `CityWindow.cs`/`WaterNetworkAsset.cs`/`WaterStage.cs`/`CityProject.cs`/
  `TerrainProfile.asset`/`TerrainTest.unity`、`Assets/*`、`README.md`、`packages-lock.json`、
  `AGENTS.md`+`Tools/verify_project.py`（另一会话在改校验器）**都是他会话现场，本轮未动、不要提交**。
- `.locks/com.collin.stealthplanner.lock`：本会话持有、**随本次提交释放**；长动作序列每 60s 续心跳。
