# Modules/04 — 地块（Parcel）与建筑位（Slot）

> 依赖：00、01、02、03。消费方：05（用途分配单位）、07（落位单位）。
> 对应里程碑：M5。状态：草案。
> 本模块是"路网 → 建筑"之间缺失的一环：没有地块与 slot，C4 的"程序化建筑生成"就只能在格网上一格一格铺，
> 那是最不像城市的样子（真实城市的建筑是**贴着街道、成组成团**的）。

## 1. 两个概念必须分层
| 概念 | 定义 | 用途 |
|---|---|---|
| **Parcel（地块）** | 被道路红线与水系边界围出的**最小土地区块** | S5 用途分配的单位、S1b 平整约束的来源 |
| **Slot（建筑位）** | 沿地块临街边切出的**摆放单元** | S6 建筑落位与准入打分的采样点 |

两级划分的价值：**同用途聚簇成街区（地块级），同街区内保留多样性（slot 级）**。
只做格网级会导致"每隔一格换一种用途"的斑马纹；只做到地块级会导致"一整个街区一栋楼"。

## 2. 数据模型

### 2.1 派生层（内存，D-13：不落盘）
```csharp
struct Parcel {
  int        id;                 // 稳定 id（由区域哈希 + 生成顺序的确定性 id 生成，见 §5）
  Polygon    boundary;           // 精确多边形（含沿河弧边）
  float2     centroid; float     area; float perimeter;
  float2     principalAxis;      // PCA 主轴（朝向与切分方向用）
  Plane      targetPlane;        // → 01 S1b 平整约束（D-01-4 中位数+条件拟合）
  float      slopeDeg;
  LandUseType dominant;          // ← S5 写入（本模块产出结构，S5 填内容）
  float4     landUse;            // ← S5 写入（四维强度，供 S6 直接用，避免 S6 再采样）
  float      waterFrontLen;      // 邻水岸线长度
  List<Frontage> frontages;      // 临街边
  SlotMode   slotMode;           // Perimeter / Grid / Campus / Agricultural（§3.3）
  List<Slot>   slots;            // 派生
  byte       flags;              // Invalid(过小/过窄)、Reserve(保留地)、ManualLocked
}
struct Frontage { int edgeId; Segment seg; GradeIndex grade; float length; }
struct Slot {
  int      id;                   // hash(parcelId, index) 派生的稳定 id
  float2   pos;                  // 建筑中心（世界 XZ）
  float    facingYaw;            // 朝向（临街边法线）
  float2   size;                 // 可用宽×深（含退让）
  float    groundY;              // 地坪（来自 01 S1b 结果，±0 对齐路面）
  float    frontLen; GradeIndex grade; boolean waterfront;
}
```

### 2.2 Model：`ParcelProfile`（SO，并入 CityProject 或独立）
| 字段 | 默认 | 语义 |
|---|---|---|
| `minParcelArea` | 900 m² | 小于则标 `Invalid` 不生成 slot（与 03 `minParcelArea` 同一真源，避免两处配置） |
| `minParcelWidth` | 18m | 窄条形地块无建筑价值，直接标 Invalid |
| `frontageInset` | 3m | 建筑退红线（沿街界面控制） |
| `depthCap` | 45m | 地块进深上限：超过则后排不产生 slot（内院/停车场，后续包消费） |
| `slotPitchMin/Max` | 8 / 40m | slot 间距区间（实际由建筑 footprint 驱动，§3.3） |
| `slotModePerimeterRatio` | 0.6 | 周长/√面积 > 此值→沿边布 slot，否则内部网格布 |
| `campusThreshold` | 见 §3.3 | 大地块且用途为公建/工业时的"大院"模式 |
| `agriculturalCoverage` | 0.15 | 农业地块 slot 覆盖率（Q-8） |

## 3. 技术路线

### 3.1 地块提取：从"掩码 + 红线"到多边形
```
1 可建掩码（00 §2.4）减去 Road/Water/WaterBuffer/OutOfBounds/OccupiedHard
2 连通分量标记 → 候选地块区域（栅格）
2b **过小块处理（Modules/09 H-8）**：面积 < `minParcelArea` 的块**优先并入共享边最长的邻块**
   （合并后记 `mergedFrom[]` 关系，可追溯）；连续两次仍过小（如被河与两条路三面夹死）才标 `Invalid`。
   理由：河/细分切出的小块若直接判废，会得到"既不能建也长不出路"的废地；真实城市里那是邻块的
   后巷/山墙地，并入即可。`Invalid` 计数进面板（>3% 提示"水切或 ρ 过密"）。
3 每个区域提取轮廓（marching squares）→ 简化（Douglas-Peucker, ε=cellSize·0.5）→ 多边形
4 与 pinned/adopted 边、河岸线做**贴合修正**（轮廓顶点吸附到最近道路/水岸线段 ≤1.5m）
5 计算 centroid/area/perimeter/PCA/坡度，并生成 `Parcel.id`
```
说明：走栅格→轮廓而非直接矢量切分，是因为 02/03 的几何求交已经很难（R-2）；
栅格路线让地块、掩码、平整约束三者共用同一份真源，代价是地块边界有锯齿——
用第 4 步的吸附修正把锯齿"拍平到街道线上"，反而得到更真实的城市地块（城市地块边本来就是贴路的直线）。

### 3.2 临街边识别
对多边形每条边，找最近的道路边：距离 ≤ `rightOfWay/2 + 2m` 且夹角合理 ⇒ 记为 `Frontage(grade)`；
邻 `riverPolygon` 岸线 ⇒ 记 `waterfront` 段。
一个地块可能有多条不同等级临街边：**主临街 = 等级最高 + 长度最长**（决定 `slotMode` 与地块朝向）。

### 3.3 Slot 生成（四种模式，按地块形态自动选）
| 模式 | 触发 | 布局 | 典型 |
|---|---|---|---|
| **Perimeter** | `perimeter/√area > slotModePerimeterRatio` | 沿每条临街边按 `slotPitch` 排布，朝外 | 市中心街区（沿街建筑+内院） |
| **Grid** | 大而方正 | 主轴/副轴网格，行列对齐，内部退让 | 住宅片区 |
| **Campus** | 大面积 + 用途=工业/公建 | 少量大 slot，间距大，允许同一建筑重复 | 工厂大院、学校 |
| **Agricultural** | `dominant=Agricultural` | 覆盖率 `agriculturalCoverage`，slot 少而散，靠近道路优先 | 农田中的谷仓/农舍 |

`slotPitch` 不是常数：**先按地块形态给出候选 pitch，再由 S6 选中的建筑 footprint 反推下一个 slot 位置**
（即"放完一栋，按它的宽度步进"）。首版用两遍法：
① 用表内 `footprintW` 中位数预生成候选 slot（粗）；② S6 落位后按实际宽度重排（细）。
两遍法避免"slot 尺寸与建筑尺寸鸡生蛋"的死锁，同时保证沿街界面连续。

### 3.4 地坪与 ±0（建筑不能悬空/半埋）
- `slot.groundY = 01 结果高度(该点) `；并检查地块整体坡度：
  若地块 `slopeDeg > maxParcelSlope`（01）→ 地块标 `TerraceNeeded`，slot 按等高线分层生成台阶（首版简化为整体取中位高度 + warning）。
- 与路面高差 > `stepLimit`（默认 0.6m）的 slot 记录 `needsStilt=true`，由 07 决定跳过或拉伸（避免"建筑底下悬空"）。

### 3.5 稳定性与增量（关键工程点）
地块与 slot 是**派生数据**，但 07 会按 slot id 做增量 diff，所以 id 必须稳定：
```
Parcel.id = hash(栅格区域规范化后的顶点哈希 + centroid 量化格)      // 与生成顺序无关
Slot.id   = hash(Parcel.id, 沿边的顺序序号)                          // 与全局遍历序无关
```
- 禁止用列表 index / 字典枚举序做 id（00 §3.1 铁律）。
- **重算触发**：改 ρ、改道路、改河 → 全量重算地块（几百 ms 可接受）；改场域 → 只更新 `dominant/landUse` 字段，不重算几何（这是拖场域能实时的根本原因）。

## 4. 交互与可视化
| 呈现/操作 | 说明 |
|---|---|
| 地块填充 + 编号 overlay | 主面板视图模式「地块」；`dominant` 配色（同 05 类型配色），边框=临街等级配色 |
| slot overlay | 细线小方框 + 朝向箭头；密度过高时（>8000 slot）只画当前视锥内且缩放到一定级别才显示 |
| 光标探针 | 悬停显示：面积、周长、临街等级、主朝向、`landUse` 四维、已生成 slot 数、Invalid 原因 |
| 保留地 | 工具：在多边形内圈一块 → `flags |= Reserve`（人工层，写 `ReserveAsset`，重生成不失效）|
| 不可建区 | 工具：圈选 → `ManualLocked`（同 01 雕刻优先，写人工层）|
| 无"必须点生成" | 所有参数（退让、进深、覆盖率）改动 → 实时 Preview（只重算 slot 数与轮廓），松手 Commit |

数据编辑入口：地块本身**不提供逐个手改形状**（它是派生物，手改会与路网真源冲突）；
要改变地块 → 通过改道路（03）或用保留地/不可建区两个**人工层工具**表达意图。这是有意的约束，写进面板帮助。

## 5. 与其他模块的契约
| 方向 | 内容 |
|---|---|
| ←01 | 用 `GetBaseHeight/GetSlopeDeg` 计算平面与坡度；产出 `targetPlane` 回喂 S1b |
| ←02 | `waterBuffer` 与 `riverPolygon` 边为不可穿越墙 + waterfront 标记 |
| ←03 | 红线并集为地块边界；临街边来自 resolved 道路边（含等级） |
| →05 | 提供 `Parcel.id/centroid/area/frontages` 作为用途分配单位（S5 按 parcel 采样场域并聚合） |
| →07 | 提供 slot 列表（pos/facing/size/groundY/grade/waterfront）；07 按 slot 做准入与加权随机 |
| →01 | S1b 的约束集合 = 有效地块 + 道路带（次序上 04 先于 S1b） |

**循环依赖提示**：S4 需要地形算坡度，S1b 需要地块 → 因此总纲 §3.2 把 S1 拆成 S1a（基底，04 之前）与 S1b（约束，04 之后）。
若把两者合成一步会形成图环，PipelineRunner 无法拓扑排序（D-12 落地）。

## 6. 模块内决策
| 编号 | 决策 | 备选 | 理由 |
|---|---|---|---|
| D-04-1 | Parcel/Slot 两级分层 | 单一格网 or 仅地块 | 聚簇与多样性并存；slot 是落位的最小采样单位 |
| D-04-2 | 地块用栅格连通分量 + 轮廓提取 + 吸附修正 | 精确矢量切分 | 与 02/03 风险同源，复用一份真源；吸附到道路线反而更真实 |
| D-04-3 | slot pitch 由建筑宽度两遍法决定 | 固定间距 | 沿街界面连续，避免"等宽格子铺"的假感 |
| D-04-4 | 地块/slot 不落盘；id 由几何哈希派生 | 存盘 + 自增 id | D-13；几何哈希保证与遍历序无关（§3.5） |
| D-04-5 | 不提供逐个手改地块形状，只提供保留地/不可建两个人工层工具 | 自由编辑多边形 | 手改会与真源冲突；约束是设计的一部分 |
| D-04-6 | 场域变化只重算 parcel 属性字段，不重算几何 | 全量重算 | 拖场域实时的根本前提 |
| D-04-7 | 过小块（<`minParcelArea`）**优先并入共享边最长的邻块**，两次仍过小才 `Invalid`（09 H-8） | 直接判废留空 | 判废会在河/细分的夹缝处留下"不能建也长不出路"的废地；真实城市里那是邻块山墙地 |

## 7. 验收
| # | 事实 | 证据 |
|---|---|---|
| A-4-1 | 无 `Invalid` 地块产生 slot；`minParcelWidth/Area` 断言成立 | 全量统计 |
| A-4-2 | 每个有效地块 ≥1 条临街边（否则应属 `Interior`，首版不生成 slot） | 结构断言 |
| A-4-3 | 改场域（半径/曲线/位置）→ 地块几何与 `Parcel.id` 集合完全不变，仅 `dominant/landUse` 变化 | id 集合比对（支撑 A-2/A-1 局部性） |
| A-4-4 | 改 ρ/道路 → 地块 id 在受影响区域外保持稳定的比例 ≥ 95%（几何哈希局部性） | 快照 diff 统计 |
| A-4-5 | slot 朝向 = 临街边法线（±2°）；`groundY` 与 01 高度一致（容差 0.05m） | 采样断言 |
| A-4-6 | 沿河地块的 waterfront slot 存在且朝向朝水 | 用例：画一条河穿城 → 统计朝水 slot |
| A-4-7 | 保留地/不可建区在"全量重生成"后仍生效（人工层，A-7 家族） | 重生成前后断言 |
| A-4-8 | slot 总数在合理区间（A-8 前置）：与地块面积呈单调关系，无爆炸（>100k） | 计数与曲线 |
| A-4-9 | 画河穿城 + ρ=0.22 场景下，`Invalid` 地块占比 ≤3%，且其面积均 < `minParcelArea`×2（过小块已被并入邻块而非留空，H-8） | 地块面积/flags 分布统计 |

## 8. 风险
- 轮廓提取的退化多边形（自交、共线点）会让后续 PCA/面积计算 NaN → 缓解：`PolygonOps.Sanitize`（去共线、保序、面积符号统一），并对退化地块标 Invalid + warning。
- 栅格路线下小地块（<4 格）轮廓极不准 → `minParcelArea` 直接过滤掉，不试图"修好"。
- 两遍法 slot 重排可能与相邻地块"对不齐"（城市里其实常见，可接受）；若要求严格对齐需引入"沿街界面轴线"（P1）。
