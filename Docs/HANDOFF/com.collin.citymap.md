# 交接单 — com.collin.citymap

> 覆写式，非追加日志（AGENTS §4）。最近更新：2026-11-05（会话 `2026-11-05 m4-j-perf`）
> 代码阶段：本会话按 §3 原子取锁开工，按 §1.6 三项门禁交锁。**未 push。**

## 1. 已完成（M3 显示层 + M3-b 参数链路 + M4-a~h + M4-i 断链修复 + M4-j 性能与同步根因修复）
- **M4-j（用户反馈「抬升后地表不同步」+「每笔非常卡、要读条」，2026-11-05，本包 **133/133**、校验器 `FAIL 0` 绿灯）**：
  两现象**同源**——面板「未装配」使显示层**无声回落到自建慢路径**。探针逐句复刻调用组合实测
  **5253 / 5234 / 5529 ms/笔**，耗时分解 `S1a=4860 / S0=114 / S1b≈0ms`；另每笔新分配
  ctx 两张 400 万格数组（≈40MB）+ 重建阶段 scratch（≈50MB）⇒ 大对象堆 ⇒ 阻塞 GC = 读条。
  登记 **D-36 / D-37**，四处修复：
  ① **装配自愈**：`_needsFirstBind` + 更新循环"探测到实例才装配"（找不到下一帧再试）
     + 每帧 `!ReferenceEquals(ActiveContext,_ctx)` 重接桥；
  ② **自建路径按变化来源分层**（项目变→全量／仅 Profile 变→只 S1a／仅实例版本变→只重放 delta + S1b）
     + ctx 与阶段对象长期复用 ⇒ 使 **D-31「雕刻不得脏 S1a」在两条路径同时成立**（旧版只在面板侧成立）；
  ③ **「数据算过」独立成重建判据** `_ownDataChanged` + 按笔刷脏盒 `RebuildRegion`
     （**刻意不把 `_terrain.ManualVersion` 补进 configKey**——补进去 ⇒ `cfgChanged=true` ⇒ 全量 289ms）；
  ④ **脏区显式搬入上下文** `_ctx.AddDirtyBound(Tracker.LastDirtyBounds)`，须在 `ConsumeDirty()` **之后立刻取**
     （该入口会把 `LastDirtyBounds` 复位成 default）。
  **修后同口径实测**：重算 **5253ms → 4ms**、执行阶段 **3 → 1**、重建 `不重建／289ms → 增量 0~2ms`
  （状态串 `增量重建 9/9 chunk（脏盒 202×202m，全图 3969）`）、mesh 顶点 y `[-7.6,5.3]` == 数据 `[-7.6,5.4]`
  ⇒ **画面已同步**；空转一帧 0ms。新增 3 条回归用例（哨兵法判"噪声没重跑"，含防恒真对照组）。
  - **两处自造当场纠正**：**H-58** 我第一版把装配写在 `OnEnable` 里 —— 此刻场景对象尚未进 `IntentRegistry`
    ⇒ `SyncStages` 只注册得出 S0（实测 `runner 阶段数=1`、`ctx.HasSlot(S1a)=False`）而 `ActiveContext` 已非空
    ⇒ 显示层放弃自建又拿不到场 ⇒ **画面全白，比修复前更糟**。纪律：**"修 A 引入 B"的验证要跑到"画面真有东西"**。
    **H-62** 第一版用例误用粗粒度入口 `UndoHistoryTracker.MarkIntent()`（并对象**当前全部通道**，rig 建时
    `SetProfile` 已 bump `TerrainModel`）断言"雕刻只脏 Flatten"→ 红；实为**测试口径错、实现无 bug**
    （传播表 `DirtyOf(DeltaStroke)=Flatten,Building` 一直正确），改两轨断言。
  - **技术路线已向用户交代**：自研高度场 + 程序化分块网格（非 Unity Terrain），三条硬约束 =
    逻辑格与渲染档位分辨率解耦、delta 可重放（D-01-1）、平整是约束求解（Terrain 无该算子）。
    **混合方案（Terrain 做手感）的成本实测没做成**：探针两度踩臆造 API（`Apply` 在 `Terrain` 不在
    `TerrainData`；`TerrainData` 非 `ScriptableObject` 派生），我一度还在脚本里留了占位假调用
    （H-47 同族，已停手删脚本）⇒ **混合方案目前只有定性评估，勿当成已量化**。
- **M4-i（用户要求"先定根因再改"，2026-11-04，本包 130/130）**：反馈①「无光圈」与②「左键变框选」**同源于 H-50**，
  全部由反射探针实测取证（非推测），四项修复：
  - **H-50 门禁真源错位**：笔刷判据读 `CityWindow.CurrentMode`（`EditorWindow` **静态**字段，域重载归零且不被
    窗口布局恢复）。探针实证 `_instance=NULL` 但用户面板正在雕刻模式 ⇒ 回调首行即 return，
    鼠标落给 `UnityEditor.MoveTool`（框选）。正解 **D-34**：`CityTerrain.SculptToolActive` 序列化字段作真源，
    笔刷只读它；`OnEnable` 重绑 `_instance`；写它**不 Bump**（新用例 `Sculpt_Tool_Active_Is_Pure_Tool_State_No_Intent_Bump`
    含 SetFlattenEnabled 对照，防空断言）。
  - **H-51 雕刻层从来不存在**：`Delta=NULL`、磁盘 `SculptDeltaAsset` 数 = 0 ⇒ 落笔必 `_blocked`，
    且提示只 `Debug.Log`。正解：`EnsureDeltaLayer()` 在创建实例与进 ② 步时自动建并绑；阻断原因改走
    `SculptBrushTool.BlockReason` ⇒ **面板黄字**。
  - **H-52 副本滋生机制**：`FindAssets` 对**内嵌包资产恒返回 0**（资产存在且有路径），旧代码据此判"不存在"
    + `GenerateUniqueAssetPath` ⇒ 每次创建实例都长出一个 `TerrainProfile 1/2/3`（上轮删的 6 个由此长回）。
    正解 **D-35**：固定路径 + `LoadAssetAtPath`，创建不加唯一后缀。
  - **H-53 光圈取不到高度**：取样只读 `CityRuntime.ActiveContext`（仅面板驱动分支有值）。正解：
    `CityTerrainView.BoundField` 只读暴露其生成 mesh 用的同一张场。
  - **H-54（附带发现）**：包内 `TerrainProfile.asset` 的 Library 导入记录坏（`GetMainAssetTypeAtPath=NULL`；
    同内容复制文件同样 NULL、`CreateAsset` 产物可加载、`ImportAsset(ForceUpdate)` 因内容哈希未变而无效）。
    已用 `CreateAsset` 覆盖同路径重建，**实测 GUID 不变**（引用不失效）；副本 `TerrainProfile 1.asset` 删除。
  - 另：笔刷格网解析改**按激活实例身份惰性重解析**（原每 GUI 事件全表扫描 = H-10 反模式）。
  - **仍未闭环**：光圈可见性与拖笔手感属 SceneView 渲染，EditMode 无从断言 ⇒ **待用户实机确认，不虚报**。
- **M4-h（显式三步步骤条 + 体验副本清理，2026-11-04，本包 129/129、校验器 `FAIL 0` 绿灯）**：
  `CityWindow.DrawWorkflowStepBar()` 把 12 §1 三段式做成可点步骤条（①参数 ②雕山水 ③平整出地表），
  点相位＝切 `FlattenEnabled` + 切模式；**相位非门禁**（00 A-24），高亮由真源回推（`SyncStepFromState`）
  避免两套状态打架。Editor GUI 不可 EditMode 断言 ⇒ **观感/手感待用户实机确认**。
  另按用户裁定删除 `Sample/Profiles/TerrainProfile 1/2/3.asset`（无引用的体验副本），真源保留，
  `assets-layout` 恢复 ok。
- **M4-g（平整介入时机 + 笔刷可见性，2026-11-04，本包 129/129、校验器 `FAIL 0` 绿灯）**：
  - `CityTerrain.FlattenEnabled`（默认开）+ `SetFlattenEnabled()` 走 ParcelModel 通道；
    `FlattenSolveStage` 在 `CommitBaseAsWorking()+Delta.ApplyTo()` **之后、收集约束之前**整段短路。
    登记为 DESIGN **D-33**（用户否决我初版"逐格退让"方案后的正确机制，退让代码已撤净无残留）。
    实测：关 → 抬 25m 保留、约束 0 条、重跑仍 25m（关闭态幂等）；开 → 约束 9 条、改动 76729 格。
  - 笔刷预览环改**贴地表 48 段描边**并移到前置条件**之前**（旧版画在 y=0 被 ±18m 地形埋住 = 用户反馈①）。
  - **两处自身错误已在 09 §26/§27 留痕**：H-48 我曾在 12 §1 写下**不存在的** `ManualLocked` 豁免机制
    （grep 不到检查点 ⇒ 文档句子必须能在代码里落地）；H-49 把"A 不该妨碍 B"误解为"A 缩小范围"，
    实际要的是"A 延后时机"。
- **M4-d/e/f（雕刻笔刷工具链，2026-11-04）**，用户实机反馈②"先雕山水再生城"的落地：
  `Runtime/Core/SculptBrush.cs`（fb8f6bc，纯函数数学 + `SculptStroke` 状态机，**放 Runtime 才能测 A-1-5
  "60Hz/144Hz 同 delta"**；固定步长积分不乘 deltaTime、位移取世界量、不足一步的位移跨调用留存）
  → `Editor/SculptBrushTool.cs`（8ab4689，`SceneView.duringSceneGui`，**仅雕刻模式接管鼠标**；
  预览环 y=0 纯几何求交，不 raycast 派生隐藏物体；一笔 = 一个 `Undo.RecordObject`；
  松手 `MarkDeltaChanged(笔刷中心,半径)` 交回 D-31 链路刷新，**壳内不重算/不重建**；超格数提示截断）
  → `CityWindow.DrawSculptPanel()`（f70fcc2，半径/强度/动作只在雕刻模式显示，**下拉只列已接线四项**）。
  本包 **128/128**（+9 条），三次增量各自复跑门禁：编译零错、控制台 0/0 或**全命中 §1.8 已批准豁免**、
  校验器 **FAIL 0 / 绿灯**。**H-47**：首版混入臆造 API（`CityRegistry`/`CellSizeMeters`/`HeightAtWorld`/
  `GetSlot<CityGrid>`——CityGrid 是**值类型**）被编译器当场抓住，全文件按 grep 过的真签名重写。
- **M4-c（DESIGN D-31 落地，2026-11-04）**：雕刻 delta 接进 S1b（`复位 → delta.ApplyTo → 拟合读意图地表 → 融合`），
  `IntentKind.DeltaStroke` 双表同步（脏 `Flatten|Building`，**不含 Terrain** ⇒ 不重跑噪声）；
  `CityTerrain.MarkDeltaChanged(笔刷位置, 半径)`（脏区域用笔刷实际位置，不用实例 transform）。
  **接线前查出静默陷阱**：`FitFromBase` 读 `BaseHeights`（`HeightField.cs:314`）而 delta 只进 `Heights`
  ⇒ 照字面接会让雕刻对目标平面不可见、且不报错。判别式取证：`未雕刻 0.106 → 雕刻后 20.124`（抬 20m）。
  本包 **119/119**，M2 断言数值一字不差无回归；门禁**三项全绿、校验器 FAIL 0**。
  **H-46（本轮自查纠正）**：M4-b 提交 `c69fbe4` 里 4 次引用"推翻 D-14"给规则变更背书，
  而 D-14 实为"显示网格分辨率解耦"、被改的规则原本**无编号** ⇒ 登记 D-31/D-32 并全量改引。
  纪律：**写任何引用前先 grep 那个源头**（本会话第三次"证据链自造"型错误）。
- **M4-b**：用户定死 `hardRetreatSlope=18°` + 陡区**只削陡角保缓**，12 文档定稿；
  `SculptDeltaAsset.cs`（delta 数据模型：稀疏+NaN 哨兵+只进 Heights+确定序重放）+ 16 条测试（本包 **116/116**）。
  H-45 自查修掉重放顺序不定值。**管线接线已在 M4-c 完成**（D-31）。
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
- **`script eval_file` 探针取证套路（M4-i 整轮靠它定位根因，四条硬约束）**：
  ① **不接受 `using` 指令**（代码被包进方法体，`using System;` 直接 `CS0210`）⇒ 一律全限定名；
  ② `Type.GetType("X, 程序集名")` 在 eval 上下文**不可靠**（实测返回 NULL 导致后续断言全失真）
     ⇒ 遍历 `AppDomain.CurrentDomain.GetAssemblies()` 取类型；
  ③ 遍历 `IntentRegistry.All` 会撞上**已销毁对象**（实测 `ConstraintAsset has been destroyed` 抛异常
     使整个探针中断、**前面几节结论一起丢失**）⇒ 每节独立 `try/catch` + **即时 `Debug.Log`**，别攒到末尾；
  ④ 读回输出：`console get` 的 stdout 是 UTF-8，Windows Python 默认 GBK 会 `UnicodeDecodeError`
     ⇒ `capture_output` 取 bytes 后 `.decode("utf-8","replace")`，且用 `json.JSONDecoder().raw_decode`
     （尾部还跟一列参数，直接 `json.loads` 报 `Extra data`）。
- **锁文件 `heartbeat_at` 必须是 epoch 数值**：写成 ISO 字符串会让 `Tools/verify_project.py`
  在 `check_locks` 抛 `ValueError`（`could not convert string to float`）⇒ **校验器整体崩溃 = 全项目红灯**。
  取锁用 `time.time()`，不要用 `datetime.isoformat()`（本会话自造，已改）。
- **`ImportAsset(ForceUpdate)` 修不了"导入记录坏"**：内容哈希未变时 Unity 直接复用旧 artifact。
  有效手段是 `CreateAsset` 覆盖同路径——实测 **GUID 不变**，既有引用不失效。
- **内嵌包资产不入 `AssetDatabase` 索引**：`FindAssets("t:X")` 对 `Packages/com.collin.*` 恒返回 0，
  但 `LoadAssetAtPath` / `IsValidFolder` / `AssetPathToGUID` 全部正常 ⇒ 判存在只能用固定路径加载（D-35）。
- **`m_Script: {fileID: 0}` + `m_EditorClassIdentifier: 包名:命名空间:类名` 是 Unity 6 内嵌包资产的正常写法**，
  不是坏引用；据此判"资产坏了"会误诊（真判据是 `GetMainAssetTypeAtPath` 是否为 NULL）。
- **端口与 PID 每次编辑器重启都会变**（实测 7800/23176 → 7801/10460）：会话开工先 `unity status`，
  别沿用上一轮的端口假设。
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
- **`script recompile` 期间 Pipeline 服务会短暂不可达**：`unity command ...` 报
  `No Unity Editor instances found with reachable Pipeline servers` **不代表失败**（域重载断连）。
  正解：轮询 `unity status` 直到出现 `ready`（同 PID 会回来），再读 `script recompile_status`；**不要重发 recompile**。
- **Unity 6 Terrain API 两处实测坑**（混合方案取证时撞上，脚本已删、结论未取到）：
  `TerrainData` **不是** `ScriptableObject` 派生 ⇒ `CreateInstance<TerrainData>()` 编译即错，须 `new TerrainData()`；
  `Apply(...)` 在 **`Terrain`** 上、不在 `TerrainData` 上。
- **`eval_file` 脚本禁止留"占位假调用"**：我曾写 `tr.ApplyEmbeddedTreePrefabsIfAny()` 当占位准备"下面再改真路径"，
  这类写法若编译侥幸通过就是**运行时炸**、不通过就是**假象取证**（H-47 同族）。宁可整脚本删掉重写。
- **探针取到"意外的 NULL/矛盾读数"时，先怀疑探针自身**：本轮 `field=NULL / chunks=0` 我一度判为"逻辑真断"，
  实际是**跨探针取到的状态串属于上一帧**（`_status` 是残留字符串）+ `FindAnyObjectByType` 可能命中别的实例。
  正解：把"所有同类实例 + 它们各自的字段"全列出来（本轮 `[z]` 探针一次列清 `CityTerrainView 实例数=1`），
  不要拿单个实例的单次读数下结论。
- **性能取证必须"逐句复刻调用组合"**：只测 `EnsureField`+S1a 得 8ms，与用户每笔实际执行的
  `new PipelineContext + 重注册 3 段 + MarkDirty(All) + Tick` = 5253ms 差 600 倍 ⇒ **测函数不等于测路径**。
- **本包测试解析口径**：`test run` 的 stdout 尾部还跟一列参数 ⇒ 必须 `json.JSONDecoder().raw_decode`；
  且 `Summary` 与失败详情在**同一次返回**的 `Results` 数组里（`Status != "Passed"`），
  **不必为取失败原因重跑一遍**（重跑一次要等全量执行，本轮白等了一轮）。

## 3. 未完成
- **显示层只做地形 mesh**。道路条带 mesh、建筑 prefab 实例、SceneView overlay（城心圆环、约束带可视化、光标读数）未做（01 §3.3/§4.4）。
- **「地表不同步 + 每笔读条」两个性能/同步问题已在 M4-j 修复并实测**（5253ms→4ms、增量重建生效、画面与数据吻合）。
  仍待用户实机复核的是**手感类**判据（EditMode 无从断言）：
  ①进入 ② 雕山水后**贴地预览环是否真的看得见**；②拖笔是否即时起山、左键是否**不再变框选**、
  **编辑是否还卡**（M4-j 的主验证点）；③Ctrl+Z 是否整笔回退、delta 是否跨域重载留存；
  ④面板是否还长出 `TerrainProfile 1/2/3` 副本。
  验证路径：面板点 **② 雕山水** → 直接拖笔 → 完事点 **③ 平整出地表**。
- **⚠️ 实机验证前置条件**：`CityTerrain` 实例此前被用户删过（探针实测 `内存 CityTerrain 数 = 0`）⇒
  **必须先点「创建地形实例（含显示层）」**（该按钮已自动建并绑雕刻层，H-51），再点 ②。
  找不到实例时面板不强弹模式（`SyncStepFromState` 的例外分支），否则会连"缺实例"提示都看不到。
- **校验器已转 `FAIL 0` 绿灯**（剩 2 项 WARN：本会话自持锁 `lock-active`；他包 `com.collin.stealthplanner`
  无 `Sample/`，非本会话产物）。演示场景经用户裁定已从 `Assets/Scenes/` 移入
  `Packages/com.collin.citymap/Sample/Scenes/TerrainTest.unity`（GUID 不变、零引用、复验可打开）。
- **河流下切层（S2/carve，01 §3.2 第 2 层）完全未做**：delta 与平整已接，河道 carve 还没有。
- **D-32 坡度三级退让只登记、未实现**：`hardRetreatSlope=18°` 与"早判 steepMask 置 NoBuild"写进 DESIGN D-32/12 文档，
  但 `TerrainProfile` 还没有该参数、代码里没有 `steepMask` 计算。
  **阻塞点如实说明**：S3/S4 至今**只有枚举、无实现体**，"让路网绕行"无处可接；等 S3 落地时一并做掩码写入，
  不空转新建未选的阶段。（我曾误把该任务标 completed，已回退 pending——状态也算证据。）
- **混合方案（Unity Terrain 做手感、自研场做生成后端）的成本未实测**：已给用户定性对比表，
  但 Terrain 侧写高度图/烘焙/反查的真实耗时**没测成**（探针两度踩臆造 API 后我主动停手，见 §4）。
  ⇒ 若用户要继续评估该方案，第一步是**先 grep 出 Unity 6 Terrain 的可用 API**再测，不要复用我这轮的失败脚本。
- 无 PlayMode 测试；真实 `Ctrl+Z` 端到端链路（A-17 完整形态）待接。
- `Sample` 小城与演示视频：M10。

## 4. 下一步（M4-j 已解卡顿与同步，先让用户实机复核手感与编辑流畅度）
1. **让用户实机验 M4-j**：重点问三件事——编辑还卡不卡（修复主目标）、抬升是否即时起山、贴地环是否可见。
   反馈回来按实机现象归因登记缺陷（连续三轮已证明：只有真跑才暴露 H-38~H-62）。
2. **S2 河流下切**（Modules/02）：接 01 §3.2 第 2 层（在 delta 之前），与山水笔刷同为"创作自然地表"环节。
   接好后自建/面板两条路径都要按 D-36 的分层判据复检一次（carve 变更该重跑什么，别又写成 `MarkDirty(All)`）。
3. **D-32 的 `steepMask` 早判**：待 S3 路网落地才有消费者；先加 `hardRetreatSlope` 参数 + 逐格坡度 + 面板"因坡放弃 N 格"。
4. ACCEPTANCE 续接编号 **80+**（M4-j 已用到 79）；代码/文档注释继续引用编号（`// 09 H-62`、`// DESIGN D-37`）。
5. **探针必须复刻"用户实际执行的调用组合"，不能只测单个函数**（H-57 教训：只测 `EnsureField`+S1a 得 8ms，
   差点反向得出"卡顿与噪声无关"；逐句复刻 `new ctx + 重注册 + MarkDirty(All) + Tick` 才量到 5253ms）。
6. **写代码前先 grep 核实引用**（H-47/H-58/H-62 三次教训：臆造 API、`OnEnable` 里假设注册表已就绪、
   误用粗粒度入口做精确断言）。文档里凡"由 X 机制保证"必须能在代码 grep 到 X 的检查点。

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
