# HANDOFF — com.collin.stealthplanner

> 覆写式交接单（非追加日志），每次交锁前整体重写五部分（AGENTS.md §4）。
> 只写可观测事实：碰了哪些文件、跑了什么指令、结果如何；不写意图与代码行为解释。

最后交接：qianwen-work-assistant · `planner-m4-2026-11-05` · 本次提交（M4 第一增量：PhaseKShortest 多条边不相交短解 + 面板接入 + 70 条用例）
> 前序：`78a103a`（骨架+DESIGN）→ `bd0d8b4`（门禁补记）→ `4334661`（DESIGN 定稿）→ `e1e957b`（M1 表+烘焙）→ `fe44abe`（M2 时间轴+传播+热区）→ `4535b3a`（M2 收口轮 表版本1→2）→ `eabaca3`（M3 可行性+Top-K）

## 1. 已完成（本轮 = M4 第一增量，静态设计 4 的算法地基）

**Runtime**（纯 CPU、只读表，判据全部走 `PhaseReachability` 公开入口）：
- `PhaseKShortest.cs`（新增）：前 M 条**边不相交**真实动线。
  - 节点 = `(cell, layer)`（**不是 cell**）：`parent[layer*n+cell]` 兼作"未发现=-1"哨兵与父指针，每轮 `Array.Fill(-1)` 重建（**不是 Clear(0)**——0 是合法格号）。
  - 每轮一次"按层递增、逐层重建前沿"的单位代价分层 BFS：首个把撤离点纳入前沿的层=最早到达；`Route.CellsPerLayer` 按层展开站位（相邻层同格=等待）。
  - 路线产出后把其全部转移 `(from,layer)→to`（**含等待边**）锁进禁止集 ⇒ 下一条结构必不同；穷尽如实返回少于 M 条。
  - 边键 `EdgeKey(fromCell,layer,toCell)` 三位打包（各 <2^21）；规模护栏 `MaxLayerCells=24M` 超限回可读失败；入参非法不返回半条路线。
  - 口径 = **非严格 Yen**（DESIGN **D-25**：理由、代价声明、升级路径都在该条；组 C 验收按 D-25 判）。
- `StealthTimelineWindow.cs`（改）：求解区块升级「静态设计 2/3/4」，新增「求前 M 条短解」按钮 + `M` 输入（1~16）+ `_routesSummary` 文本（每条到达时刻/移动跳/等待拍/末站 + 口径行 + 耗时）。结果只出文本、不驱动热区。

**测试**：本包 **63→70**（`PhaseKShortestTests` **7 条**）。全量 **332/332**，按 `FullName` 命名空间：
CityMap 141 + Stealth 121 + StealthPlanner 70（自洽）。

**实测读数**：
- 真实烘焙表（StealthSample 3 哨兵，视野体=3 可信现场，horizon=480，M=5）：
  `M3 可行=True 到达桶=55 | M4 路线数=5 | 一致性(首条==M3)=True | 5 条搜索耗时=2920ms`；
  路线1 桶55（移动55、等待0），路线2~5 桶57 且首步分别岔向格 1/2/110/217 ⇒ 结构互异属实。
- 注入内存表（8×8、中心格全时危险）UI 接线：`可行性=✅ t=7.0s（桶14）、途中可达 63 格`；
  `短解=3 条（<M=4，如实穷尽）· 步长1格 · 2ms`，其中 #3 含「移动14+等待1」。
- 单条首解 ≈0.6s、M=5 重搜 ≈M×0.6s 的成本画像如实入档（**未上四剪枝前不要拿它做自动批处理**）。

**门禁（最终代码状态）**：`console clear` → `"script recompile"` → `test run` 全量 → 重放本会话验证动作
（销毁反射遗留窗口）→ 静默 70s → `"console status"` → `verify_project.py`。
三项全绿：`compilationFailed=false`；`consoleErrors=0 / consoleWarnings=0`；校验器 **FAIL 0 绿灯**。
场景末态已还原 `TerrainTest`（非脏）。

**文档**：DESIGN 里程碑 M4 标「第一增量达成 + 后续增量清单」+ 新增 **D-25**；ACCEPTANCE 状态头、
组 C 两行推进（如实标注"本条整体不算通过"）、新增条目 **33~35**、U-6 推进、新增 **U-8**、补 M4 门禁行；
CHANGELOG 新增 Added(M4 第一增量) + Fixed/已知问题登记段、版本说明更新。

## 2. 未完成

- 🚨 **U-8（M5 实跑前必须先解决，属 A 包）**：编辑态**视野体注册间歇恒 0**——本轮多次
  `OpenScene(StealthSample)` + `EnsureRegistered` + `SyncTransforms` 后 `IVisionWorld.Volumes.Count=0`
  （`巡逻单位=3` 正常），连强制同名重载也无效；同一路径 M2/M3 取证时"过一帧后=3"可复现。
  `GlobalServices` 物体查无。疑似 A 包注册表/静态实例跨域重载残留（`StealthWorld.Volumes ← VisionRegistry`）。
  **堆栈与根因都在 `com.collin.stealth`，本会话未代修。** 复现线索已记 ACCEPTANCE U-8。
- **M4 后续增量**（下一轮候选，均在 DESIGN §6.2/§6.5/§6.7、组 E）：
  ① §6.2 四剪枝（**剪枝①消费 `IsPermanentlySafe`**，M1 条目 7 已验证该层可信，现成加速抓手）；
  ② slack（§6.5；需要察觉累积速率参数，动手前先和用户定参数口径）；
  ③ JSON/SO 数据集导出 + 注入弹窗（静态4→动态1；组 E round-trip 用例；`Route` 站位序列就是导出载体）；
  ④ 规模化耗时基准 N∈{4,8,16}。
- §6.7 参考相位时长（U-2）、增量重烘（U-3）、实跑复现器 M5、反推路点 M6、安全岛端到端 M7、
  UMPM M8、`Showcase.json`+两段式演示视频 M9（**录屏只能用户手动录**）。
- `.gitignore` 表资产忽略行仍未加（工具报 ACL 拒改，未绕过）；靠逐文件精确 add 排除。

## 3. 已知坏

- **本包无红灯**（门禁读数见上）。
- **本轮自造并已当场修复的问题**（无遗留）：
  ① `PhaseKShortest` 首版用"格级全局 arrived"当访问标志 ⇒ 同格不能跨层重入，连首条路线都找不到、
     与 M3「判可行」自相矛盾（4 条用例红）。改 `(cell,layer)` 建模后全绿。
     ⚠️ 连环坑：改成 `parent` 哨兵法后用 `Array.Clear` 初始化 ⇒ "未发现"=-1 被填成 0（0 是合法格号）
     ⇒ 又是 0 条。**哨兵值初始化必须 Fill(-1)，这类错编译不报、只把可行判成无解。**
  ② 反射创建的窗口实例残留挂 `duringSceneGui`，在无 StealthClock 场景持续 `Recompute` 刷 A 包警告
     （2 条 warn 污染门禁）⇒ 销毁全部遗留实例 + 归零静默 70s 复采恢复 0/0。
  ③ 一次 Edit 替换吞掉相邻方法的文档注释行 + 留了个未使用局部变量（会打 CS0219 顶爆门禁）——提交前 diff 清掉。
- **取证纪律（本轮再验证）**：会话恢复后编辑器端口会变（7800→7801），被打断的取证调用**结果不可用**，
  必须在新连接重跑；两段式命令整体加引号；`OpenScene` 同名场景不触发重载。
- 环境噪声豁免清单不变（`services-gateway` 等，用户 2026-11-02 批准），本轮未新增。

## 4. 下一步（建议顺序）

1. **先处理 U-8**：它是 A 包问题但挡着 M4 后续的真实场景取证与 M5 实跑。把复现步骤交给用户/或经用户
   批准去查 `com.collin.stealth` 的注册时机（**默认不代修**）。临时替代：算法层取证继续可用注入表。
2. M4 后续增量建议先做 **剪枝①**（改动小、收益实测可得：horizon 480×11664 的重搜成本直接降），
   再 **数据集导出注入**（组 E round-trip 是静态4 收口条件），slack 最后（要先定参数口径）。
3. 任何求解侧新代码：判据只准调 `PhaseReachability.ExpandSuccessors/BuildBlockedMask/TryBuildNoData`，
   视界只走 `PhaseSolver.Horizon`，步长只走 `StepCellsFor`；入口一律"可读失败 + 不返回半结果"。
4. 门禁老三样 + **静默 70s 复采**；创建过窗口/资产的探针必须在同次调用里彻底回收。

## 5. 勿碰项

- **`Sample/VisibilityTable.asset(.meta)` 不入库**（派生产物）；`PlannerBakeSettings.asset` 是源、入库。
- **单一口径纪律**（新增成员也要守）：
  `PhaseKShortest` **不得**自带任何安全/邻居判定（现仅复用 `#5` 三入口 + 自算 `EdgeKey`）；
  `BuildBlockedMask`=唯一"某桶不能站"入口；`ExpandSuccessors`=唯一邻居展开；
  `StepCellsFor`=唯一步长；`PhaseSolver.Horizon`=唯一视界；`BucketOf`=唯一填/取桶；
  `BuildDangerMask` 逐格走 `IsDangerousAt`；`EnumerateWalkableCells`=唯一 footprint；`7200` 仅一处。
- **表结构加"缺省即危险"字段必须抬 `LayoutVersion`**（D-22）；防线必须有可观测出口（D-23）。
- **短解口径以 D-25 为准**（边不相交≠严格 Yen）；升级严格 Yen 前先看 `Route` 结构，别绕开真源拼路径。
- SceneView 回调禁 `Gizmos.*`；窗口视图态不写静态；测试/探针创建的 ScriptableObject/EditorWindow
  回收前判 `EditorUtility.IsPersistent` 或只回收自建实例。
- `com.collin.core`（契约签名改动走 ADR）；`com.collin.citymap`、`com.collin.stealth` **任何文件**——
  工作区里 citymap 的 `CityWindow.cs`/`CityTerrain.cs`/`SculptFilterAsset.cs`/`TerrainProfile.asset`/
  `TerrainTest.unity`、`Assets/CityProject.asset`、被删的 `Assets/test.asset`、`README.md`、
  `packages-lock.json` **都是他会话现场，本轮未动、不要提交**。
- `.locks/com.collin.stealthplanner.lock`：本会话持有、**随本次提交释放**；长动作序列每 60s 续心跳。
