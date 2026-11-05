# HANDOFF — com.collin.stealthplanner

> 覆写式交接单（非追加日志），每次交锁前整体重写五部分（AGENTS.md §4）。
> 只写可观测事实：碰了哪些文件、跑了什么指令、结果如何；不写意图与代码行为解释。

最后交接：qianwen-work-assistant · `planner-design-2026-11-05` · 本次提交（DESIGN v0.2 定稿 + 验收组 G，随提交交锁）
> 前一次交接：`planner-m0-2026-11-05` · 提交 `78a103a`（包骨架 + DESIGN v0.1）、`bd0d8b4`（门禁证据补记）

## 1. 已完成

### M0 · B 包骨架 + DESIGN（本轮全部产出，无实现代码）

**包骨架**（`Packages/com.collin.stealthplanner/`）：
- `package.json`（`0.0.1`，依赖**只有** `com.collin.core: 0.2.0`，`unity: 6000.6`）
- `Runtime/com.collin.stealthplanner.asmdef`（`references:["com.collin.core"]`，
  `validateReferences:true`，`autoReferenced:true`，根命名空间 `Collin.StealthPlanner`）
- `Editor/com.collin.stealthplanner.Editor.asmdef`（`includePlatforms:["Editor"]`，
  引用 core + 本包 Runtime）
- `Showcase.json`（`order=31`、`category=设计工具`、`video=""`）
- `Docs/DESIGN.md`（v0.1 草案，**本轮主要产出**）、`Docs/ACCEPTANCE.md`（验收草案，零通过）、
  `Docs/CHANGELOG.md`（0.0.1 骨架）

**关键调研结论（写进 DESIGN，接手必读）**：
1. **A 的判定走主线程物理射线**：`VisionVolume.CanSeeAt → VisionGeometry.Evaluate(..., RaycastClear)`
   → `Physics.RaycastNonAlloc`。物理查询**不能后台线程**，直接决定架构（⇒ D-01 烘表一次、之后纯 CPU 查表）。
2. **契约层已在 core 实装**（`Runtime/StealthContracts/`，3 文件），A DESIGN §4.3 的接口签名
   在代码里都存在并已注册，A 侧实现是 `Packages/com.collin.stealth/Runtime/StealthWorld.cs`。
   ⚠️ 但 A 的 `Docs/Modules/00-Contract.md`（CHANGELOG 声称 M6 已冻结）**磁盘上不存在**——
   **接口真源只能读 core 那 3 个 .cs，不能按 A 包文档**。
3. **B 不是纯 Editor 包**：`ISafeIslandProvider` 的实现方按契约就是 B（A 是消费方），故 Runtime 有真内容。
4. **order=31** 是 A 包 DESIGN 第 250 行为「设计辅助工具」预留的，非本轮自填。
5. **`Services` 域重载即清空**（`core/Runtime/Services.cs:11`），注册方须在初始化入口重注册；
   B 取不到服务必须降级（面板「未接入玩法包」），A 取不到安全岛必须按「无安全岛」处理。

**DESIGN 对用户条目的映射**（全在 §5 决策表，接手改设计前先看）：
| 用户条目 | 决策号 |
|---|---|
| 静态1 可拖动时间轴/相位/位置/安全区/可达位置 | D-01 D-02 D-11（§3.1 时空表 + §3.2） |
| 静态2 可行解判定 + 剪枝降复杂度 | D-03（§6.1 时间扩展图 + §6.2 四道剪枝） |
| 静态3 无解 → 最近撤离点 5 位置 + 最早到达时间 | D-04 |
| 静态4 前 M 短解 + 弹窗确认导出 → 注入动态1 | D-05（§6.3 k-shortest）+ §6.5 slack |
| 动态1 场景配数据集 + 静态4 注入 | D-07（JSON+SO 双形态） |
| 动态2 实跑验证 + 多窗口并行（并发可配） | D-06 D-08（单窗口顺序 MVP → UMPM 需申报） |
| 动态3 反向规划 NPC 路点 → 存场景副本 | D-09（局部重烘 + 坐标下降 + 禁覆盖现场） |

**我补充的三条**（用户清单没有，但决定工具好不好用）：
分层可见性着色（§3.1）、slack 容错窗口（§6.5）、参数化增量重烘（D-11/§7）。
另补：**预测/实况差异当诊断**（§3.3，表假设 NPC 不进反应态，实跑才有 Alerted/通报，差异本身是信号）。

### 设计定稿 + 参数落地（本轮，`planner-design-2026-11-05`；仍无烘焙/求解实现）

用户答复原 §10 五问 ⇒ DESIGN **v0.1 草案 → v0.2 已评审定稿**：

| 拍板 | 落点 |
|---|---|
| 短名 `com.collin.stealthplanner`（不改目录/asmdef） | D-12 |
| 相位窗 **手配秒数** + 面板**只读**「建议参考时长」 | D-13 + **新 §6.7** |
| **仅「抵达撤离点」单目标**，多前置不排期 | D-14 |
| **先单窗口顺序实跑**；UMPM 多窗口留 M8 后续迭代 | D-15（细化 D-08，措辞已同步） |
| 格长 / Δt / P **三项可配**，默认 `0.5m / 0.5s` | D-16 + **新 §6.8** + §7 前提行改写 |

- §6.7 定的算法：`k_i=max(1,round(c_i/Δt)) → L=lcm(k…) → 参考=L·Δt`；**互质爆炸必须有兜底**
  （`L>L_max=7200 桶` ⇒ 退 `max(c_i)×2` + Warning，不许卡死/报 error）。
- §6.7 的**正确性声明要写进面板文案**：P 大小**不改变**「是否存在 ≤N 相位可行解」（可见性按各 NPC
  周期取模，D-02）；漏解风险只来自 **N**。不告知这条，策划会误以为「相位窗设小会漏解」而乱调参。
- §6.8：改参数时**实时预览**格数/桶数/体积估算，超预算把「开始烘焙」标警示色（不静默让用户卡几分钟）。
- 🔧 **消除 v0.1 遗留的内部矛盾**：草案在 §10 派生段与 §6.7 各写了一套参考相位时长算法，
  且**主算法/兜底顺序相反**（前者主用 `max(c_i)` 近似、兜底才算 LCM；后者反之）。定稿删掉 §10 那段、
  **统一以 §6.7 为唯一真源**。教训：同一算法在一份文档里写两处必然漂移，改设计前先确认只有一处定义。
- 改动文件：`Docs/DESIGN.md`（状态头、D-08 措辞、新增 D-12~D-16、新增 §6.7/§6.8、§7 前提与实跑行、
  §9 M0 行、§10 由「开放问题」改写为「决议表」）、`Docs/ACCEPTANCE.md`（状态头 + **新增组 G 6 条**
  把五条决议变成可观测判据）、`Docs/CHANGELOG.md`（Unreleased 记定稿）、本交接单。

### 顺带修掉一个 M0 遗留红灯（重要，勿回退）

- **M0 的「空 asmdef 编译干净」结论是错的**：Unity 对无脚本的程序集**必打 warning**
  （`Assembly for ... will not be compiled, because it has no scripts`）。实测本包 2 条，
  会破 §1.6b 归零门禁。M0 那次读数被 `console clear` 时机掩盖 ⇒ **取证漏洞，不是包清白**。
- 解法：落两个**属于骨架本身**的文件（不是提前写功能）——
  `Runtime/PlannerBakeSettings.cs`（**D-13/D-16 的可配参数唯一载体**：`_cellSize=0.5`、`_timeStep=0.5`、
  `_phaseWindow` 手配、`_maxPhases/_maxSolutions/_fallbackTopK`，只读属性 + `BucketsPerPhase`
  + `const MaxAlignedBuckets=7200`；`OnValidate` **只钳越界、绝不隐式联动 P**）**不含判定/求解逻辑**；
  `Editor/PlannerBakeSettingsEditor.cs`（§6.8 参数敏感性检视；活动格数需场景 footprint，
  **如实标「待 M1」不留假数字**）。
- 新增 `Tests/Editor/`（asmdef + `PlannerBakeSettingsTests.cs` **7 条**）把一次性探针升级成回归守卫。
- **实测**：本包 7/7、**全量 258/258**（分包统计 CityMap 130 + Stealth 121 + StealthPlanner 7，合计核对自洽）；
  门禁 `compilationFailed=false`、`consoleWarnings=0`、`consoleErrors=1` 全命中 §1.8 已批豁免
  （`services-gateway`），校验器 **FAIL 0 / 结论绿灯**、WARN 4（`pkg-sample` 预期 + 2 条他人/自有锁 + 1 条已豁免）。

### 本轮两次「自己写的判据把自己骗了」（M1 写判据前必读）

1. **假阳性**：单一真源用例首版扫全包数裸数字 `7200`，把注释说明、用例自己的期望值、搜索词全算进去，
   报「实际 6 次」失败。实现其实只有 1 处定义，**错的是判据**。
2. **跳过 ≠ 通过**：二版想用 `Assert.Ignore` "记录发现"，结果命中定义处立即中止、统计变死代码——
   测试判 skip 等于没守卫。**别在断言循环里用 Ignore/Inconclusive**。
   终版：只扫 `Runtime/Editor`、跳过注释行、只匹配 `= 7200` 赋值形态、定义文件豁免，
   并加**「必须真扫到 `PlannerBakeSettings.cs`」前置断言**——否则路径拼错→命中 0 文件→`hits==1` 虚假通过。

## 2. 未完成

- **烘焙 / 求解 / 时间轴 / 实跑 / 反推的实现代码**：DESIGN §9 的 M1~M9 一条未动
  （本轮只落「参数载体 SO + 检视面板 + 7 条守卫用例」，属 M0 骨架配套，不含任何算法）。
  ⚠️ M1 **必须复用 `PlannerBakeSettings` 作为参数唯一入口**，别再新建一份格长/Δt 定义。
- ~~**DESIGN §10 的 5 个开放问题待用户拍板**~~ ⇒ **已全部拍板（2026-11-05）**，结论固化进 §10 决议表
  （短名 `stealthplanner` / 相位窗手配 + 只读参考时长 / 仅撤离单目标 / 先单窗口实跑、UMPM 留 M8 /
  格长·Δt·P 三项可配，默认 0.5m·0.5s）。**M1 阻塞解除，可直接开工。**
- `Sample/` 目录未建 → 校验器 `pkg-sample` **WARN**（本包无试用演示）。属正常，M2 时间轴窗口落地时补。
- M9 演示视频未录（§6 硬性要求，且只能用户手动录，见 A 包同类经验）。

## 3. 已知坏

**本轮（设计定稿轮）交锁实测：无本包缺陷。** `compilationFailed=false`、`consoleWarnings=0`、
`consoleErrors=1`（全命中 §1.8 已批豁免 `services-gateway`），`verify_project.py` **FAIL 0 / 结论绿灯 / WARN 4**。
（对照：M0 那次报 `consoleErrors=0/consoleWarnings=0` 是**被 clear 时机掩盖的错误清白**，见上节。）

| 现象 | 归属 | 最终事实 |
|---|---|---|
| 中途出现过 1 条 error：`No command named 'status console'` | **本会话自造**——`unity list` 输出列顺序是「动作 组」，我误读成命令名；正确是 `console status` | 一次性操作噪声。`console clear` + 重编译 + **60s 静默窗口**复采后不再出现；**不写 allowlist**（自造噪声不得豁免） |
| **本包交锁后新出现的 console 红灯**：warning=`Color primaries 0 is unknown or unsupported by WindowsMediaFoundation … Packages/com.collin.stealth/ShowcaseMedia/stealth_demo.mp4` | VideoPlayer 解码 **stealth 包**演示视频的告警（堆栈空、路径不经本包程序集）；**用户证实为其本人测试展板播放视频所致** | **非本包缺陷、非 M0 产物**。75s 静默复采无新增 ⇒ 一次性外部动作，不满足 §1.8 豁免条件①，**未写 allowlist**。**未代清空控制台**（那是用户测试现场日志）。校验器在该日志被清除前持续显示 console 红灯 ⇒ 下个会话开工第一步先复跑 `verify_project.py` 确认 console 项已归零 |
| 开工基线曾报 error/warning：`No script asset for TerrainProfile` | **com.collin.citymap**（堆栈不含本包/stealth 程序集），该会话当时正在改 `TerrainProfile.asset` | **未代修**（§2 包互斥）。静默窗口复采**未再复现**，校验器转绿——大概率是对方编辑过程中的瞬态。**不归因于已修复**，citymap 侧仍可能再出现，见 §5 |
| `pkg-sample` WARN：本包无 `Sample/` | 本包，**M0 预期状态**（无实现即无演示） | 保留红灯以外的 WARN，M2 落地时间轴窗口时建 `Sample/` 消除 |

## 4. 下一步

1. ~~先让用户拍板 DESIGN §10 的 5 个开放问题~~ ⇒ **已拍板、设计已定稿（§10 决议表 + D-12~D-16）**，
   **直接开 M1**。开工时注意定稿新增的三条硬要求：格长/Δt/P **必须做成可配参数**（默认 0.5m/0.5s）、
   参考相位时长**只读不得回写手配值**且互质时必须有退化兜底、面板须给**参数敏感性估算与超预算警示**。
2. **M1**：`VisibilityTable`（Runtime，per-NPC 分片 + 三层掩码位图）+ 分帧烘焙器（Editor，
   只经 `IVisionWorld.CanSeeAt`，禁复制判定）+ 静态遮挡预筛 + 永久/临界分层 + SO 资产存读。
3. M1 落地时同步建 `Sample/`，消 `pkg-sample` WARN。
4. ⚠️ 门禁提醒：M1 会引入**大量主线程射线调用**，必须分帧（D-11），否则卡死共享编辑器并误伤并行会话。
   **本轮交锁实测**：`console clear` → `script recompile` → `test run`（258/258）→ `console status`
   = `consoleErrors=1 / consoleWarnings=0`，那 1 条全命中 §1.8 已批豁免 ⇒ 校验器 **FAIL 0 绿灯**。
   **M1 起每轮交锁必须复查「asmdef 是否有对应脚本」**——本包刚踩过：空程序集必打 warning，
   而 `console clear` 的时机能把它掩盖成"全绿"（M0 就这么漏过一次）。
   citymap 侧若再冒出 `TerrainProfile` 类报错，按 §1.8 归因例外如实上报、不代修、不自造豁免。

## 5. 勿碰项

- **`PlannerBakeSettings` 是格长 / Δt / 相位窗 / N / M / Top-K 的**唯一入口****：M1 烘焙器与 M3 求解器
  都必须从它取值。阈值同理只允许定义在该文件——**第二处硬编码会被
  `AlignedBucketCap_IsSingleSourceOfTruth` 判失败**。实测全包 `7200` 共 6 处：赋值形态 **1 处**
  （Runtime 常量定义行），另 5 处为文档注释、用例期望值与提示文案（判据已排除：只扫 `Runtime/Editor`
  且跳过注释行）。要加参数就加字段，别在别处再声明一份。
- **`com.collin.citymap` 的一切**（含 `TerrainProfile` 那个 error、`Assets/CityProject.asset`、
  `Assets/test.asset` 删除、`README.md` 改动）——另一会话进行中，未提交。
- **`com.collin.core/Runtime/StealthContracts/`**：契约是 B 的全部输入面。
  **改签名 = 同时打断 A 和 B**。若确需扩接口（我预计 M3 要加「某格的可见时段查询」以省查表次数），
  必须走 §8 ADR + 用户批准 + 同步 A 包，不在本包擅动。
- **`Packages/manifest.json` / `ProjectSettings/**`**（§7 共享文件）。M8 若引入 UMPM 会触碰 manifest，须先申报。
- **A 包（com.collin.stealth）的任何文件**：本包只经 core 契约消费，连 asmdef 都不得引用它
  （`verify_project.py` 的 `dep-direction` 会 FAIL）。
- `.locks/com.collin.stealthplanner.lock`：本会话持有，TTL 300s，长任务期须续心跳。
