# HANDOFF — com.collin.stealthplanner

> 覆写式交接单（非追加日志），每次交锁前整体重写五部分（AGENTS.md §4）。
> 只写可观测事实：碰了哪些文件、跑了什么指令、结果如何；不写意图与代码行为解释。

最后交接：qianwen-work-assistant · `planner-m0-2026-11-05` · 本次提交（M0 骨架 + DESIGN，随提交交锁）

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

## 2. 未完成

- **全部实现代码**：DESIGN §9 的 M1~M9 一条未动（M0 只出骨架 + 设计，符合 §4「DESIGN 先于代码」）。
- **DESIGN §10 的 5 个开放问题待用户拍板**（短名定案 / 相位窗口径 / 单目标 vs 多前置任务 /
  UMPM 优先级 / 格分辨率 0.5m 与 Δt 0.5s 是否贴合关卡尺度）。**问题 5 直接决定 §7 预算成立与否。**
- `Sample/` 目录未建 → 校验器 `pkg-sample` **WARN**（本包无试用演示）。属正常，M2 时间轴窗口落地时补。
- M9 演示视频未录（§6 硬性要求，且只能用户手动录，见 A 包同类经验）。

## 3. 已知坏

**交锁实测：无本包缺陷。** `compilationFailed=false`、`consoleErrors=0`、`consoleWarnings=0`、
`verify_project.py` **FAIL 0（结论绿灯，WARN 2）**。

| 现象 | 归属 | 最终事实 |
|---|---|---|
| 中途出现过 1 条 error：`No command named 'status console'` | **本会话自造**——`unity list` 输出列顺序是「动作 组」，我误读成命令名；正确是 `console status` | 一次性操作噪声。`console clear` + 重编译 + **60s 静默窗口**复采后不再出现；**不写 allowlist**（自造噪声不得豁免） |
| **本包交锁后新出现的 console 红灯**：warning=`Color primaries 0 is unknown or unsupported by WindowsMediaFoundation … Packages/com.collin.stealth/ShowcaseMedia/stealth_demo.mp4` | VideoPlayer 解码 **stealth 包**演示视频的告警（堆栈空、路径不经本包程序集）；**用户证实为其本人测试展板播放视频所致** | **非本包缺陷、非 M0 产物**。75s 静默复采无新增 ⇒ 一次性外部动作，不满足 §1.8 豁免条件①，**未写 allowlist**。**未代清空控制台**（那是用户测试现场日志）。校验器在该日志被清除前持续显示 console 红灯 ⇒ 下个会话开工第一步先复跑 `verify_project.py` 确认 console 项已归零 |
| 开工基线曾报 error/warning：`No script asset for TerrainProfile` | **com.collin.citymap**（堆栈不含本包/stealth 程序集），该会话当时正在改 `TerrainProfile.asset` | **未代修**（§2 包互斥）。静默窗口复采**未再复现**，校验器转绿——大概率是对方编辑过程中的瞬态。**不归因于已修复**，citymap 侧仍可能再出现，见 §5 |
| `pkg-sample` WARN：本包无 `Sample/` | 本包，**M0 预期状态**（无实现即无演示） | 保留红灯以外的 WARN，M2 落地时间轴窗口时建 `Sample/` 消除 |

## 4. 下一步

1. **先让用户拍板 DESIGN §10 的 5 个开放问题**（尤其短名与格分辨率），再开 M1——
   架构已定（D-01 查表），但参数量级取决于关卡尺度，先实现后改会返工。
2. **M1**：`VisibilityTable`（Runtime，per-NPC 分片 + 三层掩码位图）+ 分帧烘焙器（Editor，
   只经 `IVisionWorld.CanSeeAt`，禁复制判定）+ 静态遮挡预筛 + 永久/临界分层 + SO 资产存读。
3. M1 落地时同步建 `Sample/`，消 `pkg-sample` WARN。
4. ⚠️ 门禁提醒：M1 会引入**大量主线程射线调用**，必须分帧（D-11），否则卡死共享编辑器并误伤并行会话。
   **本次 M0 交锁三项已实测全绿**（`compilationFailed=false` → `console clear` + 重编译 + 静默窗口复采
   `consoleErrors=0 / consoleWarnings=0` → `verify_project.py` FAIL 0），随提交释放锁。
   M1 起每轮交锁须重走同样三段；citymap 侧若再冒出 `TerrainProfile` 类报错，按 §1.8 归因例外如实上报、不代修、不自造豁免。

## 5. 勿碰项

- **`com.collin.citymap` 的一切**（含 `TerrainProfile` 那个 error、`Assets/CityProject.asset`、
  `Assets/test.asset` 删除、`README.md` 改动）——另一会话进行中，未提交。
- **`com.collin.core/Runtime/StealthContracts/`**：契约是 B 的全部输入面。
  **改签名 = 同时打断 A 和 B**。若确需扩接口（我预计 M3 要加「某格的可见时段查询」以省查表次数），
  必须走 §8 ADR + 用户批准 + 同步 A 包，不在本包擅动。
- **`Packages/manifest.json` / `ProjectSettings/**`**（§7 共享文件）。M8 若引入 UMPM 会触碰 manifest，须先申报。
- **A 包（com.collin.stealth）的任何文件**：本包只经 core 契约消费，连 asmdef 都不得引用它
  （`verify_project.py` 的 `dep-direction` 会 FAIL）。
- `.locks/com.collin.stealthplanner.lock`：本会话持有，TTL 300s，长任务期须续心跳。
