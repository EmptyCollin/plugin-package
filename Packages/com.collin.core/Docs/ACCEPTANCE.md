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
