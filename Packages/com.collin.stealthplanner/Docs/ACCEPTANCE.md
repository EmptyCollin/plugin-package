# ACCEPTANCE — com.collin.stealthplanner

> 每条必须带可观测证据（谁 / 何时 / 用什么指令验证 / 结果）。只记事实，不记意图（AGENTS.md §4）。
> **当前状态：包骨架 + DESIGN 草案，尚无实现，故本表为空。** 下列为「验收草案」——
> DESIGN 定稿后要逐条落成的可观测判据，实现一条补一条证据。DESIGN 先于代码，
> 此草案的存在即为 M0 的产出物之一。

## 验收草案（待实现取证，非通过项）

| 组 | 拟验收项（可观测行为） | 拟验证方法 |
|---|---|---|
| **A. 时空表正确性** | 烘焙出的表与「同时刻调 `IVisionWorld.CanSeeAt`」逐格一致（同一判定口径，无双套逻辑） | EditMode：对 K 个 (格,t) 抽样，表值 vs 现算值 100% 相等 |
| | 表体积在预算内（见 DESIGN §预算） | 读资产字节数，断言 < 上限 |
| | 玩法包缺席时优雅降级（面板提示未接入，不抛异常） | EditMode：清空 Services 后调用，断言不崩、返回降级标志 |
| **B. 安全岛（反向注入）** | `Services.TryGet<ISafeIslandProvider>()` 取到本包实现且 `Contains` 与表口径一致 | EditMode 用例 |
| | A 包 Play 态判定确实查询到安全岛（跨包通路端到端） | Play 探针：玩家进岛 → 察觉增速下降 |
| **C. 相位搜索可行解** | 有解场景返回 ≤M 条、按到达时间升序、且每条都能通过「逐相位复现」二次验证（求解器与复现器解耦） | EditMode + 复现比对 |
| | 剪枝后求解耗时随 NPC 数增长的曲线可接受（DESIGN §性能） | 基准：N∈{4,8,16} 计时 |
| | 无解场景返回最近 K 个可藏点及其最早到达时间 | EditMode：构造无解关卡 |
| **D. 时间轴回溯** | 拖动 t，NPC 位姿/危险区/安全区**同源同步**响应（编辑态非 Play） | 手动 + 探针读 `ScrubDriver` 生效 |
| **E. 路点数据集** | 导出 JSON schema 稳定、且能被运行期注入器读回并复现同一条动线 | round-trip 用例 |
| **F. 反向规划 NPC** | 按给定玩家动线反推 NPC 路点，写回后 A 包实际巡逻经过预期节点 | Play 探针 |

## 未通过项

| # | 现象 | 复现步骤 | 状态 |
|---|---|---|---|
| — | 尚无实现，无运行期现象可记录 | — | 骨架阶段 |

## 基线隔离门禁记录

| 项 | 指令 | 时间 | 结果 |
|---|---|---|---|
| 开工基线 | `python Tools/verify_project.py` | 2026-11-05 | 1 FAIL：`console` warning=1（`No script asset for TerrainProfile`，堆栈属 com.collin.citymap，与本包无关，**不代修**，见 DESIGN §约束末注） |
| 编辑器就绪 | `unity status` | 2026-11-05 | Port 7800 ready · 6000.6.7f1 |
| **M0 交锁门禁** | `script recompile` → `console clear` → 重编译 → 60s 静默复采 `console status` → `verify_project.py` | 2026-11-05 | **三项全绿**：`compilationFailed=false`、`consoleErrors=0 / consoleWarnings=0`、校验器 FAIL 0（WARN 2：`pkg-sample` 无 Sample 目录 + `lock-active` 本会话自有锁）。空 asmdef 编译干净。提交 `78a103a` |
| 交锁后收口复采 | `console get --level warn` + 75s 静默窗口 | 2026-11-05 | warning=1：`Color primaries 0 … stealth_demo.mp4`（VideoPlayer 解码告警，路径属 **com.collin.stealth**，与本包无关）。**用户证实为其本人测试展板播放视频所致**；静默期不复现 ⇒ 一次性外部动作产物，不满足 §1.8 豁免条件①，**未写 allowlist**；未代清空控制台（用户测试现场日志）。校验器在该日志清除前将持续显示 console 红灯 |
