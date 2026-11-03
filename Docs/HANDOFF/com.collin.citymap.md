# 交接单 — com.collin.citymap

> 覆写式，非追加日志（AGENTS §4）。最近更新：2026-11-03（会话 `2026-11-03 m3-view`）
> 代码阶段：本会话按 §3 原子取锁开工，按 §1.6 三项门禁交锁。**未 push。**

## 1. 已完成（M3：Modules/01 显示层 = chunk mesh + 面板实时接线）
- **用户实机体验的第一屏达成**：S1a/S1b 算出的高度场现在能画进场景，编辑期改参数即时反映（§0.1 原则1）。
- Runtime/View 新增 2 文件：
  - `TerrainView.cs`：显示层**纯几何**（不触碰 Unity 对象、不引用 UnityEditor）。顶点按 `displayStep` 取解析网格线、
    法线走 `TerrainField.GetNormal` **同口径**（禁 `RecalculateNormals`）、UV = 世界 XZ/tileScale、顶点色 = 坡度带；
    逐 chunk 独立几何 + 脏区→chunk 最小映射 + 同拓扑局部更新 / 拓扑变化才重建 + 超 65k 顶点自动升 32bit 索引。
  - `CityTerrainView.cs`：场景宿主（`[ExecuteAlways]`）。两条数据路径（面板驱动 / 自建 S1a→S1b）、**一条构建路径**
    （编辑态与 Play 态必然一致）；chunk 物体与 mesh 一律 `HideAndDontSave`（派生态不入库）。
    `CityRuntime` 静态桥：面板 Commit → 提交序号驱动增量重建，杜绝"两份世界"。
- Editor 新增/改：`CityWindow.SyncStages()` **首次把 S0/S1a/S1b 接进 Runner**（M1/M2 交付时 Editor 无任何 `_runner.Register`，
  "重新生成"只跑 S0 ⇒ 数据层全绿但场景永远为空）；`CommitAndNotify()` 在 `Tick` **之前**取脏区（Tick 后会 `ClearDirtyBounds`）。
- **菜单入口统一收进 `Tools/Collin/CityMap/`**（用户 2026-11-03 要求：工具栏**不单开顶层 Collin**，与 stretch 包对齐）。
  实测全项目 `MenuItem` 注册路径无一例外在 `Tools/Collin/` 下，顶层 Collin 已消除。
- `Tests/Editor/TerrainViewTests.cs`：新增 15 条显示层断言（本包 84→**99**，全项目 `test list` → **141**）。
- **实现轮抓到并修复 5 处空洞**（详见 09 第七轮）：
  - H-38 绕序反 ⇒ 地表背面朝上（点积 −0.992）；H-39 误用裸缓冲流 `SetVertexBufferData` ⇒ 0 字节缓冲越界抛；
  - H-40 显示顶点/差分探针越界被 clamp ⇒ 边缘一圈假平地（16.25°→7.96°→收敛），**第一轮修法方向错**（写成分档断言开脱）；
  - H-41 `RoundToInt` 使 chunk 比请求更小且依赖舍入规则；H-42 脏区余量取整块 chunk ⇒ 点状改动放大成 9 块。
  - H-43（我的断言口径错）：把坡度色带 r/g 分量当单调量，实为**红绿对比 r−g** 单调。
- 交锁三门禁（菜单改动重编译后复跑）：编译 `failed=False`；`console clear`→重跑本包全量→`console status`
  **`consoleErrors=0/consoleWarnings=0/seeded=True`**；`verify_project.py` **`FAIL 0`、结论: 绿灯**（本会话内首次整体转绿）。

## 2. 实测得到的环境事实（新会话直接照做）
- **`test run --filter_type assembly --filter com.collin.citymap.Tests`** 是多会话共用编辑器时**唯一干净的取证方式**：
  全量跑会把**他包故意触发的 error**（实测 stealth 的 `Services_DuplicateRegistration_...` 会 `LogError`）计入共享控制台 ⇒ 破坏 §1.6b。
  不改他人测试，按程序集取本包归零证据。
- **`test run` 返回 `Summary.Total=0` 有两种成因**：①`--mode` 传错值；②编辑器当时在 **Play Mode**
  （控制台报 `This cannot be used during play mode` / `Test tree is not available`）。后者需等其退出再重跑，**不是重发命令**。
  本轮第三次全量跑就撞上他会话切 Play Mode，等退出后 141/141。
- **H-36 依旧有效**：`recompile_status=up_to_date` 不等于新代码已编译；改码后必先比 dll/源 mtime（dll ≥ src）再跑测试。
- `script eval` 对引号/换行极脆弱（本轮两次被 shell 拆坏），跨行 C# 一律 `script eval_file --file <win 路径>`；
  且 `eval_file` 返回的 `result` 字段有时为空串（枚举/序列化不稳），**核验菜单/属性别依赖反射探针，直接读源码字面量**。
- `EditorApplication` **没有** `isPlayMode` 成员（实测 CS0117），查播放状态用 `isPlaying`。
- **二维向量第二分量在本工程承载世界 Z 轴，是 `.y` 不是 `.z`**（实测 CS1061）。
- 两段式指令名整体加引号（`console clear` / `script recompile_status`）；`console get` 的 `--tail` 是**独立参数**不是指令名一部分。
- 测试故意触发的 error 会真计入 `consoleErrors`；断言期用 `Debug.unityLogger.logEnabled=false`+`finally` 复位。
- "只赋值不读取"的字段/局部变量产 CS0169/CS0414 warning ⇒ 破坏归零（本轮删了 `_wantRegions`/`_configPollAt`/`_ownSeq`/`_ctxKey`）。

## 3. 未完成
- **显示层只做地形 mesh**。道路条带 mesh、建筑 prefab 实例、SceneView overlay（城心圆环、约束带可视化、光标读数）未做（01 §3.3/§4.4）。
- `CityWindow` 面板仍无 `TerrainProfile` 参数的**实时编辑控件**（滑杆/字段）：现在改参数要靠直接编辑 SO 或走 `SetXxx` 代码入口，
  面板上只有阶段树与"重新生成"。A-1-3/A-0-3 的 Preview→Commit 两档手感尚未端到端验。
- 雕刻 delta（SculptDeltaAsset，M6）、河流下切层（M4）接入时按 01 §3.2 次序插在基底与约束之间；显示层需相应扩展重建触发面。
- 无 PlayMode 测试；真实 `Ctrl+Z` 端到端链路（A-17 完整形态）待接。
- `Sample` 小城与演示视频：M10。

## 4. 下一步（M4 建议顺序，或按用户优先级）
1. **面板实时链路**：`CityWindow` 加 `TerrainProfile` 参数控件（拖动=Preview、松手=Commit），补 A-1-3/A-0-3。
   这是"所见即所得"手感的核心，也是用户实机体验的第二屏。
2. **S2 河流下切**（Modules/02）：接入 01 §3.2 次序第 2 层，显示层随之支持 carve 叠加。
3. SceneView overlay：城心圆环 + 约束带 + 光标处高度/坡度/掩码读数（01 §4.4）。
4. 代码注释继续引用编号（`// 09 H-40`、`// 01 §3.3`），ACCEPTANCE 续接编号 **40+**。

## 5. 勿碰项
- 不改 AGENTS.md / §8 钉死版本 / `Packages/manifest.json` / `ProjectSettings/**`。
- 不动 `com.collin.core`、`com.collin.stretch`、`com.collin.stealth`；citymap 侧禁止注册 `IStretchService`（H-14b）。
- **不改 `Modules/11` 冻结表**：字段增删/偏移/标度/位定义变更须先在该文档追加登记行并经用户批准。
- 脏位新增须同步三处：`DirtyBits` 枚举 + `IntentChangePlanner` 传播 + `StageClaims` 认领，且 00 §2.4/§4.2 文档同步。
- 不引第三方 UPM 包进 Runtime/生成内核。展板场景 `Assets/Showcase/` 不因本包改动。
- 多会话共用同一编辑器实例：**不擅自 `console clear`**（除取归零证据的必要时刻），不动他人锁；
  他人会话可能切 Play Mode，跑测试前先确认 `isPlaying=False`。
- **工作区存在其他会话未提交改动**（`AGENTS.md`、`Tools/verify_project.py`、`Packages/packages-lock.json`、
  `Packages/com.collin.stealth/`、`com.collin.stretch/Sample/Profiles/`）：非本会话产物，M3 提交**不 `git add` 它们**，已如实上报。
- 已交付文档原地更新；仅用户明确要求新版本时另存。
