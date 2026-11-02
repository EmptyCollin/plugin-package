# HANDOFF — com.collin.stretch

> 覆写式交接单（AGENTS.md §4）。只写可观测事实。

最后交接：qianwen-work-assistant（千问工作助理）/ 2026-11-02 / v0.1.1（提交见 git log）

## 1. 已完成
- v0.1.0 算法与工具链全量（见 CHANGELOG）+ **v0.1.1**：包内 Sample 体验场景（3 楼 1 桥 + 程序化 OBJ + 材质 + 4 Profile）、
  ShowcaseMedia 演示视频、`StretchSaveHook`（存盘还原/重建守卫）、`[ExecuteAlways]` 预览自动重建；
- 用户反馈两处修复：示例模型绕序不自洽"缺片"（根因在生成器，改三角形直出+**不写 vn 由 Unity 默认重算法线**+生成器 assert）、
  桥贴边面误复制"栅栏"（边界内缩 0.002）；
- ACCEPTANCE 17 项带证据；§1.6b 门禁终读 errors=0 warnings=0；校验器绿灯（含 assets-layout 守卫）。

## 2. 未完成
- 拖拽把手调倍数（DESIGN D-4，推迟 v0.2）；跨段三角形用户侧检测提示（R-1 仅文档告知）；接缝焊接（R-2）；
- 楼B/楼C 的 Sample 场景里未挂 Repeater（预览态即拉伸态，运行 Play 也正确——Preview 在 Play 同样工作；若要"源网格+运行时组件"的交付形态演示，加一个 Repeater 实例）；
- README（用户向使用文档）与真实录屏替换占位演示视频。

## 3. 已知坏
- 无运行缺陷已知。约束如实记录：源网格须 Read/Write + UV0；边界含包围盒端点（面板有对齐按钮）；
  三角形重心恰落边界归上段——**贴边几何（共面墙）需边界内缩 ε**（桥案例已验证）；
  展板/示例场景在包内时 `game_view --save_path` 仍解析到 Assets/ 下（CLI 怪癖，用绝对路径也无效，截图放 .locks 需事后清理 Assets/.locks）。

## 4. 下一步
- 用户进 `Packages/com.collin.stretch/Sample/TestPlayground.unity` 体验（打开即见拉伸，选中物体看辅助线，改 Profile 倍数实时预览）；
- 反馈收集后决定 v0.2 优先级（把手 / 跨段检测 / README / 录屏）。

## 5. 勿碰项
- 算法语义（逐三角形归段 + UV 逐字，DESIGN D-3）；sharedMesh 读写约定（mesh 属性编辑期触发泄漏 error）；
- Sample 内 OBJ 的生成约定：**三角形直出、不写 vn 行**（Unity 无文件法线时按几何重算，绕序唯一决定朝向）；
  写 vn 四顶点共享索引的旧格式会复现"缺片"。实测 `ModelImporter.splayEdgeVertices` 在本版不存在（CS1061），勿再尝试；
- AGENTS.md §0（展板=Assets/Showcase 公共入口；插件演示=包内 Sample）；com.collin.core 全部（另锁范围）。
