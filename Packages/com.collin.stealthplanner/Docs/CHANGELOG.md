# CHANGELOG — com.collin.stealthplanner

格式：Keep a Changelog（https://keepachangelog.com）；版本号：semver。

## [Unreleased]
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
