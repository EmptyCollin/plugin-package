# HANDOFF — com.collin.core

> 覆写式交接单（AGENTS.md §4）。只写可观测事实。

最后交接：qianwen-work-assistant（千问工作助理）/ 2026-11-02 / commit 见本包首次入库提交

## 1. 已完成
- v0.1.0 全部 Runtime/Editor 代码：EventChannel（非泛型+抽象泛型基类+IntEventChannel）、Services、ShowcaseEntry/Sorter、ShowcaseScanner；
- 文档四件套齐（DESIGN 已评审状态、ACCEPTANCE 9 项带证据、CHANGELOG 0.1.0、本交接单）；Showcase.json 条目字段齐全；
- 编译干净（recompile_status failed=false）；校验器绿灯（交锁前运行）；
- eval 类指令实测口径：`unity command eval --code '<语句;>'`（须语句形式或末尾分号）；叶子名调用（如 `recompile_status`、`eval_file --file <相对路径>`）；**两段式命令名必须整体加引号**（`unity command "console status"`，拆开传会 Command Not Found 并在控制台留下 error）。
- §1.6b 新门禁（error 0 / warning 0）已在本包落地复验：clear→静默 65s→重跑 V1-V6→`errors=0 warnings=0`，校验器含 console 自动检查全绿。
- 注意：AI 探测失败的命令本身会往控制台写 error（本会话曾因此积下 3 条 CommandNotFound），**失败探测后必须重新走归零流程才能交锁**；命令名不确定时先 `unity command --query <词>` 查注册表再调用。

## 2. 未完成
- 展板场景本体（Assets/ 纯 UI 消费 ShowcaseScanner.CollectSorted()）——属项目级任务非本包范围，尚未立项；
- 部分提取的导出 CLI/Editor 工具（本包只提供了 DependencyClosure 计算）；
- EditMode 测试集（本包验收走的是 eval_file 内存验证，等效但未被 test framework 收录）。

## 3. 已知坏
- 无运行缺陷已知。约束如实记录：typed 通道要求消费方声明具体子类（泛型 SO 无法实例化，Unity 引擎限制）；Services 域重载后清空需消费方重注册（DESIGN §6）。

## 4. 下一步
- 立项展板场景包（建议 com.collin.showcase 作为 Assets 侧唯一入口的承载，或直接放 Assets/ 脚本——按 §0，Assets/ 只留展板，倾向后者）；
- 首个 feature 包按模板复制四件套、声明依赖 com.collin.core、asmdef validateReferences=true。

## 5. 勿碰项
- Tools/verify_project.py 的 manifest-sync 逻辑刚改过（file: 引用存在性检查），与规则联动，改前读 §10；
- AGENTS.md 全部条目（规则真源，改动走 §10）；
- com.collin.core 的钉死依赖版本（§8，升级走 ADR）。
