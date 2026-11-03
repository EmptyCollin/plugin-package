# 交接单 — com.collin.citymap

> 覆写式，非追加日志（AGENTS §4）。最近更新：2026-11-03（会话 `2026-11-03 m4-terrain-workflow`）
> 代码阶段：本会话按 §3 原子取锁开工，按 §1.6 三项门禁交锁。**未 push。**

## 1. 已完成（M3 显示层 + M3-b 参数链路 + M4-a 材质补齐/资产归位 + M4-b 参数定案/delta 地基）
- **M4-b**：用户定死 `hardRetreatSlope=18°` + 陡区**只削陡角保缓**，12 文档定稿；
  `SculptDeltaAsset.cs`（delta 数据模型：稀疏+NaN 哨兵+只进 Heights+确定序重放）+ 16 条测试（本包 **116/116**）。
  **只立地基未接管线**——接管需改脏传播（推翻 D-14），属规则级、待批准。H-45 自查修掉重放顺序不定值。
- **M4-a（本会话末）**：
  - **材质补齐**（用户反馈①"地表没材质看起伏累"）：根因是 M3 回退链 `URP/Lit→Diffuse` 都不读 `COLOR`，
    坡度顶点色白写。补 `Sample/Shaders/CityTerrainVertexColor.shader`（实测 `isSupported=True`）
    + `Sample/Materials/CityTerrainVertexColor.mat`；回退链改 `引用→自带shader→URP/Lit→Diffuse`。
    **端到端实测**：绑定项目 → `3969/3969 chunk`、块渲染器 `shader=Collin/CityMap/Terrain VertexColor`。
  - **H-44（自造，校验器 assets-layout 抓到）**：`FindOrCreateProfile()` 原静默写 `Assets/Collin/` 违反 §0；
    改固定进包内 `Sample/Profiles/`，已生成资产用 `MoveAsset` 保留 GUID 迁回（场景引用不断），assets-layout 转 `[ ok ]`。
  - **面板代建对象自洽性**：`CreateTerrainInstance()` 原没绑 `projectForStandalone` ⇒ 面板一关画面永久空白，已补绑定。
  - **工作流重排提案** `Docs/Modules/12-TerrainWorkflow.md`（**待批准，未动代码**）：三段式 + 三级坡度退让；
    结论与 §3.2 重放契约不冲突，关键点是**退让须早判**（steepMask→NoBuild，S3/S4 读之绕行），含 2 个待用户定的参数。
  - M4-a 门禁：本包 100/100、编译 `failed=False`、控制台 `error=0/warning=0`；唯一 FAIL = 他包 `stealth.lock` 超时（§3 不代删）。
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
- **M3-b 参数实时链路（改参数即时反映，A-0-3 / 01 §4.4）**：
  - `TerrainProfile.ComputeValueKey()`：地形参数**值指纹**（20 个可调字段全量覆盖、1e-4 量化、纯函数无副作用），
    是**Inspector 直改第二检**的地基——直接改 SO 字段不调 `SetXxx()` ⇒ 序列号不变 ⇒ Tracker 看不见 ⇒ 静默失效。
  - `CityWindow.DrawTerrainProfile()`：参数控件区（起伏/波长/八度/增益/倍频/域扭曲/城内平整度/地块坡度/自由权重/
    内切/外延/自动地块/冻结细节）+「创建地形实例（含显示层）/新建 Profile」入口。全部走 Profile 显式写入口
    （Controller 只写 Model，§0.1 原则2），面板**不直接写序列化字段**。
  - `CityWindow.DetectDirectProfileEdit()`：值指纹第二检，**先比 `ManualVersion` 区分来源**——显式入口已 bump
    则只同步基线、不补提交，避免面板滑条被**双重 bump**（自查抓到的自造缺陷，与 H-30 按通道记账同构）。
  - 新增 1 条用例 `Value_Key_Covers_Every_Tunable_Field`（本包 99→**100**）：反射枚举全部参数逐个扰动，断言
    「漏检字段清单为空」+ 枚举自校验（实得 20，阈值 ≥18）+ 复位后指纹回基线。**把"指纹漏字段"变成机器保证**。
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
- **归零序列必须从"已静默的 clean 状态"起跑**：`CS0618`（他包用废弃 `FindObjectsByType`）**只在重编译那一刻**产生。
  若紧接在一次 `script recompile` 之后取 `console status`，会把"别人触发的编译警告"误判成"我的改动破坏门禁"。
  本轮做法：`console clear` → 静默 60s → 再跑验证动作 → 取读数（Unity Connect 的 `services-gateway`
  KeyNotFound error 在静默窗口内**不复现**，属一次性瞬时初始化，不塞 allowlist）。
- **新增第二检/兜底逻辑时，先问"第一检已经覆盖的那部分，我会不会再做一遍"**：面板滑条走 `SetXxx()` 已 bump 版本，
  值指纹第二检若不加区分就**双重提交**（表现是"手感发黏"而非崩溃，难归因）。
  按 `ManualVersion` 分辨来源即可消除——与 H-30「记账粒度到可变面而非对象」同构。

## 3. 未完成
- **显示层只做地形 mesh**。道路条带 mesh、建筑 prefab 实例、SceneView overlay（城心圆环、约束带可视化、光标读数）未做（01 §3.3/§4.4）。
- **面板参数控件的 GUI 交互尚未实机取证**：`DrawTerrainProfile()` / `DetectDirectProfileEdit()` 已落盘并编译通过、
  数据地基（值指纹逐字段覆盖、`SetXxx` 通道归属、S1a/S1b 已注册）已断言；但滑条拖动 → 重算 → 画面这条手感
  **EditMode 断言不了**（反射调 `OnGUI` 抛「只能在 OnGUI 内调用」，项目既有实测）⇒ 须用户在
  `Tools/Collin/CityMap/City Window` 实机确认 A-1-3/A-0-3 的 Preview→Commit 两档。
- 雕刻 delta（`SculptDeltaAsset`）、河流下切层未做；接入时按 01 §3.2 次序插在基底与约束之间，显示层随之扩展重建触发面。
  **本会话已把工作流重排成文**（`Docs/Modules/12-TerrainWorkflow.md`），这两项归入其中的 M4。
- 无 PlayMode 测试；真实 `Ctrl+Z` 端到端链路（A-17 完整形态）待接。
- `Sample` 小城与演示视频：M10。

## 4. 下一步（依 12 提案，待用户批准后执行）
1. **先要用户拍板 `12-TerrainWorkflow.md` §3 的两个待定参数**：
   ① `hardRetreatSlope` 默认值（15°/18°/20°）；② 陡到留野的地块是整块丢弃还是只削陡角。
   定了才能写代码（§4「DESIGN 先于代码」）。批准同时把里程碑重排回写 `DESIGN.md`。
2. **M4 山水创作工具全套**（01 §5 + 02）：雕刻笔刷（抬/压/平滑，固定步长积分 A-1-5）+ delta 层持久化 + 河流下切笔刷；
   并实现"退让早判"——S1a/carve/sculpt 之后**立刻**逐格算 `steepMask` 置 `NoBuild`，S3/S4 读之绕行（**不能放 S1b**，否则路网已定、退让无效）。
3. **实机验收 M3/M3-b/M4-a**（成本极低，建议用户现在做）：`Tools/Collin/CityMap/City Window` → 绑/建 CityProject →
   「创建地形实例（含显示层）」→ 现在应能看到**带坡度色带的起伏地表**（材质已补）；拖「城内平整度」确认即时变化。
4. 代码注释引用编号（`// 09 H-40`、`// 12 §2`、`// 01 §3.3`），ACCEPTANCE 续接编号 **43+**（43 已被 M4-a 用到）。

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
