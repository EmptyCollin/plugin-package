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
| 9 | **M1** `BuildingRecord` 布局 = 32B 且逐字段偏移与 11 §2 冻结表一致 | `test run --mode editor` → `A45_Record_Layout_...`（`Marshal.SizeOf` + `OffsetOf` 全字段断言） | qianwen-work-assistant / `2026-11-03 m1-foundation` | 2026-11-03 | **通过**（见 §M1-1） |
| 10 | **M1** 确定性随机：同输入同值、位置敏感、阶段隔离、流可重放且无全局态 | `A0_1_*` 三条用例（128 次逐值比对） | 同上 | 2026-11-03 | **通过** |
| 11 | **M1** 脏传播求解幂等（不动点闭包），且场域变化不触发 S1a（A-27）、改河不重跑噪声但回写平整（H-29）、挪固化楼不触发地块（H-29） | `A0_3_*` 遍历全部 `IntentKind` + `A27_*` / `H29_*` ×2 / `H2_*` / `H7_*` | 同上 | 2026-11-03 | **通过**（H-29 首跑失败→修终端位后通过，见 §M1-3） |
| 12 | **M1** 意图变化按**通道**归因：城心提交只进 Center 链、不得被读成种子变更（H-30） | `Channels_On_One_Object_Do_Not_Pollute_Each_Other` + `Tracker_Dirties_On_Change_And_On_Value_Rollback`（含"序列号回滚 = undo 仍被感知"） | 同上 | 2026-11-03 | **通过**（H-30 为本轮实现发现，见 §M1-4） |
| 13 | **M1** 打开/域重载后首帧零重算（A-33 的 Tracker 部分） | 同上第二条用例的 ① 段：`Scan()` 首帧返回 `None` 且 `PendingDirty==None` | 同上 | 2026-11-03 | **通过** |
| 14 | **M1** 调度语义：同帧多次 Mark 只跑一轮、执行序与注册序无关（S1a 早于 S1b）、取消在下个边界生效、阶段异常不外泄 | `Runner_Merges_*` / `Runner_Executes_In_Fixed_Order_*` / `A0_4_*` / `Runner_Stage_Failure_*` | 同上 | 2026-11-03 | **通过** |
| 15 | **M1** 掩码位预算与文档一致（13 位）、冻结对象与负约束均判禁建 | `Mask_Registers_Thirteen_Single_Bits_As_Documented`（与 00 §2.4 表互守卫）+ `Frozen_Objects_And_Constraints_*` | 同上 | 2026-11-03 | **通过** |
| 16 | **M1** 参数校验：非整数倍分辨率组合、非法切块尺寸在构造/写入期即拒，且非法写入不污染字段、不 bump 版本 | `A0_7_*` ×2 + `World_Params_Reject_Invalid_*` | 同上 | 2026-11-03 | **通过** |
| 17 | **M1** `heightValue` 双义四种情形与有效区边界、footprint 覆盖哨兵语义（A-51/A-48） | `A51_*` ×2 + `A48_*` | 同上 | 2026-11-03 | **通过** |
| 18 | **M1** 量化往返误差 ≤1 量化步（A-1 新口径的实现侧） | `Record_Quant_Roundtrip_Within_One_Step`（8km 档 58 个采样点）+ `Yaw_Quant_*` | 同上 | 2026-11-03 | **通过** |
| 19 | **M1** 负约束"禁用不删数据、移除即失效、重跑先清后置"（A-18 的 S0 侧） | `S0_*` 四条用例 | 同上 | 2026-11-03 | **通过** |
| 20 | **M1** 全部改动后交锁三项门禁仍成立 | `script recompile_status` failed=false；`console clear`→重跑验证→`console status`；`verify_project.py` | 同上 | 2026-11-03 | 见 §M1-2 |


## 未通过项

| # | 现象 | 复现步骤 | 状态 |
|---|---|---|---|
| — | 无 | — | — |

## 本次会话执行记录（M0，全部为实测值）

> 会话 `2026-11-03 m0-skeleton`，`unity status` 当时读到 **端口 7801**（PID 3512，工程 `D:\unity_projects\plugin-package`，6000.6.7f1）。
> **【下一会话更正】**同一 PID 3512 后续再读为 **7800** ⇒ 端口读数在同一实例上会漂移，
> 本条仅保留"当时读到 7801"这一观测事实；由此得出的"实例已换 / AGENTS §1.3 过时"推论**不成立、作废**（详见交接单 §4）。
> 所有实时通道操作以**发指令前的即时查询**为准，且指令失败时先重试再判定故障。

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

## 本次会话执行记录（M1 基础设施，2026-11-03 会话 `m1-foundation`）

### §M1-1 测试总量与结果（实测）
```
unity command "test run" --mode editor --timeout 300
→ Summary: Total 56 / Passed 56 / Failed 0 / Skipped 0
```
- 参数实测纠正两处：`test list` 用 `--mode edit` 可接受，但 **`test run` 只接受 `editor|playmode|all`**
  （传 `edit` 会返回 `Invalid mode 'edit'` 且 Summary 全 0，容易被误读成"没有测试"）；
  `test run` 是异步的，`--json` 首帧即返回 `Total:0`，须读重定向后的完整响应或轮询 `test status`。
- 测试发现性核对：`test list` → `Count=54`（本轮增至 56），Assembly 列全部为 `com.collin.citymap.Tests`，
  说明测试程序集（`UNITY_INCLUDE_TESTS` + `overrideReferences` + `nunit.framework.dll`）配置正确。

### §M1-2 交锁三项门禁（实测）
| 门禁 | 指令 | 结果 |
|---|---|---|
| §1.6a 编译 | `script recompile_status` | `failed=False`、`errors=0`；`console status` `compilationFailed=False` |
| §1.6b 控制台 | `console clear` → 重跑（测试 + 校验器）→ `console status` | `consoleErrors=0`、`consoleWarnings=0` |
| §1.6c 校验器 | `python Tools/verify_project.py` | `插件包: 3 个`、`FAIL 0` |

**门禁相关的一个坑（新会话务必照做）**：测试**故意**触发的 error（"拒绝重复 stableId"的 `LogError`、
阶段抛异常的 `LogException`）会真实计入 `consoleErrors` ⇒ 交锁门禁红。
`LogAssert.Expect(...)` 只让**测试**不失败，**不回退控制台计数**；且这两条堆栈含本包程序集 ⇒
不适用 §1.8 的环境噪声豁免（不该为此往 `console_allowlist.json` 塞条目）。
正解：断言期间 `Debug.unityLogger.logEnabled = false` + `finally` 复位（生产代码路径不改）。

### §M1-3 H-29（实现抓到，测试首跑即红）
`H29_OccupiedReloc_...` 期望"不触发 Parcel"，实际得到 `Parcel(=128)` —— 因我把文档"→S3 复检"直译成"置 `Road` 位"。
修法：`OccupiedReloc` 设为**终端位**（`Next()` 不写传播），由 `StageClaims` 让 S0/S3/S6 各自 `DependsOn` 认领。
中途引入的 `Recheck` 位属多余概念，已撤销。

### §M1-4 H-30（接口粒度不足，脏位归因静默错误）
`CityProject.Kind` 恒返回 `WorldParam` ⇒ 「设为城心」被映射为 `Seed|World` → `Expand` → `All`：
**H-7 的修复被这条接线抵消**（城心重新变成一动即全城重算），且面板脏位显示错误、全程无报错。
修法（契约级）：`IntentChangeLedger` 按通道记账 + `IntentKind` 升 `[Flags]` + `DirtyOf` 逐位求并集
（`switch` 单值匹配会让组合值落 `default` 返回 `None` = 静默丢脏）；通道 `Sequence` 必须 `[SerializeField]`
（Unity Undo 只还原序列化字段，否则 undo 后序列号不变、Tracker 看不见 = H-9 的漏检形态）。
守卫：`Channels_On_One_Object_Do_Not_Pollute_Each_Other`、`Tracker_Dirties_On_Change_And_On_Value_Rollback`。

### §M1-5 顺带清掉的门禁隐患
按通道改造后 `Tracker` 里的 `structural` 与 `ComputeSetHash(out int count)` 成了"只赋值不读取"的死变量
⇒ 会产生 warning ⇒ 破坏 §1.6b。已全部删除（不用 warning 抑制手段绕过门禁）。

### §M1-6 面板/阶段认领已收敛为单一真源
`CityWindow` 曾自持一份私有 `StageBitOf` 映射、`AssembleStage` 另写一份 `DependsOn`（实测核实存在两处）。
现统一为 `StageClaims.Of(stage)`：面板一行转发，阶段属性直接读它。
`IntentChangePlanner`（位→位传播）与 `StageClaims`（位→阶段认领）两张表缺一不可，且都必须只有一处实现。

## 遗留（不属于失败项，按里程碑推进）

- A-1~A-44 中与算法/工具绑定的条目：随 M2~M11 实现逐条取证。已实测的是规则表与结构类条目（见上表 9~20）。
- A-45~A-51（Modules/11 冻结项）**已实测通过**；`BuildingRecord` 结构**已冻结**（用户 2026-11-03 确认）。
- 未实测且当前无法验证的：所有需要地形/河/路/场域/建筑真实数据的断言（A-2~A-8、A-26~A-44、A-48 的端到端形态）。
- `Sample` 小城与演示视频：M10。
- 本文档为覆写式，非追加日志（AGENTS §4）。
