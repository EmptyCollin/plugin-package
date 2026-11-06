# HANDOFF — com.collin.core

> 覆写式交接单（AGENTS.md §4）。只写可观测事实。

最后交接：qianwen-work-assistant（千问工作助理）/ session showcase-lineage-2026-11-06 / 2026-11-06 / v0.2.0（展板抽离谱系改造，见 git log）

## 1. 已完成
- v0.1.0 全部通信与排序设施 + v0.2.0 `ShowcaseRuntime`（Play 模式磁盘枚举）+ 潜行跨包契约层 `StealthContracts/`（方案 P，2026-11-03）；
- **展板抽离谱系（2026-11-06，DESIGN D-8）**：
  - `ShowcaseEntry` 增可选字段 `tier` / `extractedFrom`（JsonUtility 自动反序列化，Scanner/Runtime 零改动）；
  - `ShowcaseSorter` 增纯函数：`TierOf`（层推断）、`LineageGroups`（血缘聚成产品线，母体在前、环/悬空兜底不丢条目）、`ExportClosure`（方向化导出闭包：产品携带向下抽离能力、工具携带向上抽离运行时；`DependencyClosure` 行为不变）、`ExtractedFromEntry` / `ExtracteesOf`；
  - 五包 `Showcase.json` 补谱系字段（core=middleware；stretch=capability←citymap；citymap/stealth=product；stealthplanner=tool←stealth）；
  - 展板 UI（`Assets/Showcase/ShowcaseUI.cs`）：左栏按谱系分组+缩进、右栏增血缘行、导出携带改用 `ExportClosure`；详情区改滚动布局（长摘要不再截断）；视频区缩至 526x296 不再遮挡；
  - AGENTS.md §6 增谱系字段定义（用户批准 A 档改造含规则同步）；校验器增 `showcase-lineage` FAIL 项（tier 枚举、extractedFrom 指向存在包）；
  - **校验器 check_console 修复**：原 `"console status"` 两段式传参实测 Command Not Found（错误被吞成 WARN），改分段传参 + 空响应抛错；
- 门禁实测（2026-11-06 21:0x）：编译 `failed:false`；clear→重跑全部验证→`consoleErrors:0/consoleWarnings:0`；全量 EditMode **386/386**；校验器**绿灯**；截图核验布局无遮挡。

## 2. 未完成
- 部分提取执行器（`ExportClosure` 已就绪，缺"复制包目录+生成 manifest+导出 unitypackage"的执行器；注意导出 citymap 时应走 ExportClosure 而非仅 package.json 闭包）；
- EditMode 测试集：谱系纯函数（LineageGroups/ExportClosure）暂无入框架的用例，本轮靠 eval_file 探针取证（ACCEPTANCE #24-26）；
- 展板 UI 遗留：视频区未做 16:9 自适应；无键盘导航。

## 3. 已知坏
- 无运行缺陷已知。约束如实记录：typed 通道须具体子类；Services 域重载清空需重注册；展板独立构建时枚举为空（D-5）；
- `extractedFrom` 与 package.json 是两套真相源（D-8 已知代价），校验器只查指向存在性，不查与代码事实一致。

## 4. 下一步
- 若要 citymap→stretch 成编译级硬依赖：需修订 §5 引入"产品/能力/工具"合法方向 + citymap 重构（B 档，待用户拍板）；
- 谱系纯函数补 EditMode 用例；部分提取执行器接入 ExportClosure。

## 5. 勿碰项
- `Assets/Showcase/Showcase.unity`：**禁止为单个插件修改**（§6），条目与谱系只经各包 Showcase.json；
- AGENTS.md 全部条目（§10）；钉死依赖版本（§8）；`Tools/console_allowlist.json` 现有两条豁免（新增须再批）；
- `ShowcaseSorter.DependencyClosure` 的既有语义（部分提取与 stealthplanner 均在用）——导出闭包新语义请走 `ExportClosure`；
- 各包 `Showcase.json` 的 `video`/`order` 字段（本轮只增 tier/extractedFrom，未动其余）。
