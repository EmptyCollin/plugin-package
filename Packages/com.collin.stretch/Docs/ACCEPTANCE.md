# ACCEPTANCE — com.collin.stretch 0.1.0

> 验证者：qianwen-work-assistant（千问工作助理），2026-11-02，编辑器 PID 16552 / 端口 7800。
> 方法：`unity command eval_file` 程序化断言（结果文件摘录）+ 场景级端到端（临时物体/资产，验后即删）。

| # | 验收项（可观测行为） | 验证指令 / 方法 | 结果与证据 |
|---|---|---|---|
| 1 | 编译干净（Runtime+Editor 两程序集） | `recompile` → `recompile_status` | `failed=false`；`Library/ScriptAssemblies/` 含 com.collin.stretch(.Editor).dll |
| 2 | 用户原例：3 层楼中段复制 10 次 → 总高 12、楼顶刚性移至 12、72 顶点槽、复制区 UV 全在 (0..1) | StretchVerify A1 | `verts=72(72) maxY=12.0(12) uvStd=True top@12=True factor=4.0(4.0)` |
| 3 | 全 fixed repeat=1 逐三角形恒等 | A2 | `triEquivalent=True idx=18 v=18` |
| 4 | 三角形按重心归段（跨边界三角形整体归段，不被拉长） | A3 | `verts=27(27) maxY=4.5(4.5)`（floor1 两三角形重心 1.33/1.67 分属两段，符合 D-3） |
| 5 | 非法参数（非升序/repeat<1/端点漂移）拒绝且 Build 返回 null | A4 | `err=[末个边界应等于包围盒最大值] buildNull=True` |
| 6 | >65535 顶点自动 uint32 索引 | A5 | `verts=147600 idx=147600 fmt=UInt32` |
| 7 | 桥例：X 轴中段复制 5 → 总长 7 | A6 | `verts=42(42) maxX=7.0(7)` |
| 8 | 场景端到端：临时物体 + StretchPreview → 预览网格 maxY=12 | S1Scene | `S1 maxY=12 verts=72 pass=True` |
| 9 | Scene 辅助线回调已注册且目标解析成功 | S2Bake S2a/S2b（反射读 duringSceneGui + ResolveTarget） | `hookRegistered=True`；`resolved=true segs=3 repeat1=10` |
| 10 | 烘焙闭环：结果持久化为 Profile 子资产、重复烘焙幂等（子网格数=1）、可脱离预览独立消费 | S3/S3b/S3c | `baked=True verts=72 maxY=12`；`subMeshCount=1`；`consumerVerts=72` |
| 11 | §1.6b 门禁：clear→重跑全部验证→归零 | console clear→A1-A6+S1+S2→console status | 首轮抓到 1 条 error（S1 读 filter.mesh 触发"Instantiating mesh…leak"），修复后终读 `errors=0 warnings=0`（见下方修复记录） |

## 修复记录（验收过程真实抓出的缺陷）
- **预览时序缺陷**：`AddComponent<StretchPreview>()` 的 OnEnable 早于外部赋 profile，预览静默空跑（S1 首轮 `maxY=3` 暴露）。修复：新增 `SetProfile()` 显式装配入口，CreateCommand 与 S1 均改走它；复跑 `maxY=12 pass=True`。
- **MeshFilter.mesh 编辑期泄漏**：S1 读 `filter.mesh` 触发 Unity 泄漏告警 error（门禁抓到）。修复：Preview/Repeater 统一 `sharedMesh` 读写；S1 同步修正。
- **Builder 日志级别**：参数中间态原用 LogError 会污染门禁。修复：Build 静默返回 null，日志归调用方（Preview 警告/Bake 返回原因/面板显示）。

# v0.1.x 增量（体验场景 + 用户反馈修复，2026-11-02，同验证者）

| # | 验收项（可观测行为） | 验证指令 / 方法 | 结果与证据 |
|---|---|---|---|
| 12 | 包内 Sample 体验场景：3 楼 1 桥（楼A 3层→10层、楼B 5层→7层复合单元、楼C 原样对照、桥桥面×5） | `eval_file` 建场景 → `SaveScene` → 重开读几何 | 重开后 `楼A maxY=10.0 v=492`、`楼B maxY=7.0 v=264`、`桥 maxX=3.98 v=234`，与手算一致 |
| 13 | 用户反馈"外围看有部分面片缺失，从内侧看里面是有的"——定位为**示例 OBJ 生成器的面绕序与声明法线不自洽**（非插件缺陷：Builder 原样保留三角形绕序） | 离线右手定则 assert + Unity 侧 `Dot(法线, 面心-体心)` 朝外断言 | 修复后 `楼A 52/52 朝外`、`楼B 64/64`、`楼C 52/52`；桥 42 朝外 + 12 为塔-桥面贴合内侧面（判据对多箱组合体误伤，目视无缺面） |
| 14 | 预览网格不再污染场景文件：保存时还原、保存后重建，重开由 `[ExecuteAlways]` 自动重建 | `MarkSceneDirty`→`SaveScene`（触发 StretchSaveHook）→`OpenScene` 读几何 + 场景文本扫描 | 重开几何与保存前一致；`sceneFileMissingCheck=True`（无 missing/instanceID:0 网格引用），场景文件 23KB |
| 15 | 桥贴边面误复制（用户截图"栅栏"现象）：塔壁三角形重心恰落段边界 → 被 repeatable 段复制 | 逐段三角形计数诊断 `trisPerSeg=[18\|24\|18]`（塔应 12）→ 边界内缩 0.002 | 修复后 `verts=234`（78 三角形 = 24+30+24），目视截图桥面连续、塔壁不再成栅栏 |
| 16 | 展板消费包内演示视频（§0 纠正语义：展板只引用视频，不引用插件场景/代码） | `ShowcaseRuntime.Load()` + `ResolveVideoPath()` | `entries=2 hasStretch=True video=[ShowcaseMedia/stretch_demo.mp4] resolved=EXISTS` |
| 17 | §1.6b 门禁终态 | `console clear` → 重跑 T1-T3 → `console status` | `errors=0 warnings=0 compilationFailed=False`；校验器 assets-layout 绿灯 |

# v0.2.0 增量（数据归属重构 + AGENTS.md §0.1 三原则落地，2026-11-02，同验证者）

| # | 验收项（可观测行为） | 验证指令 / 方法 | 结果与证据 |
|---|---|---|---|
| 18 | 新签名 `Build(source, profile, repeats)` 算法回归：3→12层(×10)、倍数长度不符拒绝、Fixed 段忽略实例倍数、全 1 恒等 | `eval_file UnitV2.cs` | `U1 verts=72 maxY=12`；`U2 rejected reason=[倍数数据异常: 倍数数组与段数不匹配]`；`U3 verts=18 恒等`；`U4 verts=18 maxY=3` |
| 19 | **实例分离**（原则2）：同一 Profile 资产被两实例以 ×3 / ×20 复用，几何独立互不影响 | `eval_file VerifySep.cs` V1 | `maxY1=5.0(=1+3+1)`、`maxY2=22.0(=1+20+1)`、`v1=252 v2=1068` 各自成立 |
| 20 | 倍数序列化在实例组件、烘焙固化实例倍数、实例主编辑器就位 | V2/V3/V4 | `repeats arraySize=3`（在组件上）；`baked verts=252`(取实例×3)；`instance editor type=True` |
| 21 | 辅助线唯一入口（移除资产面板静态 `duringSceneGui`，修双绘） | `eval_file VerifyFinal.cs` 反射 | `duringSceneGui stretchHooks=0`；`instanceEditor OnSceneGUI=True`；`profileEditor HookSceneGuiRemoved=True` |
| 22 | 体验场景按新结构重开即时重建（[ExecuteAlways]+保存守卫） | `OpenScene` 读几何 | `楼A 492/maxAxis10.0`、`楼B 264/7.0`、`桥 234/3.98`，missing 脚本 0 |
| 23 | `runtime-purity` 校验（原则3：Runtime 禁引用 UnityEditor） | `python Tools/verify_project.py` | `[ ok ] runtime-purity: Runtime 程序集无 UnityEditor 依赖` |
| 24 | `CreateProfile` 裸 NRE 修复：空守卫 + try/catch 弹窗 | 代码审阅 + 归零复跑 | 重构后 clear→重跑，09:21:30 那条 `StretchCreateCommand.CreateProfile` NRE 堆栈未复现 |
| 25 | §1.6b 门禁终态 | `console clear` → 重跑 UnitV2+VerifySep+VerifyFinal → `console status` | `errors=0 warnings=0 compilationFailed=False` |

## 修复记录（本轮真实抓出/清除）
- **数据归属错误（用户指出）**：v0.1 把倍数存进共享 Profile，违反 §0.1 原则2——同一 Profile 复用多楼时无法各自不同倍数。0.2.0 迁到实例组件，V1 证明 ×3/×20 独立生效。
- **辅助线双绘**：`StretchProfileEditor` 静态 `duringSceneGui` 与 `StretchInstanceEditor.OnSceneGUI` 对同一选中实例各画一遍。移除前者，辅助线唯一入口收敛到实例编辑器（原则1）。
- **历史 NRE（seq227，09:21:30）**：来自重构前旧 `CreateProfile`（`preview.SetProfile` 未判空）。新代码已空守卫+异常捕获，归零复跑未复现，非存活缺陷。

# v0.3.0 增量（算法通用化 + 碰撞同步 + 桥重做，2026-11-02，同验证者）

| # | 验收项（可观测行为） | 验证指令 / 方法 | 结果与证据 |
|---|---|---|---|
| 26 | **任意边界切穿窗户不拉伸**：窗户 quad 跨 y=0.5，边界恰切窗户正中，段 ×3 复制 | `eval_file VerifyGeneral.cs` G1 | `verts=96 boundsY=[0.00,2.00](期望[0,2]) uvKept=True`——切口插值，UV 全在 [0,1] |
| 27 | 整段复制（无切割）窗户仍标准 | G2 | `boundsY=[0,5] verts=120` |
| 28 | **BoxCollider 同步**：拉伸 ×4 后碰撞盒与视觉同尺寸 | G3 | `box.size=(3.00,4.00,0.10) syncedY=4.00(期望4) center=(1.5,2,0.05)` |
| 29 | 桥原型任意边界（切穿桥墩中部）确定性分片 | G4 | `boundsX=[-18.6,2.2] verts=2340`，无异常 |
| 30 | **MeshCollider 同步+还原**：重建后 mc.sharedMesh=预览网格；Restore 回源 | `eval_file VerifyCollider.cs` G5 | `ok=True lenX=28.16(28.16) mcSynced=True mcRestored=True` |
| 31 | 教学链路端到端（用户同款步骤：InitFromMesh→插缝→标 Repeatable→×4） | G6 + BridgeShots 现场实测 | `ok=True reason=[(none)] lenX=28.16 verts=3096`；现场截图 `lenX=25.16 box.size.x=25.16`，还原后 `lenBack=18.56 boxBack=18.56 instNow=True(组件已删)` |
| 32 | 旧用例回归不退化（快速路径逐顶点数与 0.1.x 一致）+ 体验场景三物体 | Recheck R1/R2/R3 | `R1 verts=72 maxY=12`；`R2 verts=18 恒等`；`楼A 492/10.0 楼B 264/7.0 桥裸模型1900 noComp=True` |
| 33 | 桥重做外观：引桥节段（箱梁+柱墩+盖梁+护栏）+主跨拱肋+桥台，跨间纯空气缝 | 相机截图目视 | 截图确认结构成立；生成器绕序断言 0 异常（`windingBad=0`，1900 顶点/636 三角形） |
| 34 | 桥在场景为裸原型，**无组件、无 Profile 资产** | 场景层级 + AssetDatabase 枚举 | `桥_原型 StretchInstance==null`；`bridge profiles remaining=0` |
| 35 | 面板端点对齐缺陷修复（不再从结果网格取包围盒） | 代码路径审阅 + 楼A 资产复原回归 | 楼A profile 与 HEAD 零 diff；DrawStructure 入参改为源网格 |
| 36 | §1.6b 门禁终态 | `console clear` → 重跑 G1-G7/R1-R3 → `console status` | `errors=0 warnings=0 compilationFailed=False`；runtime-purity/assets-layout 绿灯 |

## 本轮验收中真实抓出/修复的缺陷
- **端点对齐读结果网格**（用户现场资产曾被写歪：楼A 末边界 3→8）：面板 `DrawStructure` 原从 `MeshFilter.sharedMesh` 取包围盒，编辑态即拉伸结果，点一次"端点对齐"就把结果尺寸写回边界。锁死为实例缓存源网格（`Inst.OriginalMesh`），楼A 资产已复原并回归验证。
- **轴/包围盒不同源静默失败**：`InitFromBounds` 不设轴，误配轴时 Validate 正确拦停但用户难自查——新增 `InitFromMesh(mesh, axis)` 一体初始化，菜单与教学路径同走。
- **MeshCollider 采集时序**：原只在 SetProfile 采集一次，重开场景后 OnEnable 直接 Rebuild 会漏同步——改为每次 Rebuild 前以源网格为锚点重采集。

# v0.3.1 增量（真实 CC0 资产落地 + 多子网格修复，2026-11-02，同验证者）

| # | 验收项（可观测行为） | 验证指令 / 方法 | 结果与证据 |
|---|---|---|---|
| 37 | **多材质子网格保持**：真实 FBX 栈桥（每 section 2 子网格=木板+桩柱）拉伸后两槽均有内容、材质槽数不变 | `eval_file FinalPier.cs` 实测 | 源 `sub=2 v=147600`；×3 拉伸后 `sub=2 subTris=[43008,16768] both>0=True matSlots=2 uvOk=True`（旧 Builder 只读子网格0会丢一半） |
| 38 | 真实模型中段 ×3：长度与碰撞盒同步 | 同上 | `lenZ=13.10(src 9.36) boxZ=13.10 match=True` |
| 39 | 单网格旧用例不退化（K1/K2）+ 场景四物体（K3） | `eval_file Regress.cs`（归零后重跑） | `K1 verts=72 maxY=12 sub=1`；`K2 insert=1 切穿窗户 uvOk=True`；`K3 楼A492/10.0 楼B264/7.0 桥sub=2 v=147600 noComp=True` |
| 40 | 桥在场景仍为裸模型、零 Profile（用户要求不变） | AssetDatabase 枚举 | `bridgeProfiles=0`；`桥_栈桥 StretchInstance==null` |
| 41 | §1.6b 门禁终态 | clear→重跑 K1-K3→console status | `errors=0 warnings=0 compilationFailed=False` |

## 本轮真实抓出的缺陷（现成模型独有，程序化网格永远测不出）
- **Builder 只读子网格 0**：`source.triangles` 仅含第一个材质槽——FBX 多材质模型直接丢半（栈桥会丢全部桩柱或全部木板）。修复：逐子网格处理，结果保持 `subMeshCount` 与槽位对应（K37 实证）。
- 示例资产二次污染（楼B 边界 2→1 被现场操作改歪致 300≠264）：从 HEAD 复原并回归 264/7.0 通过。教训已写入 HANDOFF 勿碰项。
