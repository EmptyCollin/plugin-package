# Modules/03 — 路网：人工点线编辑 + 程序化补全 + 道路占比系数

> 依赖：Modules/00、01（纵坡）、02（桥/水缓冲）。消费方：04（地块边界）、05（可达场）、07（临街/出入口）。
> 对应里程碑：M4（人工层）、M5（自动补全 + 桥）。状态：草案。

## 1. 需求解释
用户明确"不一定是笔刷，也可能是用点与线来描述"⇒ 人工侧做成**图（Graph）编辑器**：
点 = 路口/端点，线 = 路段。程序侧按**道路占比系数 ρ**补全剩余道路。
关键设计约束：**人工骨架是只读真源，程序只在骨架围出的空地里生长**（pinned 语义），否则补全会不断"吃掉"用户画的线。

## 2. 数据模型

### 2.1 Model：`RoadProfile`（SO）——道路等级表
每行一级：
| 字段 | Highway | Arterial | Collector | Local | Pedestrian |
|---|---|---|---|---|---|
| `rightOfWay`（红线宽 m） | 32 | 24 | 16 | 10 | 5 |
| `carriageway`（车行道宽 m） | 2×11 | 2×7 | 11 | 7 | 2.5 |
| `lanes` | 2×2 | 2×1 | 1 | 1 | 0 |
| `designSpeed`（km/h，用于纵坡/转弯限制） | 80 | 60 | 40 | 25 | 5 |
| `growthPriority` | — | — | 1.0 | 0.6 | 0.2 |
| `minSegmentLen`（m） | 120 | 80 | 40 | 25 | 15 |
| `parcelSplitLevel` | 0 | 1 | 2 | 3 | 4 |
| `crossesRiver` | 必须高架/隧道 | 桥 | 桥 | 涵洞 | 步行桥 |

等级语义解释：`parcelSplitLevel` 决定该路在地块细分递归中"第几层才允许出现"，
Highway 不参与细分（Level 0）而是外部输入（人工画或按城界生成）。这比"每种路各自一套生成规则"简洁得多。

### 2.2 Model：`RoadBudget`（SO 或 CityProject 字段）
| 字段 | 默认 | 语义 |
|---|---|---|
| `roadRatio` ρ | **0.16** | 目标道路占地占比（口径见 §4.1） |
| `ratioTolerance` | 0.01 | 收敛容差 |
| `maxNewSegments` | 2500 | 硬上限（防失控，R-06） |
| `minParcelArea` | 900 m² | 小于此不再细分 |
| `maxRecursionDepth` | 12 | 细分递归深度上限 |
| `orthogonality` | 0.7 | 0=自由角度 1=严格正交 |
| `angleJitter` | 12° | 分裂方向扰动（破网格感） |
| `biasTJunctions` | 0.55 | 新边端点吸附成 T 字口的概率（§7 D-03-6） |
| `growthMode` | AreaFirst | 分裂区域选取策略（§4.3） |
| `allowDiagonalInWaterfront` | true | 临水带允许顺岸角覆盖正交 |

### 2.3 人工层：`RoadGraphAsset`（SO，持久化）
```csharp
struct Junction  { int id; Vector2 pos; JunctionKind kind; bool pinned; bool adoptedFromAuto; }
struct RoadEdge  { int id; int a, b; GradeIndex grade; float curvature; bool pinned; bool adoptedFromAuto;
                   List<Vector2> bendPoints; }     // 仅当用户手动弯曲线时非空
```
- **存原始点线，不存平面化结果**（D-15）：交叉口自动打断、共线合并、曲线离散化全是派生（§5.2）。
  真源保持极简，用户改一条线的弯曲不会留下"打断残留"。
- `pinned` = 人工画的，只读；`adoptedFromAuto` = 原本是自动的、被用户「固化」的（此后也只读）。
- `id` 单调分配且**永不复用**（删除的 id 不回收）——增量重算、场域/建筑引用、undo 定位全靠 id 稳定。

### 2.4 派生层：`RoadNetworkDerived`（内存）
```csharp
class RoadNetworkDerived {
  List<RoadEdgeResolved> edges;      // 含打断后的实际边、离散化后的折线
  Dictionary<(int,int), Intersection> nodes;  // 交叉口聚合（同点多边 → 一个路口多边形）
  PolygonSet roadFootprint;          // 红线并集（00 掩码 Road 位来源；栅格化近似）
  float[] crowHeight;                // 逐格路面标高（供建筑±0 与地形约束）
  Graph topology;                    // 连通性、可达性
}
```

## 3. 技术路线 — 人工层（点线编辑器 `RoadGraphTool`）

### 3.1 输入映射
| 输入 | 行为 | 细节 |
|---|---|---|
| 空白处单击 | 落节点 | 吸附：已有节点(3m)/已有边端点/正交方向线（显示虚线引导） |
| 空白处按下→拖→松开 | 建边（两端自动建节点） | 中途 `Alt` 加弯曲控制点；`Shift` 锁 15° 增量角 |
| **既有边上按下→拖出→松开** | 在边上**插入 T 字/十字路口**（自动打断原边） | 用户无需"先打断再连"，这是路网工具最重要的一次交互设计 |
| 拖已有节点 | 移动 | 实时改几何；松开后 Commit（04/05/06 脏） |
| `Shift`+点选多条边 | 多选 | 批量改等级/删除 |
| 右键边 | 菜单：改等级 / 弯曲 / 反转 / 删除 / 固化(adopt) / 设为主干 | — |
| 右键节点 | 菜单：合并到邻近 / 断开 / 设为端点(kind=DeadEnd) | 端点会提示"断头路，是否自动延伸" |
| `Ctrl+Z` | 撤销一次编辑动作（一笔/一次拖动=1 记录） | — |
| 双击节点 | 进入精确坐标输入（面板显示 X/Z 数字框） | 数据编辑入口之一 |

### 3.2 吸附系统（画得快且干净的前提）
优先级（高→低）：`已有节点` > `边端点` > `边上的投影点` > `正交参考线` > `无`。
- 吸附半径 = `snapDist`（默认 3m，面板可调 1~8m）；屏幕半径兜底（近处格密、远处格稀 → 用 `HandleUtility.GetHandleSize` 折算）；
- 正交：`orthogonality>0.5` 时对新建边尝试对齐到"该节点已有边的主方向 ±0/90"；
- 引导视觉：吸附点画小方块 + 从节点引出的虚线；**必须显示当前是否吸附**，否则用户无法判断画出来的 89.7° 是不是故意的。

### 3.3 编辑态数据规则
- 等级不能为 0/未知；删除边时若孤立出 `degree=1` 的 pinned 节点 → 保留并 warning（断头是合法城市形态，但需要用户知道）。
- 禁止自环（a==b）与重复边（同端点同等级），编辑时拒绝并提示。
- 交叉但不共点（两条边在中间穿过）→ 编辑时**黄标提示**"未连接的交叉"，一键转成路口（这是用户最容易犯的错）。

## 4. 技术路线 — 程序化补全（S3）

### 4.1 ρ 的定义必须先钉死（口径问题 = 验收问题）
```
ρ = Σ_edges ( rightOfWay_i × length_i ) / Area(buildable − water − waterBuffer)
```
- 分子：用**红线宽**（不是车行道宽），因为红线才是"城市为道路让出的地"；交叉口重复计的面积用
  `− Σ_intersection overlap` 修正（近似：交叉口数 × 平均重叠，误差 <2%，可接受）。
- 分母：排除水体与水缓冲（Q-6 已定）；不含外环不可建带。
- 面板必须同屏显示「实测 ρ」与「目标 ρ」两个数字（拖动滑杆时的反馈全靠它，A-4 的观测对象）。

### 4.2 算法：区域递归细分（选定，D-05）
```
Input: 人工骨架 G0（pinned 边作为不可穿越墙）, 可建区域 R（来自 00 掩码 + 02 水系边界）
frontier = max-heap by area, {R}
while ρ_actual < ρ_target and |frontier|>0 and n_new < maxNewSegments:
    region = frontier.pop()
    if region.area < minParcelArea or depth > maxRecursionDepth: continue
    (edge, children) = Split(region)              # §4.3
    if edge == null: continue
    加入 edges（grade 选定、pinned=false）
    ρ_actual += edge.rightOfWay × edge.length / denom
    for c in children: frontier.push(c)
```
终止三重保险：达标 / `maxNewSegments` / 深度。R-06 死循环的结构性解法。

### 4.3 一次分裂怎么选（决定"像不像城市"）
1. **选区域**：默认 `AreaFirst`（最大面积优先）——保证先出主干道状骨架再填空隙；
   可选 `LongestFirst`（长边最长优先，出"沿着河的带状路网"）。
2. **选方向**：
   ```
   θ = 区域主轴方向 ± angleJitter·noise(cellSeed)   # 与主轴垂直切分
   if orthogonality > 0: θ = slerp(θ, 最近参考正交方向, orthogonality)
   if region 邻水 && allowDiagonalInWaterfront: 参考方向换成岸线切向
   ```
3. **选位置**：默认在区域"重心 ± 偏移"处切；`biasTJunctions` 概率下把一端**吸附到对面已有的 T 位置**
   （制造 T 字口而非十字；真实城市 T 口远多于十字，这条是"看起来像城市"最便宜的手段）。
4. **选等级**：分裂层深 → `parcelSplitLevel` 映射（Level 2 = Collector，Level 3+ = Local）；
   长度 ≥ `minSegmentLen(grade)` 才允许该等级，否则降级。
5. 分裂结果必须通过冲突检测：穿 `water` → 改道/插桥（02 A-2-4）；穿 pinned 边 → 视为非法，回退候选方向（最多试 4 个）。

### 4.4 与人工骨架共存（pinned 语义的严格定义）
| 操作 | 对 pinned | 对 auto/adopted |
|---|---|---|
| 细分算法穿越 | **禁止**（pinned 边是不可穿墙） | 允许被"替换"（先删旧 auto 边再切，保持 ρ 单调） |
| 端点吸附 | 可作为吸附目标 | 可 |
| 纵坡/曲线优化 | **不动** | 可被移动（≤`autoNodeMaxDrift` 3m，保守） |
| 重生成 | 原样保留（A-7） | 全部重算 |
| adopt（固化） | — | 变 pinned（`adoptedFromAuto=true`，A-14 不回归） |

### 4.5 桥与路口（几何耦合点）
- 桥：边与 `riverPolygon` 求交 → 插入 `BridgeNode`，桥跨 = 交点前后各 +`bankSlope×depth + waterBuffer·0.5`；
  桥面标高 = `max(两岸地面) + bridgeClearance`，纵坡用样条连到两端（超 `designSpeed` 限值时报 warning）。
- 交叉口多边形：同点 ≥3 边汇聚 → 生成路口面（半径 = 各边 `carriageway/2` 之和的均值），从可建掩码扣除。
- 首版**不做车道级几何**（单线中心 + 红线带宽即可），车道/人行道细节留给后续包或 View 材质。

### 4.6 增量与实时（ρ 滑杆的手感要求）
- 滑杆拖动（Preview）：只做**边数量与 ρ 数字**的增量估算（沿最大堆继续/回退 k 步分裂，不重建 mesh），
  目标单步 <30ms；
- 松手（Commit）：完整重建派生层 + 标脏 04/05/07；
- 回退（ρ 调小）：因为记录 `n_new` 与分裂历史栈，回退是**弹栈**而非重跑（保证拖动来回一致、无抖动）。
- **栈失效规则（Modules/09 H-1）**：弹栈的前提是分裂栈与当前墙集合一致。任何**墙变化**
  （pinned 边增删、adopt、锁区 boundary wall、`NoRoad` 负约束挂上）⇒ **分裂栈立即作废**，
  按当前 pinned 图确定性重建（从 R 重跑到当前 ρ）。因此"来回拖动一致"仅在同一滑杆会话内保证；
  跨墙变化后回退走全量重建，结果仍确定（同墙集合同 ρ ⇒ 同结果），但**不保证逐边相同于历史那次**。

## 5. 数据编辑与持久化
| 数据 | 编辑入口 | 载体 | 入 git | 撤销 |
|---|---|---|---|---|
| 等级表 `RoadProfile` | 面板表格（可增删等级行） | SO | ✅ | ✅ |
| `RoadBudget`（ρ 等） | 主面板滑杆/数字框 | SO | ✅ | ✅ |
| 人工节点/边 | `RoadGraphTool` + 节点 Inspector 数字框 | `RoadGraphAsset` | ✅ | 动作级 |
| 派生边/路口/红线/mesh | ❌ | 内存 | — | — |
| 导出（调试） | 面板 → `.tmp-inspect/roads.svg` | — | ❌ | — |

节点级 Inspector（`CityJunctionEditor`）暴露：坐标（X/Z 精确值）、kind、pinned、
邻接边列表（可跳转选中）、纵坡与相邻高差 —— **数据编辑不能只靠拖 handle**，精确对齐（如"这个路口要和河对岸那个正对"）必须有数字入口。

## 6. 可视化（SceneView）
| 图层 | 呈现 |
|---|---|
| 人工边 | 实线，等级配色（Highway 红→Pedestrian 灰），宽度 ∝ 红线宽 |
| 自动边 | 细实线 + 轻微透明 |
| adopted | 实线 + 端点小旗标（区分于用户手绘） |
| 红线带 | 半透明填充（Commit 后可见，拖动时只画中心线） |
| 交叉口 | 圆点（degree 数字）；未连接交叉 = 黄三角（§3.3） |
| ρ 覆盖图 | 可选热力：区域已建 ρ vs 目标（调参时定位"哪片太密"） |

## 7. 模块内决策
| 编号 | 决策 | 备选 | 理由 |
|---|---|---|---|
| D-03-1 | 人工输入 = 图（点线），不用笔刷 | 笔刷中心线 | 用户明示；且图结构天然表达路口/等级/拓扑，笔刷不表达 |
| D-03-2 | 边上按下拖出 = 自动插入分叉（T/十字） | 需先手动打断 | 少一个动作、少一个错误来源 |
| D-03-3 | 真源存原始点线，平面化/打断/离散化全派生 | 存打断结果 | 改弯曲不留残留；D-15 |
| D-03-4 | 补全用地块细分式生长 | Voronoi / L-system / MST | 天然产出可填充地块；pinned 只读可保；ρ 可迭代收敛 |
| D-03-5 | ρ 分子用红线宽、分母排除水与缓冲 | 车行道宽/全用地 | 红线才是城市为路让出的地；否则临河城被迫堆路（Q-6） |
| D-03-6 | 端点吸附造 T 字口（`biasTJunctions`） | 全十字 | 真实城市 T 口多，成本极低观感提升最大 |
| D-03-7 | 分裂历史入栈，ρ 回退 = 弹栈 | 每次全量重算 | 来回拖动不抖动、结果一致 |
| D-03-8 | id 永不复用 + adopt 固化机制 | id 复用 | 增量与跨模块引用要求 id 稳定（A-14） |

## 8. 验收
| # | 事实 | 证据 |
|---|---|---|
| A-3-1 | 在已有边上拖出新边 ⇒ 原边被正确打断为 2 段，`pinned` 属性继承，总长度守恒（容差 0.1m） | 派生层断言 |
| A-3-2 | ρ 从 0.10→0.25：实测 ρ **单调上升**；再调回 0.10，**同一滑杆会话且期间无墙变化**时边集合完全一致（弹栈可逆）；有墙变化时改为栈重建，断言降级为"同墙集合同 ρ ⇒ 同结果" | 两次快照边 id 集合比对（两种前置各一遍） |
| A-3-3 | `minParcelArea` 保护：最终所有地块面积 ≥ `minParcelArea`（除人工围出的例外），无"放不下建筑"的碎片 | 04 地块面积分布统计（A-4） |
| A-3-4 | 细分从不穿越 pinned 边、不进入 `water` 格 | 几何求交断言（全量边检查） |
| A-3-5 | 道路穿河 100% 有桥/涵洞且桥面标高 ≥ `waterLevel+bridgeClearance` | 交叉清单（A-5 联合） |
| A-3-6 | T 字口占比 ≥ 60%（默认 `biasTJunctions=0.55` 下） | 交叉口 degree 统计 |
| A-3-7 | ρ 单步 Preview 耗时 ≤30ms、无 GameObject 创建；Commit 才重建 | Profiler + 计时 |
| A-3-8 | 未连接交叉会黄标提示并可一键转路口；自环/重复边被拒绝 | 交互走查 |
| A-3-9 | 删除 auto 边后重生成不复活（除非 adopt）；adopted 边重生成保持不变 | A-14 用例 |

## 9. 风险
- **多边形切分实现成本最高**（凹区域切分、带洞区域、沿河边界）→ 缓解：用栅格近似（格网 + 掩码）做区域表示，
  分裂线用格网扫描线；矢量精确切分留 P1（与 02 R-1 同源风险，两者共用一套栅格-轮廓提取工具）。
- 细分产生的路可能"从山上直冲下来"（忽视地形）→ 缓解：分裂打分项加纵坡惩罚（`Δh/length > designSpeed` 限值则换方向）。
- ρ 高（>0.25）时地块碎片化，建筑放不下 → 面板黄标 + `minParcelArea` 兜底 + A-3-3 断言，明确告诉用户"已达上限，ρ 无法再升"而不是死循环。
- 人工骨架画得太密时自动补全无事可做，用户误以为"程序化没生效" → 面板显示「本次新增边数 = 0，原因：已达 ρ 目标 / 区域均小于 minParcelArea」。
