# Modules/11 — `BuildingRecord` 结构定稿（接口冻结）

> 目的：M0 后、M7 前冻结建筑落位数据的字段与序列化布局。
> 它同时是三方接口：**07 的输出、08 的认回依据、10 的导出源**。定稿后任何字段增删或语义变更都须先写变更记录并经用户批准。
> 状态：**字段方案已裁定（`heightValue` 保留双义，用户 2026-11-03）；「冻结」仍待用户确认**。
> 作者：qianwen-work-assistant / 会话 `2026-11-03 record-freeze`，2026-11-03。
> 冻结效力（确认后生效）：§1「冻结」列内的任何变更（字段名/偏移/标度/位定义/键名语义/指纹输入集合/归属表）
> **必须先在本文档追加登记行（含动因与影响面）并经用户批准**，才可动代码；三方消费者（07/08/10）须同步收敛。
> 未冻结项（§1 右列）按各模块自身演进，无需回本文档审批。

---

## 1. 冻结范围与不冻结内容

| 冻结 | 不冻结（属各模块自身演进） |
|---|---|
| 字段名、字节偏移、量化标度、`flags` 位定义 | 落位算法（07 §1~§3.1）、打分权重（06 §5） |
| blob 分块粒度（按 region）与字节序（小端） | region 尺寸本身（10 §2.3，1024m 可调） |
| `intentVersion` 指纹的**输入集合**与算法 | 指纹具体实现的哈希函数选择（可用 xxHash/自建 splitmix 累加，须确定性） |
| `slotKey` 命名空间划分与冲突处理协议 | `parcelId`/`slotIndex` 的几何哈希细节（04 §3.5） |
| 「不存哪些字段、由谁重算」的归属表（§5） | 三形态导出的字段映射细节（10 §5，只做 join 不做存储） |

---

## 2. 定稿结构（32 字节/栋，对齐无填充）

```
偏移  类型     字段              语义与标度
────────────────────────────────────────────────────────────────────────
 0    int      slotKey           唯一稳定键；bit31 = 命名空间位（0=派生，1=手刷）；永不复用
 4    int      rowId             → BuildingProfile 行（永不复用）；查不到 → 本条转 orphan
 8    uint     xQ                定点：round((worldX - originX)/0.01)；世界原点记在 blob 头
12    uint     zQ                同上（Z 轴）
16    ushort   yawQ              朝向：1/4 度，0..1439（=round(yawDeg*4) mod 1440）
18    short    heightValue       语义由 flags.approximated 决定，见 §4.3（唯一二义点，已消歧）
20    short    footWQ            footprint 宽覆盖，0.01m 定点；**0 = 使用表值**（见 §7 H-26）
22    short    footDQ            footprint 深覆盖，同上；0 = 表值
24    byte     landF             落位时该点金融强度（0~100，D-02 整数标度）
25    byte     landI             工业
26    byte     landR             住宅
27    byte     landA             农业
28    byte     flags             位定义见 §4.1
29    byte     reserved0         保留（必须写 0）
30    ushort   reserved1         保留（必须写 0）
────────────────────────────────────────────────────────────────────────
      合计 32 字节 → 10 万栋 = 3.2 MB（含头与分块索引 <3.6 MB）
```

C# 侧用 `struct` + `NativeArray<BuildingRecord>` 或裸 `byte[]` + `BinaryWriter` 承载，
**禁止** `List<Serializable struct>` 进 `ScriptableObject`（见 §7 H-28）。

与 10 §2.2 草案的差异（诚实记账）：

| 变化 | 理由 |
|---|---|
| **删** `thresholdUsed` | 冗余：门槛 ≡ `land{D}`（05 §2.3 `Threshold(cell,t)` 定义为该维通道值，07 §1 取 `Th=F_D`）。存它等于存两遍同一个数，且可能不同步 |
| **删** `landUseDominant` | 冗余：`dominant = argmax(landF/I/R/A)`，四字节已全量携带 |
| **删** `id` | 与 `slotKey` 同义，双键必然漂移；统一成单键（认回、负约束引用、导出 id 全用它） |
| **增** `landF/I/R/A` 4 字节 | 「为什么选它」面板（07 §4.3）与 A-16 要求四维可追溯；原 `float4` 口径降为 byte 与整数门槛标度一致（省 8B 且无损） |
| **增** `footWQ/DQ` 4 字节 | H-26：适配时缩小的 footprint 不持久化 → 跨会话重算后叠楼，静默违反 A-8 |
| `packedPos/packedPosZ` → `xQ/zQ` | 语义不变，命名明确为"定点量化"，避免被误读为位打包 |

---

## 3. 序列化布局

```
记录资产（按 region 分块，支持"只写脏 region"）
  BuildingRecordAsset (SO，轻量元数据)
    schemaVersion   : int          = 1（本定稿）
    layoutSize      : int          = 32          ← 读侧据此拒绝未知布局
    originX, originZ : float64                   ← xQ/zQ 解量化基准
    cellSize / regionSize : float32
    intentVersion   : long                       ← §6 指纹
    blockIndex      : NativeArray<int>           ← regionId → blob 内起始记录号（-1 = 无记录）
    payload         : byte[]                     ← 记录数组，小端，紧凑无对齐填充
```

**硬要求**
1. **字节序固定小端**，字段顺序固定为 §2 偏移序；禁止用 `FormatterServices`/二进制序列化器自动布局。
2. **时间戳、GUID 会话态、随机数不进 payload**（否则 A-30「重开→保存逐字节一致」永不成立）。
   展示用的"最后生成时间"放 SO 的独立字段，**比对与 diff 只比 payload**。
3. 记录顺序 = **`slotKey` 升序**（不是生成顺序、不是字典枚举序）→ 使 payload 成为纯函数，同一集合任何路径写入结果相同。
4. 空 region 不占 payload 空间，仅在 `blockIndex` 记 -1。
5. `reserved*` 写 0；读侧遇到非 0 保留位 → warning（说明文件由更新版本写出，提示升级而非崩溃）。

### 3.1 尺寸与预算口径（本会话实算，单位 **MiB**，1 MiB = 1024² B）

| 项 | 8km / 4m 档实测 | 说明 |
|---|---|---|
| 记录 payload | 10 万栋 × 32 B = **3.05 MiB** | 全图常驻 |
| 高度场 `int16` | 8.0 MiB（4.0M 格） | 10 §2.3 |
| 掩码 `ushort` | 8.0 MiB | 同上 |
| 用途场 4×`byte` | **15.3 MiB**（原文档写 15MB，按 MiB 口径实为 15.3） | 10 §2.3 |
| 全图场数据合计（量化口径） | **31.3 MiB** | 若全图常驻 |
| 全区常驻 + 记录 = 保守上限 | **≈34.4 MiB + 索引** | A-34 的 ≤40 MiB 余量仅约 **14%** |
| 按 region 惰性（工作集 2 region） | 场 ≈1.0 MiB + 记录 3.05 MiB ≈ **4.1 MiB** | 10 §3.3 的常态路径 |

> **A-34 的 ≤40MB 必须读作 40 MiB 且含索引**；若实现期把用途场退回 `float`（61 MiB）或再加一份 `float` 用途缓存，
> 预算立刻穿帮。M1 实测时以此表为准，且所有预算数字**统一标注 MiB**（历史文档中 MB/MiB 混用，A-34 断言前先定口径）。


---

## 4. 位域与取值语义

### 4.1 `flags`（bit0~5 已定义，6~7 保留）
| 位 | 名 | 语义 | 谁写 | 谁读 |
|---|---|---|---|---|
| 0 | `adopted` | 由自动固化为人工层 | adopt 命令 / 锁区批量 | 认回（不可替换）、08 冲突矩阵 |
| 1 | `userModified` | 用户在场景改过 Transform/预制件 | 保存钩子 | 认回（不可替换）、回收前强制写回 |
| 2 | `orphan` | 宿主 slot 或 rowId 已失效 | 认回第④步 / H-15 校验 | 08 info 条目三选一、`OccupiedHard` |
| 3 | `approximated` | `heightValue` 是等比缩放（非 stretch 段复制） | 07 §3.2 降级分支 | H-23 迁移、导出报告 |
| 4 | `inLockRegion` | 落位时处于锁区内 | 锁区命令 | 重算避让、08 §4.2 |
| 5 | `manualSource` | 由手刷工具产生（`slotKey.bit31=1` 时的冗余标记，便于 O(1) 分桶） | 手刷命令 | 认回第①步、导出统计 |

**约束**：`orphan=1` 与 `adopted=1` 可同真（orphan 保留 adopt 待遇）；
`approximated=1` 时该行必须 `stretchable=true`（非拉伸行走 §4.3 第三种情形）。

### 4.2 `slotKey` 命名空间与冲突协议
```
派生记录：slotKey = (int)(hash31(parcelGeomHash, slotIndex) ) ，bit31 = 0      // 0..0x7FFFFFFF
手刷记录：slotKey = (int)(0x80000000u | 自增计数器) ，bit31 = 1                  // 计数器持久化在 SO，永不回绕复用
```
- **冲突是数学必然，不是边角情况**：n=10⁵ 落进 2³¹ 空间，期望冲突对数 `n²/2³² ≈ 2.3`。
  定稿要求 **冲突解决协议**：分配时若命中已存在 key → `salt += 1` 重算（最多 4 轮）→ 仍冲突则回退到"手刷命名空间借号"并记 warning。
- 加载时跑一次 O(n) 全量重复检测（10 万条 <20ms）：发现重复 = **error**，阻断保存（重复键会让认回把两栋楼认领成一条，后果不可预期）。
- `slotKey` **永不因删除而回收**；`blockIndex` 与 payload 顺序无关，删除只体现为记录消失。

### 4.3 `heightValue` 的三种解释（唯一二义点，此处消歧）
| 表行 `stretchable` | `flags.approximated` | `heightValue` 含义 | 读法 |
|---|---|---|---|
| true | 0 | **段复制份数 repeat**（整数，≥1；0 视为非法→warning 并按 1 处理） | `SetRepeat(go, seg, heightValue)` |
| true | 1 | **缩放百分数**（100 = ×1.0；有效区 50~800） | `localScale.y = heightValue/100` |
| false | 0 | 恒为 **1**（不拉伸哨兵值） | 不改动几何，缩放恒等（A-9 断言对象） |
| false | 1 | **非法组合** | 校验 error：非拉伸行不得有 approximated 标记（H-23 迁移的前置检查） |

> **字段定位**：`heightValue` 是每栋建筑唯一的"**高度意图**"整数——它**既不是米数也不是倍数**，
> 含义由「表行 `stretchable` × 记录 `flags.approximated`」二维决定（本表即该二维的穷举）。
> 存意图而不存最终米数：后者是 `f(表行 heightMin/Max, land{D}, distanceDecay(城心锚点))` 的纯派生量，
> 改表或挪城心时楼高本就该跟着变（§5）；两种几何来源必须可区分，否则 M9 迁移时不知道该重算谁（H-23）。
>
> **裁定结果（用户 2026-11-03）：保留双义，不拆字段。**
> 代价：读取必须同时知道两个标志，漏一个就把份数当百分数解释（M9 迁移期最易出错，见 §13）。
> 收益：省 2 字节 ×10 万 = 200KB，且一个字段天然承载 M9 前后三种状态。
> 备选（拆 `repeat` + `scale` 两字段，+2B/栋、`approximated` 退化为纯标记）**已否决**。

---

## 5. 不存字段清单（以及为什么不存不会漂移）

| 不存 | 由谁重算 | 不存的安全性论证 |
|---|---|---|
| `height`（最终高度米数） | 07 §3.2：`f(rowId.heightMin/Max, land{D}, distanceDecay(锚点))` | 全部输入都在（rowId + land + 锚点）；表改了高度**本就该变**，与 `intentVersion` 失配→重算语义一致 |
| `dominant` / `thresholdUsed` | `argmax(land*)` / `land{dominant}` | 纯派生，存了必漂移（§2 差异表） |
| `groundY` / Y 坐标 | 01 `GetHeight(x,z)` | 地形是模型输出；存 Y 会让"改噪声"后建筑悬浮或埋地（H-4 类问题） |
| `parcelId` | 认回时由当前派生 parcel 集合 join | 04 明确 parcel 是派生层；持久化引用会随拓扑变化失真 |
| `footprint`（未覆盖时） | `rowId → BuildingProfile` 表值（06 §3.3 缓存） | 表在盘上可 join；仅在**覆盖**时存 `footWQ/DQ`（H-26） |
| 拉伸后 mesh / GameObject | 10 §3 视口投影 | H-17 根因；投影可随意销毁重建 |
| `stretchSegment` 段号 | 提供方（stretch）默认首选可重复段 | 06 D-06-4：段模型细节属于提供方，citymap 不得持久化它 |
| 建筑名/备注 | 不需要 | 策划信息属于表（06 `note` 列），不属于实例 |

---

## 6. `intentVersion` 指纹（认回与增量重算的开关）

**输入集合**（必须全部参与，缺一即出现"改了但没重算"的静默失效）：
```
intentVersion = FOLD(
    CityProject.seed,
    每个意图对象的 (stableId, manualVersion) 有序累加哈希 ,   ← 河笔画/pinned 边/节点/场域/锚点/表/预算/锁区/负约束
    BuildingProfile 表内容哈希（rows 的 rowId+四维+stretchable+footprint 覆盖，按 rowId 升序）,
    掩码相关 Model 的版本（TerrainProfile/RiverProfile/RoadProfile/RoadBudget/ParcelProfile）,
    世界尺寸与 cellSize/regionSize
)
```
- 必须支持 **O(1) 增量**：对象 `manualVersion++` 时同步更新累加器，禁止每次重算遍历全区（10 万级对象遍历会毁掉拖拽手感）。
- 顺序无关性：输入以 **stableId 有序**参与（00 §3.1 铁律——禁止字典枚举序）。
- 判定规则（10 §4 第①步）：
  - `intentVersion` 相等 **且** payload 内部校验（§3 硬要求 + §4.2 重复键）通过 → 直接复用记录，零重算；
  - 失配 → 重算得新集合 `N`，与旧 payload `O` 按 `slotKey` diff：
    `N∩O` 保留 O 的 `flags` 与 `foot*` 覆盖（人工位优先）；`N-O` 新建（`flags=0`）；`O-N` 转 `orphan`（不删）。
- **A-30 的可判定形式**：相同输入两次生成 ⇒ payload 逐字节相等。因记录顺序固定为 `slotKey` 升序（§3 第 3 条）而成立。

---

## 7. 本轮定稿新发现的三条隐患（H-26~H-28）

| 编号 | 隐患 | 后果 | 定稿处置 |
|---|---|---|---|
| **H-26** | 07 §3.1 重试序列第 3 步会「缩小 footprint 适配」，但该覆盖值无处存放（表是共享 Model，不能写实例差异，违反 §0.1 原则2） | 跨会话后 footprint 回到表值 → 已让位的建筑重新重叠，**A-8 静默失效**，且用户看不到报错 | `footWQ/DQ` 入记录（0=用表值）；认回把 footprint 从记录取，未覆盖才 join 表；`OccupiedHard` 烘焙同样走记录，保证未加载区占位正确 |
| **H-27** | 草案把 `slotKey` 当纯 hash 用，未算冲突：n=10⁵ / 2³¹ 空间期望约 2.3 次冲突 | 认回时两条记录认领同一个 slot → **两栋楼叠在同一位置**（A-8），或一条被另一条覆盖丢失 | §4.2 冲突解决协议（salt 重算 ≤4 轮）+ 加载期 O(n) 重复键检测 = error 阻断保存 |
| **H-28** | 草案写「`BuildingRecordAsset` 里放 `List<BuildingRecord>`」，而 Unity 会把 `[Serializable]` 结构列表**序列化成 YAML 明文** | 10 万条 ≈ 数百万行 YAML → **H-17 换皮回归**（打开/保存分钟级），本次架构调整等于白做 | §3 布局：SO 只存元数据 + `byte[] payload`（或 `NativeArray`）；按 region 分块 + `blockIndex`；禁止自动序列化器 |

> 三条都是"设计各自自洽、落到实现接缝才炸"的同一类。H-28 尤其危险：它看起来已经采用了数据层，实际又把数据塞回 YAML。

---

## 8. 与下游的映射（只读 join，不新增存储）

| 消费方 | 取用方式 |
|---|---|
| 认回（10 §4） | `slotKey → 投影实例`；`flags` 决定"不可替换"集合；`xQ/zQ/yawQ/foot*` 决定占位与碰撞复检 |
| `OccupiedHard` 烘焙（00 §2.4） | 由 `xQ/zQ/yawQ` + footprint（记录覆盖值或表值）栅格化；**未加载区同样可烘焙**（这是 D-13 与全区禁入能同时成立的前提） |
| 「为什么选它」面板（07 §4.3） | `land{F,I,R,A}` + `dominant`（argmax）+ `Th`（=`land{dominant}`）+ `rowId` → 表行；随机数不存，由 `objectSeed(6, slotKey)` 现算（同输入同输出） |
| 形态 A scene 切块 | `slotKey`→对象名与归属判定；`rowId`→prefab；`xQ/zQ/yawQ`→Transform；`heightValue` 按 §4.3 表解释 |
| 形态 B `citydata.json` | `buildings[]` 的 `x,z,yawDeg,rowId,repeat/approximated` 直取；`height`、`footprintW/D`、`dominant`、`threshold` 由导出层 join 表后**补全写出**（导出格式不含 Unity 语义，A-39 不受影响） |
| 形态 C 2D tile | `buildings` 索引图 = footprint 栅格化，像素值 → `slotKey`（索引图 32 位可容纳） |
| H-23 迁移动作 | 选择条件即 `flags.approximated=1`；完成后 `heightValue` 改写为 `repeat`、`approximated` 清 0，整条走 journal |

---

## 9. 旧键名对照（历史文档不改写，按此表读）

`Docs/Modules/09-Workthrough.md` 与 `08/07` 的**推演过程记录**保留了当时的字段命名（H-4/H-10/H-15 的行文是历史事实，不回改）。
阅读时按下表映射到本定稿：

| 旧名（历史行文） | 定稿名 | 备注 |
|---|---|---|
| `slotId` | `slotKey` | 同一含义：唯一稳定键；定稿明确其 bit31 命名空间 |
| `profileRowId` | `rowId` | 永不复用规则不变 |
| `tag.adopted` / `tag.userModified` / `tag.orphan` / `tag.approximated` | `record.flags.*` | **载体变更**：从投影组件移到记录（组件已只剩 `slotKey`） |
| `tag.stretchedHeight` | 由 `heightValue` + 表值现算 | 定稿不存最终高度（11 §5） |
| `landUseAtPlace`（float4） | `landF/I/R/A`（4×byte） | 降为整数标度，与门槛口径一致 |
| `thresholdUsed` / `landUseDominant` | 无字段，派生 | `dominant=argmax(land*)`、`Th=land{dominant}`（11 §2 差异表） |
| `repeatOrScale` / `packedPos` / `yawCcq` | `heightValue` / `xQ` / `yawQ` | 语义不变，命名消歧 |
| `recordId` | `slotKey` | 反向指针与记录主键统一为同一值，不另设 id |

## 10. 校验规则（保存前 + 生成前各一次，与 06 §3.2 同一入口）

| 级别 | 条件 | 处置 |
|---|---|---|
| error | 同 `slotKey` 重复 | 阻断保存（H-27） |
| error | `layoutSize != 32` 或 `schemaVersion` 高于本包可读版本 | 拒载并提示升级（§11 迁移） |
| error | `xQ/zQ` 解量化后越出 `worldExtent`（世界缩小后的残留） | 阻断保存，列清单 |
| error | `stretchable=false` 且 `approximated=1` | 阻断（§4.3 非法组合） |
| error | `heightValue` 按 §4.3 解释后越界（repeat<0 / scale<50 或 >800 / foot 覆盖 ≤0 且 ≠哨兵 0） | 阻断 |
| warning | `rowId` 在表中不存在 | 转 `orphan`（H-15），info 条目 |
| warning | `inLockRegion=1` 但当前无锁区覆盖该点 | 以锁区为真源重算 flags |
| warning | 记录数 > `CityProject.maxRecords`（默认 30 万） | 提示尺度与 `chunkSize`/覆盖率需下调 |

---

## 11. 版本与迁移

- 本定稿 = `schemaVersion 1`，`layoutSize 32`。
- **加字段**：优先占用 `reserved0/reserved1`（不 bump 大版本，bump minor，读侧对 0 值做"等同于表值"的向后解释）；
  reserved 用尽才 bump 大版本 → 必须同时 bump `layoutSize` 并提供迁移器。
- **改语义**（如 `heightValue` 标度）：一律 bump 大版本 + 迁移器，禁止"看起来兼容"的静默改标度。
- 迁移器必须**幂等**（00 A-0-6 同规），且迁移后 `intentVersion` 需重算（不得沿用旧指纹）。

---

## 12. 验收（编号续 A-45）

| # | 事实 | 证据方式 |
|---|---|---|
| A-45 | `sizeof(BuildingRecord)==32`，字段偏移与 §2 表逐项一致（`Marshal.OffsetOf` 断言） | EditMode 测试逐字段断言 |
| A-46 | 同输入两次生成 → payload **逐字节相等**（A-30 的可判定形式）；记录顺序恒为 `slotKey` 升序 | 双跑哈希比对 |
| A-47 | 构造 `slotKey` 冲突用例（预置碰撞 hash）→ 分配器按 salt 协议消解；加载期重复键检测报 error 且阻断保存 | 单测 + 交互走查（H-27） |
| A-48 | footprint 覆盖跨会话存活：手动缩小某栋 → 重启 → 重算后该栋 footprint 仍为覆盖值、无叠楼 | A-8 复检（H-26） |
| A-49 | 记录以 `byte[] payload` 承载：10 万条时资产 YAML 行数 < 500 行（不随记录数线性增长），打开/保存不进入分钟级 | 文本行数统计 + 计时（H-28） |
| A-50 | 「为什么选它」面板显示的 `dominant/Th` 与 `argmax(land*)/land{dominant}` 一致；不存在独立存储的 `threshold` 字段 | 面板与 payload 抽样对照（§2 差异表） |
| A-51 | §4.3 四种情形逐一验证：`repeat`/`scale`/非拉伸哨兵/非法组合各一条，非法组合被校验阻断 | 构造四条记录跑校验 |

## 13. 风险
- **双义 `heightValue` 是可读性负债（已裁定接受，用户 2026-11-03，见 §4.3）**：读侧必须同时知道 `stretchable` 与 `approximated`，漏一个就错解（M9 迁移期间最易出错）。
  强制缓解：① 所有读取一律经 `RecordGeometry.Resolve(record, row)` 单一入口，**禁止任何手写字段解释**；
  ② §4.3 二维表原样贴在该函数注释里；③ 代码审查发现直接读 `heightValue` 做算术的写法即打回；
  ④ A-51 四种情形（含非法组合被阻断）为回归必测项。
- `reserved` 位被"临时加个字段"滥用会让布局语义化失控 → 规则：占用 reserved 必须在此文档追加登记行。
- 10 万条全量重复键检测放加载期是 O(n) 但需额外 320KB 集合内存；若实测拖慢启动，改增量维护（记录分配时即入表）。
- 指纹输入集合若漏一项（例如漏了 `ParcelProfile`），会出现"改了地块参数但建筑没重排"的静默失效——
  §6 的输入清单在实现时须逐条对应单测（漏项 = 该单测红）。
