# CHANGELOG — com.collin.citymap

格式：Keep a Changelog（https://keepachangelog.com）；版本号：semver。

## [Unreleased]

### Fixed
- **M4-o（用户裁定平整语义：烘焙式滤镜层，不污染手绘 / 不实时算 / 按钮驱动 + 城区参数进面板）**（本包 **141/141** Failed 0）：
  - **重构而非打补丁**：`SculptFilterAsset`（新，只含 Additive 差值）承接烘焙产物；
    `FlattenSolveStage` 拆为 `Run`（只重放：复位→叠手绘→叠滤镜）与 `Bake`（真求解）。
    「自动层不许覆盖人工层」从口头纪律变成**结构屏障** —— 两个资产分开后，误写手绘会立刻在 diff 与测试里可见。
  - **性能自纠（实测 49.6s → 3.35s）**：全图烘焙第一次实测 **49592ms**，根因是我新写的 `AddDelta`
    每次线性查块 + 整块 1024 格判空剪枝（292 万次写 ≈ 40 亿次操作）。改为「记住上一块」＋剪枝收口到
    `Compact()`（1428 块扫一次≈12ms，代价与写入次数无关）⇒ **3351ms**。
  - **两处我造的 bug，都被测试当场抓出（不是靠肉眼看画面）**：
    ① 融合循环里我加了第二道容差 `>0.005f` 才落滤镜 ⇒ 画面改了而滤镜没记 ⇒ 重放差 0.005，被 **A1_2 幂等**撞红
       （教训：容差只能有一个真源，落不落格由资产内部 `ZeroEps` 决定，求解器不得再设阈值）；
    ② 新鲜度我第一版用 `ManualVersion`（所有通道累计）⇒ **拨一下开关就自判"已过期"**、提示用户重烘，
       被新用例撞红 ⇒ 改为「内容通道序列 + 显式作废位」（`ParcelModel` 与开关共用通道，嗅探必然误报）。
  - **测试语义迁移（关键：防恒真空过）**：求解搬进 `Bake` 后，仍用 `Run` 断言求解结果的用例会变成
    <b>恒真的空测试</b>。已迁移 `A1_3 / A1_7 / Absolute_Height_Zone / Zero_Weight / Two_Instances`
    到测试侧 `BakeS1b`；`A1_2` 升级为**双判据**（重放幂等 + 重新烘焙幂等）；
    新用例把局部性换成更强的逐格不变量：**开关差异格数必须恰好等于滤镜落位**
    （`diffNotFromFilter==0` 且 `filterNotShown==0`，双向查），并带「画面变了但滤镜没记」的显式反例断言。
  - **面板**：新增「③ 平整滤镜（烘焙式）」按钮 + 三态提示 + 清空；城区参数区（城心/半径/内外缘）
    带**生效值**显示；烘焙只冲**挂起**脏位（`DirtyBits.All` 会把 4.8s 的 S1a 白跑一遍，M4-j 的成果不能还回去）；
    烘焙登记 `Undo`（不可撤销的重计算＝用户不敢点）。
  - **滤镜内容补齐（自然化，实现用户原话「消除明显的手绘痕迹」）**：`Bake` 先做
    **只软化人工层** `H' = H − t·(a − a_soft)`，其中 `a = Heights − BaseHeights`、`a_soft` 为其
    软化核半径窗内均值（积分图 O(1)/格）——**绝不碰 S1a 自然噪声**（否则"消除手绘痕迹"会顺手
    把山体自然碎纹一起抹平，用户看到的是"整张图糊了"）。面板补「软化核半径 / 软化强度」两滑条，
    并已混入 `ComputeValueKey`（有用例专门抓"新参数漏进值指纹"）。
    实测（8km / 400 万格 / 无地块纯自然化）：烘焙 **136ms**、滤镜落格 3700（只围绕雕刻区，
    远离雕刻的纯自然格滤镜无值=True 未被污染）、重复烘焙落格 3700→3700（幂等）、
    关滤镜 `Run` 重放求解产物=0 耗时 2ms（证明"不实时算"在运行时成立）。
    自纠一处设计错：跳过判据第一版只看本格 `a≈0` ⇒ 会在笔刷轮廓外留一圈锐利硬环
    （那正是用户要消除的痕迹本身），改为「本格与邻域都近零」才跳过，实测边界外 5 格被抹开 +2.4m。

- **M4-m（用户反馈「平滑效果和我预料的完全不一样」；1 和 3 都要、保边做成开关）**（本包 **141/141** Failed 0）：
  - **尺度错位是真因**：旧平滑核是 8 紧邻格均值，核宽恒 = 2×逻辑格距（实测 4m）——半径滑条只决定"哪些格参与"、
    从不决定"抹到什么尺度"。520m 波长的山体起伏每轮只衰减 0.09% ⇒ 视觉等于没反应；且均匀坡是该核的精确不动点，
    越想"抹缓坡"越不动。M4-m 起**平滑尺度 = 笔刷半径**（登记 **D-40**）。
  - **双核分流**：非保边 → **局部积分图精确窗口均值**（每格 O(1)、任意半径无混叠）；保边 → 32 点盘采样 +
    exp(−(Δh/σ)²) 高差加权（陡坎样本被忽略 ⇒ 只抹碎不削坡）。点采样实测有混叠（R=200 对 λ=60 残余 0.51，
    解析真值 0.041），故不作默认核，只留给 bilateral（其标准实现本就依赖局部高差门控）。
  - **斜面不动点**是两核共同的硬不变量（对称核）⇒ 平滑只吃曲率、绝不推歪做好的坡。
  - **端到端实测**：λ=60m 振幅残余 R=30→**9.7%**、R=60→2.7%、R=120→1.4%、R=200→2.9%（解析 sinc 同量级）；
    斜面磨一笔最大位移 **0.005m**；成本 R=200 十事件 92ms ≈ 9ms/事件（旧核每格 8 采样，新核 O(1) 不随半径放大）。
  - 面板：动作=平滑时新增**保边开关 + σ 滑条**（0.5~15m，默认 3m）与尺度说明；强度滑条上限按动作分岔
    （平滑 1 / 抬压 5）——旧版平滑拖到 1 以上毫无反应，属滑条语义错。
  - 新增 4 条核回归用例（sinc 解析判据 + 点采样混叠对照组；斜面三档窗宽不动；角落面积钳制；保边分离度）。
    自纠一处**探针侧测量错**：保边端到端数字出现 >100% 的荒谬值，查为我探针把格号与世界坐标混算，
    不作交付判据（保边正确性以 EditMode 用例为准）。
- **M4-l（用户反馈「平滑后噪音反而变大」，H-65 / D-39）**（本包 **137/137** Failed 0；含一次契约回归自纠）：
  - **根因＝笔划期间场不更新 + 每事件对旧场重复累加**：场取 `View.BoundField`，而刷新要等松手提交后走
    S1b→显示层链路 ⇒ 20 个拖拽事件读到的全是同一份旧场，平滑增量每次算出同一个数、每次 `AddDelta` 累加。
    隔离实测：±1m 棋盘原地磨 20 事件 → 累积 delta **−21 / +21m**（单次上限本应 ≈2m）= 噪音放大 20 倍。
    抬升/下压**不受影响**（位移积分语义本就逐事件累加，是正确行为）；定高/绝对目标类也不受影响（覆盖语义幂等）。
  - **修复**：`_strokeAcc` 记录本笔每格累计增量，`EffectiveHeight` / `NeighborMeanEffective` 让 self 与邻居
    都读「场 + 本笔累计」（界外邻居只读场不查表，防越界线性格号绕行）；`BeginStroke` 清表防跨笔污染。
    修后同一场景复测收敛到 **−1.003 / +0.997**。
  - **契约优先，撤销我的顺手改动**：取证时判 λ=1 为振荡临界而钳 `λ≤0.5`，随即撞红 **M4-d 既有契约用例**
    （`strength=1 一步到邻域均值`）——是**我为取证改行为，不是行为错了**。已撤销钳制、恢复原语义，
    振荡风险如实记进 `SmoothDelta` 注释与 D-39（真实 2D 含对角邻居时实测收敛；若实机出现对倒感，再连契约请用户裁定）。
  - 新增 1 条端到端回归用例（原地磨 20 事件必须收敛 + 抬升对照组防"没写入的假绿"）；
    自纠一处测试判据**方向写反**（棋盘相邻格增量应**异号**各自朝均值回归，第一版误写"同号=收敛"，被自己用例抓出）。
- **M4-k（用户反馈「拖较长距离后只有最后一截操作生效」，H-63 / D-38）**（本包 **136/136** Failed 0）：
  - **根因在提交侧、不在笔刷数学侧**（隔离探针实测）：一条 600m 长笔划，**写入侧完整覆盖**
    —— delta 非零 4068 格、格号 x[238,411]（起笔 20.8m / 中点 53.6m / 落点 33.3m，中段甚至是最大值）；
    但松手时 `MarkDeltaChanged(落点, 半径)` 提交的脏区是**以落点为圆心的 202m 盒**（世界 x[1499,1701]）
    ⇒ 笔划含半径的真实跨度 701m 中**只有 21.6% 落在脏盒内、78.4% 改了却不重建**
    ⇒ 用户看到的正是「只有最后落在编辑器上的那一截生效」。与 H-42「算错影响范围」同族，
    但这次是**少报范围**而非报错位置。
  - **修法三段**：`SculptStroke` 增 `AccumulateExtent / TryGetExtentWorld` 累积整条轨迹
    （与 `Collect` **解耦**——截断只该停"写入格数"，不该停"影响面"记账）；
    `StampRadius` 每步登记 `center ± RadiusMeters`（放在所有早退之后、循环之前 ⇒ 无写入不虚报）；
    `EndStroke` 按轨迹盒提交（取不到范围＝被阻断时才退回圆心口径），且**在 `End()` 之前**取范围。
  - **`MarkDeltaChanged(Bounds, perSideMargin)`** 新增重载：逐轴保留长短轴语义
    （**不得折成正方形取长轴**，那会把 600×101 虚报成 600×600），侧向外扩由调用方按笔刷半径传入。
    原圆心重载与语义**原样保留**（`SetDelta` 等调用点不受影响）。
  - **自查纠正一处（登记进 D-38）**：我第一版把侧向外扩写成 `MinAffectedRadius`(16)，落地后逐轴核对发现
    短轴会从旧口径 202m **缩到 133m** —— 长轴修好了、平整开启时的**侧向约束带反而开始漏重建**。
    教训：修"少报范围"的 bug 必须**逐轴**比对新旧口径，不能只确认新范围覆盖了写入区。
  - **新增 3 条回归用例**：轨迹跨步累积（含"新笔不得残留上一笔范围"）、
    端到端脏区覆盖轨迹且短轴不被拉方（带圆心口径的**防恒真对照**）、
    退化输入仍产出可用脏盒（零尺寸会被 `AddDirtyBound` 当无脏区丢掉 ⇒ 静默不重建）。
    中途一条用例失败暴露的是**我测试模拟得不如真实流程**（漏喂 `BeginStroke` 的起笔那一步），
    实现侧无误 —— 与 H-62 同族，已按真实流程补正。
  - 端到端实测终态：脏区 **802×202m**，长轴完整覆盖笔划含半径区间（x[899,1701] ⊇ [949.5,1650.5]），
    短轴 202 == 旧口径 202 ⇒ **侧向无退化**；面积 162 千 m² vs 旧 40.8 千 m²。

- **M4-j（用户反馈「抬升后地表不同步」+「每笔卡得要读条」的根因修复）**（本包 **133/133** Failed 0）：
  实测量化（8000m / 4m 逻辑格 / 2000²=400 万格 / 3969 显示 chunk，修复前 3 轮稳定）：
  **每笔 5253 / 5234 / 5529 ms → 修复后 4 ms**；耗时分解 `S1a=4860ms、S0=114ms、S1b≈0ms` ⇒
  卡顿不是"算法慢"，而是**每笔都在重跑噪声基底**。修复后执行阶段数 **3 → 1**（只跑 S1b）。
  - **H-57（阻断级）面板"未装配"使显示层无声回落到慢路径**：`_ctx/_runner` 是瞬态字段、域重载即丢，
    而序列化的 `_project` 存活 ⇒ `ActiveContext=NULL` ⇒ 自建路径每笔 `MarkDirty(DirtyBits.All)` 重跑 S1a
    （**违反 D-31/A-2「雕刻绝不脏 S1a」**）+ 新分配两张 400 万格数组（≈40MB）与阶段 scratch（≈50MB）
    ⇒ 大对象堆 ⇒ 阻塞 GC = 用户看到的读条。登记 **D-36**。
  - **H-58（自造，已当场纠正）`OnEnable` 直接 Bind 会让画面全白**：实测此刻场景对象尚未进
    `IntentRegistry` ⇒ `FindTerrainInstance()` 返回 null ⇒ `SyncStages` 只注册得出 S0（`runner 阶段数=1`、
    `ctx.HasSlot(S1a)=False`）⇒ 面板已接管、显示层放弃自建、却永远拿不到高度场 ⇒ **比修复前更糟**。
    正解：标记 `_needsFirstBind` + 更新循环里"探测到实例才装配"，找不到下一帧再试（探测失败时**不动
    `_stageKey`、不碰 runner**，否则每帧重注册一次阶段 = 自造 churn）。
  - **H-59 自建路径按「变化来源」分层重算**：项目变→重建上下文（Mask/场数组尺寸绑死格网，必须全量）；
    仅 Profile 变→复用上下文只重跑 S1a；**仅实例版本变（雕刻）→只重放 delta + S1b**。
    `PipelineContext` 与阶段对象长期复用，消除每笔几十 MB 大对象分配。
    判据用**哨兵法**（往 `BaseHeights` 种 `123.456f`，只有 S1a 会整表覆盖它）——噪声是确定性的、
    重跑得逐位相同，所以"基底没变"证明不了没重跑，**必须靠哨兵存活 + 上下文对象身份不变**两条同时成立。
  - **H-60「算得出但画不出」（反馈①的真根因）**：`ComputeConfigKey()` 不含 `_terrain.ManualVersion`
    （实测落笔后指纹逐位不变 `14071847608170291442`）⇒ 旧版只认 `cfgChanged` 一条判据 ⇒ 高度场抬了
    **181.11m**（`BaseHeights` 全图最大才 5.36m）而一张 mesh 都不重建。登记 **D-37**：
    改由 `_ownDataChanged` 驱动重建 + 按笔刷脏盒 `RebuildRegion`；**刻意不把实例版本补进 configKey**
    （那会触发 `RebuildAll`，实测全量 289ms vs 增量 0~2ms）。
  - **H-61 脏区从未搬进上下文**：`MarkDirty(dirty)` 单参不带 bound ⇒ `UnionOfDirtyBounds()=null` = 全图，
    增量重建形同虚设。补 `_ctx.AddDirtyBound(Tracker.LastDirtyBounds)`（须在 `ConsumeDirty` 之后立刻取，
    该入口会把 `LastDirtyBounds` 复位）。修后实测状态串：`增量重建 9/9 chunk（脏盒 202×202m，全图 3969）`。
  - **新增 3 条回归用例**（`TerrainViewTests`）：雕刻须驱动 mesh 抬起（取"最近顶点 y"而非全图极值，
    避免被自然噪声峰值淹没成假红）、雕刻不得重跑噪声/重建上下文（含"改噪声须覆盖哨兵"的**防恒真对照组**）、
    笔刷脏区须是笔刷量级且可被上下文取到。
  - **测试口径自纠错（H-62）**：我第一版用 `UndoHistoryTracker.MarkIntent()` 断言"只脏 Flatten"，
    红了。查清是**测试入口选错**而非实现有 bug——`MarkIntent` 是粗粒度入口（并对象**当前全部通道**），
    rig 建时 `SetProfile` 已 bump 过 `TerrainModel` ⇒ 必然带上 `Terrain`；面板真实驱动是 `Scan()` 基线差分。
    改为"传播表 `DirtyOf(DeltaStroke)` 精确断言 + `Scan()` 真实口径断言"两轨，并把误判写进注释留痕。
- **M4-i（笔刷不可用的四处断链，用户反馈①②根因修复）**（本包 **130/130** Failed 0，校验器 **`FAIL 0`、结论: 绿灯**，
  `consoleErrors=0 / consoleWarnings=0 / seeded=True`，`RECOMPILE failed=False errors=0`）：
  - **H-50 门禁真源错位（反馈①②同源）**：笔刷接管判据原先读 `CityWindow.CurrentMode`，那是
    `EditorWindow` **静态**字段——域重载（改代码 / 重开场景 / 进 Play）后归零，而 Unity 用布局恢复
    窗口实例时不回填静态字段。探针实测 `_instance = NULL(面板未开)` 而 `CurrentMode = Inspect`，
    但用户面板明明处于雕刻模式 ⇒ 笔刷一进来就 `return`，鼠标事件原样放行给默认工具
    （实测 `activeToolType = UnityEditor.MoveTool`，空白处左键即框选）。
    正解（**DESIGN D-34**，用户裁定）：新增 `CityTerrain.SculptToolActive` 序列化字段作真源，
    笔刷只读它；面板 `_mode` 每帧由真源回推，`OnEnable` 补 `_instance = this` 重绑。
    写它**不 bump 意图通道**，由 `Sculpt_Tool_Active_Is_Pure_Tool_State_No_Intent_Bump` 钉住。
  - **H-51 雕刻层从来不存在**：实测磁盘上 `SculptDeltaAsset` 数量 = **0**、实例 `Delta = NULL`
    ⇒ `BeginStroke` 判空即 `_blocked`，而提示只 `Debug.Log` 到控制台，用户看到的现象是"拖了没反应"。
    正解：`CreateTerrainInstance` 与进入 ② 雕山水时自动建并绑 `Sample/Sculpt/SculptDelta.asset`；
    阻断原因改由 `SculptBrushTool.BlockReason` 呈现为**面板黄字**。
  - **H-52 副本资产反复滋生的机制**：`AssetDatabase.FindAssets("t:TerrainProfile")` 对**内嵌包资产恒返回 0**
    （探针实证：资产存在且有路径，仍返回 0），旧代码以"找不到"当"不存在" ⇒ 每次创建实例都走新建分支，
    `GenerateUniqueAssetPath` 追加 ` 1/2/3` ⇒ 上一轮删掉的 6 个副本长回来了（日志 seq 18696 实证）。
    正解（**DESIGN D-35**）：固定路径 + `LoadAssetAtPath`，创建不加唯一后缀。
  - **H-53 光圈高度取不到**：预览环取样只读 `CityRuntime.ActiveContext`（仅面板驱动分支有值），
    自建路径 / 域重载后为 null ⇒ 环退化画在 y=0 又被 ±18m 起伏埋掉。正解：新增
    `CityTerrainView.BoundField` 只读暴露其真正用于生成 mesh 的场，笔刷与之同源（光圈贴的就是你看到的那张皮）。
  - **附带修复（H-54）包内资产导入记录损坏**：`Sample/Profiles/TerrainProfile.asset` 的
    `GetMainAssetTypeAtPath` 返回 NULL 而**同内容复制文件同样 NULL**，Unity 自身 `CreateAsset` 产物可加载
    ⇒ 判定为该 GUID 的 Library 导入记录坏（非文件内容坏；`ImportAsset(ForceUpdate)` 因内容哈希未变而无效）。
    处置：`CreateAsset` 覆盖同路径重建，实测 **GUID 不变**（引用不失效）、类型恢复可加载。
  - **性能细节**：笔刷格网解析改为按激活实例身份惰性重解析（原为每事件全表扫描，H-10 反模式）。
  - **工作区合规（经用户裁定）**：`Assets/Scenes/TerrainTest.unity` → `Sample/Scenes/`（`MoveAsset`，
    GUID 不变、零外部引用、复验可正常打开），校验器 `assets-layout` 由 FAIL 转 **ok**；
    根目录 5 个上一会话残留 `.tmp-*.json` 经用户裁定删除。

### Added
- **M4-h（显式三步步骤条 + 体验副本清理）**（本包 **129/129** Failed 0，校验器 **`FAIL 0`、结论: 绿灯**，
  `consoleErrors=0 / consoleWarnings=0 / seeded=True`，`RECOMPILE failed=False errors=0`）：
  - **步骤条 `CityWindow.DrawWorkflowStepBar()`**：`① 地图参数 → ② 雕山水 → ③ 平整出地表` 三个按钮，
    点一步＝把该相位该有的状态设好（② 关 `FlattenEnabled` + 进雕刻模式；③ 开闸门 + 回检视模式）。
    **相位是便捷动作、不是门禁**（00 A-24「任何阶段工具在任何时刻可用」）：其它工具与参数仍随时可用。
    高亮由真源 `FlattenEnabled` + 当前模式回推（`SyncStepFromState`），**避免"面板显示第③步、
    闸门实际还关着"的两套状态打架**——单一真源仍是 D-33 的阶段闸门。
    Editor GUI 无法 EditMode 断言（反射调 `OnGUI` 抛「只能在 OnGUI 内调用」，项目既有实测）
    ⇒ **观感与手感待用户实机确认**，不虚报已验证。
  - **体验副本清理**：`Sample/Profiles/TerrainProfile 1/2/3.asset`（用户点"新建"反复产生，
    三个 GUID 经核验**无任何场景/资产引用**、未入库）按**用户裁定**走 `AssetDatabase.DeleteAsset` 删除，
    真源 `TerrainProfile.asset` 保留。删后 `assets-layout` 由 FAIL 恢复 **ok**、校验器整体转绿。
  - 过程中的两处自我纠错：一次 `Write` 落点被污染成项目根散文件 `.tmp-insp`（已 `mv` 回缓存目录、根目录清干净）；
    一次 bash 命令因引号嵌套被拆坏（改为写脚本文件执行，此前已验证该方式稳定）。
- **M4-g（平整改为阶段闸门介入 + 笔刷范围贴地可见）**（本包 **129/129** Failed 0，校验器**绿灯**
  `FAIL 0`，门禁 `consoleErrors=0 / consoleWarnings=0 / seeded=True`，`RECOMPILE failed=False errors=0`）：
  响应用户实机三条反馈（范围看不见 / 抬了没反应 / 起伏是不是真的），并按用户**否决**我初版"逐格退让"
  后的正确方向重做（DESIGN **D-33**）。
  - **阶段闸门 `CityTerrain.FlattenEnabled`（默认开）**：关闭时 S1b 在 `CommitBaseAsWorking()+Delta.ApplyTo()`
    **之后、收集约束之前**整段短路（`Outputs=0`、零改动），工作数组停在"噪声基底+人工雕刻"的**意图地表**
    上直接交出；开启时才在定稿的意图地表**之上**跑平整整理。
    **根因**：雕刻期平整照常跑时，城内 8× 强约束的拟合目标（读意图地表）会把刚抬的山当场压回平面
    ⇒ 这才是用户反馈②「笔刷抬了没反应」的**结构性**原因，不是幅度太小。
    实测判据 `Flatten_Off_Is_Phase_Gate_Not_Per_Cell_Waiver`：关 → 抬 25m 原样保留、约束 **0** 条、重跑仍 25m
    （短路位于复位+重放之后 ⇒ 关闭态同样幂等）；开 → 约束 **9** 条、改动 **76729** 格。
  - **笔刷预览环贴地描边**（用户反馈①）：48 段折线逐段取 `GetHeight(x,z)+0.08f` 贴地形绘制外圈与核心圈，
    并把绘制**移到落笔前置条件之前**——旧版画在 y=0 平面被 ±18m 起伏埋住，且数据不全时在画环前就 return，
    结果环与提示都没有。**观感仍需用户实机确认**（SceneView 绘制无法 EditMode 断言，不虚报）。
  - **起伏来源澄清（用户反馈③）**：地表起伏是**真实几何**（S1a 噪声，默认幅度 18m / 波长 520m），
    材质只把坡度染成绿→黄→红帮助读高低，并未"画出"起伏。
  - **撤销上一版错误方向**：我曾实现 `SculptDeltaAsset.MarkCovered()` + S1b 逐格退让掩码，
    被用户指出机制理解反了（要的是**时序隔离**而非平整让位），已全部撤净（无残留引用）。
  - **同时纠正一处文档不实陈述**：12 §1 原写"人工格由 `ManualLocked` 豁免、不被自动平整抹回"——
    **该机制不存在**（`ManualLocked` 只是地块掩码词表的一位，S1b 从不检查它），正是我走偏的源头，已更正留痕。
  - **违规资产处置**：`assets-layout` 抓到 `Assets/Scenes/testscene.unity`（非本会话产物），
    删除前核验"不在构建场景列表、无其他资产引用、未被 git 跟踪"，经**用户裁定**后由资源接口删除；
    删除后编辑器抛 6 条 `ReloadScene ... without a SceneAsset`，查明为内存态已随资产删除自动卸载、
    报错源消失后复跑归零。另一会话打开的 `StealthSample` 场景**未做任何改动**。
- **M4-e + M4-f（场景雕刻笔刷：交互壳 + 面板参数区）**（本包测试两次复跑均 **128/128** Failed 0，校验器**绿灯**，
  门禁 `console` 达 0/0 或全部命中 §1.8 已批准豁免）：
  - `Editor/SculptBrushTool.cs`（提交 8ab4689）：`SceneView.duringSceneGui` 静态挂接，**仅"雕刻"模式接管鼠标**、
    其它模式原样放行；预览环画在 y=0 平面（纯几何求交，不依赖碰撞体——派生 mesh 是不入库隐藏物体）；
    落笔走 `SculptBrush` 纯数学（固定步长/世界位移/carry 余量），写入 `SculptDeltaAsset`，
    松手 `MarkDeltaChanged(笔刷中心, 半径)` 交由 D-31 链路刷新（**不在壳内重算/重建**，编辑态与重生成同一条路径）。
    一笔划 = 一个 `Undo.RecordObject`（A-1-6）；超格数上限提示截断而非静默（01 §5.4）。
    本轮只接**有输入来源**的动作（抬/压/平滑/定高到起笔点），反色/清除等待数值框输入源，未接线动作明确拒绝并提示。
  - `CityWindow.DrawSculptPanel()`（提交 f70fcc2）：半径/强度/动作只在雕刻模式显示；动作下拉**只列已接线四个**。
  - **H-47（自查缺陷，编译当场暴露）**：交互壳初版混入多处**臆造 API**（`CityRegistry` 不存在、
    `CellSizeMeters`/`HeightAtWorld` 名称错、`GetSlot<CityGrid>`——CityGrid 是**值类型**，槽位约束 `where T:class`
    直接编译失败）+ 一句残缺代码。整文件按逐条核实过的真实签名重写；教训与 H-46 同族：
    **引用不核源头就写，编辑器只会比 grep 更诚实**。
  - **端到端实测状态（如实）**：反射驱动笔划时场景内**没有 CityTerrain 实例**（体验会话已被域重载重置、
    派生物体不入库），`terrain=NULL` ⇒ "落笔→delta 写入→重算刷新"的**实机闭环尚未跑通**，
    当前证据链 = 数学内核 9 条单测 + 编译/无回归；**待你在场景创建实例后实测**（见 ACCEPTANCE §M4-e-1）。
- **M4-d（笔刷数学内核，A-1-5 可执行化）**（本包 `test run --mode editor --filter_type assembly` Total **128** / Failed 0，
  较 M4-c 增 9 条断言）：
  - 新增 `Runtime/Core/SculptBrush.cs`：`SculptBrush`（纯函数数学）+ `SculptStroke`（笔划状态机），
    **放 Runtime 而非 EditorTool** —— 否则"60Hz 与 144Hz 机器产生相同 delta"只能人肉试，
    而帧率相关 bug 正是"我这台没问题"的典型。
  - 三条硬约定：固定步长积分（**绝不乘 deltaTime**）、位移取**世界量**非屏幕量（DPI/缩放会改手感强度）、
    不足一步的位移**跨调用留存**（丢了余量 ⇒ 慢速精细雕刻完全不出效果）。
  - **实现侧修掉一处真实缺陷**：`core>radius` 非法配置时"核心内全强"分支吞掉半径截止，
    会画出比笔刷还大的影响区 ⇒ `core` 钳进 `[0, radius]`。
  - 首跑 2 条失败均已归因、未用放宽阈值蒙混：一条**我的断言期望写反**（`dist<core` 本应全强），
    一条**测试口径脆弱**（`0.01f` 二进制不精确致累加落在 1.99999m，改用 `0.25f` 做严格断言 + 慢速场景只断"> 0"）。
  - **如实上报的前提缺失**：`S3_Road`/`S4_Parcel` 目前**只有枚举与脏位、无实现体**，
    故用户裁定的"steepMask 在 S3/S4 内现算"**暂无落点**；本轮改做不依赖它的场景笔刷交互壳，
    steepMask 顺延至路网阶段落地时实施（不擅自新建未被选择的阶段）。
- **M4-c（雕刻接入管线，DESIGN D-31 落地）**（本包 `test run --mode editor --filter_type assembly` Total **119** / Failed 0，
  较 M4-b 增 3 条接线回归断言）：
  - 用户裁定登记为 `DESIGN` §7 **D-31**（雕刻脏集合由「仅 S6」改为 **S1b + S6**，仍不脏 S1a）
    与 **D-32**（坡度三级退让：≤`maxParcelSlope` 轻整理 / 其间压平 / ≥`hardRetreatSlope` 18° 不压、削陡角留缓、
    陡格置 `NoBuild` 让路网绕行，且**必须早判**）；§3.2 脏传播表雕刻行同步改写。
  - **接线前查出的静默陷阱**：`FitFromBase` 读的是 `BaseHeights`（`HeightField.cs:314`），而 delta 按契约只进 `Heights`
    ⇒ 照字面接线会让雕刻**对目标平面完全不可见**，且不报错、只在下一次压平时把山悄悄抹掉。
    采用口径：S1b 起点 `CommitBaseAsWorking()` → `delta.ApplyTo()` → **拟合读此刻的"意图地表"**（自然+人工、尚未平整）
    → 融合写 `Heights`。不违反 §2.4 防漂移不变量（复位已抹掉上一轮平整结果），前置条件写进 `FitFromBase` 注释。
  - `IntentKind.DeltaStroke` + `PipelineTypes` 双表同步（`Flatten | Building`，刻意不含 Terrain/Parcel/Road）；
    `CityTerrain.Delta` / `SetDelta` / `MarkDeltaChanged(center, radius)`——**脏区域用笔刷实际位置**，
    不用实例 transform（实例常在原点 ⇒ 脏盒永远落原点 ⇒「雕了别处、画面不动」，与 H-42 同族）。
  - **H-46（自造，且是本轮唯一进了仓库的错误）**：M4-b 提交 `c69fbe4` 里我 4 次引用"推翻 D-14"为规则级变更背书，
    而 **D-14 实为"显示网格分辨率解耦"**，"雕刻仅脏 S6"住在 §3.2 表且**从无编号**。
    后来者按 D-14 查会查到 mesh 分辨率，误导是双向的。处置：登记 D-31/D-32 + 全量纠正错引 + 复核剩余引用合法。
    纪律：**引用规则编号前先 grep 该编号本体确认它说的是什么**；编号写错比不写编号更糟，因为它看起来像有出处。
  - 判别式取证：`[M4c] 未雕刻 0.106 → 雕刻后 20.124（自然 0.023，抬 20m）`——60m 人工区内整区抬 20m，
    目标平面跟着起来；M2 两条既有断言数值与上轮一字不差（无回归）。
  - 门禁：编译 `failed=False`；`console clear`→重跑→`consoleErrors=0/consoleWarnings=0/seeded=True`；
    `verify_project.py` **`FAIL 0`、结论: 绿灯**（他包 stealth 锁已被其会话续心跳，本轮无需外部裁定）。
- **M4-b（参数定案 + 雕刻 delta 层数据结构）**：
  - 用户答复 12 提案两参数 → **`hardRetreatSlope=18°`**、陡区**只削陡角保留缓部分**；12 文档定稿为"已批准"，
    并补齐"削角保缓"的可实现口径（逐格 NoBuild 由坡度场判、地块保留非 NoBuild 连通子区、
    S1b 拟合样本域 = 可建子区而非原多边形全境）。
  - `Runtime/Core/SculptDeltaAsset.cs`：人工雕刻层数据模型（01 §2.3）。稀疏 chunk 存储、
    `Additive`（增量）/ `Absolute`（目标高程，用 NaN 哨兵非 0）、`ApplyTo` 重放**只进 Heights 绝不进 BaseHeights**
    （防"把上轮人工结果当输入"的正反馈漂移，01 §2.4）、亚容差残差吸附 + 全零条目剪枝（01 §9 资产膨胀）。
    **只交付数据结构与重放，不接管线**——接管要动 `DESIGN` §3.2 脏传播表的「雕刻仅脏 S6」原规则
    （该条当时无编号；后经用户裁定登记为 **D-31** 并在 M4-c 完成接线），属规则级变更须先经批准（§10）。
  - **H-45（自查抓到的设计缺陷）**：首版 `ApplyTo` 单遍按插入序遍历 ⇒ 同格混用 Absolute+Additive 时
    结果取决于用户操作顺序（7 或 10 的不定值）。改两遍确定序（先 Absolute 赋值、后 Additive 叠加），
    测试断言"正序与反序插入结果相同"。
  - 新增 `Tests/Editor/SculptDeltaTests.cs` **16 条**（本包 100→**116**，`test run --mode editor` Failed 0）。
  - 本轮两处自造编译错误（`CS0136` 变量作用域 / `CS1003` 字符串内嵌引号）按 §1.9 一经发现即修；
    第二处是同一错误模式**本轮第二次** ⇒ 从"改报错行"升级为**机械全扫**（逐行统计代码区引号奇偶）后才重编译。
  - 门禁：编译 `failed=False`；`console clear`→重跑→`consoleErrors=0/consoleWarnings=0/seeded=True`；本包 **116/116**；
    整体唯一 FAIL 仍是他包 `com.collin.stealth.lock` 超时（非本会话，§3 不代删，沿用用户既定"现在提交、继续持锁"）。
- **M4-a（材质补齐 + 资产归位 + 工作流重排提案）**，响应用户实机体验的两条反馈：
  - **修复"地表没有材质，看起伏比较累"**（用户反馈①，根因是 M3 交付缺口）：显示层已写顶点色坡度带，
    但回退链 `URP/Lit → Diffuse` **都不读 `COLOR` 语义** ⇒ 色带白写；01 §3.3 要求的"包内 URP-compatible 材质"漏交付。
    补 `Sample/Shaders/CityTerrainVertexColor.shader`（单 pass 顶点色×兰伯特，实测 `isSupported=True`；
    include 逐个核对真实存在，首版引用不存在的 `ShadowCasterAmbient.hlsl` 会致着色器编译失败污染控制台，已删）
    + `Sample/Materials/CityTerrainVertexColor.mat`（`_Ambient=0.45`，由编辑器生成而非手搓 YAML）；
    回退链改 **引用 → 自带 shader → URP/Lit → Diffuse**。
    **端到端实测**：绑定项目重建 → `显示 3969/3969 chunk · 逻辑 2000²格 @4m · 显示 16m · chunk 128m`，
    块渲染器实际 `shader=Collin/CityMap/Terrain VertexColor`。
  - **H-44（本会话自造，校验器 `assets-layout` 抓到）**：`FindOrCreateProfile()` 原静默往 `Assets/Collin/`
    建资产，违反 §0「插件内容须进包」，且用户点「创建地形实例」时已在磁盘留下违规目录。
    改固定进 `Packages/com.collin.citymap/Sample/Profiles/`（stretch 同构先例）；**不改成弹保存框**——
    `SaveFilePanelInProject` 只能落 `Assets/`，弹框反而把违规变成"用户自选"。
    已生成资产用 `AssetDatabase.MoveAsset` **保留 GUID 迁回包内**（场景引用不断），校验器转 `[ ok ]`。
  - **面板代建对象的自洽性缺陷**：`CreateTerrainInstance()` 未绑 `projectForStandalone` ⇒
    面板一关（权威上下文消失）即 `无权威上下文且未绑定自建项目 / chunks=0` 永久空白。实测取证后补绑定。
  - **`Docs/Modules/12-TerrainWorkflow.md`（提案，待批准、未动代码）**：用户裁定三段式工作流
    （① 仅参数 → ② 平面+山水雕刻+噪音 → ③ 平整成地表）+ **三级坡度退让**。
    结论：**与 01 §3.2 重放契约不冲突**（"雕刻先于平整"本就是契约第 3→4 步），要改的是里程碑次序
    与新增 `hardRetreatSlope` 硬退让档；关键发现是**退让判定时机不能放 S1b**（路网在其之后已排定，
    退让形同虚设）⇒ 必须"早判 steepMask 置 NoBuild + S3/S4 读之绕行"。含 2 个待用户定的参数。
  - 门禁（M4-a 复跑）：编译 `failed=False`；`console clear`→重跑→**`consoleErrors=0/consoleWarnings=0/seeded=True`**；
    本包 **100/100**；`assets-layout` 转绿。唯一 FAIL 仍为他包 `com.collin.stealth.lock` 超时（§3 不代删，如实上报）。
- **M3-b（Modules/01 面板实时链路：改参数即时反映 / A-0-3 / 01 §4.4）已实现并自测**
  （本包 `test run --mode editor --filter_type assembly --filter com.collin.citymap.Tests` Total **100** / Failed 0，
  较 M3 增 1 条 `Value_Key_Covers_Every_Tunable_Field`）：
  - `TerrainProfile.ComputeValueKey()`：地形参数**值指纹**（20 个可调字段全量覆盖、1e-4 量化、纯函数无副作用）。
    用于面板的 **Inspector 直改第二检**——用户直接改 SO 字段不调 `SetXxx()` ⇒ 序列号不变 ⇒ Tracker 看不见
    ⇒ "拖了参数地形不动"的静默失效；面板每帧比指纹补提交。
  - `CityWindow.DrawTerrainProfile()`：参数控件区（起伏/波长/八度/增益/倍频/域扭曲/城内平整度/地块坡度/
    自由权重/内切/外延/自动地块/冻结细节），全部走 Profile 显式写入口（Controller 只写 Model，§0.1 原则2），
    并含"创建地形实例（含显示层）/新建 Profile"入口。
  - `CityWindow.DetectDirectProfileEdit()`：值指纹第二检，**先比 `ManualVersion` 区分来源**
    ——显式入口已 bump 则只同步基线，避免面板滑条被双重提交（自查抓到的自造缺陷，与 H-30 按通道记账同构）。
  - **取证边界如实标注**：`Value_Key_Covers_Every_Tunable_Field` 断言数据地基；GUI 滑条手感**无法 EditMode 自动断言**
    （反射调 OnGUI 抛「只能在 OnGUI 内调用」，项目既有实测），须用户实机确认。
  - 交锁门禁：编译 `failed=False`（三侧 dll 新鲜度核验）；`console clear`→静默 60s→重跑→`console status`
    **`consoleErrors=0/consoleWarnings=0/seeded=True`**；本包 **100/100**；校验器 **`FAIL 0`、结论: 绿灯**。
    门禁期间曾被他包 `com.collin.stealth` 的 7 条 `CS0618` 警告与一条 Unity 内置 `services-gateway` 瞬时 error
    挡红（均非本包产生），本会话全程**只持锁等待 + 如实归因、未改他人文件 / 未代删其锁 / 未塞 allowlist**；
    stealth 会话自行修复后最终复跑转绿，无需任何带红提交。
- **M3（Modules/01 显示层：chunk mesh + 面板实时接线）已实现并自测通过**（本包 `test run --mode editor
  --filter_type assembly --filter com.collin.citymap.Tests` Total 99 / Failed 0，
  新增 `Tests/Editor/TerrainViewTests.cs` 15 条显示层断言；全项目 `test list` → 141）：
  - `Runtime/View/TerrainView.cs`：显示层**纯几何**构建（不触碰 Unity 对象、不引用 UnityEditor）。
    顶点按 `displayStep` 取解析网格线、法线走 `TerrainField.GetNormal` **同口径**（禁用 `RecalculateNormals`）、
    UV = 世界 XZ / tileScale、顶点色 = 坡度带；逐 chunk 独立几何 + 脏区→chunk 最小映射 +
    同拓扑局部更新（`SetVertices/SetNormals/SetUVs/SetColors`）/ 拓扑变化才重建 + 超 65k 顶点自动升 32bit 索引。
  - `Runtime/View/CityTerrainView.cs`：场景侧显示宿主（`[ExecuteAlways]`）。两条数据路径（面板驱动 / 自建 S1a→S1b）、
    **一条构建路径**（编辑态与 Play 态画面必然一致）；chunk 物体与 mesh 一律 `HideAndDontSave`（派生态不入库）。
    `CityRuntime` 静态桥：面板 Commit → 提交序号驱动增量重建，杜绝"两份世界"。
  - `Editor/CityWindow.cs`：**首次把真实阶段接进 Runner**（M1/M2 交付时 Editor 目录无任何 `_runner.Register`，
    "重新生成"只跑 S0 ⇒ 数据层全绿但场景永远为空）。`SyncStages()` 注册 S0/S1a/S1b（身份指纹 = Profile×CityTerrain 实体 id）；
    地形实例查找走 `IntentRegistry`（不用 `FindFirstObjectByType`，避 H-10 反模式）；
    `CommitAndNotify()` **在 Tick 前取脏区**（Tick 后会 `ClearDirtyBounds`）。
  - 菜单入口统一收进 `Tools/Collin/CityMap/`（用户 2026-11-03 要求：工具栏**不单开顶层 Collin**，与 stretch 包对齐）。
- **实现轮抓到并修复 5 处空洞**（详见 Modules/09 第七轮）：
  - **H-38**：三角形绕序凭直觉写 `(a,b,d)+(a,d,c)` ⇒ 几何法线点积 **−0.992**、整片地表**背面朝上**（从天空看什么都没有）。
    右手定则重推改 `(a,c,b)+(c,d,b)`。
  - **H-39**：同拓扑局部更新误用裸缓冲流 `SetVertexBufferData` ⇒ 抛 `copy 1452 bytes into a **0 byte buffer**`。
    改 `SetVertices(...)` 系。**冷启动走全量路径看不出此错**，必须有覆盖增量路径的用例。
  - **H-40**：显示顶点取在图幅角网格线 ⇒ 采样与**中心差分探针**双双越界被 clamp，顶点法线偏差 16.25°。
    **第一轮修法方向错**（把缺陷写成分档断言"解释"掉）；重推后钉死探针安全域 = 四边各内缩**一个逻辑格**，
    收敛成"对每一个顶点做绝对比较"。纪律：**分档/放宽阈值的断言，先怀疑实现**。
  - **H-41**：`SnapRatio` 用 `RoundToInt` ⇒ `100/8=12.5` 归整依赖 midpoint 规则、实心得 96m **< 请求值**，
    跨机器可能切出不同 chunk 数。改 `CeilToInt`。
  - **H-42**：`ChunksTouching` 外扩余量误取"一整个 chunk" ⇒ 点状脏盒命中 9 块、增量重建收益归零。
    改一个显示步（包围盒已含过渡带）⇒ 命中 1 块。
  - **H-43**（我的断言口径错）：把坡度色带的 r/g 分量当单调量断言。先算三锚点（平 r82g115 / 土黄 r184g158 / 陡 r158g56）
    才知 r、g 均非单调，唯一单调的是**红绿对比 `r−g` = −33→26→102**。判据写错时会本能去改设计凑绿——纪律：**先算术确定哪个量真单调再写断言**。
- 交锁三项门禁（菜单改动重编译后复跑）：`script recompile_status` `failed=False`；`console clear`→重跑本包全量→
  `console status` **`consoleErrors=0/consoleWarnings=0/seeded=True`**；`verify_project.py` **`FAIL 0`、结论: 绿灯**
  （本会话内校验器整体首次转绿；此前因他包过期锁判红）。
- 流程事实沉淀：`test run --filter_type assembly` 是多会话共用编辑器时唯一干净的取证方式；
  `Summary.Total=0` 需区分"mode 传错"与"编辑器在 Play Mode"（后者看控制台）；二维向量第二分量承载世界 Z 轴、是 `.y` 不是 `.z`。
- **M2（Modules/01 数据层：S1a 基底噪声 + S1b 平整约束）已实现并自测通过**（`test run --mode editor` Total 84 / Failed 0，
  新增 `Tests/Editor/TerrainDataTests.cs` 28 条地形断言）：
  - `Runtime/Core/CityNoise.cs`：纯 C# 确定性噪声内核（value noise + fbm + domain warp，splitmix64 派生子种子），
    **不依赖 Unity PerlinNoise**（后者跨平台/版本不保证逐位一致，会击穿"同种子同图"地基）；全纯函数、无静态可变态。
  - `Runtime/Core/HeightField.cs`：`TerrainField`（Heights/BaseHeights + 量化往返）、`ConstraintStrength`
    （**Parcel=8 / RoadCrown=4 / WaterBuffer=1** 分级 + 核心贴合度）、`PlaneConstraint`（`InsetMeters`/`ExtendMeters`/
    `Exact` 硬覆盖，`MaxInsetFor/ComputeCoreHalf` 单一算法真源）、`PlaneFitting.FitFromBase`
    （**坡向取最小二乘、水平取中位数**以抗离群）。
  - `Runtime/Core/TerrainBaseStage.cs`：S1a。`TerrainProfile` SO（噪声参数 + 城内平整度旋钮 + 约束求解设置），
    `ResolveUrbanCenter` 回退图幅中心，压幅公式 `ampFactor*(amp*baseN + detailA*detail)`。
  - `Runtime/Core/FlattenSolveStage.cs`：S1b。加权融合 `h=(h·Wfree + Σ wᵢ·planeᵢ)/(Wfree + Σ wᵢ)` + 硬覆盖模式，
    scratch 缓冲改**实例级**（避免跨上下文污染）。
  - `Runtime/Core/CityTerrain.cs`：场景组件（手动平整区 + 自动地块开关），`EnumerateAutoTiles` 按 `pitch` **留街道间隙**。
- **实现轮抓到并修复 6 处空洞**（详见 Modules/09 第六轮）：
  - **H-31**：`urbanCenter` 写死 `(1000,1000)` → 小图幅城心落图外、`urbanFlatness` 旋钮完全失效。改哨兵默认回退图幅中心。
  - **H-32**：拟合截距是 LSQ 面在**样本均值点**的高程，被当成区域中心高程用 → 目标面整体偏移实测 1.0m。坡向偏移改解析式。
  - **H-33**：中位数抗离群的设计意图被 LSQ **旁路**（水平仍取均值口径）→ +395m 单点离群致目标高程偏 0.816m。改水平取中位数。
  - **H-34**：缺 `constraintInner`（核心=整块矩形）→ 相邻地块共享边界同时全权重、两独立平面被平均成 **4.79°→65.48° 陡坎**。
    增 `InsetMeters`（上限=较短半边 40%）。
  - **H-35**：自动地块无街道间隙 → 内切后仍 27.92°（两独立平面高差须在有限距离内消化，几何无解）。按 `pitch` 留街隙。
  - **H-37**：压幅只乘低频层、细节层完全绕过 `urbanFlatness` → `flatness=0` 城内标准差 **0.236m 违反 A-1-1 ≤0.2m**；
    且被我把断言阈值写成 `0.6f`（凭直觉放宽而非用文档数值）掩盖。**修公式为压整条噪声和后 → 0m**。
    纪律：**文档给绝对数值，断言就用那个数值**；另补 `flatness=0.5` 中点线性度断言防"中间段不跟手"。
  - 另记流程坑 **H-36**：`recompile_status=up_to_date` 不等于"新代码已编译"——dll mtime 可能早于源码，测试跑旧程序集
    表现为"改了码失败数值一字不差重复"。判别法：比 dll/源 mtime 或反射探新成员；改码后必先确认 mtime 再跑测试。
- 交锁三项门禁（H-37 修复后最终复跑）：`script recompile_status` `failed=False`；`console clear`→重跑全量+校验器→
  `console status` **`consoleErrors=0/consoleWarnings=0/seeded=True`**；`verify_project.py` 本包检查项全 `[ ok ]`
  （整体红灯仅因他包 `core/stretch/stealth` 锁心跳超时，非本包改动，已按 §3 如实上报待用户裁定）。

  `slotKey` 唯一键与冲突协议、`heightValue` 双义消歧表、`intentVersion` 指纹输入清单、校验与迁移规则、
  旧键名对照表）。新发现并解决 **H-26/H-27/H-28**，新增验收 **A-45~A-51**。
  **字段方案裁定（用户 2026-11-03，总纲 D-30）：`heightValue` 保留双义，不拆 `repeat`+`scale`**；
  读取强制单一入口 `RecordGeometry.Resolve`，§13 把该风险列为代码审查打回项、A-51 为回归必测。
  随附收敛：DESIGN/06/07/08/10 旧字段名统一到定稿键名，`CityBuildingTag` 收敛为仅 `slotKey` 反向指针；
  总纲升 v8；内存口径统一钉为 **MiB**（8km 全图量化 31.3MiB + 记录 3.05MiB ≈ 34.4MiB，A-34 的 ≤40MiB 余量约 14%）。
- **M1（Modules/00 基础设施 + 08 反悔骨架）已实现并自测通过**（56 个 EditMode 用例，`test run --mode editor` Total 56 / Failed 0）：
  - `Runtime/Core/CityGrid.cs`：四层分辨率解耦（逻辑格 / 显示 / chunk / region）、2km 与 8km 双档、
    floor 语义格换算与越界安全、圆外接盒裁剪、`RecordQuant` 定点标度（0.01m / 1/4 度）。
  - `Runtime/Core/DeterministicRandom.cs`：SplitMix64 混合与种子树（stage/cell/object）、可重放 `RngStream`、
    `IntentHash`（乱序输入直接抛，守 00 §3.1 铁律）。
  - `Runtime/Core/CellFlag.cs`：`ushort` 13 位掩码 + `MaskWriter` 唯一写口与禁建合集判定。
  - `Runtime/Core/PipelineTypes.cs`：`DirtyBits`（**Terrain 与 Flatten 分家**、`OccupiedReloc` 为终端位）、
    `IntentChangePlanner`（不动点闭包 `Expand`，幂等）、**`StageClaims` 阶段认领单一真源**（面板与阶段共用，已删除面板私有副本）。
  - `Runtime/Core/Pipeline.cs`：`PipelineContext`（槽位/脏区域并集/取消）、`PipelineRunner`（同帧合并、
    固定执行序 S1a→…→S4→S1b→S5→S6、失败与取消即中止）、`GenerationReport` 逐阶段耗时。
  - `Runtime/Core/Falloff.cs`：全工程唯一衰减求值器（内平台→曲线过渡→外截止；四预设单调、越界钳制）。
  - `Runtime/Core/BuildingRecord.cs`：**已冻结结构落地**（32B/栋 + `ValidateLayout` 偏移自证）、
    `BuildingFlags` 位域、`RecordGeometry.Resolve` 作为 `heightValue` 双义的唯一读取入口、`SlotKey` 命名空间。
  - `Runtime/Core/CityProject.cs`：项目根 Model、`CityCenterAnchor`（H-7：城心为独立意图锚点，非实时读场域）、
    世界参数与城心的显式提交入口（非法参数先校验后落字段）。
  - `Runtime/Core/AssembleStage.cs`：S0 装配（外环带位 + `ConstraintAsset` 负约束烘焙，先清位再置位）。
  - `Editor/UndoHistoryTracker.cs`：意图层→派生层一致性桥（首帧只建基线、双检、250ms 合并窗口）。
  - `Editor/CityWindow.cs`：主面板壳（阶段树呈现为**信息而非门禁**、自动跟随开关、工具模式互斥、非法参数提示）。
  - `Tests/Editor/`：EditMode 测试程序集（`UNITY_INCLUDE_TESTS` 约束）+ 56 条用例。
- **实现轮新发现并修复 2 处空洞**（详见 Modules/09 第五轮）：
  - **H-29**：把"复检 S3"直译成"置 `Road` 位" ⇒ `Road` 下游把 Parcel/Field/Flatten 全拖进来 ⇒
    **挪一栋已固化的楼会重算地块**（正是 08 表末行禁止的事）。改为 `OccupiedReloc` 终端位 + `StageClaims` 认领。
    纪律：文档里「→Sx 复检」不等于"置 Sx 的触发位"。
  - **H-30**：意图对象用"单一 Kind + 单一版本"记账 ⇒ 「设为城心」被读成 `WorldParam` ⇒ Expand 成 `All`
    ⇒ **H-7 的修复被这条接线静默抵消**（城心又变回一动就全城重算），且面板脏位显示错误。
    改为 **`IntentChangeLedger` 按通道（对象 × IntentKind）记账**、`IntentKind` 升 `[Flags]`、
    `DirtyOf` 改逐位求并集（否则组合值落 default = 静默丢脏）；序列号必须是 `[SerializeField]`，
    否则 Unity Undo 还原后 Tracker 看不见（H-9 漏检形态）。

### Planned
- M2~M11 见总纲 §10 里程碑表。
- **M0 后、M7 前须冻结 `BuildingRecord` 结构**（Modules/10 §2.2）——D-25 已改变 07/08 数据底座，
  M11 再改会波及认回 / journal / 检查点，返工面大。

## [0.0.1] - 2026-11-03

首个版本：**M0 包骨架**（无功能代码，纯结构与文档）。

### Added
- `package.json`：内嵌包身份，唯一依赖 `com.collin.core@0.2.0`（AGENTS §4 依赖唯一真源）。
- `Runtime/com.collin.citymap.asmdef`：仅引用 core，`validateReferences: true`，命名空间 `Collin.CityMap`。
- `Runtime/PackageIdentity.cs`：包名/版本常量（版本真源仍是 `package.json`，禁用于逻辑分支）。
  存在意义是让 Runtime 程序集非空，使 `runtime-purity` 与编译门禁在 M0 即有真实检查对象。
- `Editor/com.collin.citymap.Editor.asmdef`：引用 core + 本包 Runtime，`includePlatforms: ["Editor"]`。
- `Editor/CityMapMenus.cs`：`Collin/CityMap/Package Info` 菜单自检入口（打印包身份与四件套齐备情况）。
  **必要而非可选**：Editor 目录若只有 asmdef 没有 `.cs`，Unity 会产出 error
  `Assembly for Assembly Definition File ... will not be compiled`，直接破坏 §1.6b 控制台归零门禁
  （且堆栈含本包路径，不适用 §1.8 环境噪声豁免）。详见 ACCEPTANCE §9。
- `Showcase.json`：展板条目五字段（`order=20.0` 排在 core=0 / stretch=10 之后；`video` 暂空，M10 回填）。
- `Sample/README.md`：包内试用演示目录占位与用途说明（AGENTS §0：showcase = 包内演示，展板不引用插件代码）。
- `Docs/DESIGN.md`：**已评审 v7.1**——总纲 + `Docs/Modules/00~10` 共 11 份设计细则（约 3000 行）。
  含四轮操作序列走查发现的 25 处跨模块空洞（H-1~H-25，7 处阻断级）及全部回写裁决：
  - C1~C6 能力定义（地形 / 河流 / 路网 / 地块 / 场域 / 建筑 + 导出）；
  - 七阶段管线 S0~S6（S1 拆 S1a/S1b 以打破与地块的循环依赖）、人工层与自动层分域、派生层不落盘；
  - 场域 = 同心双圆柱（类型 / 内外径 / 内外值 / 衰减曲线），烘焙四维强度场；
  - 建筑生成 = 全图统一"主导维数值硬过滤 → softmax 加权随机"，拉伸高度由场域强度驱动；
  - 编辑管线：L1~L4 反悔分层、锁区 / 负约束 / adopt 三种局部反悔机制、上游改动冲突矩阵；
  - 大尺度：`BuildingRecord` 数据态真源 + region 工作集流式 + `CitySnapshot` 三形态导出（scene 切块 / JSON / 2D tile）；
  - 决策 D-01~D-29、验收 A-1~A-44。
- `Docs/ACCEPTANCE.md`：M0 结构 / 依赖 / 纯净性 / 编译 / 控制台归零条目与证据栏。

### Changed
- 无（首个版本）。

### Removed
- 无。

### 未做（明确记录，避免误解为遗漏）
- **新建内嵌包的发现步骤（本项目环境实测，后续会话照此执行）**：
  `Assets/Refresh` 菜单与 `AssetDatabase.Refresh(ForceUpdate)` **都无法**让编辑器发现新内嵌包
  （`script recompile` 会返回 `up_to_date` 且 0 个 `.meta` 生成）；
  必须执行 `unity command "package resolve"`，之后 `.meta` 生成、`package list` 出现该包、两侧 dll 产出。
- 未触碰 `Packages/manifest.json`：内嵌包由 Unity 自动发现，无需声明（AGENTS §7 共享文件不申报）。
- 未修改 `ProjectSettings/**`、`Assets/Showcase/`、`com.collin.core`、`com.collin.stretch`。
- 未引入任何第三方 UPM 依赖（Unity Recorder 仅在 M10 为 Sample 录制单独申报）。
- 无功能代码：M0 的交付判据是"校验绿灯 + 编译干净 + 控制台归零"，不是任何生成能力。
