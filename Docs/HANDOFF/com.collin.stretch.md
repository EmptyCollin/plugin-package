# HANDOFF — com.collin.stretch

> 覆写式交接单（AGENTS.md §4）。只写可观测事实。

最后交接：qianwen-work-assistant（千问工作助理）/ 2026-11-02 / v0.2.0（提交见 git log）

## 1. 已完成
- **0.2.0 数据归属重构**（用户引导 + AGENTS.md §0.1 三原则落地）：
  - Profile 只存共享结构（axis/sliceBounds/segmentTypes），倍数迁到 `StretchInstance.repeats[]`（场景组件序列化）；
  - `StretchInstance` 合并旧 Preview+Repeater，[ExecuteAlways] 编辑/运行同一条构建路径；
  - 主面板 `StretchInstanceEditor`（实例 Inspector：轴+结构折叠+每段倍数+即时预览），资产面板只读摘要+定位；
  - Builder/烘焙签名 `(source, profile, repeats)` + 倍数长度校验；
  - `StretchCreateCommand` 空守卫+try/catch（修裸 NRE）；移除资产面板静态 `duringSceneGui`（辅助线双绘修复）；
  - 校验器新增 `runtime-purity`；
- 体验场景 `Sample/TestPlayground.unity` 按新结构整体重建（删旧 missing-script 物体→挂 StretchInstance）：楼A ×8(maxY10/v492)、楼B ×2(maxY7/v264)、桥 ×5(maxX3.98/v234)；
- 验收：单元 U1-U4 + 实例分离 V1-V4 全绿（V1：同一 Profile ×3→maxY5、×20→maxY22 独立生效；V2 repeats 序列化在组件；V3 烘焙取实例倍数 252 顶点；V4 实例编辑器类型就位）；§1.6b 归零 errors=0 warnings=0；runtime-purity/assets-layout 绿灯。

## 2. 未完成
- 拖拽把手调倍数（D-4，仍推迟）；跨段三角形用户侧检测提示（R-1 仅文档）；接缝焊接（R-2）；
- README 用户文档、真实录屏替换占位演示视频。

## 3. 已知坏
- 无运行缺陷已知。约束记录：源网格须 Read/Write+UV0；边界含包围盒端点（面板可对齐）；
  贴边几何需边界内缩 ε（桥已内缩 0.002）；`game_view/scene_view --save_path` 相对路径落到 Assets/ 下（CLI 怪癖，须清理 Assets/.locks）。

## 4. 下一步
- 用户在 TestPlayground 选中实例体验新面板（倍数在实例上、结构折叠区、即时预览）；
- 反馈后定优先级：把手 / 跨段检测 / README / 录屏。

## 5. 勿碰项
- **数据归属语义**（结构=Profile 共享、倍数=实例序列化，§0.1 原则2）——把倍数塞回 Profile 即破坏多实例复用；
- 逐三角形归段+UV 逐字（D-3）；sharedMesh 读写约定；辅助线绘制唯一入口=StretchInstanceEditor.OnSceneGUI；
- Sample 内 OBJ 生成约定（三角形直出不写 vn，Unity 重算法线）；AGENTS.md §0/§0.1；com.collin.core（另锁范围）。
