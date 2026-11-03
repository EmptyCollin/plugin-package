# DESIGN — com.collin.core

> 状态：已评审（命名空间经用户拍板，2026-11-02）。本包是插件集合的跨包基础设施，自身同样遵守 AGENTS.md 全部流程。

## 1. 目标与非目标
- 解决什么问题：
  1. 为 feature 包之间提供**唯一合法通信面**：事件通道（EventChannel）、服务注册表（Services）、ScriptableObject 信号，兑现 §5「跨包通信只走 core」；
  2. 为展板提供**与代码解耦的条目发现与排序**：运行时枚举 `Packages/com.collin.*/**/Showcase.json`，按「依赖拓扑序 → order → 名称字典序」输出（§6）；
  3. 为「部分提取」提供依赖闭包计算：给定目标包，返回其全部传递依赖（§6）。
  4. **承载玩法类 feature 包之间的跨包契约**（接口 + 纯数据 DTO，见 `Runtime/StealthContracts/` 与 D-7）：
     让「消费方包」与「提供方包」在不互相 asmdef 引用的前提下协作（§5 的落地形态）。
- 明确不做什么：
  - 不含任何 UI / 展板场景代码（展板属于 Assets/，纯消费本包的发现与排序结果）；
  - 不做插件生命周期管理、热重载、包加载器；
  - v0.1 不实现部分提取的导出工具本身，只提供其依赖的闭包计算函数。

## 2. 约束
- Unity 6000.6.7f1；依赖方向 feature → core，core 不依赖任何 feature 包（§5）；
- asmdef `validateReferences: true` 不得关；
- 不新增外部依赖，只用 Unity 内置模块（jsonserialize / UnityEditor.PackageManager）。

## 3. 方案概述
| 组件 | 程序集 | 职责 |
|---|---|---|
| `EventChannel` | Runtime | ScriptableObject 信号通道：`Raise(payload)` / `Subscribe`，广播式跨包事件 |
| `Services` | Runtime | 静态接口注册表：feature 包注册实现、他包按接口取用；域重载后需重新注册（见 §6 风险） |
| `ShowcaseEntry` / `ShowcaseSorter` | Runtime | 条目 DTO 与纯函数排序器（Kahn 拓扑 + order + 名称兜底，可单测） |
| `ShowcaseScanner` | Editor | 经 `PackageInfo.GetAllRegisteredPackages()` 发现 `Packages/com.collin.*` 内嵌包，解析各自 `Showcase.json`，产出排序结果与依赖闭包 |
| `ShowcaseRuntime` | Runtime | 从项目根 `Packages/` 磁盘枚举 com.collin.* 的 Showcase.json 并复用 Sorter 排序；展板场景（Play 模式）的数据源；含视频绝对路径解析 |
| `StealthContracts/StealthDtos.cs` | Runtime | 潜行跨包契约的纯数据 DTO（位姿/路点/轨迹采样/单位/视野/时钟/察觉事件 + 层级与档位枚举）；只含值类型与引擎类型 |
| `StealthContracts/StealthWorldInterfaces.cs` | Runtime | 跨包接口：`IPatrolWorld`（读+写路点）、`IVisionWorld`、`IDetectionWorld`、`ISafeIslandProvider`（反向注入：辅助台实现、玩法包消费）、`IStealthScrubDriver`（回溯驱动）；实现走 `Services.Register/Get` |
| `StealthContracts/StealthChannels.cs` | Runtime | 三个 `EventChannel<T>` 具体子类（察觉事件 / 时间索引 / 世界结构变更），兑现「泛型 SO 不可直接实例化」的既有实测约束（见 ACCEPTANCE 第 8 行：泛型通道的 CreateInstance 返回 null） |

数据流：展板场景（Assets/，纯代码构建 UI，场景文件仅一个挂脚本的 GameObject）→
`ShowcaseRuntime.LoadAndSort()` → 左侧条目列表（名称+分类）/ 右侧详情（摘要+视频+依赖闭包）逐条渲染。
依赖解析真相源 = 各包 `package.json` 的 `dependencies`（经 PackageInfo 读取），不另建平行登记表（§5）。

## 4. 跨包接口
- 其他包引用程序集：`com.collin.core`（Runtime）、`com.collin.core.Editor`（仅编辑器工具）；
- 通信只允许三种：`EventChannel` 资产引用、`Services.Get<T>()`、共享 ScriptableObject；
- 本包自身在展板中的条目由 `Showcase.json` 声明，category=`core`，order=0（core 恒在最前由拓扑序保证）。

## 5. 决策记录
| 编号 | 决策 | 备选 | 理由 |
|---|---|---|---|
| D-1 | 包命名空间 `com.collin.*` | com.emptycollin.* | 用户拍板（2026-11-02） |
| D-2 | core 与展板分置：逻辑进 core，UI 留 Assets/ | 展板全部代码进一个 showcase 包 | 展板是项目级产物非交付插件；§0 规定 Assets/ 只留展板 |
| D-3 | 依赖发现走 `PackageInfo`，不手写 package.json 解析 | 正则/自研 JSON 解析 | Unity 官方 API，与包管理器解析行为一致 |
| D-4 | 排序为纯函数（Sorter）与 IO（Scanner）分离 | 揉在一个类 | 纯函数可被 EditMode 测试直接验证（ACCEPTANCE 用） |
| D-5 | 展板数据源 = `ShowcaseRuntime` 磁盘枚举（编辑器 Play 模式内读 `Packages/`） | 展板复用 Editor 侧 Scanner | 展板是运行中的纯 UI 场景，不能依赖 Editor-only 程序集；独立构建不含 Packages 目录为已知边界——展板仅项目内运行，非交付功能 |
| D-6 | 展板场景文件只含一个挂 `ShowcaseUI` 的 GameObject，全部 uGUI 由代码构建 | 场景里手工摆全 UI 层级 | `.unity` 文件保持最小且稳定，插件增减永不触碰场景文件（§6 精神贯彻到文件层，消除合并热点） |
| D-7 | **玩法包与设计辅助台之间的跨包契约层放 core**（`Runtime/StealthContracts/`：接口 + 纯数据 DTO + 通道具体子类） | 修订 §5 允许两个 feature 包互相 asmdef 引用 | 用户 2026-11-03 拍板（stealth DESIGN D-10 / Q-A1「方案 P」）。§5 禁止 feature 互引，而辅助台必须读玩法数据；把契约上提 core 后，消费方只引 core、提供方注册实现，规则不破且消费方可在提供方缺席时优雅降级。**代价**：core 从「纯基础设施」变为「承载领域契约」，故划红线——契约层只允许值类型/接口/通道子类，**禁止任何玩法逻辑与判定实现**，也禁止签名里出现 feature 包类型（否则消费方仍须引包，方案失效） |

## 6. 风险
- 静态 `Services` 注册表在域重载/退出 Play 后清空 → 缓解：约定 feature 包在自身初始化入口重注册；本版本接受该语义并写入 README 级注释；
- `Showcase.json` 由 AI 手写可能格式错误 → 缓解：Scanner 对解析失败仅记 warning 跳过，不让展板整体崩溃；校验器另查必备字段；
- 环形依赖破坏拓扑序 → 缓解：`Sort` 检测并返回 cycle 标记，剩余条目按 order+名称兜底，绝不乱序丢失。
