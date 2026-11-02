# HANDOFF — com.collin.core

> 覆写式交接单（AGENTS.md §4）。只写可观测事实。

最后交接：qianwen-work-assistant（千问工作助理）/ 2026-11-02 / v0.2.0（展板上线提交，见 git log）

## 1. 已完成
- v0.1.0 全部通信与排序设施 + **v0.2.0 `ShowcaseRuntime`**（Play 模式磁盘枚举 Packages/com.collin.*/Showcase.json + 视频路径解析）；
- **展板本体上线**：`Assets/Showcase/Showcase.unity`（仅 2 个 GameObject：Showcase[Canvas+ShowcaseUI] + EventSystem）+ `Assets/Showcase/ShowcaseUI.cs`（全部 uGUI 代码构建，DESIGN D-6）；
- 实测通过：拓扑排序（临时包 demotest 依赖 core → core 恒在前）、条目点击切换、依赖闭包显示、RenderTexture 视频播放（ffmpeg 测试片验证）；
- §1.6b 新门禁完整走通：clear→重跑验证→`errors=0 warnings=0`→校验器绿灯→交锁；
- 指令口径实测（全部验证过）：`"scene create/open/get_hierarchy"`、`"gameobject create"`、`"component add"`、`"script attach"`、`"editor save_all"`、`"editor.playmode start/stop/pause"`、`game_view --save_path`（**相对路径会解析进 Assets/**，用绝对路径）、`"package status"`、`focus`；`eval` 须语句形式（末尾分号）；`eval_file` 环境只默认 using UnityEngine，UI 类型要写全名 `UnityEngine.UI.Text`；
- 临时验收包与截图残骸已清理，packages-lock 残留经 `Client.Resolve()` 清除（grep=0）。

## 2. 未完成
- 部分提取导出工具（`ShowcaseRuntime`/`ShowcaseSorter` 已提供数据与闭包，缺"复制包目录+生成 manifest+导出 unitypackage"的执行器）；
- EditMode 测试集（验收走 eval_file，等效但未入 test framework）；
- 展板 UI 细节：视频区未做 16:9 自适应（当前固定 960x360 RT，非 16:9 素材会拉伸）；无键盘导航。

## 3. 已知坏
- 无运行缺陷已知。约束如实记录：typed 通道须具体子类（泛型 SO 引擎限制）；Services 域重载清空需重注册；展板独立构建时枚举为空（D-5 已知边界）。

## 4. 下一步
- 首个 feature 包立项（按模板四件套 + 依赖 core + Showcase.json 带真实视频）；
- 部分提取执行器（可做成 Editor 菜单 + CLI 双入口）。

## 5. 勿碰项
- `Assets/Showcase/Showcase.unity`：**禁止为单个插件修改**（§6），条目增减只经各包 Showcase.json；
- AGENTS.md 全部条目（§10）；钉死依赖版本（§8）；`Tools/console_allowlist.json` 现有两条豁免（用户批准，新增须再批）；
- Tools/verify_project.py 的 check_console（与 §1.6b 联动）。
