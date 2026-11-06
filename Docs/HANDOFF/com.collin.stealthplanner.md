# HANDOFF — com.collin.stealthplanner

> 覆写式交接单（非追加日志），每次交锁前整体重写五部分（AGENTS.md §4）。
> 只写可观测事实：碰了哪些文件、跑了什么指令、结果如何；不写意图与代码行为解释。

最后交接：qianwen-work-assistant · `planner-m5-2026-11-06` · 本次提交（**M5 第一增量 = U-2 建议参考相位时长**，本包用例 79→**95**）
> 前序：`78a103a`(骨架) → `e1e957b`(M1) → `fe44abe`(M2) → `4535b3a`(M2收口) → `eabaca3`(M3) → `5952f49`(M4①) → `78b623c`(M4性能) → `8f85ce2`(M4数据集) → `aec7b81`(M4引导搜索) → `52d438d`(stretch v0.4.4) → `7b78c8c`(M4③规模化基准) → 本次

## 0. 本轮用户指令（三条，均已落地）
1. **U-2 挪进 M5** ⇒ 已在 DESIGN §9 与 ACCEPTANCE「未通过项」双处改归属，并在 M5 内**当场收口交付**。
2. **U-3 排成 M10** ⇒ DESIGN §9 新增 M10 行；§7 预算行「增量重烘 < 1s」标注"到 M10 才允许实测判定"。
3. **开关 M5** ⇒ M5 已开启，第一增量已交付并交锁。

## 1. 已完成（M5 第一增量：U-2）

**Runtime（新增 1 文件 + 1 公开写入口）**
- `Runtime/PhaseReference.cs`（**新**）：§6.7 实现体。`Suggest(IReadOnlyList<float> cycles, float timeStep)`
  → `k_i=max(1,round(c_i/Δt))` → 整数 LCM（先除后乘推迟溢出）→ `L≤MaxAlignedBuckets` 走严格出口
  （`L×Δt`）、超上限走兜底出口（`max(c_i)×2` + 含「近似」的 Warning 文案）。
  退化输入（名单 null/空、Δt≤0/NaN/Inf、周期 0/NaN/Inf）⇒ `Valid=false` + 中文原因，**不返回 0 秒**。
  兜底阈值**只引用常量**，文件内无字面量 7200（有守卫用例 + "必须真扫到文件"前置断言）。
- `Runtime/PlannerBakeSettings.cs`：新增 `SetPhaseWindow(float)`——相位窗 P 的**唯一公开写入口**
  （理由：Editor 程序集碰不到私有字段；留反射/字符串路径等于把字段名当跨程序集契约）。
  只钳自身（下限 1s）、NaN/无穷整个忽略、**不牵连 Δt/格长/N**。

**Editor**
- `Editor/StealthTimelineWindow.cs`：新增「建议参考相位时长」只读区块（字段 `_phaseRef`、
  `Reload()` 内随表刷新、不每帧重算）。Δt 取**表的** `TimeStep`（不是设置资产的——设置可能改了未重烘）。
  「一键填入为手配 P」按钮：`Undo.RecordObject` + `SetPhaseWindow` + `SetDirty` + `Debug.Log`，
  **只有该按钮会改设置资产**。

**测试（本包 79 → 95，共新增 16 条）**
- `Tests/Editor/PhaseReferenceTests.cs` **11 条**：量化进位、互质爆炸兜底、**边界恰=cap 不兜底 / 超 cap 即兜底**、
  严格与兜底两条出口的文案都带正确性声明（+ **反向断言**旧句不得回流）、退化输入给原因不抛、
  NaN 周期按 1 桶不污染 LCM、纯函数同输入同输出、**算法零副作用**（含反向对照证明写入口确实能改）、
  只钳自身不牵连他参、包内无第二处 7200。
- `Tests/Editor/PhaseWindowConsistencyTests.cs` **5 条**（组 G 第 3 条**首次取证**）。

**⚠️ 本增量最重要的产出：实测推翻 §6.7 对外文案一句，登记 D-29**
`PhaseSolver.Horizon = MaxPhases × BucketsPerPhase`、`BucketsPerPhase = ⌈P/Δt⌉` ⇒ **P 在视界公式里**。
所以 §6.7「P 取多大**不改变**『是否存在 ≤N 相位的可行解』」**不成立**：固定 N 只调小 P 会缩短覆盖时长、
把有解截断成无解。准确口径：**不漏解取决于总视界 `N×⌈P/Δt⌉`**（原句还说"真正决定是否漏解的是 N"也不完整）。
- **不是实现 bug**（实现按 D-02/D-13 是对的），矛盾在文档给策划的话。
- **未为圆文案改实现**（把 horizon 改成只跟 N 走 = 删掉 P 的时间语义）。
- **§6.7 原句保留未动**（对外口径变更属规则级、须用户拍板）⇒ **用户 2026-11-07 已裁定并改写原句**；
  但面板文案 `CorrectnessDeclaration` 已先按正确口径落地（保留成立的前半句"P 不参与可见性判定"），
  兜底出口那句同步补「与总视界覆盖时长」。

**实测读数（真实烘焙表，窗口内）**
三片周期 **26.5 / 28.0 / 29.9s** ⇒ k=53/56/60 ⇒ **LCM=44,520 远超 7200 ⇒ 真实关卡本身就落在兜底档**，
建议值 **59.73s**（手配 P=60 恰在旁边 ⇒ 策划当前取值习惯合理）。
核心防线实测：`Reload 前 P=60 N=4` → `Reload 后 P=60 N=4` **一字未改**；
反向对照 `SetPhaseWindow(44.5)` ⇒ P=44.5、桶数联动 89；越界钳到 1s；NaN 忽略。
磁盘 `Sample/PlannerBakeSettings.asset` 的 `_phaseWindow: 60` 取证后 `git status` **无变更**。

**同轮清除的本包自造红灯**
`PhaseKShortest.EdgeKey` 的 **CS0675**（末位裸 `int` 参与 `long` 的 `|` ⇒ 符号扩展）——
该告警自 M4 引导搜索增量起就挂在本包、记忆条目里记着但一直没真修。按同文件 `PackKey` 既有写法补 `(uint)`，
**语义不变**（`toCell < 2^21 ⇒ 非负 ⇒ 零扩展与符号扩展同值），**不走 §1.8 豁免**。
重编译后 CS0675 不再出现、测试逐位不变。⚠️ 注释里**不写行号**引用同族方法（写死会漂移），改按 `PackKey` 名引用。

**文档**：DESIGN §9（M5 标注第一增量达成 + **新增 M10**）、§10 决议表新增 **D-29**、§7 实测回填段引用校正；
ACCEPTANCE 状态头、组 G 三条判据全部改态（其中第 3 条为**否定性结论**）、U-2/U-3 归属改写、条目 **46~48**；
CHANGELOG 新增 M5 第一增量段；HANDOFF 覆写（本文件）。

## 2. 未完成

- **M5 剩余**：实跑复现器（单窗口，真调 A）+ 注入器（读 `PlayerRouteDataset` 沿 `cellsPerLayer` 逐层驱动）
  ⇒ 收组 E 后半、组 C「每条解逐相位复现」；slack（§6.5，与实跑察觉度同做）；预测/实况差异诊断（动态2）。组 C/F。
- ✅ **D-29 已裁定并落地（2026-11-07）**：§6.7 原句按总视界口径改写完毕，旧句以删除线留档并标注「勿再引用」。
- M6 反推 NPC 路点（动态3）、M7 `ISafeIslandProvider` 端到端（组 B）、M8 UMPM（需申报）、
  M9 演示视频+收版、**M10 增量重烘（U-3，本轮新排）**。
- 剪枝③（Pareto 支配）、剪枝④（相位 mod 归并）。

## 3. 已知坏

- **本包无红灯**。全量 **402/402**（CityMap 184 + Stealth 123 + StealthPlanner **95**）；
  `compilationFailed=false`；控制台 **0 error / 0 warning（零豁免条目）**。
- **本轮自造的 3 条 error 已归因并清掉（如实记）**：试命令形态时打出
  `No command named 'script'/'console' is available`——堆栈全在 `com.unity.pipeline`、不含项目程序集，
  属 CLI 传参形态问题，非本包代码缺陷。**命令形态结论（第 N 次验证，别再当单一口径）**：
  本轮 `script recompile` / `console status` **不加引号被拒**、**加引号可用**；
  而 `script eval_file --file <win路径>` **不加引号可用**（`--path` 报无此参数）。
  ⇒ **遇一次失败立刻换另一种形态，禁止连发**（§2.1.2）。
  连带：`verify_project.py` 的 `check_console` 用加引号形态，故它**每次运行自造 1 条 error** 并输出
  `[WARN] console: 无法读取控制台状态`——属 §7 共享文件 + §10 规则类缺陷，**本会话未自行修改，已上报**。
  ⇒ 归零取证一律以手动 `console status` 的 `groundTruth` 为准，且**最后一步才跑校验器**。
- **本轮我自己造的判据错（3 处，均在提交前用现算真值纠正）**：
  ① 照抄 §6.7 举例「23 与 29 ⇒ 兜底」写断言——**Δt=0.5 下其 LCM=1334 根本不超 7200**，
     那是 Δt=1 的口径；现算后改用 44.5/48.5（k=89/97 ⇒ LCM=8633）。
  ② 「P=30/60/120 跨越后读数相等」是**假判据**——三个值都不是周期 8s 的整数倍，相位本就错开；
     改为**双向判据**（整数倍必等 / 非整数倍必不等）。
  ③ 兜底倍数注释写成 10,342,022，现算真值 **10,352,162**；另"5 处 LCM 乘积"等凭印象数字全部重算。
  **外加一处结构判据的选择**：组 G 第 3 条初版想用「改 P 后逐位比对危险掩码」——但表根本没有 settings 引用，
  该比对**必然恒真**（= 绿得毫无意义），改成扫公开签名里有没有 `PlannerBakeSettings` 参数。
  （H-67 / D-26 同族第五次以上：**期望值与判据本身都必须现算现证。**）
- **eval 探针新踩两条（已在文档留痕）**：① `PhaseReference.Result` 的成员是**公共字段**，
  用 `GetProperty` 取回 null ⇒ NRE（须 `GetField`）；② `EditorWindow.CreateInstance` **不跑 OnEnable**
  ⇒ `_phaseRef` 停在 struct 默认值（Valid=false），会造出"没改写 P"的**假绿**——必须反射调窗口静态 `Open()`。
- 环境噪声豁免清单不变（`services-gateway` 等 3 条），本轮未新增、未使用。

## 4. 下一步（建议顺序）

0. **本轮无悬置决策**：D-29 已由用户 2026-11-07 裁定并落地（§6.7 原句按总视界口径改写、
   兜底举例纠正、文档头 v0.3、D-29 备选③处置记录、`_maxPhases` 的 Inspector Tooltip 同步；
   取证见 ACCEPTANCE **条目 49**）。⇒ 直接进第 1 条。
2. **M5 主件**：实跑复现器 + 注入器（组 E 后半 / 组 C 逐相位复现）。实跑前确认真实场景视野体在场
   （U-8 已在 A 包 `3da5586` 修复，本包真实场景取证不再需要注入内存表兜底）。
   ⚠️ Play 态取证有既有硬约束：窗口失焦时 Play 循环不推进 ⇒「探针 + focus + 立即取证」连做，不能并行发指令。
3. slack（§6.5）随实跑的察觉度一起做，不要回表里硬造。
4. 若要继续压求解成本：**对症的是每层掩码构建（6.8ms/层@16 守卫，随守卫数线性），不是搜索侧**
   （展开数随守卫数反而下降）。外推 G=32 单条约 784ms 已破 §7 预算 ⇒ 真上 30+ 守卫关卡时调格长/Δt，
   **不要先动启发式**（D-27：启发必须保持忽略墙与危险窗）。
5. 门禁老三样：`script recompile`（形态遇错即换）→ `test run`（一次交锁只跑一次）→ `console clear` →
   重放本会话验证动作 → `console status` → **最后**跑 `verify_project.py`；心跳每 60s 续
   （`python .tmp-inspect/heartbeat_loop.py com.collin.stealthplanner`，锁消失即退出）。

## 5. 勿碰项

- **不入库（派生产物）**：`Sample/VisibilityTable.asset(.meta)`、`Sample/WaypointDataset.json(.meta)`、
  `Sample/PlayerRouteDataset.asset(.meta)`。**入库（源）**：`PlannerBakeSettings.asset`、全部代码/测试/文档。
- **单一口径纪律**：`BuildBlockedMask`=唯一阻断掩码入口；`ExpandSuccessors`=唯一邻居展开（两签名同一实现）；
  `StepCellsFor`=唯一步长；`PhaseSolver.Horizon`=唯一视界；`BucketOf`=唯一填/取桶；
  `EnumerateWalkableCells`=唯一 footprint；`MaxAlignedBuckets=7200` **仅 `PlannerBakeSettings` 一处**
  （`PhaseReference` 必须引用常量，硬编码会被守卫用例判失败）；
  **相位窗 P 的唯一写入口=`SetPhaseWindow`**（别再开第二条，也别让算法/求解路径回写）；
  `LastExpansionCount` 只许读、不许被任何求解分支读取。
- **建议参考时长必须是只读展示**（D-13）：任何"自动填入 P"的路径都算违宪，只有面板按钮可以改。
- **启发式必须保持"忽略墙与危险窗"**（D-27）；**并列最优之间选哪条不是契约**（D-27 末段）。
- 表/数据格式加"缺省即危险"字段必须抬版本（D-22/D-26）；防线必须有可观测出口（D-23）。
- **编辑器一律非模态**：禁 `DisplayDialog`/`SaveFilePanel`（§2.1.5）；SceneView 回调禁 `Gizmos.*`（用 `Handles.*`）；
  测试清理判 `EditorUtility.IsPersistent` 或只回收自建实例。
- **`eval_file` 戒律**：无 `using`、无裸 `return;`、无 `goto`、不留未使用局部变量（CS0219 会打 warning 破门禁）、
  泛型写全 `System.Collections.Generic.*`、参数 `--file` + **Windows 绝对路径**、
  struct 结果用 **`GetField`** 而非 `GetProperty`、窗口必须走静态 `Open()`（`CreateInstance` 不跑 OnEnable）。
- `com.collin.core`（契约签名改动走 ADR）、`com.collin.citymap`（另一会话活跃中）、`com.collin.stealth`、
  `com.collin.stretch`（v0.4.4 已收版）、`Assets/*`、`README.md`、`packages-lock.json`、`AGENTS.md`、
  `Tools/verify_project.py`、`Tools/console_allowlist.json`——**本轮均未动、不要提交**。
- ⚠️ 工作区仍存他会话未提交改动（`AGENTS.md`、`Tools/verify_project.py`、`README.md`、`packages-lock.json`、
  `Assets/test.asset`(删)、`Assets/CityProject.asset`(未跟踪)、citymap 的 `TerrainProfile.asset` /
  `TerrainTest.unity`）＋本包派生产物 3 项 ⇒ 本包提交**逐文件 `git add`**，一律不带它们。
- `.locks/com.collin.stealthplanner.lock`：本会话持有、**随本次提交释放**。
