# ACCEPTANCE — com.collin.core 0.1.0

> 每条带可观测证据（谁 / 何时 / 用什么指令验证 / 结果）。只记事实，不记意图（AGENTS.md §4）。
> 验证者：qianwen-work-assistant（千问工作助理），2026-11-02，编辑器 PID 16552 / 端口 7800。
> 证据原文存于验证时 `.locks/verify_result.txt`、`.locks/v6_result.txt`（已按 §9 会话结束前清理，结果摘录如下）。

| # | 验收项（可观测行为） | 验证指令 / 方法 | 日期时间 | 结果与证据 |
|---|---|---|---|---|
| 1 | 脚本编译干净，无失败 | `unity command recompile` → 轮询 `recompile_status` | 2026-11-02 | `{"status":"completed","failed":false,"errors":[]}`；修复前曾捕获 `CS0104 PackageInfo 二义性`，加 using 别名后转绿 |
| 2 | 发现器能枚举 com.collin.* 包并解析 Showcase.json 全字段 | `unity command eval_file --file .locks/CoreVerify.cs`（V1 段） | 2026-11-02 | `count=1 cycle=False \| id=com.collin.core name=Collin Core cat=core order=0 deps=[] sumLen=63` |
| 3 | 环形依赖不丢条目且置 cycle 标记 | 同上（V2 段，构造 a↔b 环） | 2026-11-02 | `cycle=True n=2` |
| 4 | 显示顺序=依赖拓扑→order→名称 | 同上（V3 段，core 被 x/z 依赖，z.order=1） | 2026-11-02 | `topo=core,x,z cycle=False` |
| 5 | 依赖闭包计算（部分提取基础） | 同上（V4 段，x→core） | 2026-11-02 | `closure=com.collin.x,com.collin.core` |
| 6 | Services 注册/取用/清理语义 | 同上（V5 段，注册 IEqualityComparer=Ordinal） | 2026-11-02 | `works=True afterClear=False` |
| 7 | EventChannel 广播：订阅计数、退订停播；typed 通道载荷正确 | `eval_file .locks/V6Bisect.cs`（分步 try/catch） | 2026-11-02 | `create/subscribe/raise1/raise2/counter=2/typed-create/destroy` 全 OK |
| 8 | 泛型 `EventChannel<T>` 不可被 Unity 直接实例化（`CreateInstance` 返回 null） | 分步二分 + `V6Probe.cs` | 2026-11-02 | `created_nonnull=False`。处置：泛型类改 abstract，文档注明须用具体子类；core 提供 `IntEventChannel` 样板（本行为 v0.1.0 设计约束的一部分，非缺陷） |
| 9 | 规则校验器对 core 绿灯 | `python Tools/verify_project.py` | 2026-11-02 | 见交锁前最终运行输出 |
| 10 | §1.6b 新门禁下复跑：clear→静默 65s→重跑 V1-V6→归零复核 | `unity command "console clear"` → `"console status"` → `eval_file .locks/CoreVerify.cs` | 2026-11-02 | 静默窗口后 0/0/0；复跑 `V1-V6 全 OK（V6 hits=2 typed=5）`；终读 `errors=0 warnings=0 compilationFailed=False`；校验器含 console 项全绿 |

## 归零前已存在、与本包改动无关的环境错误（§1.6b 第 7 条，如实上报）
2026-11-02 13:29:17（用户截图三条，缓冲区归因确认）：
1. `KeyNotFoundException: 'services-gateway'` — 堆栈 `UnityEditor.Connect.ServicesConfiguration.BuildPaths()`（Unity 云连接内部）；
2. `UnavailableForLegalReasons ... generators.ai.unity.com (SettingsResult)` — `com.unity.ai.assistant` 包轮询其云端服务被区域策略拒绝（451）；
3. 同上（`PointsBalanceResult`）。
三条堆栈均不含 `Packages/com.collin.*` 或 `Assets/` 程序集。**未写入豁免清单**（§1.8 要求用户批准，尚未获批）；若复发将按新规则阻塞交锁，届时需拍板豁免或处置。

## 未通过项
无。

# v0.2.0 增量（展板上线，2026-11-02，同验证者）

| # | 验收项（可观测行为） | 验证指令 / 方法 | 结果与证据 |
|---|---|---|---|
| 11 | 运行时枚举：Play 模式展板读出 Packages/ 下全部 com.collin.* 条目 | `eval 'ShowcaseRuntime.LoadAndSort()'` 打印 entries 计数 | `FINAL entries=1 first=com.collin.core cycle=False`（临时包删除后的终态） |
| 12 | 拓扑排序：临时包 demotest（依赖 core，order=0）与 core 并列时 core 在前 | 建临时包→进 Play→`game_view` 截图 | 截图左栏顺序 core、test；`scene get_hierarchy` 确认两条目按钮 `Item_com.collin.core`、`Item_com.collin.demotest` |
| 13 | 依赖闭包显示：选中 demotest 详情行显示其闭包 | `eval` 读 DetailDeps 文本 | `DetailTitle=[Demo Test] DetailDeps=[依赖闭包：com.collin.core]` |
| 14 | 视频播放：ShowcaseMedia/demo.mp4（ffmpeg 生成 3s testsrc）经「播放演示」渲染到视频区 | 点击 `PlayBtn`→`game_view` 截图 | 截图视频区显示彩条测试帧；`frame=44`（45 帧片播完）；修复过程：初版 targetTexture 未创建致白块，显式创建 RenderTexture 后转绿 |
| 15 | §1.6b 门禁终态：clear→重跑验证（Play+枚举+截图+stop）→归零 | `console clear` → 验证动作 → `console status` | `GATE errors=0 warnings=0 compilationFailed=False`；校验器绿灯 |
| 16 | 临时验收包与截图残骸已清理，lock 残留经 Client.Resolve() 清除 | `rm -rf Packages/com.collin.demotest Assets/.locks` → refresh → `grep -c demotest packages-lock.json` | 计数 0；编译 `failed=false` |

注：过程中控制台曾出现 demotest mp4 的色彩原色 warning（WindowsMediaFoundation，堆栈不含项目程序集）——该包已删除，按 §1.6b 归零流程随 clear+重验一并消除，未申请豁免。

# v0.2.0 增量（潜行跨包契约层，2026-11-03，验证者 qianwen-work-assistant · stealth-m05-2026-11-03）

> 本增量只**新增** `Runtime/StealthContracts/` 三个文件，未改动既有 4 个源文件的任何一行（§2 并行前提）。

| # | 验收项（可观测行为） | 验证指令 / 方法 | 日期时间 | 结果与证据 |
|---|---|---|---|---|
| 17 | 契约层编译进 core 程序集 | `unity command "script recompile"` → `recompile_status`；查 `Library/ScriptAssemblies/` | 2026-11-03 19:26 | **通过** — `com.collin.core.dll` / `.pdb` 均已产出；错误列表内**无一条**路径含 `com.collin.core` |
| 18 | 接口签名不出现 feature 包类型（方案 P 的成立前提，DESIGN D-7 红线） | 逐文件静态扫描：`using Collin.Stealth` 与 stealth 类型名 | 2026-11-03 19:10 | **通过** — 扫描结论「跨包污染：无」；共声明 19 个类型，全部为接口 / 值类型 / 枚举 / 通道子类 |
| 19 | 消费方**只凭 core 符号**即可实现接口并取用 | 消费方视角 EditMode 用例（stealth `Tests/Editor/StealthContractConsumerTests.cs`，4 个 Fake 实现只用 `Collin.Core.Stealth` 类型） | 2026-11-03 20:28 | **通过** — 契约用例 **18 条全绿**（本包合计 42 条）；含 `Services.Register/Get` 往返、路点写回、`default` 安全空值 |
| 20 | 三个通道**具体子类**可被 Unity 实例化并正确广播（守住 0.1.0 第 8 行的实测约束） | EditMode `DetectedChannel_…` / `TimeIndexChannel_…` / `WorldChangedChannel_…` | 2026-11-03 19:26 | **通过** — `CreateInstance` 非 null；载荷字段（含 `timeIndex`）逐一断言；退订后停播 |
| 21 | 镜像枚举与提供方枚举**值序一致**（不一致时两侧编译互不知情，会静默错读） | EditMode `CoreMirrorEnums_ValueOrder_MatchesStealthProviderEnums`（仅测试程序集可同时看见两侧类型） | 2026-11-03 19:26 | **通过** — `StealthLoopMode` ↔ `PatrolLoopMode` 逐值对齐；`StealthClockDto.mode` 与 `StealthClockMode` 同值 |
| 22 | 位姿 `default` 不得被误读成「位于第 0 段」 | EditMode `ConsumerCanAssembleDto_…`（`legIndex=0` 但未置 `valid` ⇒ `HasLeg==false`） | 2026-11-03 19:26 | **通过** — 该缺陷在实现轮自查发现并补 `valid` 位（原契约只看 `legIndex >= 0`） |
| 23 | 既有能力未被新增文件破坏（回归） | `unity command "test run" --mode editor` 全量 | 2026-11-03 19:26 | **通过** — 全工程 **124/124**；其中 CityMap 84、Stealth 40（v0.2.0 时代的展板/排序/通道用例未受影响） |

## 交锁门禁（2026-11-03 20:28 末次实测，达成）

- `script recompile_status` → `failed:false, errors:0`；`console clear` → 重跑 recompile + test run →
  `console status` 的 `groundTruth` = `compilationFailed:false, consoleErrors:1, consoleWarnings:0`
  （该 1 条命中 §1.8 **已批准**豁免清单，堆栈不含项目程序集）；
  `python Tools/verify_project.py` → **结论: 绿灯**（FAIL 0 / WARN 4：三个包锁 active + 已豁免噪声）。
- 全量测试 **141/141 通过**（CityMap 99 / Stealth 42）。
- 过程记录（如实）：19:26 时曾读到 `consoleErrors:15`，全部来自 `com.collin.citymap/Runtime/View/`
  两个**未被版本库跟踪**的进行中文件（另一会话 19:19 写入；其会话随后自行修好）。
  按 §2 包互斥与 §1.9「只回滚自己引入的错误」，本会话**未修改**该两文件。
- 本会话自造的 1 条 error 已修复：契约的「重复注册」用例**故意**触发 core 冲突 error，
  原先用 `LogAssert.Expect` 声明预期——实测该方法**只让测试不失败、不回退 `consoleErrors` 计数**，
  仍会破 §1.6b 门禁。正解改为断言期间 `Debug.unityLogger.logEnabled=false` + `finally` 复位。

# v0.2.0 增量（展板抽离谱系，2026-11-06，验证者 qianwen-work-assistant · showcase-lineage-2026-11-06）

> 本增量：`ShowcaseEntry` 增可选字段 `tier`/`extractedFrom`；`ShowcaseSorter` 增谱系纯函数
> （`TierOf`/`LineageGroups`/`ExportClosure`/`ExtractedFromEntry`/`ExtracteesOf`，DESIGN D-8）；
> 五包 Showcase.json 补谱系字段；展板 UI 改分组呈现；校验器增 `showcase-lineage` 检查；AGENTS §6 同步。

| # | 验收项（可观测行为） | 验证指令 / 方法 | 日期时间 | 结果与证据 |
|---|---|---|---|---|
| 24 | 展板左栏按谱系分组：中间件层独立一组；citymap 组含 stretch（缩进跟随）；stealth 组含 stealthplanner | Play 模式 `eval_file .tmp-inspect/probe_ui.cs` 读 Content 子节点序列 | 2026-11-06 20:36 | `[UIORDER] Group_ \| Item_core \| Group_citymap \| Item_citymap \| Item_stretch \| Group_stealth \| Item_stealth \| Item_stealthplanner`；组内母体在前 |
| 25 | 详情血缘行方向正确：能力标「向下抽离」、工具标「向上抽离·运行依赖」；母体标「已抽离出」 | 同上，逐条点击读 DetailLineage | 2026-11-06 20:36 | stretch=`抽离自 CityMap（向下抽离·能力）`；stealthplanner=`抽离自 Stealth（向上抽离·工具，运行依赖…）`；citymap/stealth=`已抽离出 …` |
| 26 | 导出闭包按谱系合成：citymap 携带 stretch；stealthplanner 携带 stealth；stealth 单导出**不**含工具 | 同上读 DetailDeps | 2026-11-06 20:36 | citymap=`Collin Core、Collin Stretch`；stealthplanner=`Collin Core、Collin Stealth`；stealth=`Collin Core`（无工具）；stretch/core 各自只带底座 |
| 27 | 右栏滚动详情无遮挡：长摘要完整可达、视频区不压文字 | `capture game_view` 截图 + 图像理解核验 | 2026-11-06 20:34 | 核验结论「布局无遮挡、截断或重叠」；截图存 `.tmp-inspect/shot2.png`（不入库） |
| 28 | 校验器 `showcase-lineage` 项生效且全项目绿灯 | `python Tools/verify_project.py` | 2026-11-06 20:44 | 结论: 绿灯（FAIL 0） |

## 交锁门禁（2026-11-06 20:44 末次实测，三项全绿）

- `console clear` → 重跑本会话全部验证动作（recompile → Play → 逐条点击探针 → 截图 → stop）→
  `console status` groundTruth = `compilationFailed:false, consoleErrors:0, consoleWarnings:0`；
  （第一轮复跑曾计 1 条 warning=`Editor is already in play mode`，为验证脚本误重复进 Play 所致，
  按序重跑后归零，非环境噪声、未申请豁免）
- `script recompile_status` → `failed:false`；`python Tools/verify_project.py` → **结论: 绿灯**。
- 全量 EditMode 测试 **386/386 通过（Failed 0）**（`unity command test run --mode EditMode`，
  2026-11-06 21:0x；本会话改动前基线同为 386，无回归）。
- 校验器修复顺带登记：`check_console` 原以 `"console status"` 两段式传参，实测该形式在
  `unity --json command` 下报 Command Not Found（被吞成 WARN"无法读取控制台状态"）——
  改为分段传参并对空响应抛错；修复后 console 项真实读数 error=0 warning=0。
- 过程记录（如实）：门禁期间曾两度自造噪声并当场消除——①验证脚本误重复进 Play 产生
  1 条 `Editor is already in play mode` warning（按序重跑归零）；②测试等待期未续锁心跳，
  校验器判 5 条 lock-stale FAIL（§2.1.8 教训重演，续期自身锁后转绿）。

# v0.2.0 增量（展板选中态错位修复，2026-11-06，验证者 qianwen-work-assistant · showcase-fix-2026-11-06）

> 用户实机反馈：UI 面板点「地图工具」（citymap）高亮却画到「建模工具」（stretch）上。
> 根因：`RebuildLineageList` 按血缘分组渲染（母体在前、抽离品跟随），而 `Select` 用「渲染位置 ==
> 全局拓扑下标」直接比色——两套序不一致时高亮错位（stretch 在全局序里排在 citymap 之前）。
> 修复：`Assets/Showcase/ShowcaseUI.cs` 新增 `_buttonEntryIndex`（渲染位置→全局下标映射），
> AddItem 登记、Reload 清空、Select 按映射判定高亮。

| # | 验收项（可观测行为） | 验证指令 / 方法 | 日期时间 | 结果与证据 |
|---|---|---|---|---|
| 29 | 逐条点击每个 Item，被点项高亮、其余 dim，且高亮项与详情标题一一对应 | Play 模式 `eval_file .tmp-inspect/probe_sel.cs`，逐条 Invoke 后读 Image.color 与 DetailTitle | 2026-11-06 21:3x | core/citymap/stretch/stealth/stealthplanner 五条均 `被点项高亮=True 全局高亮数=1 高亮项=<被点项> 详情=<对应名>`；citymap↔stretch 错位消失 |

## 交锁门禁（2026-11-06 末次实测，三项全绿）

- `console clear` → 重跑本会话全部验证（recompile → Play → 逐条点击探针 → 截图 → stop）→
  `console status` groundTruth = `compilationFailed:false, consoleErrors:0, consoleWarnings:0`；
- `script recompile_status` → `failed:false`；`python Tools/verify_project.py` → **结论: 绿灯**。
- 说明：本轮仅改 `ShowcaseUI.cs`（Assets 展示层），未触碰 core/包代码与测试程序集，全量测试基线 386 不受影响。
