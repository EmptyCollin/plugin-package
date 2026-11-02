# HANDOFF — com.collin.stretch

> 覆写式交接单（AGENTS.md §4）。只写可观测事实。

最后交接：qianwen-work-assistant（千问工作助理）/ 2026-11-02 / v0.3.1（提交见 git log）

## 1. 已完成
- 0.3.0 全部内容（边界裁剪通用化、碰撞/物理同步、面板边界精调、InitFromMesh、桥教学步骤）；
- **0.3.1 多子网格修复**：Builder 原 `source.triangles` 只读子网格 0——真实 FBX 多材质模型会丢一半。
  改为逐子网格处理，结果保持 subMeshCount 与材质槽对应（验收 37：栈桥 sub=2、双槽都有三角形、matSlots=2）；
- **示例桥换为真实 CC0 资产**：Poly Haven `modular_wooden_pier`（FBX 1K + 4 贴图，CC0 免署名，
  来源登记 Sample/Models/CREDITS.md）；场景物体 `桥_栈桥` = 三直段按材质合并的连续栈桥
  （Sample/Meshes/pier_long.asset，双材质 147600 顶点、9.36m），**裸模型无组件无 Profile**（用户要求维持）；
- 实测：真实栈桥中段 ×3 → lenZ 9.36→13.10、boxZ=13.10 精确跟随、UV 无超界；还原后 9.36/147600 干净；
- 程序化 `桥_原型.obj` 已删除；楼B 示例资产被现场操作污染（边界 2→1，300≠264）已从 HEAD 复原并回归；
- 归零门禁 errors=0 warnings=0；回归 K1/K2/K3 全绿；runtime-purity/assets-layout 绿灯。

## 2. 未完成
- 拖拽把手（D-4）；接缝焊接（R-2）；README 用户文档；演示视频换真实录屏；
- pier_long 合并是一次性脚本操作，未沉淀为包内工具（如常换资产需要，做成 Editor 菜单）。

## 3. 已知坏
- 无运行缺陷已知。约束记录：源网格须 Read/Write+UV0（创建命令自动开）；多材质模型现已被支持但
  **每子网格独立分片**（跨槽相邻三角形不焊接，R-2 同样适用）；FBX 导入 bounds 可能极小（厘米→米换算），
  实例靠 transform scale 补偿，边界用局部坐标；`game_view --save_path` 会把路径重定位到 Assets/.locks（用后清）。

## 4. 下一步
- 用户体验：打开 TestPlayground，选中 `桥_栈桥` 按教学步骤自建拉伸（轴 Z、边界随意、中段 Repeatable）；
- 反馈定优先级：把手 / README+录屏 / 焊接 / 合并工具化。

## 5. 勿碰项
- **逐子网格处理 + 材质槽保持**（0.3.1 根基，退回 `source.triangles` 即丢多材质模型一半）；
- 裁剪分片+切口插值（D-3 通用性）；数据归属（结构=Profile、倍数=实例，§0.1 原则2）；
- sharedMesh 读写约定；DrawStructure 必须传**源网格**；
- Sample 的 CC0 资产与 CREDITS.md（许可登记不可丢）；桥保持裸模型无 Profile（用户明令）；
- AGENTS.md §0/§0.1；com.collin.core（另锁范围）。

## 附：桥的手动创建步骤（真实资产版，教学实测过）
1. 打开 `Packages/com.collin.stretch/Sample/TestPlayground.unity`，选中 **桥_栈桥**；
2. 菜单 `Tools/Collin/Stretch 拉伸/新建拉伸参数`（自动开 Read/Write、建 Profile、挂实例）；
3. 面板拉伸轴选 **Z**（桥长方向；FBX 局部坐标，长度约 9.36）；
4. 在"边界精调"里随意插两个中间边界（如 -2.5 与 0.5，或直接用 % 滑条）——真实模型没有整齐缝，
   切在哪都对（裁剪算法保证）；
5. 把中间那段类型改 **Repeatable**、倍数填 3 → 栈桥变长约 1/3，BoxCollider 自动同尺寸；
6. 定稿 `烘焙选中物体的结果`；反悔点"还原原网格"。
