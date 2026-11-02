# CHANGELOG — com.collin.stretch

格式：Keep a Changelog；版本号：semver。

## [Unreleased]

## [0.1.1] - 2026-11-02
### Added
- 包内 `Sample/TestPlayground.unity` 体验场景：3 楼 1 桥（3层→10层 / 5层→7层复合单元 / 原样对照 / 桥面×5），
  含程序化 OBJ 模型、窗户贴图、材质与 4 份 Profile 资产；
- `ShowcaseMedia/stretch_demo.mp4` 演示视频，`Showcase.json` 指向它（展板只消费视频，§0）；
- `StretchSaveHook`（Editor）：场景保存前还原预览、保存后重建——磁盘场景永远只存源网格+Profile 引用。
### Changed
- `StretchPreview` 加 `[ExecuteAlways]`：打开场景即自动重建拉伸预览，无需手动刷新。
### Fixed
- 示例模型绕序与声明法线不自洽导致外侧被剔除"缺片"（用户反馈；根因在生成器非插件）——
  改为三角形直出+导入器重算法线，生成器加右手定则 assert；
- 桥段边界恰与塔壁三角形重心重合导致塔壁被误复制（"栅栏"现象）——边界内缩 0.002 修复。

## [0.1.0] - 2026-11-02
### Added
- `StretchProfile`：拉伸参数资产——单轴、slice 边界划段、fixed/repeatable 段、整数复制倍数，含校验与位移计算；
- `StretchMeshBuilder`：逐三角形按重心归段整体复制，UV/法线逐字保留（杜绝巨型窗户）；>65535 自动 uint32；
- `StretchPreview`（编辑期内存预览）/ `StretchRepeater`（运行时替换网格并同步 BoxCollider）；
- Editor：`Tools/Collin/Stretch 拉伸` 菜单（创建参数+挂预览 / 烘焙结果为 Profile 子资产）、
  Profile 面板（归一化边界插入、段编辑、端点对齐）、Scene 辅助线（段框线/周期线/xN 标签）。
