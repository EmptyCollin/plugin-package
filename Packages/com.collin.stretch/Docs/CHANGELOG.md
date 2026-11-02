# CHANGELOG — com.collin.stretch

格式：Keep a Changelog；版本号：semver。

## [Unreleased]

## [0.1.0] - 2026-11-02
### Added
- `StretchProfile`：拉伸参数资产——单轴、slice 边界划段、fixed/repeatable 段、整数复制倍数，含校验与位移计算；
- `StretchMeshBuilder`：逐三角形按重心归段整体复制，UV/法线逐字保留（杜绝巨型窗户）；>65535 自动 uint32；
- `StretchPreview`（编辑期内存预览，不写磁盘）/ `StretchRepeater`（运行时替换网格并同步 BoxCollider）；
- Editor：`Tools/Collin/Stretch 拉伸` 菜单（创建参数+挂预览 / 烘焙结果为 Profile 子资产）、
  Profile 面板（归一化边界插入、段编辑、端点对齐）、Scene 辅助线（段框线/周期线/repeatable 标签）。
