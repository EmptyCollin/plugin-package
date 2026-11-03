# 交接单 — com.collin.citymap

> 覆写式交接单（AGENTS §4）。最近更新：2026-11-03（会话 `2026-11-03 record-freeze`）

## 1. 已完成
- **M0 已落地并提交**（上一会话）：`ebb6f4b com.collin.citymap: M0 包骨架`，0.0.1，包被 Unity 识别（插件包 3 个）。
- **本会话 = `BuildingRecord` 接口冻结定稿**（交接单 §4 指定的"M0 后、M7 前"前置项已完成）：
  - 新增 **`Docs/Modules/11-BuildingRecord.md`**（13 节：冻结范围 / 32B 偏移表 / 序列化布局 / 位域与取值语义 /
    不存字段清单 / `intentVersion` 指纹输入 / 新发现隐患 / 下游映射 / 旧键名对照 / 校验规则 / 版本迁移 / 验收 / 风险）。
  - 结构定为 **32 B/栋**、小端、按 region 分块 + `blockIndex`、记录顺序固定 `slotKey` 升序、**禁用自动序列化器**。
  - 相对 v6 草案的字段变更：删冗余 `id`/`landUseDominant`/`thresholdUsed`（`dominant=argmax(land*)`、`Th=land{dominant}` 为纯派生），
    增 `landF/I/R/A`（4B 四维追溯，byte 与整数门槛同标度）与 `footWQ/DQ`（4B 占地覆盖，见 H-26）。
  - **`heightValue` 双义**（repeat ↔ 缩放百分数）用 `stretchable × approximated` 二维表消歧，读取强制经
    `RecordGeometry.Resolve` 单一入口（11 §4.3）；非拉伸行写哨兵 1。
- **定稿过程中新发现 3 处隐患（H-26~H-28），全部已在文档内解决**：
  - H-26：07 §3.1 重试序列会"缩小 footprint 适配"，但覆盖值无处存放（表是共享 Model）→
    跨会话回到表值 → **已让位的建筑重新重叠，A-8 静默失效**。→ `footWQ/DQ` 入记录，0=用表值。
  - H-27：草案把 `slotKey` 当纯 hash 用没算冲突。n=10⁵ 落进 2³¹ 空间，**期望冲突对数 2.33（本会话实算）**，
    是数学必然 → 两条记录认领同一 slot = 两栋楼同位置。→ salt 重算协议 + 加载期 O(n) 重复键检测 = error 阻断保存。
  - H-28：草案写 `List<BuildingRecord>` 进 SO，Unity 会把 `[Serializable]` 结构列表**序列化成 YAML 明文**，
    10 万条 ≈ 数百万行 → **H-17 换皮回归**（看着用了数据层，实际又把数据塞回 YAML）。→ SO 只存元数据 + `byte[] payload`。
- **活文档已按 11 §9 对照表收敛旧字段名**（`slotId→slotKey`、`profileRowId→rowId`、`tag.*→record.flags.*`、
  `repeatOrScale→heightValue`、`recordId→slotKey`）：改动落在 DESIGN / 06 / 07 / 08 / 10。
  `CityBuildingTag` 在 07 §5 收敛为**只剩 `slotKey` 一个字段**（其余全为投影期缓存），消除与记录的双真源。
  09 的历史推演行文**不回改**（是历史事实），靠 11 §9 对照表读。
- 顺带修掉 A-6-6 的历史错误：它引用了从未定义过的 `CityBuildingTag.stretchSource`，已改为按 `flags.approximated` 核对。
- 总纲升 **v8**：文档地图登记 11、验收索引追加 A-45~A-51、修订记录补 v8 段。

## 2. 实测与算术复核（本会话自行验证，非引用文档）
- 32B 偏移表逐项累加 **= 32，无重叠、无空洞**；最大对齐 4（int/uint），32 % 4 == 0 ⇒ 声明成立。
- `flags` 用单 byte 承载 6 个已定义位（余 2 位保留）⇒ 足够。
- 冲突期望 **2.33** 次（n=100000，空间 2³¹）。
- 内存口径（MiB）：8km/4m 全图场数据量化合计 **31.3**；+ 记录 3.05 ⇒ **≈34.4**，
  而 A-34 写的是"≤40MB" ⇒ **余量只有约 14%**。已在 11 §3.1 钉明"40 一律读作 40 MiB 且含索引"，
  并警告用途场若退回 `float`（61 MiB）预算立刻穿帮。历史文档 MB/MiB 混用，断言前必须先统一口径。

## 3. 未完成
- **等用户确认"冻结"**：11 号文档状态目前是「定稿（待确认冻结）」。确认后 §1 的冻结范围才具约束力
  （字段增删/语义变更须先登记 + 用户批准，reserved 位占用需在 11 追加登记行）。
- M1~M11 全部代码（M0 只给了结构与自检入口，无任何生成能力）。
- R5 可选推演（AGENTS §6 插件包 unitypackage 提取 × 人工层/记录是否丢固化成果）。
- 文档待办（未变）：`minParcelArea` 03/04 双处配置未归一（M5 前）；01~05 仍沿用 v6 前"实例/GameObject"措辞（M11 前统一）；
  00 §8 旧数值与 4m/byte 口径不一致（M1 实测覆写，届时一并按 11 §3.1 的 MiB 口径校准）。
- `CityMapMenus` 只有 `Package Info` 一项；City Window 与工具入口待 M1+ 挂菜单。无测试程序集（建议 M1 随 PipelineRunner 建 `Tests/`）。

## 4. 已知坏
- 本包无已知缺陷；门禁实测见下。
- **环境**：AGENTS §1.3 写端口 7800，实测 `unity status` 为 **7801**（实例已换）。实时通道一律以 `unity status` 为准。
  改 AGENTS 属 §10 高风险，未动，等用户决定。
- `git status` 里 `Assets/桥_杆_桥_stretchProfile*.asset` 等未跟踪文件属其他会话产物，非本包、未触碰。

## 5. 下一步
1. **用户确认冻结** → 11 状态改「已冻结」；若要改字段，改前先谈影响面（07 输出 / 08 认回 / 10 导出三方）。
2. M1：格网（4m/2m 双档）→ SplitMix64 种子树 → `ushort` 掩码 → `PipelineRunner` 脏传播 →
   `CityWindow` 壳 → 08 的 `manualVersion` + `UndoHistoryTracker`（首帧建基线）→ `CityCenterAnchor`。
   代码注释引用编号（`// 09 H-17`、`// 11 H-26`、`// 10 H-25`），ACCEPTANCE 逐条对 A-xx（A-45~A-51 属本定稿）。
3. 本会话改动**待用户决定是否提交**（AGENTS §9：一次交锁=一次提交；未获指示不 push）。

## 6. 勿碰项
- 不改 09 的历史推演行文（旧键名按 11 §9 读，不改写历史事实）。
- 新建内嵌包后必须 `package resolve` 触发发现；asmdef 目录不留空（M0 ACCEPTANCE §9 两条实测坑）。
- 不改 AGENTS.md / §8 钉死版本 / `Packages/manifest.json` / `ProjectSettings/**`。
- 不动 `com.collin.core`、`com.collin.stretch` 文件；M9 段 1 才向 core 加接口文件（需先申报取锁）。
- citymap 侧禁止注册 `IStretchService`（H-14b）；不引第三方 UPM 进生成内核（Recorder 仅 M10 Sample）。
- 多会话共用同一编辑器实例：**不擅自 `console clear`**，除非自己要取归零证据且已跑完本会话验证动作。
- 已交付文档原地更新；仅用户明确要求新版本时另存。
