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
| 29 | **M3** 显示顶点落在「首格中心内缩一格」的探针安全域，且**每个**顶点高度对解析平面做绝对比较（A-1 显示侧） | `Plane_Field_Reproduces_Exact_Heights_And_Normals_On_Mesh`（75×75 网格里逐点断言，偏差阈值 1e-4m） | qianwen-work-assistant / `2026-11-03 m3-view` | 2026-11-03 | **通过**（H-40 为本轮实现缺陷，两轮才收敛，见 §M3-2） |
| 30 | **M3** 顶点法线 = `GetNormal` 差分口径，**不用** `RecalculateNormals`（01 §3.3） | 同上用例的第 ③ 段（解析法线 `normalize(-A,1,-B)` 与逐点法线比较，阈值 0.05°） | 同上 | 2026-11-03 | **通过**（若改用几何平均，本条实测会红到 7.96°/16.25°） |
| 31 | **M3** 三角形绕序使地表朝上（否则被背面剔除 = 用户看不到地形） | `Triangle_Winding_Makes_Surface_Face_Up`（逐三角形几何法线点积 ≥0.9 + 索引不越界 + 三角形数 = quad 数×6） | 同上 | 2026-11-03 | **通过**（H-38 首跑即红：点积 **−0.992**，见 §M3-1） |
| 32 | **M3** chunk 尺寸对齐 displayStep **向上**取整、图外/退化 chunk 不产出、末尾按数据域截断 | `Chunk_Size_Snaps_To_Display_Step_Multiple` / `Chunk_Vertex_Rect_Covers_Full_Map_And_Clips_At_Edge` / `Degenerate_Chunk_Request_Is_Clamped_Not_Crashing` | 同上 | 2026-11-03 | **通过**（H-41：`RoundToInt` 使 100→96 比请求值更小且依赖舍入规则，见 §M3-3） |
| 33 | **M3** 脏区→chunk 映射为最小集合（局部改动绝不触发全图） | `Dirty_Region_Maps_To_Minimal_Chunk_Set`（点状盒恰命中 **1** 块；30m 盒 < 全图；逐块相交复核 + 无重复） | 同上 | 2026-11-03 | **通过**（H-42：余量误取整块 chunk ⇒ 点状改动放大成 9 块，见 §M3-3） |
| 34 | **M3** 增量写 mesh 走局部更新、拓扑变化才重建；超 65k 顶点自动升 32bit 索引 | `Apply_To_Mesh_Keeps_Topology_On_Resize_And_Grows_When_Needed` / `Mesh_Grows_To_Uint32_Only_When_Vertices_Exceed_16bit_Index` | 同上 | 2026-11-03 | **通过**（H-39：`SetVertexBufferData` 抛「copy 1452 bytes into a **0 byte buffer**」，见 §M3-1） |
| 35 | **M3** 坡度顶点色可调、可辨、超参考值钳住；关闭时写纯白 | `Slope_Colors_Are_Banded_And_Monotonic`（单调判据 = **红绿差** r−g：−33→26→102）/ `Slope_Colors_Off_Writes_White_VertexColors` | 同上 | 2026-11-03 | **通过**（H-43 是我的断言口径错，见 §M3-4） |
| 36 | **M3** 场景里真的出现地形：自建路径与面板驱动路径两条都产出 chunk 物体与 mesh，派生态不入库 | `Standalone_Rig_Builds_Chunk_Children_And_Meshes`（16/16 chunk、顶点总量 >100、`HideAndDontSave` 逐项断言）/ `Panel_Context_Takes_Precedence_Over_Standalone_And_Only_Rebuilds_On_Commit` | 同上 | 2026-11-03 | **通过**（实测 `[M3] 自建路径：显示 16/16 chunk · 逻辑 200²格 @2m · 显示 8m · chunk 128m`，见 §M3-5） |
| 37 | **M3** 显示开关/尺寸变化/数据源缺失三类状态转换正确（不残留、不静默空白） | `Hiding_Terrain_Destroys_Chunks_And_Showing_Rebuilds_Them` / `Changing_Chunk_Size_Rebuilds_Without_Leftover_Old_Chunks` / `Missing_Data_Source_Fails_Verbose_Instead_Of_Blank` | 同上 | 2026-11-03 | **通过**（实测 `[M3] chunk 数随尺寸变化：200m=4 100m=16 400m=1`） |
| 38 | **M3** 面板已注册真实阶段（M1/M2 的 Runner 里没有 S1a/S1b，"重新生成"只跑 S0） | `test run --mode editor --filter_type assembly --filter com.collin.citymap.Tests` → **Total 99 / Failed 0**；`test list` → Count **141** | 同上 | 2026-11-03 | **通过**（99 = M1 56 + M2 28 + M3 15；见 §M3-6） |
| 39 | **M3** 全部改动后交锁三项门禁成立，且**校验器整体首次绿灯** | `script recompile_status` failed=false → `console clear` → 重跑本包全量测试 + `verify_project.py` → `console status` | 同上 | 2026-11-03 | **通过**：`consoleErrors=0/consoleWarnings=0/seeded=True`、`FAIL 0 WARN 3`、**结论: 绿灯**（见 §M3-7） |
| 40 | **M3-b** 地形参数值指纹覆盖**每一个**可调字段（漏一个 = 该字段被 Inspector 直改时面板永远检不出） | `Value_Key_Covers_Every_Tunable_Field`（反射枚举全部 `[SerializeField]` 参数逐个扰动比对；枚举自校验 ≥18 个，实得 **20**；漏检字段数 = **0**；复位后指纹回到基线 = 无时间/地址副作用） | qianwen-work-assistant / `2026-11-03 m3-view` | 2026-11-03 | **通过**（见 §M3-9） |
| 41 | **M3-b** 面板参数控件区 + Inspector 直改第二检已落盘并通过编译（§0.1 原则1「所见即所得」的交互面） | `CityWindow.DrawTerrainProfile()` / `DetectDirectProfileEdit()` / `TerrainProfile.ComputeValueKey()`；`script recompile_status` → `failed=False`、`errors=0`（rt+Editor+Tests 三侧 dll mtime ≥ 源码） | 同上 | 2026-11-03 | **编译通过、数据侧已断言；GUI 交互未取证**（见 §M3-9 末段：反射调 GUI 会抛「只能在 OnGUI 内调用」，故滑条手感须你实机确认） |
| 42 | **M3-b** 面板增量交锁门禁复跑（最终） | `console clear` → 静默 → `test run --filter_type assembly --filter com.collin.citymap.Tests` → `verify_project.py` → `console status` | 同上 | 2026-11-03 | **本包 100/100**；**`consoleErrors=0/consoleWarnings=0/seeded=True`**；校验器 **`FAIL 0`、结论: 绿灯**（此前外部红灯已由 stealth 会话自行修复消解，本会话未越界处置，见 §M3-10 / §M3-11） |
| 43 | **M4-a** 地表有材质且**坡度顶点色真的可见**（01 §3.3 交付项，用户反馈①） | `Sample/Shaders/CityTerrainVertexColor.shader` 实测 `Shader.Find(...).isSupported=True`；端到端：反射绑定 CityProject+Profile → `RebuildNow()` → `status=显示 3969/3969 chunk · 逻辑 2000²格 @4m · 显示 16m · chunk 128m`，`MeshRenderer.sharedMaterial.shader.name = Collin/CityMap/Terrain VertexColor` | qianwen-work-assistant / `2026-11-03 m4-terrain-workflow` | 2026-11-03 | **通过**（见 §M4-a-1；根因是 M3 回退链只有 URP/Lit→Diffuse，二者都不读 `COLOR` ⇒ 色带白写） |
| 44 | **M4-a** 插件资产不落 `Assets/`（AGENTS §0） | `python Tools/verify_project.py` → `[ ok ] assets-layout`；`ls Assets/Collin` → 不存在；资产已在 `Packages/com.collin.citymap/Sample/Profiles/TerrainProfile.asset`（`MoveAsset` 保留 GUID，场景引用不断） | 同上 | 2026-11-03 | **通过**（H-44 为本会话自造缺陷、由校验器抓到，见 §M4-a-2） |
| 45 | **M4-a** 面板代建的地形实例脱离面板仍自洽 | 现场读数原为 `无权威上下文且未绑定自建项目 / chunks=0`；补 `SetStandaloneProject(_project)` 后同一路径产出 3969 chunk（见条目 43） | 同上 | 2026-11-03 | **通过**（见 §M4-a-3） |
| 46 | **M4-a** 工作流重排与三级坡度退让已成文，**未动代码** | `Docs/Modules/12-TerrainWorkflow.md`（§1 三段式映射 / §2 退让须早判 / §3 三档语义 / §4 里程碑重排 / §6 风险） | 同上 | 2026-11-03 | **提案待批准**：`hardRetreatSlope` 默认值与"整块丢弃 vs 只削陡角"两项须用户定，批准前不实现（§4 DESIGN 先于代码） |
| 47 | **M4-b 用户裁定**：`hardRetreatSlope = 18°`；陡区处置 = **只削陡角、保留缓部分**（非整块丢弃） | 用户 2026-11-03 直接答复（12 §3 的两个待定参数） | 同上 | 2026-11-03 | **已定案**，12 文档已按此定稿（状态从"提案待批准"改为"已批准"） |
| 48 | **M4-b** 雕刻 delta 层数据结构（01 §2.3）满足契约：稀疏剪枝、Absolute 用 NaN 哨兵、**只进 Heights 绝不进 BaseHeights**、重放顺序确定 | `Tests/Editor/SculptDeltaTests.cs` **16 条**（`test run --filter_type assembly` → 本包 Total **116 / Failed 0**）；关键三条：`Absolute_Target_Zero_Meters_Is_Honored_Not_Dropped`（0m 合法）、`Additive_Applies_To_Working_Heights_Not_Base`（防正反馈漂移）、`Additive_And_Absolute_Withstand_Each_Other_Independently`（含**反序插入结果相同**的 H-45 判据） | qianwen-work-assistant / `2026-11-03 m4-terrain-workflow` | 2026-11-03 | **通过**（见 §M4-b-1；H-45 为本轮自查缺陷） |
| 49 | **M4-b** 交锁三项门禁 | `script recompile_status` failed=false → `console clear` → 重跑本包全量 + `verify_project.py` → `console status` | 同上 | 2026-11-03 | **编译 `errors=0`**；**`consoleErrors=0/consoleWarnings=0/seeded=True`**；`assets-layout` `[ ok ]`；本包 **116/116**。整体 FAIL 1 = 他包 `com.collin.stealth.lock` 超时 560s（非本会话，§3 不代删；沿用用户既定"现在提交、继续持锁"） |
| 50 | **M4-c 用户裁定（D-31）**：雕刻笔刷脏集合由「仅 S6」改为「**S1b + S6**」，仍不脏 S1a | 登记 `DESIGN` §7 **D-31**（坡度三档退让另登 **D-32**）；§3.2 脏传播表雕刻行同步改写 | qianwen-work-assistant / `2026-11-03 m4-terrain-workflow` | 2026-11-04 | **已定案并接线**（见 §M4-c-1） |
| 51 | **M4-c** 雕刻真的波及地块目标平面（不是"叠一下又被压平抹掉"）；且无雕刻时与 M2 结论**逐位一致** | `TerrainDataTests` 新增 **3 条**（本包 116→**119**，Failed 0）：`Delta_Survives_S1b_Rerun_Without_Double_Apply`（幂等双向 + 基底逐位洁净）、`Carving_Inside_Manual_Zone_Raises_Its_Target_Plane`（判别式）、`Without_Delta_Pipeline_Result_Is_Bitwise_Unchanged`（容差 **0f**） | 同上 | 2026-11-04 | **通过**。实测 `[M4c] 未雕刻 0.106 → 雕刻后 20.124（自然 0.023，抬 20m）`（见 §M4-c-1） |
| 52 | **M4-c** H-46：我把自己写错的规则编号引用**提交进了仓库**（c69fbe4），本轮自查发现并全量纠正 | 错引 `D-14`（实为"显示网格分辨率解耦"）指代"雕刻仅脏 S6"；后者住在 §3.2 表且**无编号**。纠正 = 登记 D-31/D-32 + 改 4 处代码/测试注释 + 3 处文档；复核 `grep D-14` 仅剩 5 处合法引用 | 同上 | 2026-11-04 | **已纠正**（见 §M4-c-2） |
| 53 | **M4-c** 交锁三项门禁 | `script recompile_status` → `console clear` → 重跑本包全量 + `verify_project.py` → `console status` | 同上 | 2026-11-04 | **本包 119/119**；**`consoleErrors=0/consoleWarnings=0/seeded=True`**；`verify_project.py` **`FAIL 0`、结论: 绿灯**（他包 stealth 锁此时已被其会话续心跳，不再超时）。见 §M4-c-3 |
| 54 | **M4-d** 笔划强度与帧率无关（**A-1-5 的可执行形态**） | `SculptBrushTests.Same_World_Distance_Produces_Same_Delta_Regardless_Of_Frame_Rate`：同一段 13.7m 位移按 30/60/144Hz 切帧积分，累计增量必须一致（含"积分确实产生量"的自校验断言，防恒真）；`Sub_Step_Movement_Accumulates_...` 验余量跨调用留存 | qianwen-work-assistant / `2026-11-04 m4-sculpt-brush` | 2026-11-04 | **通过**（见 §M4-d-1） |
| 55 | **M4-d** 衰减核语义正确：核心内全强、 EaseOut 单调、退化输入不崩不 NaN | `Falloff_Is_Full_In_Core_...`（含逐点单调扫描）、`Degenerate_Radii_Return_Zero_Not_Nan`；实现侧新增 **core 钳进 [0,radius]**（原实现 `core>radius` 时"核心内全强"分支会吞掉半径截止，画出比笔刷更大的影响区） | 同上 | 2026-11-04 | **通过**。两条首跑失败均已归因（见 §M4-d-2） |
| 56 | **M4-d** 一笔划影响格数**截断而非静默**（01 §5.4）、重复格不双计、新笔划不残留上笔截断 | `SculptStroke` 三条状态机断言（上限 100 喂 500 点 → 恰 100 + `Truncated=true`；`Begin` 重置；同格 50 次 → `CellCount==1`） | 同上 | 2026-11-04 | **通过** |
| 57 | **M4-d** 交锁三项门禁 | 清台 → 静默 → 本包全量 → `verify_project.py` → `console status` | 同上 | 2026-11-04 | 本包 **128/128**；`console` 检查 **`error=0 warning=0`**；校验器 **`FAIL 0`、结论: 绿灯**（含 `pkg-layout`/`runtime-purity`/`assets-layout` 全 ok）。见 §M4-d-3 |
| 58 | **前提核实（如实上报）**：`S3_Road`/`S4_Parcel` **尚无实现体**，故用户裁定的"steepMask 在 S3/S4 内现算"**当前无落点** | `ls Runtime/Core` 仅 `AssembleStage`/`TerrainBaseStage`/`FlattenSolveStage`；`StageId` 与 `DirtyBits.Of` 里 S3/S4 有枚举位但无对应类 | 同上 | 2026-11-04 | **已上报用户**：steepMask 顺延至路网阶段落地时实施；本轮改做不依赖它的场景笔刷交互壳（A-1-5/A-1-6 半边已在 M4-d 立住） |
| 59 | **M4-e** 场景笔刷交互壳编译通过且无回归；一处**自造缺陷 H-47**（臆造 API）被编译器当场抓住并整体重写 | `Editor/SculptBrushTool.cs` 首版报 3 错：`CS0452`（CityGrid 是值类型，不满足 `GetSlot<T> where T:class`）+ 2×`CS1061`（对值类型用 `HasValue/Value`）；另有残缺语句与不存在的 `CityRegistry`。按逐条核实过的真实签名重写 → `recompile_status failed=False / errors=0`，Editor dll mtime ≥ 源码；本包全量 **128/128 Failed 0** | 同上 | 2026-11-04 | **通过**（见 §M4-e-1） |
| 60 | **M4-e** 交互壳职责边界：仅"雕刻"模式接管鼠标、笔刷参数改静态字段不触发重生成、松手才提交脏区且用**笔刷中心**（非 transform 位置）、一笔 = 一个 undo、超格数**提示截断不静默**、未接线动作**明确拒绝** | 源码审阅 + 编译验证：`CityWindow.CurrentMode != TerrainSculpt` 直接 return；`StampRadius` 走 `SculptBrush.DeltaFor/FalloffAt/SmoothDelta`（纯数学）；`EndStroke` 调 `_terrain.MarkDeltaChanged(_lastCenter, RadiusMeters)`；`BeginStroke` 前置 `Undo.RecordObject`；`_stroke.Truncated` → `Hud(...)`；`ActionSupported` 白名单 | 同上 | 2026-11-04 | **实现到位**；行为断言的 Runtime 半边由 M4-d `SculptStroke` 三条状态机用例覆盖 |
| 61 | **M4-f** 面板雕刻参数区（半径/强度/动作）仅雕刻模式显示，动作下拉**只列已接线四项** | `CityWindow.DrawSculptPanel()` 提交 f70fcc2；自查修掉一处类型错误（`Popup` 第三参需 `string[]`，初版误传枚举数组，同时使标签数组沦为死字段）；编译 `errors=0` | 同上 | 2026-11-04 | **通过** |
| 62 | **M4-e/M4-f** 交锁三项门禁（两次增量各自复跑） | 清台 → 本包 `--filter_type assembly` 全量 → `verify_project.py` → `console status` | 同上 | 2026-11-04 | 两次均 **128/128**；校验器 **`FAIL 0`、结论: 绿灯**（交互壳那次 `console 0/0`；面板那次 `error=1` **全部命中 §1.8 已批准豁免清单**，被正确降为 WARN、无虚假绿灯）；`pkg-layout`/`runtime-purity`/`assets-layout` 全 ok |
| 63 | **端到端实机闭环（落笔→delta 写入→重算→画面刷新）尚未跑通**，如实标注 | 反射驱动 `BeginStroke/ApplyDrag×3/EndStroke` 做真笔划验证时返回 **`terrain=NULL hasGrid=False`**——体验会话被域重载重置、派生 chunk 物体 `HideAndDontSave` 不入库，场景内已无 CityTerrain 实例；该次运行**未写入任何数据**（前置校验即返回），现场未被污染 | 同上 | 2026-11-04 | **待实测**（非失败项，见遗留清单）：需用户在场景「创建地形实例」后拖笔验证。当前证据链 = 数学内核 9 条单测 + 编译/无回归，**不含实机手感** |
| 64 | **M4-g 平整以阶段闸门介入（D-33，用户否决逐格退让后重做）** | `CityTerrain.FlattenEnabled`（默认开）+ setter 走 ParcelModel 通道触发重算；`FlattenSolveStage` 在 `CommitBaseAsWorking()+Delta.ApplyTo()` 之后、收集约束之前整段短路。断言 `Flatten_Off_Is_Phase_Gate_Not_Per_Cell_Waiver`：**关平整 → 抬 25m 原样保留、`Outputs=0`、重跑仍 25m（关闭态幂等）；开平整 → `Outputs=9` 条约束、改动 76729 格** | qianwen-work-assistant（会话 `2026-11-04 m4-brush-fix`） | 2026-11-04 | **通过**（见 §M4-g-1） |
| 65 | **M4-g 笔刷范围可见（用户反馈①）**：预览环由 y=0 平面改为**贴地表采样描边**，且绘制**先于**落笔前置条件（旧版数据不全时在画环前 return ⇒ 连环都没有、也无提示） | `SculptBrushTool.OnSceneGUI`：48 段折线逐段取 `field.GetHeight(x,z)+0.08f` 贴地绘制外圈与核心圈；编译 `errors=0`、本包 129/129 无回归 | 同上 | 2026-11-04 | **通过（代码与编译）/ 观感待用户实机确认**（SceneView 绘制不可 EditMode 断言，不虚报） |
| 66 | **M4-g 交锁三项门禁 + 违规资产处置** | 清台 → 本包 `--filter_type assembly` 全量 → `verify_project.py` → `console status`；`assets-layout` 抓到 `Assets/Scenes/testscene.unity`（非本会话产物）经用户裁定删除 | 同上 | 2026-11-04 | **129/129**；`RECOMPILE failed=False errors=0`；`GATE consoleErrors=0 consoleWarnings=0 seeded=True`；校验器 **`FAIL 0`、结论: 绿灯** |
| 67 | **M4-h 显式三步步骤条（12 §1 三段式的 UI 面，用户选"升级成显式步骤条"）** | `CityWindow.DrawWorkflowStepBar()`：① 地图参数 / ② 雕山水 / ③ 平整出地表 三个按钮，点一步＝把该相位该有的状态设好（② 关 `FlattenEnabled`+进雕刻模式，③ 开闸门+回检视）。**相位非门禁**（00 A-24）：其它工具与参数仍随时可用；高亮由真源 `FlattenEnabled`+当前模式回推（`SyncStepFromState`），杜绝"面板显示第③步、闸门实际还关着"的两套状态。编译 `errors=0` | qianwen-work-assistant（会话 `2026-11-04 m4-brush-fix`） | 2026-11-04 | **通过（代码/编译/无回归）；观感与手感待用户实机确认**（Editor GUI 不可 EditMode 断言，不虚报） |
| 68 | **M4-h 违规/副本资产清理**：`Sample/Profiles/TerrainProfile 1/2/3.asset`（用户体验点"新建"产生的副本，GUID 均无任何场景/资产引用、未入库）经**用户裁定删除**，走 `AssetDatabase.DeleteAsset`；真源 `TerrainProfile.asset` 保留 | 删前核验三项：`EditorBuildSettings` 计数 0、`grep guid` 无引用者、`git status` 为 `??` 未跟踪；删后 `assets-layout` 由 FAIL 恢复 **ok** | 同上 | 2026-11-04 | **通过**；校验器整体转 **`FAIL 0`、绿灯** |
| 69 | **M4-i 根因取证（用户要求"先定根因再改"）**：四项全部实测取证，不靠推测 | `script eval_file` 反射探针（`.tmp-inspect/`，用完即删）：① `_instance=NULL(面板未开)` 且 `CurrentMode=Inspect`，与用户截图（面板在雕刻模式）矛盾 ⇒ 静态字段被域重载清零；② `Tools.activeToolType=UnityEditor.MoveTool` ⇒ 左键落给默认工具（框选）；③ `CityTerrain.Delta=NULL` + `FindAssets(t:SculptDeltaAsset)=0` ⇒ 雕刻层从来不存在；④ `duringSceneGui 钩子数=3` 含 `SculptBrushTool.OnSceneGUI` ⇒ 回调注册正常，排除"没挂上" | qianwen-work-assistant（会话 `2026-11-04 m4-brush-rootcause`） | 2026-11-04 | **根因确认**：反馈①②同源（H-50 门禁真源错位），非幅度/非渲染问题 |
| 70 | **M4-i H-50 修复：工具接管态真源进场景实例（D-34）** | `CityTerrain.SculptToolActive`（序列化，默认 false）+ `SetSculptToolActive`（**不 Bump**）；笔刷 `ToolActive(out terrain)` 只读注册表实例；`CityWindow.OnEnable` 重绑 `_instance`；`SyncStepFromState` 由真源回推 `_mode`（**仅当真源存在才纠正**，无实例时不强弹，否则用户连"缺实例"提示都看不到）。端到端探针：未开启 `ToolActive=False` → 开启 `ToolActive=True 解析实例=__e2e_terrain` | 同上 | 2026-11-04 | **通过**；`Sculpt_Tool_Active_Is_Pure_Tool_State_No_Intent_Bump`（含 SetFlattenEnabled 对照 +1，防空断言）；本包 **130/130** |
| 71 | **M4-i H-51/H-52/H-53 修复**：雕刻层自动就位、包内资产固定路径、光圈取数与画面同源 | `EnsureDeltaLayer()`（创建实例与进②步时调用）；`FindOrCreateProfile` 改固定路径（探针实证 `FindAssets` 对内嵌包恒 0）；`CityTerrainView.BoundField` 只读暴露，笔刷 `CurrentField()` 优先读它。端到端：`EnsureDeltaLayer 后 Delta=SculptDeltaAsset path=…/SculptDelta.asset 可再次加载=True`；`BlockReason` 由面板黄字呈现（不再只打控制台） | 同上 | 2026-11-04 | **通过（代码/编译/端到端探针）**；**光圈可见性与拖笔手感仍待用户实机确认**（SceneView 渲染不可 EditMode 断言，不虚报） |
| 72 | **M4-i H-54 附带发现：包内资产导入记录损坏**（控制台 warning `No script asset for TerrainProfile` 的真因） | 对照实验：`GetMainAssetTypeAtPath(原文件)=NULL`；**同内容复制文件（新 GUID）同样 NULL**；Unity 自身 `CreateAsset` 产物 `可加载=True`；`ImportAsset(ForceUpdate)` 无效（内容哈希未变复用旧 artifact）⇒ 判定为 Library 侧该 GUID 记录坏，非 YAML 内容坏（两者逐字节一致）。处置：`CreateAsset` 覆盖同路径重建，实测 **GUID 不变**（`49770fd0…→同值`，既有引用不失效）、类型恢复 `TerrainProfile` | 同上 | 2026-11-04 | **已修复**；`LoadAssetAtPath(原路径)=True`，副本 `TerrainProfile 1.asset` 一并删除 |
| 73 | **M4-i 交锁门禁** | 清台 → 静默 60s 取基线（`error=0 warning=0`，无噪声需归因）→ 重跑本会话全部验证动作（recompile → 本包 test run → `verify_project.py`）→ `console status` | 同上 | 2026-11-04 | `RECOMPILE failed=False errors=0`；`Tests.dll` mtime ≥ 测试源 mtime（H-36 第二检）；**130/130**；`consoleErrors=0 consoleWarnings=0 seeded=True`；校验器 **`FAIL 0`、结论: 绿灯** |
| 74 | **M4-i 校验器红灯成因与处置（非本会话产物）**：`assets-layout: Assets/Scenes/TerrainTest.unity` | 取证三项：该场景 mtime `22:12:16` **早于本会话首个动作（22:25 之后）**；`EditorBuildSettings.scenes` 仅含 SampleScene（不命中本场景）；全工程 `.asset/.unity/.prefab/.json` 文本扫描**外部引用计数 = 0** | 同上 | 2026-11-04 | **经用户裁定"移进包内 Sample/"**：`AssetDatabase.MoveAsset` → `Packages/com.collin.citymap/Sample/Scenes/TerrainTest.unity`，返回空串（成功）、**GUID 不变**（`d159991b…` → 同值）、旧路径 `AssetPathToGUID=""` 且磁盘旧文件已消失；`OpenScene` 复验 `有效=True 根物体=2 脏=False`。另按用户裁定删除根目录 5 个上一会话残留 `.tmp-*.json`（mtime 11:14–11:46，逐个核过内容为 `test run` 输出） |
| 75 | **M4-j 卡顿根因量化取证（用户反馈「每笔非常卡、要读条」）** | 三轮 `script eval_file` 探针：① 耗时分解 `S1a=4860ms / S0=114ms / S1b≈0ms`（⇒ 卡顿不是算法慢，是每笔重跑噪声）；② 自建路径完整重跑 3 轮 **5253 / 5234 / 5529 ms/笔**，`new ctx + 3 阶段 + MarkDirty(All) + Tick`；③ 面板路径对照 `MarkDirty(Flatten|Building)` = **3~9ms**；另 `ComputeConfigKey` 落笔前后逐位不变（`14071847608170291442`）证明"算了不画"；数据侧 `Heights−BaseHeights` 最大正差 **181.11m**（BaseHeights 全图最大 5.36m）证明雕刻确实生效 | qianwen-work-assistant（会话 `2026-11-05 m4-j-perf`） | 2026-11-05 | **根因确认**：面板「未装配」（`_ctx` 瞬态丢失）⇒ 显示层无声回落自建路径 ⇒ 违反 D-31/A-2 重跑噪声 + 每笔 ≈90MB 大对象分配 ⇒ 阻塞 GC。**修复后实测 4ms/笔、执行阶段 3→1** |
| 76 | **M4-j 修复：装配自愈 + 自建路径分层 + 数据变更独立判据（D-36 / D-37）** | `CityWindow`：`_needsFirstBind` 标记 + 更新循环探测到实例才装配（找不到下一帧再试，探测失败不动 `_stageKey`/不碰 runner）、每帧 `!ReferenceEquals(ActiveContext,_ctx)` 重接桥、提交前 `_ctx.AddDirtyBound(Tracker.LastDirtyBounds)`；`CityTerrainView`：按来源分层（项目变→全量／Profile 变→只 S1a／实例变→只 delta+S1b）+ ctx 与阶段对象长期复用 + `_ownDataChanged`/`_ownRegion` 驱动增量重建 | 同上 | 2026-11-05 | **通过**；实测终态 `status=[增量重建 9/9 chunk（脏盒 202×202m，全图 3969）]`、`chunks=3969`、`field=有`、空转两次 0/0ms（序号防抖成立）；mesh 顶点 y 范围 `[-7.6,5.3]` 对上数据 `[-7.6,5.4]` ⇒ **画面与数据一致** |
| 77 | **M4-j 自造缺陷 H-58：`OnEnable` 直接 Bind 会让画面全白（比修复前更糟）** | 探针实测 `runner 阶段数=1 [S0_Assemble]`、`ctx.HasSlot(S1a)=False`、`stageKey=14695981039346656037`（= 素数，即 `_terrain`/`Profile` 当时均为 null 未参与混列）⇒ 面板已接管、显示层已放弃自建路径，但高度场永不产出 | 同上 | 2026-11-05 | **已当场纠正**（改为更新循环重试装配）。纪律补条：**"修 A 导致 B"的验证必须跑到"画面真有东西"**，只断言"装配标志已置位"会漏掉这类更严重的回归 |
| 78 | **M4-j 测试口径自纠错 H-62**：第一版用例用 `UndoHistoryTracker.MarkIntent()` 断言"只脏 Flatten"，实跑得 `Terrain, Flatten, Building` → 红 | 查清 `MarkIntent` 是**粗粒度**入口（并对象当前**全部**通道），rig 建时 `SetProfile` 已 bump `TerrainModel` ⇒ 必带 `Terrain`；面板真实驱动是 `Scan()` 基线差分。**不是实现有 bug**（`DirtyOf(DeltaStroke)=Flatten,Building` 传播表本就正确） | 同上 | 2026-11-05 | 改为两轨断言：传播表 `DirtyOf` 精确判 + `Scan()` 真实口径判；**133/133** 全绿。教训与 H-43 同族：断言红先查自己的口径，别急着改实现 |
| 79 | **M4-j 交锁门禁（§1.6 三项）** | 清台 → 静默 60s 取基线（`0/0/0 seeded=True`，无噪声需归因）→ 重跑本会话全部验证动作（`script recompile` → 本包 `test run` → `verify_project.py`）→ `console status` | 同上 | 2026-11-05 | `RECOMPILE failed=false errors=[]`；`Tests.dll` mtime ≥ 测试源 mtime（H-36 第二检）；**133/133 Failed 0**；`consoleErrors=0 consoleWarnings=0 seeded=True`（error/warning 明细条数 = 0）；校验器 **`FAIL 0`、结论: 绿灯**（2 项 WARN：`lock-active` 本会话自持锁 + `pkg-sample` 属他包 `com.collin.stealthplanner`，非本会话产物） |

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

## 本次会话执行记录（M3：Modules/01 显示层 = chunk mesh + 面板实时接线，全部为实测值）

> 会话 `2026-11-03 m3-view`，客户端标识 qianwen-work-assistant，持 `com.collin.citymap` 锁。
> 测试规模：本包程序集 84 → **99**（新增 `Tests/Editor/TerrainViewTests.cs` 15 条）；
> 全项目 `test list --mode editor` → **Count 141**（其余 42 条属 `com.collin.stealth.Tests`，非本包）。

### §M3-1 本轮实现缺陷（H-38 / H-39，两条都是"跑起来才看得见"）
| 编号 | 缺陷 | 实测症状 | 修法 |
|---|---|---|---|
| **H-38** | 三角形绕序写成 `(a,b,d)+(a,d,c)`（想当然的"标准扇形"） | 逐三角形几何法线点积 **−0.992** ⇒ 整片地表**背面朝上**，从天空视角**什么都看不见**。这是显示层最刺眼的故障，但肉眼看代码完全看不出 | 按右手定则重推：`Cross(c−a, b−a).y = +Δz·Δx > 0` ⇒ 改为 `(a,c,b)+(c,d,b)`（与 Unity 官方程序化平面范例同构） |
| **H-39** | 同拓扑局部更新用了 `SetVertexBufferData`（**裸缓冲流** API） | 抛 `SetVertexData() with out-of-bounds arguments; would need to copy 1452 bytes at offset 0 into a **0 byte buffer**`。"0 字节缓冲"正是"流未声明布局"的证据 | 改用 `SetVertices/SetNormals/SetUVs/SetColors(arr, start, length)`（反射枚举 `Mesh` 成员确认签名）。**冷启动走全量路径看不出此错，只有第二次同拓扑写入才触发** ⇒ 必须有覆盖"增量路径"的用例 |

### §M3-2 H-40：显示顶点采样被 clamp（两轮才收敛，第一轮修法方向就错了）
**症状**：解析平面场（`h = 0.25x − 0.15z`）上，顶点法线与解析法线偏差 **16.254°**。

**第一轮**（方向错）：我以为偏差来自"最左/最下两排落在首列格中心外侧半格、被 `Sample` 的 clamp 钳位"，
于是**在测试里按数据域内外分两档断言**，并把边缘偏差写成可解析预测 `A·cellSize/2 = 0.25m` 来"钉住成因"。
——这是**给实现缺陷写断言开脱**：数值虽被解释通了，但那圈假平地仍然在画面里。

**重新推导后才看见真问题**：`GetNormal` 是**中心差分**（探针 = `±cellSize/2`）。
即使顶点本身落在数据域内，**它的探针照样越界**。把基准移到首格中心后偏差只剩 **7.96°**，
而 `7.96° = atan(0.2915) − atan(0.1458)` —— 正是"有效梯度减半"的解析值，与残余完全吻合。

**最终修法**：显示顶点的**探针安全域** = 四边各内缩**一个逻辑格**
（`DisplayOrigin = Origin + cellSize`，`DisplayIndexCount = floor((Columns−2)·cellSize / DisplayStep) + 1`）。
代价是地形边缘比图幅内缩 ≤ (1 格 + 1 显示步)（8km 图上 ≤12m），且完全落在 `buildableInset`（默认 60m）
外环不可建带内 —— 视觉上不可见，远优于"边缘出现一圈比实际更平的假平地"。导出侧不受影响（10 §5.1 独立重切）。
**收敛判据**：修正后**对每一个顶点**做绝对比较（高度 1e-4m、法线 0.05°），不再需要分档。

### §M3-3 H-41 / H-42：chunk 尺寸与脏区余量两处量级/舍入错
- **H-41**：`SnapRatio` 用 `RoundToInt` ⇒ `100/8 = 12.5` 归整依赖具体 midpoint 规则（可能 12 也可能 13），
  实测给 **96m（比用户请求更小）**。跨机器可能切出不同数量 chunk = 破坏"同参数同结果"的地基承诺。
  改 `CeilToInt` ⇒ 104m，且满足"对齐结果 ≥ 请求值"。
- **H-42**：`ChunksTouching` 的外扩余量误取"一整个 chunk"（128m）。
  但阶段上报的包围盒（`PlaneConstraint.GetAffectedBounds`）**已经包含** `ExtendMeters` + 半格容差；
  显示层真正需要的只是**几何影响半径 = 一个显示步**。实测点状脏盒命中 **9** 块（3×3）⇒ 增量重建收益当场归零，
  表现为"改了没报错但明显卡"。改 `m = max(cellSize, DisplayStep)` ⇒ 点状盒命中 **1** 块。

### §M3-4 H-43：我的断言口径错（凭直觉写判据，险险把设计改坏）
`Slope_Colors_..._Monotonic` 我先后写过两条错判据：
1. `mid.g > mid.r`（"半程仍以绿为主"）—— 色带在 t=0.5 处正好是土黄锚点，那里 r 已大于 g；
2. `steep.r > mid.r`（"半程之后继续变红"）—— 把红分量当成了单调量。

**先把三个锚点算出来再写判据**：平 (r82,g115)、土黄 (r184,g158)、陡 (r158,g56)
⇒ **r 与 g 都非单调**（r 先升后降、g 先升后降），唯一单调的是**红绿对比 `r−g` = −33 → 26 → 102**。
判据改为：`dFlat < dMid < dSteep`，并加一条"色带必须跨越偏绿→偏红分界"（`dFlat < 0 < dSteep`）。

**这条的风险**：判据写错时，人会本能地去调锚点颜色"凑绿" —— 那是被自己的断言牵着改设计。
纪律：**先用算术确定哪个量真的单调，再写断言**。

### §M3-5 实机可见性证据（控制台打点，两次独立复跑数值一致）
```
[M3] 自建路径：显示 16/16 chunk · 逻辑 200²格 @2m · 显示 8m · chunk 128m
[M3] chunk 数随尺寸变化：200m=4 100m=16 400m=1
```
- 自建路径（Play / 无面板）：`projectForStandalone` + `TerrainProfile` → 自己跑 S1a→S1b → **16 个 chunk 全部产出**；
  每块都有非空 `MeshFilter/MeshRenderer`、`vertexCount > 3`、三角形数 > 0；
  派生根与全部 chunk 物体逐项断言 `HideAndDontSave`（01 §6：显示 mesh 不落盘）。
- chunk 数随尺寸正确变化（400m 档 = 1 块覆盖全图、100m 档 = 16 块），且**缩小后旧块被回收**（不残留成"两层地形叠一起"）。
- M2 的两条打点在本轮复跑中数值未变（`urbanFlatness 含细节 0→0.813m`、`A1_3 坡度 4.79°→1.41°`）
  ⇒ 显示层改动没有回退数据层行为。

### §M3-6 面板接上了真实阶段（M1/M2 遗留的"只有骨架"闭环）
`grep` 实测：M2 交付时 Editor 目录**没有任何 `_runner.Register`** ⇒ 面板"重新生成"只跑 S0，
S1a/S1b 在编辑期永远算不出高度场（画面必然为空）。本轮 `CityWindow.SyncStages()`：
- 注册 `AssembleStage` + `TerrainBaseStage` + `FlattenSolveStage`，身份指纹 = (Profile × CityTerrain) 的
  `GetEntityId().GetHashCode()`（Unity 6 起 `GetInstanceID` 废弃为 error），变了才重注册；
- 地形实例查找走 `IntentRegistry`（O(千级)），**不用 `FindFirstObjectByType`**（场景无 CityTerrain 时每帧全场景遍历 = 09 H-10 反模式）；
- `CommitAndNotify()`：**脏区域必须在 `Tick` 之前取**（`Runner.Tick` 成功后会 `ClearDirtyBounds()`，
  事后取永远是 null ⇒ 增量重建成空话）；
- 本包测试 `test run --mode editor --filter_type assembly --filter com.collin.citymap.Tests` → **Total 99 / Failed 0**。

### §M3-7 交锁三项门禁（实测，**校验器整体首次绿灯**）
| 门禁 | 指令 | 结果 |
|---|---|---|
| §1.6a 编译 | `script recompile_status`（先做 H-36 新鲜度核验：rt/tests 两个 dll mtime ≥ 源码） | `completed`、**`failed=False`、`errors=0`** |
| §1.6b 控制台 | `console clear` → 重跑本包全量测试 + 校验器 → `console status` | **`consoleErrors=0`、`consoleWarnings=0`、`seeded=True`**；校验器 `console: error=0 warning=0` |
| §1.6c 校验器 | `python Tools/verify_project.py` | `插件包: 4 个`、**`FAIL 0`**、`WARN 3`（全是 `lock-active`）→ **结论: 绿灯** |

**门禁过程记录（三次波折，如实留下）**：
1. 第一次全量跑 141 条：**134 通过 / 7 失败** → 归因 4 实现缺陷（H-38/39/40/41/42）+ 3 断言口径错。
2. 修完第二次：140/1（仅 `Plane_Field` 残余 7.96°）→ 才发现 H-40 的第一轮修法方向错（见 §M3-2）。
3. 第三次 `test run` 返回 **Total 0（不是"没有测试"）**：控制台报 9 条
   `InvalidOperationException: This cannot be used during play mode` / `Test tree is not available`；
   反射查询实测 `isPlaying=False`、当前活动场景是 **`com.collin.stretch/Sample/TestPlayground.unity`**
   ⇒ 是**其他会话把编辑器切进 Play Mode 又退出**造成的时序冲突（共用同一实例，§2）。
   等其退出后重跑：**141/141 全绿**。
4. 但全量跑会把他包错误计入共享控制台 ⇒ 破坏 §1.6b。实测那条 error 的堆栈是
   `Collin.Stealth.Tests.StealthContractConsumerTests.Services_DuplicateRegistration_KeepsFirstAndLogsError`
   —— stealth **故意触发**的断言（不是环境噪声，也不是本包代码），我不改他人测试，
   改为**按程序集取证**（`--filter_type assembly`）：本包 99/99 且归零序列内 `error=0/warning=0`。
   与 M1 §M1-2 记录的同款纪律一致：**归零门禁以"本会话验证动作新产生的条目"为准**。

### §M3-8 本会话新增的流程事实（供下一会话直接照做）
- `test run` 支持 `--filter <名或程序集>` + `--filter_type testName|assembly|category`；
  **按程序集过滤是多会话共用编辑器时唯一干净的取证方式**。
- `test run` 返回 `Summary.Total = 0` 有两种成因，必须区分：①`--mode` 传错值；
  ②编辑器当时在 Play Mode（`Test tree is not available`）。后者要看控制台，不是重发命令。
- `script eval` 对引号/换行脆弱，跨行 C# 一律用 `script eval_file --file <win路径>`；
  `EditorApplication` **没有** `isPlayMode` 成员（实测 CS0117），查播放状态用 `isPlaying`。
- 二维向量的第二分量在本工程语义里承载世界 Z 轴，**是 `.y` 不是 `.z`**（实测 CS1061）。

## 本次会话执行记录（M3-b：面板实时链路，2026-11-03）

> 同会话 `2026-11-03 m3-view` 的续做增量。§M3-1~§M3-8 是显示层（chunk mesh + 阶段接线），
> 本节是"改参数即时反映"的交互面（01 §4.4 / A-0-3）。本包用例 99 → **100**。

### §M3-9 值指纹逐字段覆盖（把"漏检"变成机器保证）
`TerrainProfile.ComputeValueKey()` 是面板 **Inspector 直改第二检**的地基：用户在 SO 上直接改字段
不调用任何 `SetXxx()` ⇒ 序列号不变 ⇒ Tracker 看不见 ⇒ "拖了参数地形不动"的静默失效。
指纹若漏掉某字段，**那个字段**被直改就永远检不出，而它只在被漏字段上发生、手测极难命中。
故 `Value_Key_Covers_Every_Tunable_Field` 用反射枚举全部 `[SerializeField]` 参数逐个扰动比对：
- 枚举自校验：`Assert.GreaterOrEqual(tunable.Count, 18)`，实得 **20**（含 schemaVersion/stableId 之外的全部参数）；
- 逐字段扰动后比对指纹，**漏检字段清单为空**（`missed.Count == 0`）；
- 复位后指纹回到基线 ⇒ 证明纯由参数决定、无时间/随机/地址成分。
浮点扰动量取 `+0.0037`（高于 `Q()` 的 1e-4 量化粒度），确保"值真的变了"。

### §M3-9b 双重 bump 的自造缺陷（自查修掉）
面板滑条走 `SetXxx()` 已 bump 一次版本，值指纹第二检若不加区分会**再 bump 一次** ⇒ 每次拖动双重提交。
修法：第二检先比 `ManualVersion`——**版本已变 = 走的是显式入口，只同步基线；版本未变但值变了才补 `Touch()`**。
（同类"两条路径都能触发的提交，必须能区分来源"——与 H-30 按通道记账同构。）

### §M3-9c GUI 交互未能自动取证（须用户实机确认）
`DrawTerrainProfile()` 的滑条拖动 → 重算 → 画面更新这条端到端手感，**EditMode 无法自动断言**：
反射调 `OnInspectorGUI/OnGUI` 会抛「You can only call GUI functions from inside OnGUI」
（项目既有实测结论）。本轮已断言的是**数据地基**（值指纹覆盖面、`SetXxx()` 通道、阶段注册），
GUI 控件本身的可用性须你在编辑器 `Tools/Collin/CityMap/City Window` 里实机确认。
**这条是"编译通过 + 数据侧已证 ≠ 交互已证"的如实标注，不计入未通过项、也不预先宣称通过。**

### §M3-10 面板增量交锁门禁（最终复跑，**三项全绿**）
| 门禁 | 结果 |
|---|---|
| §1.6a 编译 | `recompile_status` `failed=False`、`errors=0`；rt+Editor+Tests 三侧 dll mtime ≥ 源码（H-36 新鲜度核验通过） |
| §1.6b 控制台 | `console clear` → 静默 → 重跑本包全量测试 + 校验器 → `console status` = **`consoleErrors=0/consoleWarnings=0/seeded=True`** |
| §1.6c 校验器 | `插件包: 4 个`、**`FAIL 0`**、本包项全 `[ ok ]` → **结论: 绿灯** |
| 测试 | 本包 `--filter_type assembly` → **Total 100 / Passed 100 / Failed 0** |

**过程中的外部红灯与其消解（如实记录）**：面板增量写好后，两度撞上**外因**红灯——
① `com.collin.stealth/VisionProfile.cs`（`AwarenessTuning` 未定义）编译失败冻结脚本域；
② stealth 会话的 7 条 `CS0618`（用了 Unity 6 废弃的 `FindObjectsByType`）+ `com.collin.stealth.lock` 超时。
按 §1.6「禁止带红交付」、§2/§5「不改删他人包文件」、§3「超时锁不得自动删除」，
我全程**持锁等待、未越界改删**。用户确认 stealth 已修 → stealth 会话随即补齐自身修复并刷新其锁心跳 →
最终复跑 **三项全绿、校验器 FAIL 0**，无需任何"带红提交"裁定即正常提交。
**教训**：多会话共用一个编辑器时，本包门禁读数会被他包瞬时状态污染；判红前先读堆栈/路径确认归属，
不据外部噪声擅自改他人文件或塞 allowlist。

### §M3-11 门禁过程中观察到的外部瞬时噪声（均已消解；本会话全程未越界处置）

面板增量门禁复跑期间，控制台两度出现**非本包产生**的噪声。如实记录其证据与最终消解方式：

| 项 | 内容 | 证据（归属判定） | 最终消解 |
|---|---|---|---|
| 1 条 error | `KeyNotFoundException: The given key 'services-gateway' was not present` | 堆栈仅 `UnityEditor.Connect.ServicesConfiguration.*`（全 Unity 内置程序集，无任何 `Packages/com.collin.*` 或 `Assets` 帧）⇒ 环境瞬时初始化噪声 | 后续干净复跑（清台→静默→只跑本包程序集）内不再出现；**未写 allowlist**（无需，也未经批准） |
| 7 条 warning | `CS0618: FindObjectsByType / FindObjectsSortMode 已废弃` | 路径全在 `Packages\com.collin.stealth\Runtime\{StealthClock,DetectionRegistry,VisionRegistry,StealthWorld}.cs` ⇒ stealth 会话代码 | **stealth 会话自行修复**（用户确认"stealth已修"）→ 最终复跑 `consoleWarnings=0`、校验器 `FAIL 0 / 绿灯` |

**关键纪律（本会话严格遵守，未因红灯诱惑越界）**：
- 未修改/删除 `com.collin.stealth` 的任何文件（§5）；未删除其超时锁（§3）；未擅自把噪声塞进 `Tools/console_allowlist.json`（共享文件、须批准，§7）。
- 红灯期间**只持锁等待 + 如实归因**，未带红提交、未绕门禁。外部项消解后才完成最终复跑取绿灯证据。
- 判红先读堆栈/路径确认归属：`recompile_status` 的 `errors` 与 `console get` 的 `stackTrace` 字段是归因的唯一依据，不凭"控制台有红点"就改本包或他人代码。

**本包门禁事实**（噪声之外，全程达标）：
- `test run --filter_type assembly --filter com.collin.citymap.Tests` → **Total 100 / Passed 100 / Failed 0**；
- `script recompile_status` → `failed=False`、`errors=0`（rt+Editor+Tests 三侧 dll mtime ≥ 源码）；
- `verify_project.py` 本包项（`pkg-layout`/`runtime-purity`/`dep-*`/`asmdef`/`showcase`/`manifest-sync`/`hygiene`）全 `[ ok ]`。

## 本次会话执行记录（M4-a：材质补丁 + 资产归位 + 工作流提案，2026-11-03）

> 同会话续做，起因是用户实机体验后的两条反馈。测试规模不变（本包 100 条）。

### §M4-a-1 用户反馈①「地表没有材质，看起伏比较累」——根因与修复
**根因（M3 交付缺口，非用户操作错误）**：显示层按 01 §3.3 已把坡度写进顶点色，
但材质回退链只有 `URP/Lit → Diffuse`，而**这两者都不读 `COLOR` 语义** ⇒ 色带等于白写。
01 §3.3 原文同时要求"本包不依赖任何 shader 包，提供 URP-compatible 材质引用（`Sample/` 内）"
——这条要求 M3 漏交付了，本轮补齐：
- `Sample/Shaders/CityTerrainVertexColor.shader`：单 pass 顶点色 × 兰伯特主光。
  实测 `Shader.Find(...).isSupported = True`。**写之前逐个核对了 include 真实存在**：
  `Core.hlsl`（坐标变换）+ `Lighting.hlsl`（其第 7 行 include `RealtimeLights.hlsl:100` 定义 `GetMainLight()`）。
  首版曾引用不存在的 `ShadowCasterAmbient.hlsl`，会直接导致着色器编译失败污染控制台（§1.6b），已删除该 pass。
- `Sample/Materials/CityTerrainVertexColor.mat`（`_Ambient=0.45`，由编辑器 `AssetDatabase.CreateAsset` 生成，
  不手搓 .mat 的 YAML 以免序列化版本不符）。
- `CityTerrainView.ResolveMaterial()` 回退链改为 **引用 → 自带顶点色 shader → URP/Lit → Diffuse**。

**端到端取证（决定性证据，不是"编译通过就算好"）**：反射绑定项目 + Profile 后 `RebuildNow()`，实测
```
status=显示 3969/3969 chunk · 逻辑 2000²格 @4m · 显示 16m · chunk 128m   chunks=3969
shader=Collin/CityMap/Terrain VertexColor
```
⇒ 3969 块全部产出，且**块渲染器实际用的就是自带着色器**，坡度色带这次真的可见。

### §M4-a-2 H-44（本会话自造缺陷，由校验器抓到，非他包噪声）
`CityWindow.FindOrCreateProfile()` 原先**静默往 `Assets/Collin/` 创建 Profile 资产**，
直接违反 AGENTS §0「禁止把插件代码/资产放 Assets/」⇒ 校验器 `assets-layout` **FAIL**，
且用户点「创建地形实例」时已在磁盘留下违规目录。
- **取证**：`Assets/Collin/TerrainProfile.asset` 参数全为默认值（18/1.2/0.25/12），
  与"用户反馈拖滑条没反应"相互印证（拖动中途被域重载打断，值未落盘）。
- **修法**：目标目录固定为 `Packages/com.collin.citymap/Sample/Profiles/`（与 stretch 包同构先例一致）。
  **没有**改成弹保存框：`SaveFilePanelInProject` 只能落 `Assets/` 下，弹框反而把违规路径变成"用户自选"。
- **迁回**：用 `AssetDatabase.MoveAsset` 把已生成的资产移进包内（**保留 GUID ⇒ 场景引用不断**），
  再 `DeleteAsset` 清掉空目录。复跑校验器 `assets-layout` 转 `[ ok ]`。
- **纪律**：面板里任何"自动创建资产"的路径，必须写包内；`Assets/` 只留展板（§0）。

### §M4-a-3 另一处实机才暴露的缺陷（面板创建的实例无数据源）
反射读现场：`status=无权威上下文且未绑定自建项目（Play 里需指定项目） chunks=0`。
根因：`CreateTerrainInstance()` 建了 `CityTerrainView` 却没绑 `projectForStandalone` ⇒
**面板一关（权威上下文消失）画面就永远空白**。修法：创建时一并 `SetStandaloneProject(_project)`。
教训：面板"代用户创建对象"时，必须保证**脱离面板后该对象仍自洽**。

### §M4-a-4 豁免清单归属澄清（不是本会话塞的）
门禁期间那条 Unity Connect 的 `services-gateway` error 从 FAIL 降为 WARN，
是因为**其他会话**已建 `Tools/console_allowlist.json`（提交 `b753078`，`approved_by` 记录用户 2026-11-02 批准，
条目为 `services-gateway` / `UnavailableForLegalReasons` / `generators.ai.unity.com`）。
本会话**未修改该共享文件**；校验器现输出 `console: error=0 warning=0`（归零序列内本就干净）。

### §M4-a-5 交锁三项门禁（M4-a 最终复跑）
| 门禁 | 结果 |
|---|---|
| §1.6a 编译 | `recompile_status` `failed=False`、`errors=0`；Editor dll mtime ≥ 源码（H-36 新鲜度核验） |
| §1.6b 控制台 | `console clear` → 重跑本包全量测试 + 校验器 → **`consoleErrors=0/consoleWarnings=0/seeded=True`** |
| §1.6c 校验器 | 本包项全 `[ ok ]`（含转绿的 `assets-layout`）；**唯一 FAIL = 他包 `com.collin.stealth.lock` 超时 647s**（session 非本会话，§3 不代删） |
| 测试 | 本包 `--filter_type assembly` → **Total 100 / Passed 100 / Failed 0** |

红灯外部来源处置与 §M3-10 同：如实上报 + 持锁等待，未越界改删他人文件或锁。

### §M4-a-6 工作流重排提案（已写就，**待批准，未动代码**）
用户裁定三段式（① 仅参数 ② 平面+山水雕刻+噪音 ③ 平整成地表）与**三级坡度退让**。
结论：与 01 §3.2 重放契约**不冲突**（"雕刻在平整前"本就是契约第 3→4 步），要改的是里程碑次序 + 新增硬退让档。
提案落盘 `Docs/Modules/12-TerrainWorkflow.md`，内含两个**必须由用户定的参数**：
`hardRetreatSlope` 默认值、陡区是整块丢弃还是只削陡角。按 §4「DESIGN 先于代码」，批准前不实现。

## 本次会话执行记录（M4-b：参数定案 + 雕刻 delta 层数据结构，2026-11-03）

> 同会话续做。用户答复 12 提案的两个待定参数 → 提案定稿；随后只落**无规则张力的地基**（delta 数据结构）。
> 本包用例 100 → **116**（`SculptDeltaTests.cs` 16 条）。

### §M4-b-0 参数定案（用户 2026-11-03 直接答复）
| 参数 | 定值 | 落到哪 |
|---|---|---|
| `hardRetreatSlope`（硬退让线） | **18°** | 12 §3 表第三档阈值；后续进 `TerrainProfile` |
| 陡区处置 | **只削陡角、保留缓的部分**（非整块丢弃） | 12 §3；实现上即"地块多边形按坡度裁剪出可建子区"，而非置整块 `NoBuild` |

### §M4-b-1 SculptDeltaAsset（01 §2.3 人工层）——先立地基，不接管线
刻意**只交付数据结构 + 重放函数**，不接进 S1a/S1b：接管线要动脏传播语义，而
"雕刻改高度后地块要不要重算"这条当时住在 `DESIGN` §3.2 脏传播表、**无编号**（原规则「雕刻仅脏 S6」；
注意 `D-14` 是"显示网格分辨率解耦"，与本条无关——本段先前误引为 D-14，已于 M4-c 纠正），
属规则级变更（§10 须先看 diff 批准）。地基独立、无张力，可以先绿着立起来。
→ **后续**：M4-c 已获批（登记为 **D-31**）并完成接线，取证见 §M4-c。

四条契约各由一条用例钉住：
| 契约 | 用例 | 为何危险 |
|---|---|---|
| **绝不写 BaseHeights** | `Additive_Applies_To_Working_Heights_Not_Base` + `Repeated_Rerun_On_Reset_Base_Is_Stable_No_Drift`（5 轮复位重放恒定 4.5m） | 人工混进基底 ⇒ 下轮约束把自己上轮结果当输入 ⇒ **逐轮漂移**（01 §2.4 正反馈） |
| **Absolute 用 NaN 哨兵，不用 0** | `Absolute_Target_Zero_Meters_Is_Honored_Not_Dropped` | 0m 是合法高程；拿 0 当"无目标"会把"压到海平面"静默吞掉（与 11 §2 heightValue 双义同坑） |
| **稀疏剪枝** | `Additive_Back_To_Zero_Prunes_Chunk` / `Residual_Noise_Below_Epsilon_Is_Snapped_To_Zero`（1e-8 残差吸附） | 全零条目不删 → 反复大笔刷把资产撑爆（01 §9） |
| **重放顺序确定** | `Additive_And_Absolute_Withstand_Each_Other_Independently`（**正序与反序插入都得 7+3=10**） | 见 §M4-b-2 |
| 越界安全 | `Out_Of_Map_Writes_Are_Rejected_Without_Corrupting` / `Edge_Chunk_Clipped_By_Map_Does_Not_Write_Outside`（100m/30m 非整除档实测） | `values` 按整 chunk 分配大于实际覆盖，靠裁剪兜住；图外写会造"永远读不到的幽灵格" |
| 累加语义归调用方 | `Apply_Is_Accumulative_By_Design_And_Caller_Owns_Reset` | 复位责任在调用方（同 S1b）。若有人为"看着幂等"改成内部去重，会静默破坏 carve+delta 的叠加次序 |

### §M4-b-2 H-45（自查抓到的设计缺陷，未成为对外缺陷前修掉）
首版 `ApplyTo` 是**单遍按条目插入顺序**遍历。同一格若既有 Absolute 目标又有 Additive 增量，
结果就取决于用户先拖笔刷还是先压平：`7+3=10` 或 `先加后赋=7` —— **不定值，且用户摸不到规律**。
改为**两遍确定序**：先 Absolute 遍（赋值），再 Additive 遍（叠加）。测试同时断言
"两种插入顺序结果相同"，防止有人改回单遍图省事。

### §M4-b-3 本轮自造编译错误（§1.9：一经发现立即修复，未外泄）
两处，都是我自己写坏、且**第二次踩同一坑**：
1. `CS0136`：`PruneIfEmpty` 里 if/else 两个平级 `for (var i …)` + 后置同名循环 ⇒ 作用域冲突。
   改为单一循环（顺带让两模式判空口径更显式）。
2. `CS1003`：**C# 字符串字面量里嵌 ASCII 双引号**（`"0 当"无目标"哨兵"`）。
   同一错误模式本轮第二次出现（第一次是 `Slope_Colors` 断言）。
   ⇒ 修复时不再"只改报错行"，而是**机械全扫**：逐行取 `//` 之前代码统计 `"` 数量，奇数即字符串被截断。
   扫描结果 `奇数引号行数 = 0` 后才重编译。纪律：**同类错误第二次出现，就把补丁式修复升级成机械自检**。

### §M4-b-4 交锁三项门禁（M4-b 复跑）
| 门禁 | 结果 |
|---|---|
| §1.6a 编译 | `recompile_status` `failed=False`、`errors=0`；rt+Tests 两侧 dll mtime ≥ 源码（H-36 新鲜度核验） |
| §1.6b 控制台 | `console clear` → 重跑本包全量 + 校验器 → **`consoleErrors=0/consoleWarnings=0/seeded=True`**；校验器 `console: error=0 warning=0` |
| §1.6c 校验器 | 本包项全 `[ ok ]`（含 `assets-layout`）；整体 FAIL 1 = 他包 `com.collin.stealth.lock` 超时 560s（非本会话，§3 不代删） |
| 测试 | 本包 `--filter_type assembly` → **Total 116 / Passed 116 / Failed 0** |

## 本次会话执行记录（M4-c：雕刻接入管线 / D-31 落地，2026-11-04）

> 同会话续做（跨日至 11-04）。用户答复"雕刻是否触发 S1b 重算" → 批准推翻旧「仅脏 S6」。
> 本包用例 116 → **119**（`TerrainDataTests` 新增 3 条接线回归）。

### §M4-c-1 接线决策与取数口径（本轮最关键的一处判断）
批准后先查一件事：**S1b 的约束目标平面从哪个数组拟合**。实测在
`HeightField.cs:314` —— `FitFromBase` 走 `field.GetBaseHeight()` = **BaseHeights**。
而 delta 依契约只进 `Heights`。**若照字面接线（S1a 末尾把 delta 并进 BaseHeights 之外什么都不改），
雕刻对拟合完全不可见，用户要的"雕出新地形、周边地块自动重算"会落空**；
更糟的是它不会报错、只会悄悄把雕出来的山在下一次压平时抹掉。

采用的口径（`CommitBaseAsWorking()` 之后、融合之前）：
```
复位 Heights = BaseHeights  →  delta.ApplyTo(Heights)  →  拟合读 Heights（= 意图地表）  →  融合写 Heights
```
- 拟合此刻读到的 Heights = **自然 + 人工、尚未平整**，正是"意图地表"；
- **不违反 §2.4 防漂移不变量**：漂移指"把上一轮的**平整结果**当输入"，而复位刚把它抹掉。
  该不变量改由"必须先复位再传 `fromIntentSurface:true`"这条前置条件守护，已写进 `FitFromBase` 注释；
- S1a 保持"只产基底"，`BaseHeights` 全程不被人工层污染（这条由 `Delta_Survives_...` 的逐位断言守住）。

### §M4-c-2 判别式取证（不是"变一点就算过"）
```
[M4c] 未雕刻 0.106 → 雕刻后 20.124（自然 0.023，抬 20m）
```
在 60m×60m 人工平整区内整区抬 20m：若拟合仍读基底，压平会把这 20m 拉回未雕刻时的目标平面 ⇒ 差值≈0。
实得差值 **20.018m**（≈抬升量）⇒ 目标平面确实跟着雕刻走了。
同时 M2 两条既有用例数值与上轮**一字不差**（`A1_3 4.79°→1.41°`、`urbanFlatness 0→0.813m`）⇒ 无回归。

三条新用例各自钉一件事：
| 用例 | 钉住的失败模式 |
|---|---|
| `Delta_Survives_S1b_Rerun_Without_Double_Apply` | 双向：复位把 delta 冲掉（雕刻只活一轮）/ 不复位导致叠两遍；外加**基底逐位洁净**（`0f` 容差） |
| `Carving_Inside_Manual_Zone_Raises_Its_Target_Plane` | 雕刻被自己的压平抹掉（即 §M4-c-1 那个静默陷阱） |
| `Without_Delta_Pipeline_Result_Is_Bitwise_Unchanged` | 新取数口径污染旧行为（A-1-2 幂等回归，容差 **0f**） |

### §M4-c-3 H-46：我把写错的规则编号引用**提交进了仓库**
上一轮（M4-b，提交 `c69fbe4`）我在代码注释与文档里 4 次写"推翻 D-14"。本轮接线前核对编号时发现：
`DESIGN` 的 **D-14 是"显示网格分辨率与逻辑格解耦"**，与雕刻毫无关系；
"雕刻仅脏 S6"住在 §3.2 脏传播表里、**从来没有编号**。
⇒ 我等于用不存在的规则名给一次规则级变更背书，后来者按 D-14 去查会查到 mesh 分辨率，**误导是双向的**。

处置：① 把用户两项裁定正式登记为 **D-31**（雕刻脏集合改 S1b+S6）与 **D-32**（坡度三级退让），
§3.2 表雕刻行同步改写；② 全量纠正错引（4 处代码/测试注释 + ACCEPTANCE/CHANGELOG/HANDOFF 各 1 处）；
③ 复核 `grep D-14` 仅剩 5 处，逐一确认为"分辨率解耦"的合法引用（含 00 §2.2、FoundationTests 的 A-0-7 用例）。
**纪律**：引用规则编号前必须先 `grep` 那个编号本体确认它说的是什么；
编号写错比不写编号更糟——它看起来像有出处。（同类：H-43 凭直觉写判据、H-40 给缺陷写断言开脱，
本会话已是第三次"证据链自己造假"型错误，且这次真的进了仓库。）

### §M4-c-4 交锁三项门禁（本增量复跑，2026-11-04）
| 门禁 | 结果 |
|---|---|
| §1.6a 编译 | `recompile_status` `failed=False`、`errors=0`；rt+Editor+Tests 三侧 dll mtime ≥ 源码 |
| §1.6b 控制台 | `console clear` → 重跑本包全量 + 校验器 → **`consoleErrors=0/consoleWarnings=0/seeded=True`**；校验器 `console: error=0 warning=0` |
| §1.6c 校验器 | **`插件包: 4 个`、`FAIL 0`、`WARN 2`（两条 lock-active）→ 结论: 绿灯**（他包 stealth 锁此时已被其会话续心跳，本轮无需外部裁定） |
| 测试 | 本包 `--filter_type assembly` → **Total 119 / Passed 119 / Failed 0** |

### §M4-d 笔刷数学内核（Runtime 纯函数，2026-11-04）

**为何把笔刷数学放 Runtime 而不是 EditorTool**：A-1-5 要"同一笔划在 60Hz 与 144Hz 机器上产生相同 delta"。
若积分写在 `OnSceneGUI` 里，这个性质只能靠人肉试手感——而**帧率相关 bug 恰好是"我这台机器没问题"的典型**，
人肉必漏。故 `SculptBrush`（数学）+ `SculptStroke`（状态机）均为无 UnityEditor 依赖的纯代码，可 EditMode 直测。

三条硬约定与其成因：
1. **固定步长积分**（D-01-3）：增量 = `笔刷世界位移 × 强度 × 衰减`，**绝不乘 `deltaTime`**。
2. **位移取世界量而非屏幕量**：屏幕 DPI/缩放会改变"同样手速产生的强度"，跨机器不一致。
3. **余量跨调用留存**（`AccumulateSteps` 的 `carry` 传引用）：丢弃不足一步的位移，
   **慢速精细雕刻会完全不出效果**（手挪得慢就永远凑不满 0.5m）——这是"细节笔刷没反应"的典型成因，肉眼极难归因。

**首跑 2 条失败，归因不同（未用放宽阈值蒙过去）**：
- `Degenerate_Radii...`：**我的断言期望写反**——`FalloffAt(3f, 5f, 5f)` 中 `dist=3 < core=5` 本应全强 1，我写了 0。
  实现正确，改断言。同时暴露实现的一处真实缺陷：`core>radius` 非法配置时"核心内全强"分支**吞掉半径截止**，
  会画出比笔刷还大的影响区 ⇒ 实现补 **`core` 钳进 `[0, radius]`**。
- `Sub_Step_Movement...`：期望 4 步实得 3，根因是 `0.01f` 二进制不精确、200 次累加落在 1.99999m 而非 2m，
  少凑一步。属**测试口径脆弱**，不是实现问题 ⇒ 改用二进制精确的 `0.25f` 做严格断言
  （200×0.25m=50m → 恰 100 步且 `carry==0`），慢速场景只断"显著 > 0"（浮点固有噪声不该钉死精确值）。

**交锁门禁（本增量复跑，2026-11-04）**：本包 `--filter_type assembly` → **Total 128 / Passed 128 / Failed 0**；
`recompile_status failed=False / errors=0`，rt+Tests dll mtime ≥ 源码；校验器 `console: error=0 warning=0`、
`pkg-layout`/`runtime-purity`/`assets-layout` 全 ok → **`FAIL 0`、结论: 绿灯**（那条 Unity Connect 噪声命中
`Tools/console_allowlist.json` 已批准子串，被正确豁免）。

**前提核实（如实上报，非失败项）**：用户裁定的"steepMask 在 S3/S4 内现算"**当前无落点**——
`Runtime/Core/` 只有 `AssembleStage`/`TerrainBaseStage`/`FlattenSolveStage` 三个实现体，
`S3_Road`/`S4_Parcel` 仅存在于 `StageId` 枚举与 `DirtyBits.Of` 映射中。
故 steepMask 顺延至路网阶段落地时实施；本轮改做**不依赖 S3/S4 的场景笔刷交互壳**，
避免为了"看起来完成了退让"而擅自新建你没选的阶段。

## 本次会话执行记录（M4-e/M4-f：场景笔刷交互壳 + 面板参数区，2026-11-04）

### §M4-e-1 交互壳：职责边界与两条"必须不在这里做"的事
`Editor/SculptBrushTool.cs`（提交 8ab4689）。它**只做三件事**：输入（鼠标轨迹→世界落点）、
可视化（半径预览环）、把 `SculptBrush` 的纯数学结果写进 `SculptDeltaAsset` 并 `MarkDeltaChanged`。
两条刻意"不在这里做"的约束：
1. **不在壳内重算地形/重建 mesh** —— 落笔后交回既有 D-31 链路（面板合并窗口 → S1b 重算 →
   显示层按脏区增量重建）。若在壳内另开一条刷新路径，"编辑态所见"与"重新生成所得"就会分叉（§0.1 原则3）。
2. **只在 `CityWindow.CurrentMode == TerrainSculpt` 时接管鼠标** —— 其它模式原样放行，绝不吞事件。

预览环画在 **y=0 平面用纯几何求交**，不依赖碰撞体/射线检测：派生 chunk 物体是 `HideAndDontSave`
的隐藏物体，拿它做 raycast 会在域重载后静默失效。

一笔划 = 一个 `Undo.RecordObject`（A-1-6）；`EndStroke` 用**笔刷中心**而非 `transform.position`
提交脏区（H-42 同族：算错影响范围 ⇒ "雕了别处、画面不动"）；超格数上限**提示截断**不静默；
未接线动作（反色/清除/定高到指定值，缺数值框输入源）**明确拒绝并提示**，不给静默失败留口子。

### §M4-e-2 H-47：臆造 API 被编译器当场抓住（与 H-46 同族教训）
首版交互壳报 3 错：`CS0452`（`CityGrid` 是**值类型**，不满足 `GetSlot<T> where T : class`）、
2×`CS1061`（对值类型用 `HasValue/Value`），另含不存在的 `CityRegistry` 与一句残缺代码。
教训固化：**写引用前先 grep 真实签名**，编辑器比自审更诚实。正确取格网方式是 `ctx.Grid`
（= `Project.GetGrid()`，值类型 ⇒ 有效性判据取 `CellCount>0 && CellSize>1e-5f`，不是 null 判断）。

### §M4-f 面板参数区（提交 f70fcc2）
半径/强度/动作**只在雕刻模式显示**（常驻会挤占面板，且其它模式改了无效）。动作下拉**只列已接线的四个**
——列出不可用项会让用户选到没作用的选项，比"看不见"更糟。自查修掉一处类型错误
（`EditorGUILayout.Popup` 第三参需 `string[]`，初版误传枚举数组、并让标签数组沦为死字段）。

### §M4-e-3 端到端实机闭环**尚未跑通**（如实标注，非失败项）
反射驱动 `BeginStroke → ApplyDrag×3 → EndStroke` 做真笔划验证时返回 **`terrain=NULL hasGrid=False`**：
体验会话被域重载重置、派生 chunk 不入库，场景内已无 `CityTerrain` 实例。
该次运行**没有写入任何数据**（前置校验即返回），现场未被污染、探针已清理。
所以"落笔→delta 写入→重算→画面刷新"的**实机手感仍待用户验证**；
当前证据链 = 数学内核 9 条单测（M4-d）+ 编译干净 + 128/128 无回归，**不含实机体验**。

### §M4-e-4 交锁门禁（两次增量各自复跑，2026-11-04）
| 增量 | 本包测试 | 校验器 | 控制台 |
|---|---|---|---|
| M4-e 交互壳 | **128/128** Failed 0 | **`FAIL 0`、结论: 绿灯** | `error=0 warning=0` |
| M4-f 面板区 | **128/128** Failed 0 | **`FAIL 0`、结论: 绿灯** | `error=1` 全部命中 §1.8 已批准豁免（正确降为 WARN，无虚假绿灯） |
两次均 `recompile_status failed=False / errors=0`、Editor dll mtime ≥ 源码；
`pkg-layout`/`runtime-purity`/`assets-layout` 全 ok。

## 本次会话执行记录（M4-g：平整阶段闸门 + 笔刷范围可见，2026-11-04）

> 同会话续做，起因是用户实机体验后三条反馈：①笔刷范围看不见 ②抬了地表没反应 ③起伏是真是假。
> 以及一次**方向纠正**：我先按"平整对雕刻格逐格退让"实现，被用户指出机制理解反了
> ——要的时序隔离（雕刻期平整根本不介入），不是让平整给雕刻让位。

### §M4-g-0 用户纠正原话与我的错误方向

> 「错了，自动平整还是要执行，但是它不应该在雕刻时阻碍我，雕刻完后进入自动平整/噪音起伏的环节，
> 才是它应该发挥作用的时机」

错误方向已实现的 `SculptDeltaAsset.MarkCovered()` + S1b `_covered/_coverFilled` 退让掩码**全部撤净**
（`grep covered` 零残留，重编译 `errors=0`），改为 D-33 阶段闸门。

> 同时纠正一处**我自己写下的文档不实陈述**：12 §1 曾写"人工钉住的格由 `ManualLocked` 豁免、不被自动平整
> 抹回"。核查后确认**该机制不存在**——`ManualLocked` 只是地块掩码词表里的一位，S1b 从不检查它。
> 这条虚构的豁免正是我走偏的源头（文档给了我不存在的信心）。已在 12 §1 原位更正留痕。

### §M4-g-1 阶段闸门（D-33，核心交付）

`CityTerrain.FlattenEnabled`（默认 `true`）+ `SetFlattenEnabled()` 走 ParcelModel 通道（切换即触发一次
S1b 重算，否则从关到开画面仍是旧的未平整态）；S1b 在 `CommitBaseAsWorking()` + `Delta.ApplyTo()` **之后**、
`CollectConstraints()` **之前**整段短路。

断言 `Flatten_Off_Is_Phase_Gate_Not_Per_Cell_Waiver`（`flatness=0`、`SetAutoFlatten(true,3)` 制造
"平整本来会介入"的前提、中心 17×17 格抬 25m），**实测打点**：

```
[M4g] 关平整：雕刻后 25m 原样保留、约束 0 条；开平整：约束 9 条、改动 76729 格
```

三条判据都是判别式而非"变一点就算过"：
- 关 → `Outputs == 0`（阶段闸门，不是逐格跳过）
- 关 → `Heights[idx] == BaseHeights[idx] + 25f`（抬升**原样保留**，反馈②的判据）
- 关 → 重跑仍 25m（短路位于复位+重放之后 ⇒ **关闭态同样幂等**，不会把雕刻叠第二遍）
- 开 → `Outputs > 0` 且改动 76729 格（平整在**定稿意图地表之上**发挥作用）

`Ok(0,0)` 而非 `Ok(field.Heights.Length,0)`：关闭时平整一格未动，虚报改动量会污染面板诊断
（与下方"无约束"分支同口径）。

### §M4-g-2 笔刷范围可见（反馈①）

预览环从 `DrawWireDisc(y=0)` 改为**贴地采样描边**：48 段折线，每段取 `field.GetHeight(x,z)+0.08f`
（略抬防 z-fighting），外圈 + 核心圈（`RadiusMeters*CoreRatio`）各画一遍；并把绘制**移到前置条件
`return` 之前**——旧版数据不全时连环都不画、也没提示，用户既看不到范围也不知道为什么。

根因：环画在 y=0 平面，而地表 ±18m，环被整个埋在地形下面。
**观感仍需用户实机确认**（SceneView 绘制无法 EditMode 断言，不虚报"已解决"）。

### §M4-g-3 起伏来源澄清（反馈③）

地表起伏是**真实几何**：S1a 噪声基底，默认 `baseAmplitude=18m / baseWavelength=520m` + 细节层。
材质（M4-a 的 `CityTerrainVertexColor`）只把坡度染成绿→土黄→红棕帮助读高低，**没有"画出"起伏**。

### §M4-g-4 违规资产处置（用户裁定）

`assets-layout` 抓到 `Assets/Scenes/testscene.unity`（今晚新建、非本会话产物）。删除前核验三项：
不在 `EditorBuildSettings`（grep 计数 0）、无其他资产/代码引用、未被 git 跟踪（`??` 未跟踪态）。
经**用户明确裁定"是我的临时场景，删掉"**后走 `AssetDatabase.DeleteAsset`（不用 rm，避免编辑器留悬空引用）。

删后编辑器刷出 6 条 `ReloadScene cannot be used with a scene without a SceneAsset: testscene`
——这是**我删除动作的后果**，不能靠 `console clear` 抹掉绕门禁。查明内存态场景已随资产删除自动卸载
（剩余场景只剩另一会话的 `StealthSample`，**未做任何改动**），报错源消失，复跑归零。

### §M4-g-5 交锁门禁（最终）

| 项 | 结果 |
|---|---|
| 本包测试 | `--filter_type assembly` Total **129** / Passed 129 / Failed 0 |
| 编译 | `recompile_status failed=False`、`errors=0` |
| 控制台 | `consoleErrors=0 consoleWarnings=0 consoleLogs=17 seeded=True` |
| 校验器 | **`FAIL 0`、结论: 绿灯**；`pkg-layout`/`runtime-purity`/`assets-layout` 全 ok |

> 过程中一次自我纠错：重编译脚本只比对了 Runtime/Editor 源码时间戳就报"干净"，而本轮改的是**测试文件**
> ——按 H-36 教训单独核验 `com.collin.citymap.Tests.dll` mtime ≥ 测试源码后才跑测试，避免取到假绿。

## 遗留（不属于失败项，按里程碑推进）

- A-1~A-44 中与算法/工具绑定的条目：随 M2~M11 实现逐条取证。已实测的是规则表与结构类条目（见上表 9~79）。
- **M4-e/M4-f 的实机闭环待用户验证**（§M4-e-3）：场景内创建地形实例后拖笔，看是否即时起伏、
  一笔一撤销是否成立、超格数提示是否出现。这是"雕刻笔刷"从"代码正确"到"手感成立"的最后一跳。
- A-45~A-51（Modules/11 冻结项）**已实测通过**；`BuildingRecord` 结构**已冻结**（用户 2026-11-03 确认）。
- 未实测且当前无法验证的：所有需要河/路/场域/建筑真实数据的断言（A-2~A-8、A-26~A-44、A-48 的端到端形态）。
  **地形侧已在 M2/M3/M4 打通**（S1a/S1b + 显示 mesh + 雕刻数据层与重放均实机可见/已断言）；尚缺的条目：
  A-1-2（重生成下 delta 逐点保持——delta 层、重放与**笔刷工具（M4-e）均已就位**，
  **端到端实机手感待用户验证**：拖笔是否即时起伏、Ctrl+Z 是否整笔回退、雕刻资产是否跨域重载留存）、
  A-1-4（道路各向异性，需 M5 路网）、
  A-1-5（帧率无关）**数学半边已由 M4-d 断言**（纯函数侧 30/60/144Hz 一致 + 余量留存），
  交互壳已按"世界位移驱动、不乘帧时"接线（M4-e），**拖动实感待用户确认**；
  A-1-6（一笔一 undo）状态机半边已断言、`Undo.RecordObject` 半边已接（M4-e），**待实机确认**；
  A-1-8 的"拖动场域不触发 S1a"端到端形态（需 M7 场域）。
- **M4-g 阶段闸门的实机手感待用户验证**（§M4-g-1/§M4-g-2）：数据侧已断言（关平整 25m 保留 + 0 约束、
  开平整 9 约束），但"切到雕刻模式后**贴地预览环是否真的看得见**"、"关平整后拖笔是否即时起山"
  属 SceneView 渲染与交互，EditMode 无从断言，**不虚报已解决**。
  验证路径：面板把「③ 自动平整」关掉 → 切「雕刻」→ 拖笔。
- `Sample` 小城与演示视频：M10。
- 本文档为覆写式，非追加日志（AGENTS §4）。
