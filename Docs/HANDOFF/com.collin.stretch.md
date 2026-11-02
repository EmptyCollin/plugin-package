# HANDOFF — com.collin.stretch

> 覆写式交接单（AGENTS.md §4）。只写可观测事实。

最后交接：qianwen-work-assistant（千问工作助理）/ 2026-11-02 / v0.3.2（提交见 git log）

## 1. 已完成
- 0.3.0/0.3.1 全部内容（边界裁剪通用化、碰撞/刚体同步、多材质子网格保持、示例桥=真实 CC0 栈桥）；
- **0.3.2 UI 修复**（用户截图反馈三条）：
  - 边界"改了被重置"：根因=旧实现 `Clamp(相邻边界)` 夹死输入。现允许越界输入，
    `StretchProfile.NormalizeOrder()` 升序重排（segmentTypes 跟随移动），实例 repeats 经 `RemapByOrder` 同步；
    端到端入口 `StretchInstance.NormalizeBounds()`（验收 N1/N2/N3/N4）；
  - 面板重构：限高滚动区（400px）、每段一行（范围+类型+倍数+删）、边界一行紧凑输入、插入/端点对齐同行；
  - **Scene 视图拖拽边界把手**：青色点 FreeMoveHandle，位移换算 `dWorld/(lossyScale×下方段倍数)`，
    拖过相邻边界同样自动重排（D-4 把手就此落地；栈桥 scale=100 的换算在 lossyScale 项覆盖）；
- 归零门禁 errors=0 warnings=0；旧回归 K1-K3 全绿；runtime-purity/assets-layout 绿灯。

## 2. 未完成
- 倍数的拖拽交互（仍为整数输入框）；接缝焊接（R-2）；README 用户文档；演示视频换真实录屏；
- pier_long 合并为一次性脚本，未沉淀成 Editor 工具。

## 3. 已知坏
- 无运行缺陷已知。约束记录：重排后段索引会变（外部若缓存旧索引必须经 map 转换）；
  把手只画中间边界，首尾端点用"端点对齐"；`game_view --save_path` 路径会被重定位到 Assets/.locks（用后清）。

## 4. 下一步
- 用户体验新面板与拖拽（TestPlayground 选中 桥_栈桥 或楼A），反馈定优先级：倍数交互 / README+录屏 / 焊接；
- 部分提取导出执行器（core HANDOFF 遗留候选）。

## 5. 勿碰项
- **越界→NormalizeOrder 重排语义**（退回"夹在相邻间"即复现用户报告的"改了被重置"缺陷）；
- 把手位移换算（lossyScale×repBelow，两者缺一都会导致大缩放/复制段上把手漂移）；
- 逐子网格+材质槽保持（0.3.1）；裁剪分片+切口插值（D-3）；数据归属（结构=Profile、倍数=实例，§0.1 原则2）；
- sharedMesh 读写约定；面板端点对齐必须用 `Inst.OriginalMesh`（源网格），传 MeshFilter.sharedMesh 即复现边界污染；
- Sample 的 CC0 资产与 CREDITS.md；桥保持裸模型无 Profile（用户明令）；AGENTS.md §0/§0.1；com.collin.core（另锁范围）。
