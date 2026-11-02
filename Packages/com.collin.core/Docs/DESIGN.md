# DESIGN — com.collin.core

> 状态：已评审（命名空间经用户拍板，2026-11-02）。本包是插件集合的跨包基础设施，自身同样遵守 AGENTS.md 全部流程。

## 1. 目标与非目标
- 解决什么问题：
  1. 为 feature 包之间提供**唯一合法通信面**：事件通道（EventChannel）、服务注册表（Services）、ScriptableObject 信号，兑现 §5「跨包通信只走 core」；
  2. 为展板提供**与代码解耦的条目发现与排序**：运行时枚举 `Packages/com.collin.*/**/Showcase.json`，按「依赖拓扑序 → order → 名称字典序」输出（§6）；
  3. 为「部分提取」提供依赖闭包计算：给定目标包，返回其全部传递依赖（§6）。
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

数据流：展板场景（Assets/，后续任务）→ `ShowcaseScanner.CollectSorted()` → 逐条渲染名称/分类/摘要/视频。
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

## 6. 风险
- 静态 `Services` 注册表在域重载/退出 Play 后清空 → 缓解：约定 feature 包在自身初始化入口重注册；本版本接受该语义并写入 README 级注释；
- `Showcase.json` 由 AI 手写可能格式错误 → 缓解：Scanner 对解析失败仅记 warning 跳过，不让展板整体崩溃；校验器另查必备字段；
- 环形依赖破坏拓扑序 → 缓解：`Sort` 检测并返回 cycle 标记，剩余条目按 order+名称兜底，绝不乱序丢失。
