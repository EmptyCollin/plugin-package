# 交接单 — com.collin.citymap

> 覆写式交接单（AGENTS §4）。最近更新：2026-11-03（会话 `2026-11-03 m1-foundation`）
> 代码阶段：本会话按 §3 原子取锁开工，按 §1.6 三项门禁交锁。**未 push。**

## 1. 已完成（M1：Modules/00 基础设施 + 08 反悔骨架 + 11 结构落地）
- **`Modules/11` 已由用户确认冻结**；`BuildingRecord` 按冻结表**落成代码**（32B/栋，
  `ValidateLayout()` 对全部 15 个字段偏移自证 = A-45 的静态部分）。
- Runtime/Core 新增：`CityGrid`（四层分辨率解耦、2km/8km 双档、floor 换算、圆外接盒、`RecordQuant`）、
  `DeterministicRandom`（splitmix64 种子树 + `RngStream` + `IntentHash`）、`CellFlag`+`MaskWriter`（13 位掩码、禁建合集）、
  `PipelineTypes`（`DirtyBits` / `IntentChangePlanner` 不动点闭包 / **`StageClaims` 阶段认领单一真源**）、
  `Pipeline`（Context/Runner/GenerationReport）、`Falloff`（全工程唯一衰减口径）、
  `CityProject`（含 `CityCenterAnchor`、世界参数与城心的显式提交入口、**`IntentChangeLedger` 通道记账**）、
  `AssembleStage` + `ConstraintAsset`（S0：外环带 + 负约束烘焙）。
- Editor 新增：`UndoHistoryTracker`（首帧基线 / 双检 / 250ms 合并窗口）、`CityWindow`（阶段树=信息非门禁、
  自动跟随、工具模式互斥）。
- `Tests/Editor`：测试程序集 + **56 条 EditMode 用例，`test run --mode editor` Total 56 / Failed 0**。
- **实现轮抓到并修复 2 处空洞**（详见 09 第五轮）：
  - **H-29**：文档"→S3 复检"被直译成"置 `Road` 位"⇒ 下游把 Parcel/Field/Flatten 拖进来 ⇒ **挪一栋固化楼会重算地块**
    （违反 08 表末行自己写的"不得牵连"）。修法：`OccupiedReloc` 设**终端位**，由 `StageClaims` 让 S0/S3/S6 直接认领。
  - **H-30**：意图对象用"单一 Kind + 单一版本"记账 ⇒ 「设为城心」被读成 `WorldParam` ⇒ Expand 成 `All` ⇒
    **H-7 的修复被静默抵消**，面板脏位也错，全程不报错。修法：`IntentChangeLedger` **按通道（对象 × IntentKind）记账**、
    `IntentKind` 升 `[Flags]`、`DirtyOf` 逐位求并集；`Sequence` 必须 `[SerializeField]`（否则 undo 看不见 = H-9 漏检形态）。
- 纪律沉淀：**"→Sx 复检" ≠ "置 Sx 的触发位"**；**记账粒度必须到"可变面"而非"对象"**。

## 2. 实测得到的环境事实（新会话直接照做）
- **`test run` 的 `--mode` 只接受 `editor|playmode|all`**（`test list` 可接受 `edit`）；
  传错值时 Summary 全 0，极易误判成"工程里没有测试"。异步执行，须读重定向的完整 JSON 或轮询 `test status`。
- **测试故意触发的 error 会真实计入 `consoleErrors` ⇒ 破坏 §1.6b 归零门禁**。
  `LogAssert.Expect` 只让测试不失败、不回退计数；堆栈含本包程序集 ⇒ 不可用 §1.8 豁免。
  正解：断言期间 `Debug.unityLogger.logEnabled=false` + `finally` 复位（已在两处用例这样处理）。
- "只赋值不读取"的局部变量会产生 warning ⇒ 同样破坏归零（本轮清掉 `structural` 与 `ComputeSetHash(out int)`）。
- 端口读数会漂移（同一 PID 先后读到 7801/7800）：**发指令前现查 `unity status`，失败先重试再判故障**。
- 新建内嵌包仍需 `package resolve`（M0 记录，仍有效）。

## 3. 未完成
- M2~M11 全部功能代码。M1 只交付框架与内核，**不含任何生成能力**：
  S1a~S6 六阶段目前是空槽（`StageClaims` 已定义认领关系，缺 `IPipelineStage` 实现）。
- `CityWindow` 只有骨架：`新建城…` 按钮是占位 Label（08 §3.5 四重确认流未实现）；阶段树无真实耗时来源（无阶段可跑）。
- `OccupiedHard` 烘焙只留了接口（输入是 `BuildingRecord` 集合，M8 才有数据）。
- 无 PlayMode 测试；`IntentChangeLedger` 的 undo 感知目前靠**人为回滚序列号**模拟，真实 `Ctrl+Z` 链路待 M2 接上场景对象后补验（A-17 完整形态）。
- R5 可选推演（AGENTS §6 插件包 unitypackage 提取 × 人工层）。
- 文档待办（未变）：`minParcelArea` 03/04 双处配置未归一（M5 前）；01~05 仍用 v6 前"实例/GameObject"措辞（M11 前统一）。

## 4. 下一步（M2 建议顺序）
1. **S1a 基底噪声 + 显示 mesh + S1b 平整约束**（01 模块），把第一个真实阶段接进 `PipelineRunner`
   —— 顺带验证 `StageClaims`/`Expand` 在真阶段下是否仍然正确（M1 的 FakeStage 只测了调度语义）。
2. `TerrainProfile` SO + 编辑器参数实时链路（Preview→Commit 两档，A-1-3/A-0-3）。
3. 噪声实现必须是纯函数（A-0-1 已锁死种子侧，噪声侧尚未有测试）→ 补 `fbm` 的确定性用例。
4. 代码注释引用编号（`// 09 H-29`、`// 11 §4.3`），ACCEPTANCE 续接编号 21+。

## 5. 勿碰项
- 不改 AGENTS.md / §8 钉死版本 / `Packages/manifest.json` / `ProjectSettings/**`。
- 不动 `com.collin.core`、`com.collin.stretch`；citymap 侧禁止注册 `IStretchService`（H-14b）。
- **不改 `Modules/11` 的冻结表**：字段增删/偏移/标度/位定义变更须先在该文档追加登记行并经用户批准
  （三方消费者：07 输出、08/10 认回、10 导出）。
- 脏位新增须同步三处：`DirtyBits` 枚举 + `IntentChangePlanner` 传播 + `StageClaims` 认领，且 00 §2.4/§4.2 文档同步。
- 不引第三方 UPM 包进 Runtime/生成内核（Recorder 仅 M10 Sample）。展板场景 `Assets/Showcase/` 不因本包改动。
- 多会话共用同一编辑器实例：**不擅自 `console clear`**（除取归零证据的必要时刻），不动他人锁。
- 已交付文档原地更新；仅用户明确要求新版本时另存。
