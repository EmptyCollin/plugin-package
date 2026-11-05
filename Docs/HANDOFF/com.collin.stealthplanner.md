# HANDOFF — com.collin.stealthplanner

> 覆写式交接单（非追加日志），每次交锁前整体重写五部分（AGENTS.md §4）。
> 只写可观测事实：碰了哪些文件、跑了什么指令、结果如何；不写意图与代码行为解释。

最后交接：qianwen-work-assistant · `planner-guided-2026-11-05` · 本次提交（M4 引导搜索增量：§6.2 剪枝② 落地 + 决策 D-27 + 本包 79 条）
> 前序：`78a103a`(骨架+DESIGN) → `bd0d8b4` → `4334661` → `e1e957b`(M1) → `fe44abe`(M2) → `4535b3a`(M2收口轮) → `eabaca3`(M3) → `5952f49`(M4第一增量) → `3da5586`+`b8680b6`(U-8) → `78b623c`(M4性能) → `8f85ce2`(M4数据集)

## 1. 已完成（本轮 = M4 引导搜索增量，§6.2 剪枝②）

**Runtime**：
- `PhaseKShortest.cs`：`SearchEarliest` 从「逐层灌满全图的分层 BFS」改为**可采纳启发式引导 best-first**。
  - 启发 `GoalHeuristic = ceil(切比雪夫格距 / stepCells)`，**忽略墙与危险窗**（计入就可能高估 ⇒ 破坏可采纳性）；
    刻意**不用** M3 绕墙距离场当启发（它不含危险窗，绕远即高估）。DESIGN 新增 **D-27**。
  - **视界剪枝** `layer + h > horizon-1 ⇒ 不入队`；起点下界超视界直接判无解。
  - ⚠️ **目标改「首次出队」返回**（旧 BFS 是「首次发现」）——引导式乱序发现，沿用旧写法会返回更晚路线。
  - 堆键 `(f,layer,cell)` 打包进一个 long ⇒ 同分先浅层、同层先小格号，**与插入顺序无关**（确定性未破，实测重复跑逐位相同）。
  - `(cell,layer)` 发现标志 + `Fill(-1)` 哨兵**原样保留**（D-25 两条坑未复发）。
  - 新增 `public static long LastExpansionCount`：**只作诊断的出堆计数，不参与任何判定**（D-23 同源要求：收益必须可观测）。
- `PhaseReachability.cs`：`ExpandSuccessors` 新增**带层偏移**重载（`maskHere/hereOffset`+`maskNext/nextOffset`），
  `IntermediateCellsClear` 同步加偏移；**旧签名转为 `hereOffset=0` 别名** ⇒ M2 传播与既有测试仍走同一份实现，
  未造第二套判据。加此重载的原因：引导式乱序弹节点，若仍每次弹节点整层拷贝 11664 格，拷贝会把省下的展开吃回去。

**测试**：本包 `PhaseKShortestTests` 7 → **8 条**（本包 78 → **79**）。
- 新增 `ForcedReentry_NoTie_UniqueEarliestRoute`：四格走廊手工推演出**折返被强制 + 最早到达层唯一**
  （路线唯一 `[0,1,0,1,2,3]`、到达层唯一 5），并断言首条到达层 == `PhaseSolver`。
- ⚠️ **改写了一条旧断言**：`ReentryAfterForbidden` 原「第二条必含折返」在全量首跑变红。根因**不是求解器坏**——
  该场景有**两条并列最早解**（到达层同为 4：`[0,0,0,1,2]` 与 `[0,1,0,1,2]`），BFS 与引导式各取其一、都正确；
  旧断言实为**把某种搜索顺序当契约**。改为钉「≥2 条 + 两两边不相交 + 逐跳合法」，折返迁往新用例。

**实测（真实烘焙表 108×108/n=11664、horizon=480、step=3、M=5；同会话带 JIT 预热；结果落盘取证）**：
- 基线 BFS（改造前逐行复刻，**发现 goal 当场收口**）：**1807ms / 展开 1,311,731**；
  引导式：**466ms / 展开 244,473** ⇒ **展开削减 5.37×、墙钟加速 3.88×**；
  首条到达层 **55 = 55**（与条目 29/33/34 同表读数逐位一致）、条数 **5 = 5**、**逐跳合法 5/5**；重复跑逐位相同。
  （墙钟加速低于展开削减属正常：多了堆维护与逐节点启发计算；**机制指标才是"削掉无用洪水"的证据**。）
- Python 差分 **4000 例**（两套算法逐行复刻 + 一份独立朴素最早到达真值）：有解性分歧 0、
  到达层偏离真值 0、非法路线 0、边不相交破坏 0、非确定性 0；**关掉视界剪枝答案不变** ⇒ 剪枝不错杀可行解。
  中规模网格削减比随规模上升：40²/120=5.1×、60²/200=5.9×、80²/300=6.5×。

**门禁（最终代码状态）**：`compilationFailed=false`（本包 0 错误）；
`console clear` → 重编译 → 重放 A/B 探针 → 静默 72s ⇒ **error=0 / warning=0**；
校验器**本包相关检查全 ok**（pkg-layout / runtime-purity 等）。
全量 **377**：本包 **79/79 全绿**；CityMap 175（**7 条失败全在他包水路功能进行中改动**）、Stealth 123。
⚠️ 校验器整体 FAIL 3 = citymap 超时锁（381s，按 §3 未删）+ stretch 两处 `DisplayDialog`（他包，未代修）。

**文档**：DESIGN 新增 **D-27** + §6.2 末段实施记录 + 里程碑 M4 ①标达成（含受阻与补跑经过）；
ACCEPTANCE 状态头 + 条目 **40~42**；CHANGELOG 新增 M4 引导搜索段（含三条过程教训）；HANDOFF 覆写（本文件）。

## 2. 未完成

- **M4 只剩一项**：规模化耗时基准 N∈{4,8,16}（引导式已落地，现在测才有意义）。
- **M5**：实跑复现器 + 注入器（读 `PlayerRouteDataset` 沿 `cellsPerLayer` 逐层驱动，收组 E 后半条）；
  slack（§6.5，用户已拍板挪 M5/M7）。
- 剪枝③（Pareto 支配）、剪枝④（相位 mod 归并）、§6.7 参考相位时长（U-2）、增量重烘（U-3）、M6~M9。

## 3. 已知坏

- **本包无红灯。**
- **本轮三次自造问题，均在提交前修掉、无遗留**（如实记）：
  ① 追加用例 8 时替换锚点选错，**把用例 7（墙阻断）整条删掉** ⇒ Read 复核后原样补回；
  ② A/B 基线第一版写成「整层展完才收口」，而改造前实际是「发现 goal 当场 return」⇒
     **会把基线改贵、加速比虚高**，逐行比对旧实现后改正（对照实验的基线必须与原实现逐位一致）；
  ③ `eval_file` 探针三连坑（`using`、裸 `return;`、`System.HashSet<>` 应为 `System.Collections.Generic.HashSet<>`）
     + 参数是 `--file` 而非位置参数。
- **外部阻塞的处理方式值得沿用**：本轮曾被 citymap 会话半成品挡住全域编译约 1 小时（一度 104 处 CS 错误、
  锁心跳 >2300s）。本包 0 错误 ⇒ 未代修、未删他人超时锁、未带红提交；
  经用户拍板「等对方自己修」，等待期先完成不依赖编译域的 Python 差分验证，恢复后立刻补跑 C# 套件与真实表 A/B。
- 环境噪声豁免清单不变（`services-gateway` 等 3 条），本轮未新增。

## 4. 下一步（建议顺序）

1. **规模化基准 N∈{4,8,16}**：沿用 `.tmp-inspect/guided_ab.cs` 的写法（同会话预热 + 落盘取证 +
   `LastExpansionCount` 机制指标），对比引导式在不同相位轮数下的展开数/耗时增长曲线，
   并核 `VisibilityTableAsset` 表体积是否仍 <4MB（DESIGN 预算）。
2. 然后进 **M5**：复现器 + 注入器（组 E 后半、动态设计 1）；实跑前须确认真实场景视野体在场（U-8 已修）。
3. 门禁老三样：`"script recompile"` → `test run`（一次交锁只跑一次）→ `console clear` →
   重放本会话验证动作 → 静默 70s → `console status` → `verify_project.py`；心跳每 60s 续。

## 5. 勿碰项

- **不入库（派生产物）**：`Sample/VisibilityTable.asset(.meta)`、`Sample/WaypointDataset.json(.meta)`、
  `Sample/PlayerRouteDataset.asset(.meta)`。**入库（源）**：`PlannerBakeSettings.asset`、全部代码/测试/文档。
- **单一口径纪律**：`BuildBlockedMask`=唯一阻断掩码入口；`ExpandSuccessors`=唯一邻居展开
  （**两个签名是同一实现**，旧签名=新签名 hereOffset=0，别再加第三种）；`StepCellsFor`=唯一步长；
  `PhaseSolver.Horizon`=唯一视界；`BucketOf`=唯一填/取桶；`EnumerateWalkableCells`=唯一 footprint；
  `7200` 仅一处；数据集唯一真相=`cellsPerLayer`（D-26）。
  **`LastExpansionCount` 只许读、不许被任何求解分支读取**——它是诊断量，进判定就等于把观测做成第二套口径。
- **启发式必须保持"忽略墙与危险窗"**（D-27）：想提精度而引入绕墙距离场/危险窗 ⇒ 可能高估 ⇒
  破坏可采纳性 ⇒ 首条不再是解、与 M3 自相矛盾。回归网：`FirstRouteArrival_MatchesPhaseSolver`、
  `ForcedReentry_NoTie_UniqueEarliestRoute`、`.tmp-inspect/guided_diff_test.py`（Python 差分）。
- **并列最优之间选哪条不是契约**（D-27 末段）：写用例钉"某走法必须存在"时，必须构造**无并列、该走法被强制**的场景。
- 表/数据格式加"缺省即危险"字段必须抬版本（D-22/D-26）；防线必须有可观测出口（D-23）。
- **编辑器一律非模态**：禁 `DisplayDialog`/`SaveFilePanel`（§2.1.5，校验器判红）；
  SceneView 回调禁 `Gizmos.*`（用 `Handles.*`）；测试清理判 `EditorUtility.IsPersistent` 或只回收自建实例。
- **`eval_file` 戒律**（本轮各踩一次）：无 `using`、无裸 `return;`、泛型写全 `System.Collections.Generic.*`、
  参数 `--file`、每节即时 `Debug.Log` + **落盘**（日志缓冲重编译后会读空）。
- `com.collin.core`（契约签名改动走 ADR）、`com.collin.citymap`（**另一会话活跃中，水路功能改到一半**）、
  `com.collin.stealth`、`com.collin.stretch`（2 处 DisplayDialog 待其会话自修）、`Assets/*`、`README.md`、
  `packages-lock.json`、`AGENTS.md`、`Tools/verify_project.py`、`Tools/console_allowlist.json`——**本轮均未动、不要提交**。
- `.locks/com.collin.stealthplanner.lock`：本会话持有、**随本次提交释放**。
