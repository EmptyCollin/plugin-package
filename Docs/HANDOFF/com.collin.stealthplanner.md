# HANDOFF — com.collin.stealthplanner

> 覆写式交接单（非追加日志），每次交锁前整体重写五部分（AGENTS.md §4）。
> 只写可观测事实：碰了哪些文件、跑了什么指令、结果如何；不写意图与代码行为解释。

最后交接：qianwen-work-assistant · `planner-m1-2026-11-05` · 本次提交（M1 表 + 烘焙 + 22 条用例，随提交交锁）
> 前序：`78a103a`（骨架+DESIGN）→ `bd0d8b4`（门禁补记）→ `4334661`（DESIGN 定稿+参数载体）

## 1. 已完成（本轮 = M1）

**Runtime**（不引 UnityEditor，引擎可复用）：
- `VisibilityTable.cs`：分片（每哨兵一片，覆盖自身周期，索引 `t mod c_i`）+ 位图打包
  + **永久安全层**（跨片 AND）+ `BucketOf`（填桶/取桶唯一口径，公开静态）。
  越界/未烘一律**保守判危险**。查询复杂度 = 哨兵数。
- `VisibilityBakeRunner.cs`：烘焙驱动。分帧**按查询预算不按毫秒**（确定性、可断言）。
  两道静默防线：`SkippedGuards`（位姿不可解析⇒整片跳过）、`ZeroVisibilityGuards`
  （收尾扫整片零可见⇒上报可疑）。
- `VisibilityTableAsset.cs`：SO 载体，`LayoutVersion=1` 不符即拒绝使用（防读旧资产静默出错）、
  `TryQuery` 未烘时 `degraded` 给原因文本。

**Editor**：`ContractVisibilitySource`（转调玩法包判定 + **距离预筛**，水平超视距即跳过，
方向保守：只少问不漏问）、`VisibilityBakeService`（**会话** + footprint 唯一实现：向下验地面+向上验站高）、
`VisibilityBakeWindow`（交互分帧泵 + **脚本模式菜单**）。
`Sample/PlannerBakeSettings.asset` **入库**（源）；`VisibilityTable.asset` **不入库**（派生，见下）。

**实测（StealthSample，3 哨兵）**：活动格 11,660/11,664 · 永久安全格 4,482 · 位图 252KB ·
查询 1,970,540 · 预筛省 1,586,206 条射线（**80.5%**）实打 384,334 · 首烘主循环 **0.82s**（预算 <30s）·
表值 vs 直调契约 400 抽样**零不一致** · 永久安全 1,250 抽验**零违例** ·
三轮「重开→烘→**逐片快照**」SHA256 **全等**。

**修掉两个实测发现的缺陷（写进 DESIGN D-17 / CHANGELOG）**：
1. **冷启动首烘静默荒谬**：重开场景后第 1 次烘出「永久安全格=11664=全场」（哨兵全瞎），第 2 次才回落。
   根因 = 弧长表惰性构建 + PhysX 未同步 + `CanSeeAt` 取不到视野体时**静默返 false**。
   修 = 烘焙前 `Physics.SyncTransforms()` + `WarmUpPoses()`（探 `PoseAt` 与 `TryPoseAtWorld` **两条路径都要探**）。
2. **窗口只 Load 不创建资产**：新克隆工程「开始烘焙」永久灰死。修 = `EnsureAsset` 缺失即创建
   （只补最后一级目录；父目录不存在则告警返 null，不递归误建）。实测删光两资产→自动重建→烘焙成功。

**测试**：新增 `VisibilityTableBakeTests` 15 条（本包 7→**22**）：mod 回绕、负 t、上界钳制、
**填桶↔取桶 round-trip**（含 c_i 不整除 Δt 的逐桶边界）、永久安全 AND、越界保守、格中心落回本格、
**分帧结果与一次跑完逐格逐桶等价**、降级哨兵跳过且上报、预算 0 仍推进、体积线性。

**交锁门禁**（末次复跑）：编译 `failed=false`；全量 **279/279**；`console clear` 重跑验证动作后
`consoleErrors=0 / consoleWarnings=0`；`verify_project.py` **FAIL 0 绿灯**（仅剩自有 `lock-active` WARN）。

## 2. 未完成

- **M2~M9 全部未动**。下一站 M2（时间轴窗口）。
- 组 A 第 3 条「玩法包缺席优雅降级」**代码有、断言无**（U-1，M2 补）。
- §6.7「建议参考相位时长」算法未实现（U-2，与 M3 求解器同期）。
- **增量重烘未实现**（U-3）：每次全量重算。DESIGN D-11 目前只兑现「分帧+可中断+订阅失效」，
  **对外不得声称支持增量**。首烘 0.82s 使该优化暂不紧迫。
- `.gitignore` 未加「表资产」忽略行：**工具报 `Cannot preserve Windows file access permissions` 拒改，
  未绕过**；且该文件属仓库共享、citymap 会话在活动。本轮靠**逐文件精确 add**（不含
  `Sample/VisibilityTable.asset*`）保证不入库；**长期规则需用户拍板**（拟好的两行见 CHANGELOG 处置记录）。
- `Sample/` 已有资产 ⇒ `pkg-sample` WARN 消除；但本包尚无自己的可玩演示场景（M2 时定形态）。

## 3. 已知坏

- **本包无**：门禁三项末次全绿（读数见上）。
- **取证方法两条硬教训（M2 起必守，ACCEPTANCE 有档）**：
  ① `AssetDatabase.LoadAssetAtPath` 返回**同一缓存实例**，重烘会覆写上一份 ⇒
     任何「两次烘焙比较」必须**先拷位图快照**，否则是自比假信号（本会话真踩过，一度报出假「确定性通过」）；
  ② 烘焙/取证类操作前若刚重编译（=冷启动），**先跑一遍丢弃再采数**，或直接走带 `WarmUpPoses` 的正式入口。
- **脚本模式不需要前台**：M1 的烘焙读数全部由「重编译后直接反射调 `BakeFromScript`」取得，
  未做任何窗口聚焦操作。⇒ M2 起若要取时间轴/烘焙读数，优先走脚本入口，别再依赖 `focus`
  （上轮 A 包实测：`focus` 每次 CLI 调用都会把焦点抢回终端，且失焦即停泵）。
- 他会话的 error 冒到本包门禁读数的情况本轮未发生；若下轮发生按 §1.8 归因例外上报，不代修不自造豁免。

## 4. 下一步（M2 建议顺序）

1. **时间轴窗口**：`EditorWindow` + 滑块量程 `0..P`（手配，读 `PlannerBakeSettings`），
   经 `IStealthScrubDriver.ScrubTo` 推位姿；**危险/安全热区只查表**（`VisibilityTableAsset.Table`），
   绝不在 UI 层现算判定；每个 `GuardSlice.displayName` 一条泳道。
2. 补 U-1 降级断言（`Services.Clear()` 后 `TryCreate()` 返 null + 窗口不抛）。
3. 建 `Sample/` 演示用最小场景或复用 stealth 的 StealthSample，定下 M2 取证口径。
4. ⚠️ M2 若做「改参数即时重烘预览」，先读 D-17：冷启动首烘不可信，预览泵必须复用会话（别每帧重枚举 footprint）。

## 5. 勿碰项

- **`Sample/VisibilityTable.asset(.meta)` 不入库**（派生产物，见 §2）；`PlannerBakeSettings.asset` 是源、入库。
- **`PlannerBakeSettings` = 参数唯一入口**（格长/Δt/P/N/M/Top-K）；阈值常量（7200）只允许定义在
  `PlannerBakeSettings.cs`，第二处硬编码会被 `AlignedBucketCap` 用例判失败。
- **`VisibilityTable.BucketOf` = 填桶/取桶唯一口径**；`VisibilityBakeService.EnumerateWalkableCells` =
  footprint 唯一实现。**都是「一处定义」纪律**（DESIGN §6.7 草案期两套口径漂移的老毛病，勿复发）。
- **判定永远转调玩法包契约**（`IVisionWorld.CanSeeAt`）；本包任何文件出现自建可见性逻辑即破 DESIGN §4 红线。
- `com.collin.core`（契约签名改动 = 同时打断 A/B，走 ADR）；`com.collin.citymap` 一切（他会话活动中）；
  `Packages/manifest.json`、`ProjectSettings/**`、`.gitignore`（§7 共享 + 本轮实测写不动）；
  A 包（`com.collin.stealth`）任何文件（asmdef 层也不得引用，`dep-direction` 守）。
- `.locks/com.collin.stealthplanner.lock`：本会话持有，TTL 300s；跑 `test run`/烘焙等长动作**中途续心跳**。
