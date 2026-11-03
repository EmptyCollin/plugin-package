# 交接单 — com.collin.citymap

> 覆写式，非追加日志（AGENTS §4）。最近更新：2026-11-03（会话 `2026-11-03 m2-terrain`）
> 代码阶段：本会话按 §3 原子取锁开工，按 §1.6 三项门禁交锁。**未 push。**

## 1. 已完成（M2：Modules/01 数据层 = S1a 基底噪声 + S1b 平整约束）
- Runtime/Core 新增 5 文件：
  - `CityNoise.cs`：纯 C# 确定性噪声内核（value noise + fbm + domain warp，splitmix64 派生子种子）。
    **不用 Unity PerlinNoise**（跨平台/版本不保证逐位一致）。全纯函数、无静态可变态。
  - `HeightField.cs`：`TerrainField`（Heights/BaseHeights + 量化往返）、`ConstraintStrength`
    （Parcel=8 / RoadCrown=4 / WaterBuffer=1 分级 + 核心贴合度）、`PlaneConstraint`
    （`InsetMeters`/`ExtendMeters`/`Exact` 硬覆盖；`MaxInsetFor`/`ComputeCoreHalf` 是核心几何的唯一算法源）、
    `PlaneFitting.FitFromBase`（坡向取最小二乘、水平取中位数以抗离群）。
  - `TerrainBaseStage.cs`（S1a）：`TerrainProfile` SO + 城内平整度旋钮；`ResolveUrbanCenter` 回退图幅中心；
    压幅公式 `ampFactor*(amp*baseN + detailA*detail)`（H-37 修复后压在整条噪声和上）。
  - `FlattenSolveStage.cs`（S1b）：加权融合 + 硬覆盖。scratch 缓冲改实例级，避免跨上下文污染。
  - `CityTerrain.cs`：场景组件（手动平整区 + 自动地块开关），`EnumerateAutoTiles` 按 pitch 留街道间隙。
- `Tests/Editor/TerrainDataTests.cs`：新增 28 条地形断言（累计 56→**84**，`test run --mode editor` **Total 84 / Failed 0**）。
- **实现轮抓到并修复 6 处空洞**（详见 09 第六轮）：
  - H-31 城心写死落图外⇒旋钮失效；H-32 拟合截距取点口径错（均值点当中心）；H-33 中位数被 LSQ 旁路；
    H-34 缺内切⇒边界陡坎 4.79°→65.48°；H-35 自动地块无缝隙⇒两独立平面不可满足；
  - **H-37**：压幅只乘低频层、细节层绕过 `urbanFlatness`⇒`flatness=0` 城内标准差 0.236m **违反 A-1-1 ≤0.2m**。
    **它是被我自己放宽的断言阈值（写 0.6f 而非文档的 0.2f）掩盖的**——修公式后为 0m。
    纪律：**文档给绝对数值，断言就用那个数值**；补 `flatness=0.5` 中点线性度断言防中间段不跟手。
- 交锁三门禁最终复跑：`recompile_status failed=False`；`console clear`→重跑全量+校验器→
  **`consoleErrors=0/consoleWarnings=0/seeded=True`**；`verify_project.py` 本包项全 `[ ok ]`。

## 2. 实测得到的环境事实（新会话直接照做）
- **H-36：`recompile_status` 返回 `up_to_date` 不等于新代码已编译**。dll mtime 可能早于源码，测试跑旧程序集，
  表现为"改了码失败数值一字不差重复"。判别法：比 `Library/ScriptAssemblies/*.dll` 与源文件 mtime（dll ≥ src），
  或反射探新成员。**改码后必先做新鲜度核验，再跑测试**。
- 两段式指令名整体加引号：`console clear` / `script recompile_status` / `console status`；
  把 `console get --tail 60` 当单条指令名传入会报 Command Not Found，**且真实会真计入 consoleErrors ⇒ 破坏归零门禁**。
- `test run` 的 `--mode` 只接受 `editor|playmode|all`（`test list` 可接受 `edit`）；传错值 Summary 全 0，易误判成"没测试"。
- 测试故意触发的 error/异常会真计入 `consoleErrors`；`LogAssert.Expect` 不回退计数，堆栈含本包程序集⇒不可用 §1.8 豁免 正解：断言期 `Debug.unityLogger.logEnabled=false`+`finally` 复位。
- "只赋值不读取"的局部变量产 warning ⇒ 同样破坏归零。
- 端口读数在同一 PID 上会漂移：发指令前现查 `unity status`，失败先重试再判故障。
- 新建内嵌包仍需 `package resolve`。

## 3. 未完成
- **M2 只交付了数据层与 S1a/S1b 求解，未生成任何显示 mesh**（原 M2 规划含 mesh，已顺延）。
  下一步 S1a/S1b 结果只能靠测试断言与数据读取验证，用户在场景里"看不见"。
- `CityWindow` 面板仍只有骨架：世界参数/城心提交入口在，但未接 `TerrainProfile` 参数的实时编辑链路（A-1-3/A-0-3 Preview→Commit 两档）。
- A-1-2（delta/Absolute/Additive 三模式在**重生成**下的逐点保持）、A-1-4（道路各向异性）、A-1-6（一次笔划=1 条 undo）
  尚未取证——依赖 M3 工具笔划与 mesh 提交后才能端到端验。
- `OccupiedHard` 烘焙只有接口（输入 `BuildingRecord` 集合，M8 才有数据）。
- 无 PlayMode 测试。

## 4. 下一步（M3 建议顺序）
1. **S1a/S1b 显示 mesh**：把已算好的 `TerrainField` 画出来（01 §3.3 法线/坡度与 `GetNormal/GetSlopeDeg` 同口径），
   这是"实机体验"的第一屏——用户此时能第一次在场景里看到地形。
2. `TerrainProfile` 参数实时链路接进 `CityWindow`（改参数即时反映，§0.1 所见即所得），补 A-1-3/A-0-3。
3. 接上 M1 遗留的真实 `Ctrl+Z` 链路验（A-17 完整形态）。
4. 代码注释继续引用编号（`// 09 H-37`、`// 01 §3.4`），ACCEPTANCE 续接编号 29+。

## 5. 勿碰项
- 不改 AGENTS.md / §8 钉死版本 / `Packages/manifest.json` / `ProjectSettings/**`。
- 不动 `com.collin.core`、`com.collin.stretch`、`com.collin.stealth`；citymap 侧禁止注册 `IStretchService`（H-14b）。
- **不改 `Modules/11` 冻结表**：字段增删/偏移/标度/位定义变更须先在该文档追加登记行并经用户批准。
- 脏位新增须同步三处：`DirtyBits` 枚举 + `IntentChangePlanner` 传播 + `StageClaims` 认领，且 00 §2.4/§4.2 文档同步。
- 不引第三方 UPM 包进 Runtime/生成内核。展板场景 `Assets/Showcase/` 不因本包改动。
- 多会话共用同一编辑器实例：**不擅自 `console clear`**（除取归零证据的必要时刻），不动他人锁。
- **工作区存在其他会话未提交改动**（`AGENTS.md`、`Tools/verify_project.py`、`Packages/packages-lock.json`、
  `Packages/com.collin.stealth/`、`com.collin.stretch/Sample/Profiles/`）：非本会话产物，M2 提交**不 `git add` 它们**，
  已如实上报用户处理。
- 已交付文档原地更新；仅用户明确要求新版本时另存。
