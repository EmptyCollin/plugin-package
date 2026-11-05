# CHANGELOG — com.collin.stealthplanner

格式：Keep a Changelog（https://keepachangelog.com）；版本号：semver。

## [Unreleased]

## [0.0.1] - 2026-11-05
### Added
- 包骨架（DESIGN 先于代码，AGENTS.md §4）：`package.json`（依赖仅 `com.collin.core`）、
  `Runtime/` 与 `Editor/` 两套 asmdef（`validateReferences: true`，Runtime 不引 UnityEditor）、
  `Showcase.json`（`order=31`，沿用 stealth DESIGN §4.4 为设计工具预留的序号位）。
- `Docs/DESIGN.md` v0.1 草案：潜行关卡设计辅助台的完整方案与决策记录，覆盖用户提出的
  静态设计 1~4 与动态设计 1~3。核心架构是「时空可见性查找表」——把玩法包的主线程射线判定
  烘焙成离线数据资产，之后求解/仿真全部退化为纯 CPU 查表，以同时满足「可后台并行」与
  「编辑器/引擎双端共用」两条硬约束。
- `Docs/ACCEPTANCE.md` 验收草案（逐条待实现后取证，当前无一条通过）。

> 本版仅骨架 + 设计，无任何 Runtime/Editor 实现代码，故不进入可玩状态、`Showcase.json.video` 留空。
