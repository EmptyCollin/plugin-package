# AGENTS.md — 插件集合工程 · 多 AI 协作统一规则

本文件是全项目**唯一规则真源**，适用于所有参与的 AI 客户端。
客户端专属入口文件（CLAUDE.md、.cursorrules 等）只能指向本文件，不得转述或改写规则。

## 0. 项目定位
- Unity 6（6000.6.7f1）工程，产品形态：**一个纯 UI 展示场景（展板）+ 若干相互独立的插件包**。
- 插件包 = 最小开发 / 文档 / 交付 / 加锁单元，一律为内嵌包：`Packages/com.collin.<插件短名>/`。
- 禁止把插件代码放 `Assets/`。`Assets/` 只留：**展板（`Assets/Showcase/`，公共展示接入口）**、全局设置与模板默认内容。
- 每个插件的 showcase = **包内试用演示**：场景与示例资源放 `Packages/com.collin.<短名>/Sample/`，
  展板只消费各包 `Showcase.json` 的元数据与演示视频，**不引用插件代码与场景**。

## 0.1 插件开发原则（所有 com.collin.* 包强制遵循）
1. **所见即所得**：参数改动必须即时反映到场景预览，禁止"改配置→手动点生成→才看到效果"的黑盒流程；
   编辑态与 Play 态呈现一致。面板直接建在场景实例的 Inspector 上，不让用户跳去资产文件编辑。
2. **MVC 解耦**：共享配置（Model：轴、结构、分段——进 ScriptableObject）与实例状态
   （进场景组件序列化字段，如倍数、实例级覆盖）分离；View 只读 Model 渲染；
   Controller（Editor 面板/命令）只写 Model 并触发刷新。
   判据：**同一配置资产被 N 个实例复用时，实例间的差异数据禁止存在配置里**。
3. **编辑器/引擎双侧友好**：Runtime 程序集禁止引用 UnityEditor（校验器 `runtime-purity` 强制）；
   编辑增强（面板、辅助线、保存钩子）全部走 Editor 程序集 + 事件挂接；
   同一组件在编辑态与运行态用同一条构建路径，引擎侧无需重复配置。

## 1. 会话进入协议（必须按序，不得跳过）
1. 读本文件；
2. 跑校验：`python Tools/verify_project.py`，红灯先处理或如实上报，禁止带红交付；
3. `unity status` 确认编辑器 ready（本项目端口 7800）；
4. 声明目标插件包，按 §3 取包写锁；
5. 开工；
6. **交锁门禁（三项全绿才许交锁提交）**：
   a. 编译干净：`unity command recompile_status` → `failed=false`；
   b. 控制台归零：`unity command "console clear"` → 重跑本会话全部验证动作 →
      `unity command "console status"` 的 `groundTruth` 必须 `consoleErrors == 0` 且 `consoleWarnings == 0`
      （两段式命令名须整体加引号，实测不加入报 Command Not Found）；
   c. 校验器绿灯：`python Tools/verify_project.py`。
   编译错误会让编辑器进入 Safe Mode，切断**所有** AI 的实时通道——爆炸半径是全项目，不只你自己的包。
7. 门禁语义：**以归零后新产生的 error/warning 为准**，禁止用交锁前 `console clear` 抹掉历史错误来绕门禁；
   归零前已存在的错误若与本会话改动无关，记入 ACCEPTANCE 后如实上报用户。
8. 归因例外：error/warning 的堆栈**不含任何项目程序集**（`Packages/com.collin.*`、`Assets/`）时，
   可申报为环境噪声，但须同时满足：①在静默窗口（clear 后 60s 不执行任何操作）内自行复现；
   ②连堆栈记入该包 ACCEPTANCE；③经用户批准写入 `Tools/console_allowlist.json` 后方可豁免。
   未获批准的噪声同样是红灯。

## 2. 并行与互斥
- 同一时间一个包只允许一个 AI 迭代。
- 不同 AI 并行改不同包：允许，前提是不触碰 §6 共享文件、且各持自己包的锁。
- 若目标包的**依赖或被依赖项**正被其他 AI 锁定：必须停下，向用户显式报告冲突双方与影响，经用户确认后才能继续。默认不许绕。

## 3. 锁协议
- 锁文件 `.locks/<包名>.lock`（JSON：`holder` 客户端标识 / `session` / `acquired_at` / `heartbeat_at` / `ttl_s`=300）。
- 取锁 = 原子创建，已存在即失败；工作期每 60s 更新 heartbeat；交锁删除自己的锁文件。
- 心跳超时锁：不得自动删除，报告用户处理。`.locks/` 不入库。

## 4. 包结构与文档四件套
包内必备：`package.json`（依赖唯一真源）、`Runtime/`（+asmdef）、`Editor/`（+asmdef）、`Showcase.json`（§6）、
`Docs/DESIGN.md`、`Docs/ACCEPTANCE.md`、`Docs/CHANGELOG.md`。模板见 `Docs/templates/`。
- **DESIGN 先于代码**：新包第一份产出是设计文档 + 验收草案。
- ACCEPTANCE 每条必须带证据：谁 / 何时 / 用什么指令验证 / 结果。只记可观测事实，不记意图。
- 过程记录 = 根目录 `Docs/HANDOFF/<包名>.md`，**覆写式交接单**（非追加日志），每次交锁前更新五节：
 已完成 / 未完成 / 已知坏 / 下一步 / 勿碰项。
- CHANGELOG 用 semver + Keep-a-Changelog。

## 5. 依赖规则
- 依赖方向**严格单向：feature → core**。feature 包之间禁止引用（asmdef 层面切断，`validateReferences: true` 不得关）。
- 跨包通信只走 `com.collin.core` 的接口 / 事件通道 / ScriptableObject 信号。
- `com.collin.core` 建立后，任何新 feature 包必须声明依赖它。
- 编辑器指令集不做静态假设：先 `unity list` 运行时发现。

## 6. 展板（纯 UI，与代码解耦）
- 展板场景运行时枚举 `Packages/**/Showcase.json` 生成条目，**禁止为单个插件修改展板场景文件**。
- `Showcase.json` 字段：`name` / `category` / `summary` / `video`（包内相对路径）/ `order`（float）。
- 显示顺序：依赖拓扑序（被依赖在前）→ `order` 定并行分支先后 → 名称字典序兜底。
- 演示视频放包内 `ShowcaseMedia/`（mp4 已配 git-lfs）。
- **演示视频内容硬性要求**：`video` 指向的视频必须同时覆盖两段，顺序固定：
  ①**怎么用**——真实编辑器操作过程：把组件加到场景物体、在 Inspector 里划边界/切分、
    拖动复制倍数滑条；画面须能看到被操作的控件与鼠标动作，不能只给结果。
  ②**用了之后的效果**——场景视图对参数改动的即时响应，含改前/改后对照。
  受环境限制无法实录时，须在包 `Docs/ACCEPTANCE.md` 标注"示意（非实录）"并说明原因，
  否则视为该包演示不合格。
- 部分提取：工具按「目标包 + 依赖闭包」复制包目录、生成独立 manifest 并导出 unitypackage；导出物不入库。

## 7. 共享文件与合并热点
以下改动必须在会话内向用户申报：`Packages/manifest.json`、`ProjectSettings/**`。
`Packages/packages-lock.json` 机器生成：禁止手工解冲突，冲突时任取一侧后由 Unity 重新生成。

## 8. 版本钉死与升级评审
以下版本**钉死，禁止任何 AI 自行升级或重指**：
| 依赖 | 钉死版本 |
|---|---|
| Unity 编辑器 | 6000.6.7f1 |
| com.unity.pipeline | 0.9.1-exp.1 |
| com.unity.ai.assistant | 2.21.0-pre.2 |
| com.unity.ai.inference | 2.6.1 |
升级流程：先写 `Docs/DECISIONS/ADR-xxxx.md`（动因 / 影响 / 回滚）→ 用户批准 → 改 manifest → 重跑校验。

## 9. 卫生与提交
- 不入库（已在 .gitignore）：`.tmp-inspect/`、`.locks/`、`Builds/`、`*.unitypackage`。
- 临时调查产物只能落在上述目录，否则会话结束前清理。
- 一次交锁 = 一个可编译增量 = 一次提交；message 格式 `<包名或scope>: <中文摘要>`；
  按文件 `git add`，禁止 `git add .`。**不 push，除非用户明确要求。**

## 10. 本文件的变更
规则本身 = 高风险变更：先给用户看 diff 草案，批准后才许写入，并同步 `Tools/verify_project.py` 的对应检查项。
