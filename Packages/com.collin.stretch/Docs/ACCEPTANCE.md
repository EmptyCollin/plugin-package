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
