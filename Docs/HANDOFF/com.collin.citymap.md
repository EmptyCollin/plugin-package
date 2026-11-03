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
- ~~`heightValue` 双义 vs 拆字段~~ → **用户已裁定（2026-11-03）：保留双义**（总纲 D-30，11 §4.3 已改写为裁定文本）。
- **仅剩「确认冻结」一步**：11 号文档头部现为「字段方案已裁定；待用户确认冻结」。
  用户说一声"冻结"即改状态为「已冻结」，§1 冻结范围随之生效（此后字段增删/语义变更须先登记 + 用户批准，
  reserved 位占用须在 11 追加登记行）。
- M1~M11 全部代码（M0 只给了结构与自检入口，无任何生成能力）。
- R5 可选推演（AGENTS §6 插件包 unitypackage 提取 × 人工层/记录是否丢固化成果）。
- 文档待办（未变）：`minParcelArea` 03/04 双处配置未归一（M5 前）；01~05 仍沿用 v6 前"实例/GameObject"措辞（M11 前统一）；
  00 §8 旧数值与 4m/byte 口径不一致（M1 实测覆写，届时一并按 11 §3.1 的 MiB 口径校准）。
- `CityMapMenus` 只有 `Package Info` 一项；City Window 与工具入口待 M1+ 挂菜单。无测试程序集（建议 M1 随 PipelineRunner 建 `Tests/`）。

## 4. 已知坏
- 本包无已知缺陷；门禁实测见下。
- **环境（本会话更正一条我自己的错误结论）**：上一轮我写下"AGENTS §1.3 的 7800 已过时、实测为 7801"。
  **该结论不成立**：本会话对**同一 PID 3512** 两次查询 `unity status`，先读到 **7801**、后读到 **7800**。
  即 `unity status` 的 Port 列显示会在同一实例上漂移，根因未查明（不确定是 CLI 解析、多监听口还是缓存所致）。
  ⇒ **不得据端口读数断定"实例已换"或"文档过时"**，也不因此去改 AGENTS §1.3（§10 高风险，用户未批准）。
  操作指引：发指令前现查 `unity status`；若某端口指令失败，换另一读数重试或直接重试同一命令，两次仍失败才当作真故障上报。
- **纪律（本轮两次踩到，写给所有会话）**：**单时刻观测不可写成永久结论**。
  同类错误：① "stretch 锁已释放"（随后被重新取得）；② "端口实为 7801"（随后读回 7800）。
  涉及锁、端口、编译状态、内存实测的表述，一律写成"某会话某时刻实测值 + 使用前必须复核"。
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
