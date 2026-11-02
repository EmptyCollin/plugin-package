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
