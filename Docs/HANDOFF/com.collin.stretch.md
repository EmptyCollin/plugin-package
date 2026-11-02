# HANDOFF — com.collin.stretch

> 覆写式交接单（AGENTS.md §4）。只写可观测事实。

最后交接：qianwen-work-assistant（千问工作助理）/ 2026-11-02 / v0.1.0（提交见 git log）

## 1. 已完成
- v0.1.0 全量：Profile（划段/倍数/校验/位移）、MeshBuilder（逐三角形归段复制、UV 逐字保留、uint32 自动）、
  Preview（编辑期内存预览）、Repeater（运行时替换+BoxCollider 同步）、Bake（Profile 子资产烘焙，幂等）、
  面板（归一化 % 插边界、端点对齐、段编辑）、Scene 辅助线（段框线/周期线/xN 标签）；
- ACCEPTANCE 11 项带证据（3 层→10 层、桥例、恒等、重心归段、非法拒绝、uint32、端到端、烘焙闭环）；
- 验收中抓出并修复 3 缺陷：OnEnable 早于赋 profile 的时序（→SetProfile 显式装配）、MeshFilter.mesh 编辑期泄漏（→统一 sharedMesh）、Builder 中间态 LogError 污染门禁（→静默 null，日志归调用方）。

## 2. 未完成
- 拖拽把手调倍数（DESIGN D-4 修订，推迟 v0.2）；
- 跨段三角形的用户侧检测提示（R-1 目前仅文档告知，未做"重心贴边告警"）；
- 接缝焊接/法线平滑（R-2）；README/使用文档（展板视频）。

## 3. 已知坏
- 无运行缺陷已知。约束如实记录：源网格须 Read/Write + UV0；边界须含包围盒端点（面板有对齐按钮）；
  三角形重心恰落边界时归上段（SegmentIndexAt 的 [lo,hi) 语义）。

## 4. 下一步
- 用户拿真实楼/桥 FBX 试用面板，反馈边界选取体验；
- v0.2 候选：拖拽把手、跨段三角形检测、展板演示视频。

## 5. 勿碰项
- 算法语义（逐三角形归段 + UV 逐字，DESIGN D-3）——改动即破坏"无巨型窗户"承诺；
- StretchPreview/Repeater 的 sharedMesh 读写约定（mesh 属性编辑期触发泄漏 error，撞 §1.6b）；
- AGENTS.md §5/§6/§8；com.collin.core 全部（另一把锁的范围）。

## 附：本会话实测指令口径（补充 core HANDOFF 未覆盖项）
- 嵌入包新建后 `AssetDatabase.Refresh()` 不一定收录，需 `Client.Resolve()` + 轮询 `"package status"`；
- `scene_view`/`game_view` 的 --save_path 相对路径解析到 **Assets/** 下，必须传绝对路径；
- `scene_view` 截图用的相机不受 `SceneView.lastActiveSceneView.LookAt` 影响（多 Scene 窗口时截了别的视角）；
- 单面 quad 的辅助线框从正面看与轮廓重合不可辨，验证辅助线用斜视角程序化断言（反射 duringSceneGui backing field）替代截图判读。
