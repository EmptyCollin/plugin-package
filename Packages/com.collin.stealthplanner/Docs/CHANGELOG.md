# CHANGELOG — com.collin.stealthplanner

格式：Keep a Changelog（https://keepachangelog.com）；版本号：semver。

## [Unreleased]

### Added（M1：时空可见性表 + 烘焙，session planner-m1-2026-11-05）
**Runtime**（无 UnityEditor 依赖，引擎侧可复用）
- `VisibilityTable.cs`：表结构本体。按哨兵**分片**、时间索引用 `t mod c_i`（D-02，不物化全局 LCM）；
  每片是「桶 × 格」的位图（`byte[]` 手工打包）；派生**永久安全层**（跨片 AND，供求解器剪枝①不展开时间维）；
  查询 `IsDangerousAt(cell,t)` 复杂度 = 哨兵数（与格数、相位窗长度无关）；越界与未烘一律**保守判危险**。
  `BucketOf` 定为填桶/取桶的**唯一口径**（公开静态纯函数，跨程序集可回归钉住）。
- `VisibilityBakeRunner.cs`：烘焙驱动（算法在 Runtime 而非 Editor 的两条理由：引擎可复用同一填桶口径、
  且桶数进位/越界钳制/降级排除必须能在 EditMode 用假世界断言）。**分帧按「查询预算」而非毫秒**
  ——预算切分是确定性的（同输入同分片、可断言），`DateTime` 卡时间片则每帧分点不同。
- `VisibilityTableAsset.cs`：持久化载体（SO）。带 `LayoutVersion` 校验（读旧资产一律拒绝并要求重烘，
  不静默给错数据）+ 烘焙参数快照 + `TryQuery` 降级出口（未烘/版本不符 ⇒ 保守判危险并给原因文本）。

**Editor**
- `ContractVisibilitySource.cs`：把 core 契约适配成 Runtime 的可见性源，**判定一律转调玩法包**（不自建第二套）。
  加**距离预筛**：每桶一次纯数学水平距离比较，超视距即跳过契约调用。方向保守正确
  （3D 距离 ≥ 水平距离 ⇒ **只会少问、绝不漏问**）。
- `VisibilityBakeService.cs`：`VisibilityBakeSession`（可跨帧续跑的会话，持有 footprint 与驱动状态，
  避免「每帧重枚举一次 footprint」）+ 服务化的**可活动 footprint 枚举唯一实现**（向下验地面 + 向上验站高，
  两条缺一不可：只查地面会把掩体内部当可走格）。
- `VisibilityBakeWindow.cs`：交互窗口（分帧泵 + 进度条 + 可中断，禁模态弹窗）与**脚本模式菜单**
  （一次跑完并打读数——本工程实测失焦时编辑器不推进泵，自动化取证必须有不靠帧回调的路径）。
- `Sample/PlannerBakeSettings.asset`：**入库**（是源）。
- `Tests/Editor/VisibilityTableBakeTests.cs`：**15 条**（本包 7 → 22）。

### Fixed（M1 实测发现的严重缺陷 ⇒ DESIGN 新增 **D-17**）
- **冷启动首烘静默给出荒谬数据**：重开场景后第 1 次烘焙把永久安全格算成 **11664 = 全部格**
  （等于「三个哨兵全是瞎子」），第 2 次才回落正确的 4482，**全程不报错**。
  根因两条：① 玩法包弧长查找表**惰性构建** + PhysX 未同步 ⇒ 首烘读到未初始化位姿；
  ② `CanSeeAt` 在视野体按 actorId 取不到时**静默返回 false** ⇒ 整张表被烘成全场安全。
  修法：烘焙前 `Physics.SyncTransforms()` + `WarmUpPoses()`（每单位把 `PoseAt`/`TryPoseAtWorld` 各探一遍逼表建起来）；
  并加第二道防线 `ZeroVisibilityGuards`（收尾扫「整片零可见」⇒ 上报为可疑，绝不当可信读数）。
- **窗口/脚本模式只 Load 不创建资产** ⇒ 新克隆工程永远没有输出资产、「开始烘焙」按钮永久灰死。
  改为共用 `EnsureAsset`（只补最后一级目录，父目录不存在则告警返回 null，不递归误建路径）。

### 实测（M1，示例场景 3 哨兵）
| 读数 | 值 |
|---|---|
| 活动格 / 矩形格 | 11,660 / 11,664 |
| 永久安全格 | 4,482（剪枝①可省掉的时间扩展节点） |
| 表体积 | 252.0 KB（DESIGN §7 目标 < 4MB，余量充足）|
| 查询数 | 1,970,540 |
| 预筛省下的射线 | 1,586,206（**80.5%**）｜实打 384,334 |
| 首烘主循环 | **0.82s**（§7 目标 <30s）｜footprint 枚举 0.01s |
| 表值 vs 直调契约 | 随机 400 个 (格,t) **零不一致**；永久安全格 1,250 次抽验 **零违例** |
| 确定性 | 三轮「重开场景→烘焙→逐片快照」SHA256 **完全一致** |
| 测试 | 本包 **22/22**，全量 **279/279**；门禁三项全绿、校验器 FAIL 0 |

> ⚠️ 取证方法两条教训（已写进 ACCEPTANCE）：
> ① `AssetDatabase` 返回**同一个被缓存的实例**，第二次烘焙会覆写第一次的表 ⇒
>    验确定性**必须先拷位图快照**，否则「首=次」是同一实例自比的假信号（我第一版探针就踩了）。
> ② 我上一版验确定性用的探针里 `a1`/`a2` 是同一实例 ⇒ 那句「首=次?True」当时就是假读数，
>    真结论来自条目 8 的快照法。

### 版本
维持 **0.0.1**：M2~M9 未开工，无演示视频，不满足收版条件。

### Changed（DESIGN 定稿，session planner-design-2026-11-05）
- 用户 2026-11-05 拍板原 §10 五个开放问题 ⇒ **DESIGN 状态 v0.1 草案 → v0.2 已评审（定稿），M1 可开工**：
  ① 短名维持 `com.collin.stealthplanner`；② 相位窗 **用户手配秒数** + 面板给**只读**「建议参考时长」
  （`lcm(round(c_i/Δt))·Δt`，互质爆炸时兜底 `max(c_i)×2` 并 Warning，绝不改写手配值）；
  ③ 目标**仅「抵达撤离点」**，多前置任务不排期；④ 实跑**先单窗口顺序**，UMPM 多窗口留作后续迭代（M8 不提前）；
  ⑤ 格长 / Δt / 相位窗 P **三项全部面板可配**，默认 `0.5m / 0.5s`。
- 新增决策 **D-12~D-16**（对应上述五条）、算法章 **§6.7**（参考相位时长口径 + 正确性声明：P 不改变
  「是否存在 ≤N 相位可行解」的答案，漏解由 N 决定）、**§6.8**（参数敏感性实时预览估算，超预算警示）；
  §7 预算与 D-08 措辞同步为「默认可配 / 并行属后续迭代」。
- `ACCEPTANCE.md`：状态头转定稿；新增**验收草案组 G**（6 条，逐项把上述决议变成可观测判据——
  含「改 P 后解的存在性不变」「参考值不自动写入 P」「互质退化路径不卡死」「无多目标谓词」）。
- 🐛 **修复骨架期遗留红灯**：M0 只放了 asmdef、没有脚本，Unity 对空程序集**必打 warning**
  （`Assembly for ... will not be compiled, because it has no scripts`，实测本包两条）。
  该 warning 会在交锁时破 §1.6b 归零门禁——M0 那次读数恰好被 `console clear` 时机掩盖，本轮才暴露。
  解法是落两个**属于骨架本身**的最小文件（不是提前写功能）：
  `Runtime/PlannerBakeSettings.cs`（决议 D-13/D-16 的可配参数载体 SO：格长 0.5m、Δt 0.5s、
  相位窗 P 手配、N/M/Top-K，含 `MaxAlignedBuckets=7200` 与 `BucketsPerPhase`；**不含任何判定或求解逻辑**）
  与 `Editor/PlannerBakeSettingsEditor.cs`（§6.8 参数敏感性检视：即时显示桶数/轮数/输出规模并对越界给
  Warning；活动格数因需场景 footprint 而**如实标注「待 M1」**，不留假数字）。
  修后本包 warning 归零。
- **新增测试程序集与 7 条守卫用例** `Tests/Editor/`（asmdef + `PlannerBakeSettingsTests.cs`），
  把一次性探针升级成可重复回归：默认值即拍板值、三项真可配、`BucketsPerPhase` 向上进位
  （10/3 必须得 4，截成 3 会漏掉末尾一段时间）、P<Δt 病态配置桶数不为 0、越界钳制**不牵连 P**、
  改 Δt **不静默改写**手配的 P、`7200` 赋值形态全包只允许一处。
  ⚠️ 该用例判据连错两轮才修对，教训记进 ACCEPTANCE 已验证事实 5：
  ①首版扫全包 + 数裸数字，把注释说明、用例自己的期望值、搜索词全算成重复定义（报「6 次」假阳性）；
  ②二版用 `Assert.Ignore` 想"记一笔"，结果命中定义处即中止、统计变死代码，测试判「跳过」而非通过
  ——**跳过不等于通过，等于没守卫**。终版收紧为「只扫 Runtime/Editor + 跳注释 + 只匹配 `= 7200`
  赋值形态 + 定义文件豁免」，并加**「必须真扫到定义文件」前置断言**堵掉"路径拼错→命中 0 文件→虚假通过"。
- 实测：**本包 7/7 绿，全量 258/258**（按 `FullName` 分包统计：CityMap 130 + Stealth 121 + StealthPlanner 7，合计核对自洽）；
  门禁 `compilationFailed=false`、`consoleWarnings=0`、`consoleErrors=1` 全部命中 §1.8 已批准豁免
  （`services-gateway`），校验器 **FAIL 0 / 结论绿灯**。
- 除上述骨架配套与参数守卫外**仍无烘焙/求解实现代码**（DESIGN 先于代码），无 `Sample/`，
  `Showcase.json.video` 留空。

## [0.0.1] - 2026-11-05
### Added
- 包骨架（DESIGN 先于代码，AGENTS.md §4）：`package.json`（依赖仅 `com.collin.core`）、
  `Runtime/` 与 `Editor/` 两套 asmdef（`validateReferences: true`，Runtime 不引 UnityEditor）、
  `Showcase.json`（`order=31`，沿用 stealth DESIGN §4.4 为设计工具预留的序号位）。
- `Docs/DESIGN.md` v0.1 草案：潜行关卡设计辅助台的完整方案与决策记录，覆盖用户提出的
  静态设计 1~4 与动态设计 1~3。核心架构是「时空可见性查找表」——把玩法包的主线程射线判定
  烘焙成离线数据资产，之后求解/仿真全部退化为纯 CPU 查表，以同时满足「可后台并行」与
  「编辑器/引擎双端共用」两条硬约束。
- `Docs/ACCEPTANCE.md` 验收草案（逐条待实现后取证，当前无一条通过）。

> 本版仅骨架 + 设计，无任何 Runtime/Editor 实现代码，故不进入可玩状态、`Showcase.json.video` 留空。
