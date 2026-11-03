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
| 21 | **M2** 噪声内核为纯函数、值域正确、不退化不周期化，octave/波长设置生效，域扭曲改变采样但仍纯；同种子逐位一致、异种子必变（A-0-1 / A-1-1 的噪声侧） | `Noise_Is_Pure_And_Within_Range` / `Noise_Is_Not_Too_Periodic_Or_Degenerate` / `Fbm_Respects_Octave_And_Wavelength_Settings` / `DomainWarp_Changes_Sample_But_Is_Pure` / `A1_Same_Seed_Produces_Bitwise_Identical_Base` / `A1_Different_Seed_Changes_Terrain` | qianwen-work-assistant / `2026-11-03 m2-terrain` | 2026-11-03 | **通过**（见 §M2-1） |
| 22 | **M2** `urbanFlatness` 旋钮真实生效：城内低频起伏显著低于城外（A-1-1） | `A1_1_UrbanFlatness_Controls_Inner_Roughness`（2400m 世界、800m≈1.5λ 取窗，含"低频确实存在"的自校验断言） | 同上 | 2026-11-03 | **通过**（断言口径为第 5 处自纠错，见 §M2-2） |
| 23 | **M2** 逐格种子局部稳定：改图幅/改远处不重算已有格（A-0-1 空间侧）；取消不产出"报 OK 的半成品" | `A0_1_Base_Is_Locally_Stable_Per_Cell_Seed` + `A1_7_Change_Far_From_Constraint_Leaves_Remote_Cells_Bitwise_Identical` + `Cancellation_Stops_S1a_Without_Partial_Garbage_Being_Reported_Ok` | 同上 | 2026-11-03 | **通过**（H-31 首跑失败→修城心回退后通过，见 §M2-3） |
| 24 | **M2** 约束求解把受约束区坡度压进红线，且**压平后坡度不得反升**（A-1-3） | `A1_3_Constrained_Parcel_Becomes_Within_Slope_Limit`（逐地块核心坡度打印诊断） | 同上 | 2026-11-03 | **通过**（H-34/H-35 为本轮实现发现，见 §M2-4） |
| 25 | **M2** 拟合稳健性：缓坡保留目标坡向、陡区压平、中位数抗单点离群、欠采样拒绝、平面过中心高程（A-1-4） | `A1_4_*` / `Fit_Plane_Passes_Through_Center_Elevation` / `Fit_Median_Is_Robust_To_Single_Outlier` / `Fit_Rejects_Under_Sampled_Region` | 同上 | 2026-11-03 | **通过**（H-32/H-33 为本轮实现发现，见 §M2-4） |
| 26 | **M2** 权重带几何正确：核心全权重、衰减带单调过渡、带外零权、`AffectedBounds` 覆盖衰减带、径向模式内半径正确 | `Constraint_Full_Weight_Inside_And_Zero_Beyond_Band` / `Constraint_Affected_Bounds_Covers_Decay_Band` / `Radial_Mode_Uses_Inner_Radius_From_HalfSize_X` | 同上 | 2026-11-03 | **通过**（见 §M2-5） |
| 27 | **M2** S1b 语义：幂等无漂移、绝对高程区硬覆盖、零权重区被忽略且计数、缺 Profile 响亮失败、实例间不共享 scratch（H-4/H-31） | `A1_2_S1b_Is_Idempotent_No_Drift_On_Rerun` / `Absolute_Height_Zone_Holds_Target_Elevation` / `Zero_Weight_Zone_Is_Ignored_And_Counted` / `S1b_Without_Profile_Fails_Loudly_With_Reason` / `Two_Instances_Do_Not_Share_Scratch_Buffers` / `Constraint_Strength_Makes_Parcel_Core_Almost_Fully_Adherent`（倍率 8/4/1 与贴合度分测） | 同上 | 2026-11-03 | **通过**（见 §M2-6） |
| 28 | **M2** 数据层读取正确 + 全部改动后交锁三项门禁仍成立 | `Sample_Is_Continuous_Between_Cell_Centers` / `Normal_And_Slope_Agree_With_Known_Plane` / `Quant_Roundtrip_Losses_Stay_Within_One_Step` / `EnsureField_Rebuilds_When_Grid_Size_Changes` + §1.6 三门禁 | 同上 | 2026-11-03 | **通过**（见 §M2-7 / §M2-8） |


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

## 本次会话执行记录（M2：S1a 基底噪声 + S1b 平整约束，全部为实测值）

> 会话 `2026-11-03 m2-terrain`，客户端标识 qianwen-work-assistant，持 `com.collin.citymap` 锁。
> 测试规模：`test list` 由 56 → **84**（新增 `Tests/Editor/TerrainDataTests.cs` 28 条地形断言）。
> 最终复跑：`test run --mode editor` → **Total 84 / Passed 84 / Failed 0**。

### §M2-1 噪声内核（纯函数 + 确定性）
`CityNoise.cs` 为纯 C# 自研内核（value noise + fbm + domain warp，splitmix64 派生子种子），**不依赖 Unity PerlinNoise**
—— 后者跨平台/跨版本不保证逐位一致，会破坏"同种子同图"的地基承诺。
实测：`Noise_Is_Pure_And_Within_Range`（同输入两次调用同值、值域闭合）、
`Noise_Is_Not_Too_Periodic_Or_Degenerate`（非退化且无格点周期性）、
`Fbm_Respects_Octave_And_Wavelength_Settings`、`DomainWarp_Changes_Sample_But_Is_Pure`、
`A1_Same_Seed_Produces_Bitwise_Identical_Base`（逐位比对）、`A1_Different_Seed_Changes_Terrain`。

### §M2-2 `urbanFlatness` 旋钮（控制台实测数值）
测试内 `Debug.Log` 打点，控制台原样读数（H-37 修复后的最终值）：
```
[M2] urbanFlatness 含细节 0→0.813m；锁细节纯低频(800m 窗) 0→1.463m
```
断言与实测对应关系：
| 断言 | 文档口径 | 实得 |
|---|---|---|
| `sFlat ≤ 0.2f` | **A-1-1 原文**："=0 时城内标准差 ≤0.2m" | **0m** ✅ |
| `sRough ≥ 0.3f`（接近裸噪声） | A-1-1："=1 时接近裸噪声标准差" | **0.813m** ✅ |
| `sMid == 0.5 × sRough`（±6%） | 旋钮线性（面板拖动跟手） | 0.4065m 期望，实得在容差内 ✅ |
| `lowRough > 1f`（自校验：低频确实铺开） | — | **1.463m** ✅ |
| `lowFlat < lowRough/10` | 低频被真正压掉 | **0m** ✅ |

**两条口径教训（均已写进代码注释）**：
1. **λ 与采样窗口必须同量级**。低频 λ≈520m，用 120m 窗口断言"起山"是物理错误——首跑在此得 0.73m 假失败。
   现改为世界 2400m、取窗 200 格（≈1.5λ），并**先断言 `lowRough > 1f` 证明低频存在**，再谈压掉多少
   （比值型断言若不先验分母非退化，可以长期"什么都没测到"却看起来合理）。
2. **文档给了绝对数值，断言就必须用那个数值**（→ 由此抓出 **H-37**，见 Modules/09 §19）：
   我原先把阈值写成 `0.6f`（凭"城内还剩细节噪声"的直觉放宽），实现就因此**躲过了一条真实的 A-1-1 违规**
   ——细节层完全绕过旋钮，`flatness=0` 时实测 0.236m > 文档 0.2m。

### §M2-3 空间局部稳定性
`A0_1_Base_Is_Locally_Stable_Per_Cell_Seed`：600m 与 1200m 两档图幅**先把城心钉成同一锚点意图**
（`TrySetCenterAnchor(300,300,270)`）再比对 ⇒ 原有格高度 0 处不一致。
补两条护栏断言：`CellCount` 必须不同（否则"扩图幅"没真的发生）、循环必须有实际样本。
`A1_7_Change_Far_From_Constraint_Leaves_Remote_Cells_Bitwise_Identical`：远端格**逐位不变**（容差写死 `0f`），
且被改格数 < 全图一半 ⇒ 证明"只遍历约束外接盒"真的生效（这是 A-2 增量重算的合法性前提）。

### §M2-4 约束求解效果（H-32/33/34/35 的数值证据）
```
[M2] A1_3 地块核心最大坡度 4.79° → 1.41°（红线 5°，约束 9 条，改动 293764 格）
```
- 上限断言：`after ≤ maxParcelSlope(5°) + 1.5°` 且 `after < before`。实得 **1.41°**，余量 3.6°（远优于容差）。
- 这条断言在 H-34/H-35 修复前是 **4.79° → 65.48°**（压平反而制造陡坎，见 Modules/09 §15）。
  即：**同一份代码、同一批种子，仅靠"核心区留内切 + 自动地块之间留街道间隙"，把结论从"几何上不可满足"变成"满足且余量 3 倍"**。
- `改动 293764 格` ≈ 全图（1200m/2m ⇒ 600×600=360000）的 82%：自动试设地块为 3×3 且铺满图幅，衰减带相互重叠，
  故触及面接近全图属预期；M5 真实路网下改为按走廊外接盒，该数字会显著下降（届时须重取证据）。
- 拟合稳健性四条：`Fit_Plane_Passes_Through_Center_Elevation`（H-32：截距口径）、
  `Fit_Median_Is_Robust_To_Single_Outlier`（H-33：+395m 单点离群 ⇒ 水平偏移 0.816m 的均值行为已消除）、
  `Fit_Rejects_Under_Sampled_Region`、`A1_4_Fit_Preserves_Gentle_Slope_But_Flattens_Steep_Area`。

### §M2-5 权重带几何
`Constraint_Full_Weight_Inside_And_Zero_Beyond_Band` / `Constraint_Affected_Bounds_Covers_Decay_Band` /
`Radial_Mode_Uses_Inner_Radius_From_HalfSize_X`：带外点一律用 `plane.CoreHalfSize / ExtendMeters`
（与实现同一算法，即 `MaxInsetFor`/`ComputeCoreHalf` 单一真源）计算，不再用纸面直觉 `30+12`。

### §M2-6 S1b 求解语义
`A1_2_S1b_Is_Idempotent_No_Drift_On_Rerun`（跑两次最大偏差 ≤1e-5，钉死"就地叠加正反馈漂移"风险）、
`Absolute_Height_Zone_Holds_Target_Elevation`（硬覆盖绕过软融合上限）、
`Zero_Weight_Zone_Is_Ignored_And_Counted`、`S1b_Without_Profile_Fails_Loudly_With_Reason`、
`Two_Instances_Do_Not_Share_Scratch_Buffers`（scratch 由 static 改实例字段，H-4 类跨上下文污染）、
`Constraint_Strength_Makes_Parcel_Core_Almost_Fully_Adherent`：强度倍率 **Parcel=8 / RoadCrown=4 / WaterBuffer=1**，
核心贴合度 ≥0.9；贴合度是趋于饱和的比值，**禁止按倍率线性放大断言**（我首跑就犯了这个错）。

### §M2-7 数据层读取
`Sample_Is_Continuous_Between_Cell_Centers`、`Normal_And_Slope_Agree_With_Known_Plane`
（差分坡度对解析值 `atan(0.1)` 误差 ≤0.15°；法线约定 `n=(-dh/dx,1,-dh/dz)` ⇒ 上坡方向 x 分量为**负**）、
`Quant_Roundtrip_Losses_Stay_Within_One_Step`、`EnsureField_Rebuilds_When_Grid_Size_Changes`。

### §M2-8 交锁三项门禁（实测·H-37 修复后最终复跑）
| 门禁 | 指令 | 结果 |
|---|---|---|
| §1.6a 编译 | `script recompile_status` | `status=completed`、**`failed=False`、`errors=0`**；先做程序集新鲜度（`dll=tests=1793692748 ≥ 源码`）再跑 |
| §1.6b 控制台 | `console clear` → 重跑全量测试 + 校验器 → `console status` | `test run --mode editor` → **Total 84 / Passed 84 / Failed 0**；`consoleErrors=0`、`consoleWarnings=0`、`seeded=True`（`consoleLogs=10`）；校验器 `console: error=0 warning=0` |
| §1.6c 校验器 | `python Tools/verify_project.py` | `插件包: 4 个`，本包相关检查项**全部 `[ ok ]`**（`pkg-layout` / `runtime-purity` / `dep-*` / `asmdef` / `showcase` / `manifest-sync` / `hygiene` / `console`）；**整体仍判红灯**，原因见下 |

**红灯的唯一来源是别包的锁心跳超时，与本包改动无关**（§1 第 2 步要求"红灯先处理或如实上报"，此处选择如实上报，不自行处理他人锁）：
- `com.collin.core.lock`（session `demo-video-2026-11-03`）超时 5153s；
- `com.collin.stretch.lock`（同 session）超时 5153s；
- `com.collin.stealth.lock`（session `stealth-m0-2026-11-03`）在最后一次复跑时超时 489s。
三者 holder 字符串同为 `qianwen-work-assistant`，但 **session 不属于本会话** ⇒ 按 §3
"心跳超时锁：不得自动删除，报告用户处理"。**待用户裁定**（续心跳或代为释放）。
本包 `com.collin.citymap.lock` 工作期内每轮复跑都刷新心跳，校验器只报 `lock-active` WARN。

### §M2-9 本会话踩到并记录的两个流程坑
1. **H-36（dll 未刷新）**：`script recompile_status` 返回 `up_to_date / failed=False` 但
   `Library/ScriptAssemblies/com.collin.citymap.Tests.dll` 的 mtime 早于源码 ⇒ 测试跑旧程序集，
   表现为"改了代码、失败数值一字不差重复"。判别法只有一条：**比 mtime**（dll ≥ src）。
   本会话此后每次改码都先做新鲜度核验，因此后续全部为真值。
2. **控制台门禁会被我自己的指令污染**：把 `console get --tail 60` 当成单条指令名传入，
   实际得到 `ExecuteCommandByName: No command named ...` ⇒ **`consoleErrors` 从 0 变 1**。
   正确写法是 `console get` + 独立参数 `--tail 60`。教训：取归零证据期间**任何**指令拼错都会自己制造红灯，
   所以归零序列要么一次写对，要么重跑一遍取新证。

## 遗留（不属于失败项，按里程碑推进）

- A-1~A-44 中与算法/工具绑定的条目：随 M2~M11 实现逐条取证。已实测的是规则表与结构类条目（见上表 9~28）。
- A-45~A-51（Modules/11 冻结项）**已实测通过**；`BuildingRecord` 结构**已冻结**（用户 2026-11-03 确认）。
- 未实测且当前无法验证的：所有需要地形/河/路/场域/建筑真实数据的断言（A-2~A-8、A-26~A-44、A-48 的端到端形态）。
- `Sample` 小城与演示视频：M10。
- 本文档为覆写式，非追加日志（AGENTS §4）。
