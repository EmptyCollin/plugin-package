# HANDOFF — com.collin.stealthplanner

> 覆写式交接单（非追加日志），每次交锁前整体重写五部分（AGENTS.md §4）。
> 只写可观测事实：碰了哪些文件、跑了什么指令、结果如何；不写意图与代码行为解释。

最后交接：qianwen-work-assistant · `planner-m4scale-2026-11-06` · 本次提交（M4③ 规模化耗时基准取证，**零代码改动、零新增用例**，本包仍 **79 条**）
> 前序：`78a103a`(骨架+DESIGN) → `bd0d8b4` → `4334661` → `e1e957b`(M1) → `fe44abe`(M2) → `4535b3a`(M2收口轮) → `eabaca3`(M3) → `5952f49`(M4第一增量) → `3da5586`+`b8680b6`(U-8) → `78b623c`(M4性能) → `8f85ce2`(M4数据集) → `aec7b81`(M4引导搜索) → `52d438d`(stretch v0.4.4 弹窗清零，他会话代交)

## 1. 已完成（本轮 = M4 第③项：规模化耗时基准）

**本增量不改任何 Runtime/Editor 代码、不加任何用例**：M4③ 的交付物是"实测数据 + 预算校准"，
把 DESIGN §7 表下的"上述数字是设计预算假设，非实测"换成有锚点的实测回填。

**取证方法（沿用 guided_ab 三条写法，全部落盘、不依赖日志缓冲）**
- 探针 `.tmp-inspect/scale_n_template.cs`（模板）→ `sed` 参数化出 `scale_n4/8/16.cs`，
  `unity command script eval_file --timeout 240 "<win 路径>"` 每档一次调用（§2.1.1 一次只做一件可观测的事）；
  结果落盘 `.tmp-inspect/scale_n.txt`。
- `.tmp-inspect/scale_npc.cs`（M=5 整批，守卫轴）与 `.tmp-inspect/scale_m1.cs`（M=1 单条，两轴混合）；
  落盘 `scale_npc.txt` / `scale_m1.txt`。合成表只在内存 `new VisibilityTable()` + `CreateInstance` 资产，
  **不落盘、不覆盖 `Sample/VisibilityTable.asset`**（真实表全程只读）。
- 预热一律跑一次同参数求解再计时（剔 JIT / 大数组首触页）。

**轴一 · 相位轮数 N（真实烘焙表 108×108 / n=11664 / 3 守卫片 / dt=0.5 / step=3 / M=5，horizon=N×120）**

| N | horizon | 节点数 | 墙钟 | 展开数 | 条数 | 首条到达层 |
|---|---|---|---|---|---|---|
| 4 | 480 | 5,598,720 | 498ms | **244,473** | 5 | 55 |
| 8 | 960 | 11,197,440 | 507ms | **244,473** | 5 | 55 |
| 16 | 1920 | 22,394,880 | 546ms | **244,473** | 5 | 55 |

⇒ **展开数与答案（到达层序列恒 `[55,57,57,57,57]`）逐位相同**：引导搜索成本由"到撤离点的最短路径长度"
决定，**与视界长度无关**；视界只放大掩码缓冲与按需建层的一次性成本（4×视界 ⇒ +48ms）。
附测**超上限兜底**：horizon=2880（33,592,320 节点 > `MaxLayerCells` 25,165,824）⇒ `ok=False`、**0ms**、
原因文本可读（「请减小相位轮数 N / 相位窗 P 或增大格长」）⇒ **未静默算爆内存**。

**轴二 · 守卫数 G（合成危险密度：108×108 / 0.5m / Δt=0.5s / horizon=480 / 每片周期 60s / 整行满布连续带）**

| G | 表体积 | M=1 单条 | M=5 整批 | 展开数(M=5) | 掩码 160 次构建 |
|---|---|---|---|---|---|
| 4 | 711,504 B | **209ms** | 520ms | 243,866 | 353ms（2.2ms/层）|
| 8 | 1,411,344 B | **270ms** | 577ms | 228,694 | 581ms（3.6ms/层）|
| 16 | 2,811,024 B | **460ms** | 724ms | 209,063 | 1088ms（6.8ms/层）|

真实表（3 守卫）M=1 单条：horizon 480/960 **108ms**、1920 **217ms**。
⇒ 增长**亚线性但分口径**（M=1 ×2.20 / M=5 ×1.39，**不可互换**）；**展开数随 G 下降**（危险更密⇒前沿更早被阻断）；
**真正线性的是每层掩码构建** ⇒ 后续要再压成本，对症的是掩码侧不是搜索侧。

**表体积核验（§7 目标 < 4MB / R-4）**：真实表位图本体 **258,066 字节 = 0.26 MB（预算的 6.5%）**、
磁盘 `.asset` 540,594 字节。换算式 `每片 = ⌈周期/Δt⌉ × 格数 ÷ 8`（本网格 1458 字节/桶/片）：
真实表三片合计 246,402 字节 ⇒ 每片 ~82,134 / ~56.3 桶 ⇒ **周期 ~28.2s**（落在 §7 的 20~40s 档）；
同 4MB 预算下可容片数：真实表周期 **~48 片**、40s ⇒ **34 片**、60s ⇒ **23 片**。

**文档**：DESIGN 新增 **D-28**（两轴分开量 + 成分拆开报 + 单位/可比性纪律）+ §7 实测回填段 +
里程碑 M4③ 标 **✅ 已达成**；ACCEPTANCE 状态头改写 + 组 C 第 2 条由「⏳部分数据/不得声称通过」转
「✅已取证（附边界）」+ 条目 **43~45**；CHANGELOG 新增 `### Benchmarkd（M4③）` 段（含门禁与通道纠正）；
HANDOFF 覆写（本文件）。**M4 至此 ①③ 全达成**；② 注入器 ⇒ M5。

## 2. 未完成

- **M5**：实跑复现器 + 注入器（读 `PlayerRouteDataset` 沿 `cellsPerLayer` 逐层驱动，收组 E 后半 + 组 C「逐相位复现」两条）；
  slack（§6.5，用户已拍板挪 M5/M7）。
- 剪枝③（Pareto 支配）、剪枝④（相位 mod 归并）、§6.7 参考相位时长（U-2）、增量重烘（U-3）、M6~M9。
- **§7 预算表剩余未实测行**：「增量重烘 < 1s」（U-3 未实现，不得声称已达）。

## 3. 已知坏

- **本包无红灯**。全量 **386/386**（26.78s，本包 79/79 全绿）；`compilationFailed=false`；
  控制台 `console clear` → 重放本会话验证动作（三档真实表探针 + 守卫轴 + M=1 探针）→ `error=0 / warning=0`；
  `verify_project.py` **FAIL 0**（stretch v0.4.4 已把那 2 处 `DisplayDialog` 清零）。
- ⚠️ **校验器 `check_console` 有共享文件级缺陷（本轮实测坐实，本包未代修）**：
  `Tools/verify_project.py` 用 `unity --json command "console status"`（命令名**整体加引号**），
  实测该形态被服务端命令索引拒绝 ⇒ **每次跑校验器都会自己往控制台写一条 error**
  （`No command named 'console status' is available. Did you mean 'console status'?`，堆栈全在 `com.unity.pipeline`，
  不含任何项目程序集），并让校验器输出 `[WARN] console: 无法读取控制台状态（'NoneType' object has no attribute 'get'）`。
  本轮环境的可用形态是**不加引号**（`unity command console status` / `console clear` / `script recompile`）。
  ⚠️ **与旧记录并列、不互相推翻**：本包 M2 交接记录与 citymap 交接单记的是**相反形态**（必须整体加引号，
  漏引号报 `No command named 'script'`）⇒ 两边都是真实观察，只能得出
  **"两种形态都见过失败案例，遇一次失败立刻换另一种试，禁止连发"**（§2.1.2：CLI 超时不取消服务端执行）。
  该文件属 §7 共享文件 + §10 规则类变更（须先给用户 diff、并同步 AGENTS.md），
  **本会话未自行修改，已上报**。
  ⇒ 门禁期间的取证顺序：**最后一步才跑校验器**，并在其自造 error 后重新 clear + 重放，
  控制台归零一律以手动 `console status` 的 `groundTruth` 为准。
  ⚠️ AGENTS.md §1.6b 括注「两段式命令名须整体加引号」与实测相反，同属规则文档待纠正项。
- **本轮我自己造的 5 处口径错，全部在提交前改正（如实留痕，别再照旧文抄）**：
  ① 体积触线片数凭"4MiB/40s"算成 **36 片**，与 §7 的十进制 MB 口径混用 ⇒ 按 4,000,000 字节真值是 **34 片**；
  ② 同一批字节数在 MiB 与 MB 间混写（0.68/1.35/2.68 与 0.71/1.41/2.81 并存）⇒ 统一十进制 MB；
  ③ 「G×4 ⇒ 耗时×1.4」写成唯一倍数，实际 M=1 是 **×2.20**、×1.39 只属 M=5 ⇒ 分口径写；
  ④ 「外推 32 守卫破线」初版无依据 ⇒ 改为按实测比率 ×1.70 外推 **784ms** 并标注**外推非实测**；
  ⑤ 「首烘 0.82~0.88s（条目 10/12）」引用错——条目 12 是冷启动缺陷记录、不含耗时 ⇒ 改引条目 10 的 0.82s。
  **外加一处凭印象写次数**：D-28 原写"本包第四次踩此坑"无可核依据 ⇒ 改为"反复栽过的同类坑"。
  教训同源（H-67 / D-26 一族）：**换算数必须现算、引用编号必须现查、次数没有依据就别写。**
- **差点犯的口径错（被实测拦下）**：一开始想用 M=5 整批耗时 **÷5** 去对 §7「单条求解 < 0.5s」预算行。
  实测证明**这个反推系统性低估、且幅度随守卫数变大**：真实表 498/5=99.6ms vs 真值 **108ms**（低估 8%）；
  合成 G=4 104 vs 209（**50%**）、G=8 115 vs 270（57%）、G=16 **145 vs 460（68%）**
  ⇒ 在 G=16 会把"已贴预算线"说成"宽松达标"。根因：`FindRoutes` 的 M 轮重搜**复用同一份逐桶掩码缓存**
  （`built[]` 按需建层），第 2~M 轮边际成本远低于首轮。⇒ 预算口径必须**另跑 M=1**（已跑，条目 44）。
- **跨会话数字不可比（本包又验证一次）**：N=4 档本轮 498ms、上一会话同表同参 466ms ⇒ **差值不作结论**；
  只有**展开数 244,473 逐位相同**可比（纯函数计数）。文档已把这条写成纪律。
- 环境噪声豁免清单不变（`services-gateway` 等 3 条），本轮未新增、也未使用。

## 4. 下一步（建议顺序）

1. **进 M5**：实跑复现器 + 注入器（组 E 后半、静态设计 4 的注入侧、组 C「每条解逐相位复现」二次验证）。
   实跑前确认真实场景视野体在场（U-8 已在 A 包 `3da5586` 修复，本包真实场景取证不再需要注入内存表兜底）。
2. slack（§6.5）随 M5 实跑的察觉度一起做，不要回表里硬造。
3. **若要继续压守卫数轴的成本**：对症的是**每层掩码构建**（实测 6.8ms/层@16 守卫，随 G 线性），
   不是搜索侧（展开数随 G 反而下降）。外推 G=32 单条约 784ms 已破 §7 预算 ⇒ 真出现 30+ 守卫关卡时，
   调格长/Δt 或把掩码构建挪到后台，**不要先动启发式**（D-27：启发必须保持忽略墙与危险窗）。
4. 门禁老三样：`script recompile`（不加引号）→ `test run`（一次交锁只跑一次）→ `console clear` →
   重放本会话验证动作 → `console status` → **最后**跑 `verify_project.py`；心跳每 60s 续
   （`.tmp-inspect/heartbeat_loop.py <包名>` 可后台跑，锁消失即退出）。

## 5. 勿碰项

- **不入库（派生产物）**：`Sample/VisibilityTable.asset(.meta)`、`Sample/WaypointDataset.json(.meta)`、
  `Sample/PlayerRouteDataset.asset(.meta)`。**入库（源）**：`PlannerBakeSettings.asset`、全部代码/测试/文档。
- **单一口径纪律**：`BuildBlockedMask`=唯一阻断掩码入口；`ExpandSuccessors`=唯一邻居展开
  （两个签名是同一实现，旧签名=新签名 hereOffset=0，别再加第三种）；`StepCellsFor`=唯一步长；
  `PhaseSolver.Horizon`=唯一视界；`BucketOf`=唯一填/取桶；`EnumerateWalkableCells`=唯一 footprint；
  `7200` 仅一处；数据集唯一真相=`cellsPerLayer`（D-26）。
  **`LastExpansionCount` 只许读、不许被任何求解分支读取**——诊断量进判定就等于把观测做成第二套口径。
- **规模化基准的两条轴不得合并成一个数**（D-28）：相位轮数轴只用真实表、守卫数轴只能合成表且必须标"合成密度"；
  体积一律**十进制 MB**；墙钟只在同会话互比，跨会话只比展开数。
- **启发式必须保持"忽略墙与危险窗"**（D-27）：想提精度而引入绕墙距离场/危险窗 ⇒ 可能高估 ⇒
  破坏可采纳性 ⇒ 首条不再是解、与 M3 自相矛盾。回归网：`FirstRouteArrival_MatchesPhaseSolver`、
  `ForcedReentry_NoTie_UniqueEarliestRoute`、`.tmp-inspect/guided_diff_test.py`（Python 差分）。
- **并列最优之间选哪条不是契约**（D-27 末段）：写用例钉"某走法必须存在"时，必须构造**无并列、该走法被强制**的场景。
- 表/数据格式加"缺省即危险"字段必须抬版本（D-22/D-26）；防线必须有可观测出口（D-23）。
- **编辑器一律非模态**：禁 `DisplayDialog`/`SaveFilePanel`（§2.1.5，校验器判红）；
  SceneView 回调禁 `Gizmos.*`（用 `Handles.*`）；测试清理判 `EditorUtility.IsPersistent` 或只回收自建实例。
- **`eval_file` 戒律**：无 `using`、无裸 `return;`、泛型写全 `System.Collections.Generic.*`、
  代码**只读 `Failure` 的小写字段 `ok`/`reason`**（本轮先按大写写、编译前自查改掉）；
  参数**本轮实测用位置参数成功**（`--path` 实测报「没有参数 --path」；`--file` 未在本轮验证，沿旧记录待查）；
  每节即时 `UnityEngine.Debug.Log` + **落盘**。
- `com.collin.core`（契约签名改动走 ADR）、`com.collin.citymap`（另一会话活跃中）、`com.collin.stealth`、
  `com.collin.stretch`（v0.4.4 已收版）、`Assets/*`、`README.md`、`packages-lock.json`、`AGENTS.md`、
  `Tools/verify_project.py`、`Tools/console_allowlist.json`——**本轮均未动、不要提交**。
- ⚠️ 工作区仍存他会话未提交改动（`AGENTS.md`、`Tools/verify_project.py`、`README.md`、`packages-lock.json`、
  `Assets/test.asset`(删)、`Assets/CityProject.asset`(未跟踪)、citymap 的 `TerrainProfile.asset` /
  `TerrainTest.unity`）＋本包派生产物 3 项 ⇒ 本包提交**逐文件 `git add`**，一律不带它们。
- `.locks/com.collin.stealthplanner.lock`：本会话持有、**随本次提交释放**。
