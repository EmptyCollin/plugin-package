# HANDOFF — com.collin.stealthplanner

> 覆写式交接单（非追加日志），每次交锁前整体重写五部分（AGENTS.md §4）。
> 只写可观测事实：碰了哪些文件、跑了什么指令、结果如何；不写意图与代码行为解释。

最后交接：qianwen-work-assistant · `planner-m2fix-2026-11-05` · 本次提交（M2 收口轮：表版本 1→2 + 零可见防线补出口 + 54 条用例）
> 前序：`78a103a`（骨架+DESIGN）→ `bd0d8b4`（门禁补记）→ `4334661`（DESIGN 定稿）→ `e1e957b`（M1 表+烘焙）→ `fe44abe`（M2 时间轴+传播+热区）

## 1. 已完成（本轮 = M2 收口轮，把 M2 遗留的 U-5 定案并补防线出口）

**破坏性变更（用户 2026-11-05 拍板，选方案①）**：
- `VisibilityTableAsset.LayoutVersion` **1 → 2**。v1 资产一律拒用：`Table` 返 null、`IsBaked=false`、
  `TryQuery` 返 false 且**保守判危险**、原因文本 `表结构版本 1 ≠ 当前 2，请重烘`。
  端到端实测（直接读库里那份 v1 资产）：`[V2] 重烘前 资产Version=1 代码LayoutVersion=2 Table=null(已拒用) …`。
  ⚠️ **所有用户机器上已有的表都需要重烘**（菜单 `Tools/Collin/Stealth Planner/烘焙可见性表`，实测 0.82~0.88s；
  表是派生产物、不入库，新克隆环境无此负担）。

**防线出口补齐（新增决策 D-23）**：`ScanZeroVisibility()` 此前算出名单但**无任何出口**（会话不暴露、
日志不打印、表不记录、窗口无从警告）⇒「三个哨兵全瞎」的表被下游当可信数据。本轮补三处出口：
- `VisibilityBakeSession.ZeroVisibilityGuards`（对外可读）；
- 脚本模式日志追加 `零可见=N（名字…）`；
- **写进表**：烘焙收尾把「整片零可见」与「被跳过（位姿不可解析）」**合并**登记进
  `VisibilityTable.UnknownGuardNames`（二者表侧语义相同：该哨兵的视野在表里等于不存在 ⇒ 其视野区被画成安全）。
  时间轴窗口横幅措辞同步改为覆盖两类来源。

**测试**：本包 **48→54**（全量 **316/316**；分包 CityMap 141 + Stealth 121 + StealthPlanner 54）。
新增 `VisibilityTableAssetVersionTests` **5 条**（版本常量=2 / v1 拒用且原因含两版号 / 未烘与版本冲突两条提示不同 /
`ClearBaked` 回到版本 0）+ `ZeroVisibilityGuard_IsReported_AndPersistedOnTable` **1 条**（名单必须落到表上）。
起点成功路径实测：`IsEvaluated(c) && !IsDangerousAt(c,0f)` 筛出格 0 → `状态=t=0.00s（桶 0）步长 3 格
危险三角=774 可达三角=6 警告=(空)`。

**本轮实测读数（视野体在场的可信现场，3 哨兵）**：`片数=3 活动格=11,660/11,664 永久安全格=4,482
位图KB=252.0 查询=1,970,540 预筛省射线=1,586,206 实打射线=384,334 主循环=0.88s 跳过=0 零可见=0`；
表侧 `Version=2 足迹=True 无数据格=4 可疑名单=0` ⇒ 与 M1 条目 11/12 读数逐位一致，D-22 抬版本没有改变判定结果。

**文档**：DESIGN 新增 **D-22**（版本随字段新增必须抬升）、**D-23**（防线必须有可观测出口）；
ACCEPTANCE 新增条目 **25~28**、U-5 改「✅ 已收口」、状态头转收口轮、补本轮门禁行；CHANGELOG 新增
「Changed（M2 收口轮）」与两处 Fixed。

## 2. 未完成

- 🚨 **需用户处置：citymap 的超时锁让校验器整体判红灯。** 读数
  `lock-stale: com.collin.citymap.lock 心跳超时（1783s），holder=qianwen-work-assistant`；
  该包文件最后写入已是 50 分钟前（疑似会话已停）。**按 §3「心跳超时锁不得自动删除，报告用户处理」，
  本会话未删、未碰 citymap 任何文件。** 后果：只要这把锁在超时状态，`verify_project.py` 就一直 FAIL 1
  ⇒ 下个会话开工基线也是红的（与本包代码无关）。处置二选一：恢复那个会话让它续心跳，或你确认后删掉
  `.locks/com.collin.citymap.lock`（它还有个 `.lock.tmp` 残留）。
- **M3~M9 全部未动**。下一站 M3（相位图 A\* + 四剪枝 + 无解 Top-K，静态2/3）。
  `Field.EarliestSeconds` 与 D-18 的中间格/终点分桶判据是 M3 的直接可复用产物，**别在求解器里再写一套判据**。
- `VisibilityTableAssetVersionTests` 之外，**A 包编辑态的视野体注册时机问题未由本包解决**（属他包，未代修）：
  实测「切场景后同一帧立刻烘焙」会得 `视野体=0 巡逻单位=3` ⇒ 烘出永久安全格=全部格的全瞎表。
  本包的处置只是让它**不再静默**（三处出口）；根因在 A 包注册表，M3 若要自动化取证得先过一帧。
- **增量重烘未实现**（U-3）；§6.7「建议参考相位时长」未实现（U-2，与 M3 同期）。
- **本包没有自己的 `Sample/` 演示场景**，取证与演示复用 A 包 `StealthSample`；M9 的两段式演示视频未落
  （**录屏只能用户手动录**，本包不得自行生成或标注"示意视频"）。
- `.gitignore` 仍未加「表资产」忽略行：工具报 `Cannot preserve Windows file access permissions` 拒改，**未绕过**；
  靠逐文件精确 `git add` 保证不入库。**长期规则需用户拍板。**

## 3. 已知坏

- **本包无红灯**：本轮门禁 = 编译 `false` + 归零后重跑验证动作 `consoleErrors=0/consoleWarnings=0` +
  校验器的本包相关检查全 ok。整体 FAIL 1 的来源见 §2 第一条（他包锁）。
- **本会话自造过两次红灯，均已当场修复**（§1.9）：
  ① `CS0246`（新测试漏 `using …EditorTooling`）；
  ② **测试清理越界**——`Resources.FindObjectsOfTypeAll<T>()` 会列出**磁盘上的真实资产**，
     `TearDown` 里 `DestroyImmediate` 被 Unity 拦下打 error，**5 条用例断言全对却全红**。
     修法是判 `EditorUtility.IsPersistent`；同类隐患（`TimelineRecomputeTests` 原会销毁
     **他人正开着的时间轴窗口**）一并改成只回收自建实例。
- **我在文档里写过三处不实数字/推断，均已按实测校正**：本包 53→**54**（含 `VisibilityTableBakeTests`
  实际 16 条）、全量分包 130+121+48+7→**141+121+54**、以及「本轮读数与前次完全一致」这句证据不足
  （桶 0 危险三角 774 与条目 17 的 768 不同，各桶本随相位变）⇒ 改成逐项比对。**教训：文档数字一律
  从 `test run` 输出与 `git status` 回填，别凭印象。**
- **本会话两次忘记续锁心跳**（3271s、436s），都让校验器直接判红「禁止交付」。
  续自己活跃的锁没问题，规则禁的是自动删他人超时锁。
- 环境噪声 `KeyNotFoundException: 'services-gateway'` 仍在 `Tools/console_allowlist.json` 在册（用户 2026-11-02 批准）；
  本轮未新增豁免、未代清他会话日志。
- **取证纪律（M3 必守）**：① 比较两次烘焙必须**先拷位图快照**（`LoadAssetAtPath` 返回同一缓存实例）；
  ② 反射直调 `OnGUI()` 是假信号（无 GUI 上下文必抛 NRE），界面验证要**走菜单开窗口 + `Repaint()`**；
  ③ `SceneView.duringSceneGui` 里**禁用 `Gizmos.*`**（必抛、每次重绘一条 error，顶爆门禁）；
  ④ 两段式命令名必须整体加引号（`"script recompile"`），写错的失败请求本身会污染控制台；
  ⑤ 切场景后先过一帧再烘焙，否则撞全瞎表。

## 4. 下一步（M3 建议顺序）

1. **先让用户处理 citymap 超时锁**（§2 第一条），否则开工基线一直是红的、门禁无法真正绿灯。
2. 跑 `python Tools/verify_project.py` + `unity status` + 取包锁，确认基线。
3. 相位图 A\*（§6.1）+ 四剪枝（§6.2）：**剪枝①现在就该消费 `IsPermanentlySafe`**（M1 条目 7 已独立验证该层可信）；
   边的判据**直接复用 `PhaseReachability` 的中间格/终点分桶语义**（§6.9 / D-18）。
4. N 相位封顶判有无解（静态2）→ 无解 Top-K + 最早到达时间（静态3，D-04）；§6.7 参考相位时长（U-2）。
5. 求解器**必须能后台/多线程**（D-01 的全部意义）：表与传播都无 UnityEditor 依赖、可只读并发；
   **任何新增求解入口不得在主线程现算射线**。
6. 面板上「表版本不符」的当前提示是文本横幅——M3 若要"一键重烘"入口，注意 D-22 抬版本后
   v1 资产读不出表体，任何重烘路径都得走 `EnsureAsset` + 脚本模式同一入口。

## 5. 勿碰项

- 🚨 **`.locks/com.collin.citymap.lock`（超时锁）——本会话未删，下个会话也不要自动删**（§3）。
- `com.collin.citymap` 与 `com.collin.stealth` **任何文件**：工作区里 citymap 的
  `SculptBrushTool.cs` / `SculptBrush.cs` / `SculptBrushTests.cs` / `TerrainProfile.asset` / `TerrainTest.unity`
  与 `Assets/` 下的 `CityProject.asset`、被删的 `test.asset` **都是他会话现场，未动、不要提交**。
  A 包视野体注册时机的问题（§2）也不得由本包代修。
- **`Sample/VisibilityTable.asset(.meta)` 不入库**（派生产物；本轮重烘已把它升到 version=2）；
  `PlannerBakeSettings.asset` 是源、**入库**。
- **单一口径纪律**（破了必被用例判红，别绕）：`VisibilityTable.BucketOf` = 填桶/取桶唯一口径；
  `BuildDangerMask` 逐格走 `IsDangerousAt`（**不许自己按桶扫位图**）；
  `VisibleCellCountInBucket` 与 `VisibleCellCountInSliceBucket` 共用同一条位扫描；
  `VisibilityBakeService.EnumerateWalkableCells` = footprint 唯一实现；
  `PhaseReachability.StepCellsFor` = 步长唯一口径；阈值 `7200` 只在 `PlannerBakeSettings.cs` 一处。
- **判定永远转调玩法包契约**（`IVisionWorld.CanSeeAt`）；本包源码出现任何 A 包具体类型即破方案 P。
- **表结构再加字段时必须抬 `LayoutVersion`**（D-22）——别再"只增字段 + 兼容默认值"。
- **测试里凡涉及 ScriptableObject / EditorWindow 的清理，必须先判 `EditorUtility.IsPersistent`
  或只回收自建实例**（本轮实测：扫全部再销毁会试图删库内资产、并销毁他人窗口）。
- `Packages/manifest.json`、`ProjectSettings/**`、`.gitignore`、`README.md`、
  `Packages/packages-lock.json`（机器生成，本包条目由 Unity 自动写入，**不手工解冲突、本轮仍未提交**）。
- `.locks/com.collin.stealthplanner.lock`：本会话持有、**随本次提交已释放**；下轮重新原子取锁，
  长动作（`test run` / 烘焙 / 多次采样）序列里**每 60s 续心跳**。
