# HANDOFF — com.collin.stealthplanner

> 覆写式交接单（非追加日志），每次交锁前整体重写五部分（AGENTS.md §4）。
> 只写可观测事实：碰了哪些文件、跑了什么指令、结果如何；不写意图与代码行为解释。

最后交接：qianwen-work-assistant · `planner-m4data-2026-11-05` · 本次提交（M4 数据集增量：`PlayerRouteDataset` D-07 双形态 + 窗口非模态导出 + 8 条回归 + 决策 D-26 + DESIGN §3.2 v0.2 修正）
> 前序：`78a103a`（骨架+DESIGN）→ `bd0d8b4`（门禁补记）→ `4334661`（DESIGN 定稿）→ `e1e957b`（M1）→ `fe44abe`（M2）→ `4535b3a`（M2 收口轮 表版本1→2）→ `eabaca3`（M3 可行性+Top-K）→ `5952f49`（M4 多条短解+面板）→ `3da5586`+`b8680b6`（U-8 修复在 A 包+本包登记）→ `78b623c`（M4 性能增量 掩码按需建层）

## 1. 已完成（本轮 = M4 数据集增量，静态设计 4 的导出侧）

**Runtime**（纯数据+纯函数，无 UnityEditor 引用）：
- `PlayerRouteDataset.cs`（新增）：D-07 双形态数据集（SO + `ToJson/FromJson`）。
  - canonical=**每层站位时间线** `cellsPerLayer`（下标=桶、值=格索引；相邻相等=等待），新增决策 **D-26**
    （不存稀疏路点的三条理由）；稀疏视图走 `MovementWaypoints()` 派生。
  - 随存**网格快照**（origin/格长/Δt/维度）+ `GridMatches(table)`；`SchemaVersion=1` 不匹配**拒载**。
  - 失败路径一律可读原因 + 不留半张（`BuildFromRoutes` 守 null/空、`ToJson` 空集报错、`FromJson` 空串/坏语法/无路线分报）。
- `StealthTimelineWindow.cs`（改）：「导出为路点数据集（JSON + SO）」按钮（无表/无路线禁用）+ `_exportSummary` 文本。
  导出到包内固定路径 `Sample/WaypointDataset.json` + `Sample/PlayerRouteDataset.asset`；
  **SO 从刚落盘的 JSON 读回**（两形态同源 + 顺带证明 JSON 可解析）；临时 SO 在 finally 销毁。
  ⚠️ **非模态**：DESIGN §3.2 原写「弹窗确认」与 AGENTS §2.1.5 冲突（citymap 刚因同类问题清了零、stretch 仍有 2 处被判红），
  照原文实现=自造红灯 ⇒ 改三处非模态确认，已在 DESIGN §3.2 标注 **v0.2 修正**、CHANGELOG、ACCEPTANCE 条目 39 各记一笔。
- `Tests/Editor/PlayerRouteDatasetTests.cs`（新增）**8 条**：逐层往返无损、
  **手写线上 JSON 字面量钉字段名契约**（JsonUtility 对改名静默补零，这是唯一能抓住它的方式）、
  版本 999 拒载、四类非法输入、网格不匹配判拒、折叠视图与时间线自洽、BuildFromRoutes 守 null/空、SchemaVersion 单源。

**实测（真实烘焙表，U-8 修复后首次全程用真场景，未再退回注入内存表）**：
反射驱动窗口 `SolveRoutes()`+`ExportRoutesAsDataset()` → 落盘核验：JSON 7454 字节
（schema=1、108×108、格长0.5、Δt0.5）**5 条**、SO 5 段 `_schemaVersion: 1`；
逐条断言 `层数=到达桶+1`、`首=startCell`、`末=goalCell` 全通过；#1 到达桶 55（=先前同表可行性读数）、#2~#5 桶 57。
⚠️ 探针自身 `[EXP]` 日志两次读取为空缓冲（重编译后 console 缓冲重置）⇒ 证据改为**落盘产物+解析**；
"当时视野体非 0"只有**控制流推论**（探针在 `vols<=0` 会中止而产物写出了），U-8 直接确证仍以 A 包条目 121 为准——已如实写进条目 38。

**测试与门禁**：本包 **70→78**；全量 **367/367**（CityMap 166 + Stealth 123 + StealthPlanner 78）。
`compilationFailed=false`（本包 0 错误）；归零后 `error=0/warn=0`；校验器本包相关检查全 ok，
整体 FAIL 2 = `com.collin.stretch` 的 `DisplayDialog`（他会话，未代修）。场景末态 TerrainTest（非脏）。

**文档**：DESIGN 新增 **D-26** + §3.2 v0.2 修正 + 里程碑 M4 清单更新（导出侧收口/注入器归 M5）；
ACCEPTANCE 状态头、组 E 转"部分取证"、新增条目 **37~39**、U-6 更新、补 M4 数据集交锁门禁行；
CHANGELOG 新增 Added(M4 数据集增量) 段 + 版本行更新；HANDOFF 覆写（本文件）。

## 2. 未完成

- **M4 剩余**：① **§6.2 剪枝②（可采纳启发式引导搜索）**——实测真瓶颈（洪水占 ~81%），下一个增量就做它；
  ② 规模化耗时基准 N∈{4,8,16}（等引导搜索落地后一起测才有意义）。
- **M5**：实跑复现器 + **注入器**（沿 `cellsPerLayer` 逐层驱动、复现同一条动线）——组 E 后半条靠它收口；
  slack（§6.5，用户已拍板挪 M5/M7）。
- §6.7 参考相位时长（U-2）、增量重烘（U-3）、M6 反推路点、M7 安全岛端到端、M8 UMPM、M9 收版+两段式视频。
- `.gitignore` 表资产忽略行仍未加（工具 ACL 拒改，未绕过）；本轮把 `WaypointDataset.json(.meta)`、
  `PlayerRouteDataset.asset(.meta)` 也按**派生产物不入库**处理（内容来自探针端点参数），继续靠逐文件精确 add。

## 3. 已知坏

- **本包无红灯**。
- **本轮自查拦下三处（均在提交前修正，无遗留）**：
  ① 我把条目 38 初稿写强了（"与可行性逐位一致"像是本探针做的程序化断言、"SO 读回 5 条"像是看到了探针输出）
     ——按实际证据改写并标注推论边界；② 里程碑里同样的措辞随之收紧；③ `RoundTrip` 用例初稿含凑数重复调用与恒真断言，删净。
  教训沿用：**写文档/断言时"我知道的"和"我看到的"必须分开**。
- **console 日志缓冲在重编译后会重置**、`--level log` 可能读到空 ⇒ 取证别只依赖日志回读，
  **落盘产物/资产文件是更硬的证据**；必要时让探针把结论写进文件再解析。
- 会话中两次 Edit 因我把"Grep/Bash 检索结果"当已读范围而遭拒（工具规则：必须用 Read 覆盖）；
  一次 Edit 参数漏 `file_path`/`old_string` 被 schema 拒。都按提示重发，无副作用。
- citymap 会话活跃中（`0e767ba` 刚落，自报已清 DisplayDialog）；stretch 2 处 channel-discipline FAIL 仍在，未代修。
- 环境噪声豁免清单不变；未新增。

## 4. 下一步（建议顺序）

1. **剪枝②引导搜索**（M4 最后一个大件）：`h(cell)=绕墙格距离×Δt`（现成多源 BFS 距离场可复用，天然可采纳）。
   ⚠️ 硬约束：只许加"搜索策略/出队顺序"，**发现表与每跳合法性仍走 `ExpandSuccessors`/`BuildBlockedMask`**；
   回归网是 `PhaseKShortestTests` 7 条（首层=可行性解、折返、墙阻断）——改完必须原样绿。
2. 引导搜索落地后补 **规模化基准 N∈{4,8,16}**（一次测两套：BFS vs 引导）。
3. 然后进 **M5**：复现器 + 注入器（读 `PlayerRouteDataset`，沿 `cellsPerLayer` 逐层驱动），收组 E 后半条。
4. 门禁老三样 + 静默 70s；探针创建的窗口/SO 同次销毁；文档数字从 `test run` 输出回填后才落笔。

## 5. 勿碰项

- **不入库（派生产物）**：`Sample/VisibilityTable.asset(.meta)`、`Sample/WaypointDataset.json(.meta)`、
  `Sample/PlayerRouteDataset.asset(.meta)`。**入库（源）**：`PlannerBakeSettings.asset`、全部代码/测试/文档。
- **单一口径纪律**：`BuildBlockedMask`=唯一阻断掩码入口（含按需建层的 `EnsureLayer` 也只调它）；
  `ExpandSuccessors`=唯一邻居展开；`StepCellsFor`=唯一步长；`PhaseSolver.Horizon`=唯一视界；
  `BucketOf`=唯一填/取桶；`EnumerateWalkableCells`=唯一 footprint；`7200` 仅一处；
  **数据集唯一真相=`cellsPerLayer`**（D-26），注入/展示都从它派生，别在下游另存一套时间语义。
- **表/数据格式加"缺省即危险"字段必须抬版本**（D-22 表、D-26 `SchemaVersion`）；防线必须有可观测出口（D-23）；
  短解口径=D-25（边不相交≠Yen）；M3 可行性口径=D-24（层序传播≠A*）。
- **编辑器一律非模态**：禁 `DisplayDialog`/`SaveFilePanel`（§2.1.5，校验器判红）；确认=文本+日志+产物。
- SceneView 回调禁 `Gizmos.*`（用 `Handles.*`）；窗口视图态不写静态；测试清理判 `EditorUtility.IsPersistent` 或只回收自建实例。
- `com.collin.core`（契约签名改动走 ADR）；`com.collin.citymap`、`com.collin.stealth`、`com.collin.stretch`、
  `com.collin.citymap` 工作区现场（`CityWindow.cs` 等）、`Assets/*`、`README.md`、`packages-lock.json`、
  `AGENTS.md`、`Tools/verify_project.py`（他会话在改）——**本轮均未动、不要提交**。
- `.locks/com.collin.stealthplanner.lock`：本会话持有、**随本次提交释放**；长序列每 60s 续心跳。
