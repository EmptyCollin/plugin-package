# ACCEPTANCE — com.collin.citymap

> 每条必须带可观测证据（谁 / 何时 / 用什么指令验证 / 结果）。只记事实，不记意图（AGENTS.md §4）。
> 全量设计验收草案 A-1~A-44 分布在各 `Docs/Modules/*.md` §验收；本表记录**已实际执行并取证**的条目。

## 已完成

| # | 验收项（可观测行为） | 验证指令 / 方法 | 验证者（客户端+标识） | 日期时间 | 结果与证据 |
|---|---|---|---|---|---|
| 1 | M0 骨架齐全：`package.json` + `Runtime/`(+asmdef) + `Editor/`(+asmdef) + `Showcase.json` + `Docs/{DESIGN,ACCEPTANCE,CHANGELOG}` + `Sample/` 全部存在 | `python Tools/verify_project.py` 的 `pkg-layout` 检查项 | qianwen-work-assistant / `2026-11-03 m0-skeleton` | 2026-11-03 | 见下方"本次会话执行记录"§1 |
| 2 | 依赖方向合规：本包仅依赖 `com.collin.core`，无 feature→feature 引用 | `verify_project.py` 的 `dep-direction` / `dep-core` 检查项；`package.json` 与两侧 asmdef 的 references 字段 | 同上 | 2026-11-03 | 见 §2 |
| 3 | Runtime 纯净：Runtime 源码零 `using UnityEditor` | `verify_project.py` 的 `runtime-purity` 检查项 | 同上 | 2026-11-03 | 见 §3 |
| 4 | `validateReferences: true` 未被关掉（两侧 asmdef） | `verify_project.py` 的 `asmdef` 检查项（全项目扫描，命中 false 即 FAIL） | 同上 | 2026-11-03 | 见 §4 |
| 5 | 内嵌包被 Unity 发现且编译干净（AGENTS §1.6a） | `unity command "script recompile_status"` → `failed` / `compilationFailed` | 同上 | 2026-11-03 | 见 §5 |
| 6 | 控制台归零（AGENTS §1.6b）：清台后重跑验证动作，无新增 error/warning | `console clear` → 重跑本次验证 → `console status` 的 `groundTruth.consoleErrors/consoleWarnings` | 同上 | 2026-11-03 | 见 §6 |
| 7 | `Showcase.json` 五字段齐全且 `order` 为数字；`video` 空串不触发文件存在性 FAIL | `verify_project.py` 的 `showcase` 检查项 + 本会话 JSON 解析断言 | 同上 | 2026-11-03 | 见 §7 |
| 8 | 本包不入 `manifest.json`（内嵌包由 Unity 自动发现，§7 共享文件未触碰） | `verify_project.py` 的 `manifest-sync`；`git status` 无 `Packages/manifest.json` 改动 | 同上 | 2026-11-03 | 见 §8 |

## 未通过项

| # | 现象 | 复现步骤 | 状态 |
|---|---|---|---|
| — | 无 | — | — |

## 本次会话执行记录（M0，全部为实测值）

> 会话 `2026-11-03 m0-skeleton`，编辑器实例 **端口 7801**（PID 3512，工程 `D:\unity_projects\plugin-package`，6000.6.7f1）。
> 注意：AGENTS §1.3 写的端口 7800 与本会话实际端口不符，实例已更换过——`unity status` 是唯一可信来源。

### §1 结构齐全（实测）
`python Tools/verify_project.py` → `插件包: 3 个 | FAIL 0`，`[ ok ] pkg-layout: com.collin.citymap 结构齐全`。
本包首次被计入插件包数（此前一直是 2 个）。

### §2 依赖方向（实测）
`package.json` dependencies = `com.collin.core: 0.2.0`（core 实测版本即 0.2.0）；
Runtime asmdef references = `[com.collin.core]`；Editor asmdef references = `[com.collin.core, com.collin.citymap]`、
`includePlatforms: ["Editor"]`。校验器 `dep-direction` / `dep-core` 无输出项（即无 WARN/FAIL）。

### §3 Runtime 纯净（实测）
`[ ok ] runtime-purity: Runtime 程序集无 UnityEditor 依赖`——`Runtime/PackageIdentity.cs` 零 using。

### §4 asmdef 校验开关（实测）
两侧均 `validateReferences: true`，校验器 `asmdef` 检查项无 FAIL。

### §5 编译状态（实测）
- 指令名从运行时发现（`unity list`）：**两段式 `script recompile` / `script recompile_status`**；
- `script recompile_status` → `status=completed`（后续为 `up_to_date`）、**`failed=False`、`errors=0`**；
- `console status` → `compilationFailed=False`；
- 产物：`Library/ScriptAssemblies/com.collin.citymap.dll` 与 `com.collin.citymap.Editor.dll` 均已生成；
  域内已加载程序集含 `com.collin.citymap` 与 `com.collin.citymap.Editor`（反射枚举实测）。

### §6 控制台归零（实测）
`console clear` → 重跑本会话全部验证动作（校验器 + 反射自检 + 重编译状态查询）→
`console status` groundTruth = **`consoleErrors=0, consoleWarnings=0, seeded=True`**；`console get --tail 15` 中 error/warning 条目数 **0**。
门禁顺序符合 §1.7（以归零后新产生的条目为准，非用 clear 掩盖历史）。

### §7 Showcase 字段（实测）
`keys=[name,category,summary,video,order]`、`order` 为 float `20.0`、`video` 空串、`summary` 297 字符。
校验器 `showcase` 检查项无 FAIL（空串不触发视频存在性检查）。
排序定位：core=0 < stretch=10 < citymap=20。

### §8 共享文件未触碰（实测）
`git status` 中无 `Packages/manifest.json`、无 `ProjectSettings/**` 改动 ⇒ AGENTS §7 申报义务未触发。

### §9 本会话踩到并解决的两个真实问题（对后续会话有直接价值）

| 问题 | 现象 | 解法（已验证有效） |
|---|---|---|
| **新建内嵌包不被发现** | `script recompile` 返回 `up_to_date`、0 个 `.meta` 生成；`editor invoke_menu "Assets/Refresh"` 与 `AssetDatabase.Refresh(ForceUpdate)` **均无效**；`package list` 里查不到本包 | **`unity command "package resolve"`** 才触发嵌入包发现（返回 `requiresRecompile: true`），随后 25 个 `.meta` 生成、两侧 dll 产出。此为本项目环境下新建内嵌包的必要步骤 |
| **空 asmdef 目录 → error 破坏门禁** | 仅 `Editor/*.asmdef` 无 `.cs` 时，控制台产生 error：`Assembly for Assembly Definition File '...Editor.asmdef' will not be compiled...`，校验器 `console` 项直接 FAIL（§1.8 归因不适用：堆栈含本包路径，非环境噪声） | 每个 asmdef 目录**至少放一个真实 `.cs`**。本包以 `Editor/CityMapMenus.cs`（`Collin/CityMap/Package Info` 菜单自检）满足，同时该文件也是 §4 要求的 Editor 侧首个合法入口 |

> 附带修掉的自写 bug：`CityMapMenus.BuildInfoText()` 用 `File.Exists()` 判目录，导致自检误报"缺 Docs/Modules/"；
> 已改 `Directory.Exists()`，重编译后实测输出 `模块细则：11 份`。

## 遗留（不属于 M0 失败项，按里程碑推进）

- A-1~A-44 全部设计验收项：随 M1~M11 实现逐条取证，当前无一条已实测。
- 演示视频与 Sample 小城：M10；`BuildingRecord` 结构冻结：M0 后、M7 前（交接单 §4.3）。
- 本文档为覆写式，非追加日志（AGENTS §4）。
