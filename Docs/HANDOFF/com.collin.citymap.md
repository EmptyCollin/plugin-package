# 交接单 — com.collin.citymap

> 覆写式交接单（AGENTS §4）。最近更新：2026-11-03（会话 `2026-11-03 m0-skeleton`）

## 1. 已完成（M0 包骨架，已交锁）
- **设计已评审通过**（上一会话 v7.1）；本会话进入代码阶段，按 §3 原子取锁、按 §1.6 三项门禁交锁。
- 包骨架七件全部落地并被 Unity 识别（`verify_project.py` 报 **插件包: 3 个**）：
  - `package.json`（version `0.0.1`，唯一依赖 `com.collin.core@0.2.0`）
  - `Runtime/com.collin.citymap.asmdef`（refs=[core]，validateReferences=true）+ `Runtime/PackageIdentity.cs`
  - `Editor/com.collin.citymap.Editor.asmdef`（refs=[core, citymap]，includePlatforms=[Editor]）+ `Editor/CityMapMenus.cs`
  - `Showcase.json`（上一会话已建，本会话被正式纳管）、`Sample/README.md`
  - `Docs/{DESIGN(已评审 v7.1),ACCEPTANCE(证据已回填),CHANGELOG}`
- 25 个 `.meta` 由 Unity 生成（含两侧 asmdef.meta），未手工编写任何 GUID。
- **交锁门禁实测全绿**：`script recompile_status` failed=False/errors=0；`console clear` → 重跑全部验证动作 →
  `consoleErrors=0 consoleWarnings=0`；`verify_project.py` FAIL 0。两侧 dll 产出、域内已加载确认。
- ACCEPTANCE §9 记录了本会话踩到的**两个环境级真实坑**（后续会话必须照做，否则会重复浪费一轮）：
  1. **新建内嵌包不会被 `Assets/Refresh` 或 `AssetDatabase.Refresh()` 发现**
     （表现为 `script recompile` 返回 `up_to_date`、0 个 .meta、`package list` 查不到本包）。
     唯一有效手段：**`unity command "package resolve"`**。
  2. **asmdef 目录为空（只有 asmdef 没有 .cs）会产生 error 并让控制台门禁 FAIL**，
     且堆栈含本包路径 ⇒ 不适用 §1.8 环境噪声豁免。每个 asmdef 目录至少要放一个真实 `.cs`。
- 顺带修掉自写 bug：`CityMapMenus` 用 `File.Exists()` 判目录导致误报"缺 Docs/Modules/"，
  改 `Directory.Exists()`，重编译后实测输出「模块细则：11 份」。

## 2. 未完成
- M1~M11 全部功能代码（见总纲 §10）。M0 交付判据只是"结构 + 门禁绿"，**无任何生成能力**。
- `CityMapMenus` 目前只有 `Package Info` 一项；City Window、各工具入口尚未挂菜单（M1 起补）。
- 无 EditMode/PlayMode 测试程序集与用例（M0 未规划，M1 建议随 PipelineRunner 一并建 `Tests/`）。
- R5 可选推演（AGENTS §6 插件包 unitypackage 提取 × 人工层）仍未跑。

## 3. 已知坏
- 本包无已知缺陷；M0 门禁证据全绿（见 ACCEPTANCE §1~§8）。
- **环境观察（非本包问题，须上报）**：AGENTS §1.3 写本项目编辑器端口为 **7800**，
  但本会话 `unity status` 实测是 **7801（PID 3512）**——实例已更换过。所有实时通道操作以 `unity status` 为准，
  不要按文档写死端口。是否更新 AGENTS §1.3 需用户决定（§10 规则变更属高风险，我没动）。
- 文档待办（继承自上轮，未变）：`minParcelArea` 03/04 双处配置未归一（M5 前）；
  01~05 沿用 v6 前"实例/GameObject"措辞（M11 前统一）；00 §8 旧内存数值与 4m/byte 口径不一致（M1 实测覆写）；
  `tileCellSize` 与 `chunkSizeM` 两个参数实现时勿混。
- **本次交锁按 §9「一次交锁 = 一个可编译增量 = 一次提交」随 M0 提交**，仅逐路径 `git add` 本包文件与 `Docs/HANDOFF/com.collin.citymap.md`；
  **不 push**（AGENTS §9）。其他会话的未跟踪文件（`Assets/桥_杆_桥_stretchProfile*.asset` 等）一律不纳入本次提交。

## 4. 下一步
1. 需要提交时：按 §9 逐文件 `git add`（禁 `git add .`），message `com.collin.citymap: M0 包骨架`；**不 push**。
   注意 `.meta` 与包目录成对加入，漏 `.meta` 会导致下次 Unity 重新生成 GUID、产生无意义 diff。
2. **M0 后、M7 前冻结 `BuildingRecord` 结构**（Modules/10 §2.2）——D-25 改了 07/08 数据底座，
   M11 再改会波及认回/journal/检查点。
3. M1（Modules/00 基础设施）实现顺序：格网（**4m/2m 双档**）→ SplitMix64 种子树 → `ushort` 掩码
   → `PipelineRunner` 阶段调度与脏传播 → `CityWindow` 壳 → 08 的 `manualVersion` + `UndoHistoryTracker`（首帧建基线）
   → `CityCenterAnchor`。代码注释引用空洞编号（`// 09 H-17`、`// 10 H-25`）。
4. M1 开工前先跑 `unity status` 确认实例，再按 §3 原子取本包锁。

## 5. 勿碰项
- **新建内嵌包后必须用 `package resolve` 触发发现**（见 §3），别浪费时间试 `Assets/Refresh`。
- 不给 asmdef 目录留空（见 §3），也不关掉 `validateReferences`。
- 不改 AGENTS.md / §8 钉死版本 / `Packages/manifest.json` / `ProjectSettings/**`
  （内嵌包无需写进 manifest，本会话未写）。
- 不动 `com.collin.core`、`com.collin.stretch` 文件；M9 段 1 才允许向 core 新增接口文件，且需先申报取锁。
- citymap 侧禁止注册 `IStretchService`（H-14b）；不引第三方 UPM 包进生成内核（Recorder 仅 M10 Sample 用途）。
- 展板场景 `Assets/Showcase/` 不因本包改动；本包演示只进 `Sample/`。
- 当前存在其他会话（历史标识 `showcase-url-fix` / `video-diag`）共用同一编辑器实例：
  **不擅自 `console clear`**，除非确认自己的验证动作已跑完并需要取归零证据。
- 已交付文档原地更新；仅用户明确要求新版本时另存。
