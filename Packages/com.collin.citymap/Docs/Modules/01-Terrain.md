# Modules/01 — 地形：自动生成 + 人工雕刻

> 依赖：Modules/00。消费方：02（下切）、03（道路纵坡）、04（地块平整）、05（坡度场）、07（沿坡校正）。
> 对应里程碑：M2（生成）、M3（雕刻工具）。状态：草案。

## 1. 需求本质与解法思路
用户诉求"城市相对平整、但局部可能有起伏"是**一对矛盾**：噪声给起伏，而建筑与道路要求平地。
错误解法是"把噪声调小"（全城变煎饼，失去地形趣味）或"事后磨平"（磨平会扩散、且每次重生成磨一遍不可控）。

本模块解法：**分离"自然起伏"与"人工平整"两个 pass，用加权融合同时满足**（D-04/D-12）
```
h_final = 基底噪声 h_base  ⊕(加权融合)⊕  平整约束集合 {plane_i}  ⊕(叠加)⊕  河流下切 carve  ⊕(叠加)⊕  人工雕刻 delta
```
次序固定（写入契约 §3.5），任何一次重生成按同一顺序重放，所以"人工劳动不被摧毁"是结构保证而非运气。

## 2. 数据模型

### 2.1 Model：`TerrainProfile`（SO，共享配置）
| 字段 | 默认 | 语义 |
|---|---|---|
| `baseAmplitude` | 18m | 低频起伏幅度（fbm 振幅） |
| `baseWavelength` | 520m | 低频波长（≥城尺度 1/4，避免城市内部碎起伏） |
| `octaves` / `gain` / `lacunarity` | 4 / 0.5 / 2.0 | fbm 参数 |
| `domainWarpAmp` | 0.35·λ | 域扭曲强度，打破噪声的规则格感 |
| `detailAmplitude` / `detailWavelength` | 1.2m / 24m | 细节（只塑形，不参与规划） |
| `urbanFlatness` | 0.25 | **城市区幅度衰减系数**（用户"相对平整"的主旋钮，0=全平 1=全起伏） |
| `urbanCenter` / `urbanRadius` | 世界中心 / 0.45·worldSize | 城心与半径（与场域协同，见 §4.4） |
| `maxParcelSlope` | 5° | 单地块允许最大坡度；超过则拟合平面（§4.2） |
| `constraintFalloff` | EaseOut 曲线 | 平整约束的衰减曲线（走 00 §3.3 统一求值器） |
| `constraintInner/Outer` | 地块内切 / +12m | 约束影响带的内外边界（米） |
| `roughnessLock` | false | 调试：冻结细节噪声，只看规划形状 |

### 2.2 Instance：`CityTerrain`（场景 MonoBehaviour）
序列化：`TerrainProfile profile`（引用共享 Model）、`SculptDeltaAsset delta`（人工层引用）、
`heightAsset`（自动层快照，可选）、`displayStep` 覆盖。
不序列化：运行时高度数组、mesh（派生）。
判据落地：**同一 `TerrainProfile` 被多个城市实例复用时，实例的雕刻/高度差异一律不写进 Profile**（§0.1 原则2）。

### 2.3 人工层：`SculptDeltaAsset`（SO，持久化）
```csharp
struct DeltaChunk { Vector2Int chunkCoord; float[] deltaPerCell; int schemaVersion; }
// 稀疏：只存有非零 delta 的 chunk；全零 chunk 条目删除
```
- 存**增量**（叠加值），不存绝对高度。理由：绝对高度会与噪声重算冲突（改了噪声参数后人工值该保留还是让位？
  答案由"叠加"语义自动给出：保留人工的**相对改动量**）。
- 用户显式"设为绝对高度"的操作（§5.4 压平到指定值）实现为 `delta = target − h_base`，
  并在重算基底时**重解 delta**（保持绝对效果）——记入 `DeltaChunk.mode ∈ {Additive, Absolute}`。
- 体积：1024² 格全用满 = 4MB；稀疏通常远小于此。

### 2.4 只读采样 API（Runtime，供其他模块与外部包）
```csharp
float  GetHeight(float x, float z);            // 双线性采样，非格点连续
float  GetBaseHeight(float x, float z);        // 不含 delta/carve（规划用）
Vector3 GetNormal(float x, float z);           // 中心差分
float  GetSlopeDeg(float x, float z);
bool   IsBuildable(x, z, out NoBuildReason r); // 读 00 §2.4 掩码
```
`GetBaseHeight` 与 `GetHeight` 必须分开：规划（地块朝向、道路纵坡）用**含人工与下切的真高度**，
而"计算新约束目标平面"用基底，否则约束会把自己上一轮的结果当输入而逐轮漂移（正反馈）。

## 3. 技术路线

### 3.1 S1a 基底高度（噪声，自动层）
```
u = dist(p, urbanCenter) / urbanRadius
ampFactor = lerp(urbanFlatness, 1.0, smoothstep(0.6, 1.4, u))   // 城内压低，城外放开
h_base = ampFactor · A · fbm(domainWarp(p))
       + detailA · fbm_detail(p)
       + ringBias(u)            // 可选：外环整体渐降，让城市坐落在一块台地上
```
- 城外缘用 `smoothstep` 过渡（不是硬边），否则会出现"城圈内是平地、圈外突然起山"的假边界。
- 纯函数、走 00 §3 的种子与噪声实现。

### 3.2 重放次序（契约，固定不可变）
```
1 h_base(S1a)  →  2 河流下切 carve(02)  →  3 雕刻 delta(本模块人工层)  →  4 平整约束融合 S1b(依赖 04 结果)
```
- 3 在 4 之前：人工雕刻是"用户说这里就该这样"，优先级高于自动平整；但 4 只作用于约束带内的格，
  与人工区的冲突由 `ManualLocked` 掩码格豁免（人工钉住的格不参与约束融合）。
- 每一步都可单独关闭（面板开关），便于定位"这个包是谁造成的"。

### 3.3 显示 mesh 构建
- 顶点按 `displayStep` 取样高度；法线由高度差分算（不用 mesh 的 `RecalculateNormals`，差分口径与 `GetNormal` 必须一致）。
- 逐 chunk 独立 mesh：`chunkId → MeshFilter/MeshRenderer`，重建只换脏 chunk 的 `Mesh`（`Mesh.UpdateVertexData` 优先于重建数组）。
- 顶点色/UV：`uv = worldXZ / tileScale`（世界映射，避免拉伸接缝）；顶点色可写坡度/用途用于调试着色。
- 材质：本包**不依赖任何 shader 包**，提供 URP-compatible 的 SimpleLit 材质引用（`Sample/` 内），
  正式项目替换为自己的地形材质即可（View 只读，不参与生成逻辑）。

### 3.4 S1b 平整约束融合（关键技术点）
约束源（S4 之后才齐全）：
```csharp
struct PlaneConstraint { Plane plane; float innerR; float outerR; float weight; ConstraintKind kind; }
kind ∈ { Parcel(强), RoadCrown(带状·沿程弱/剖面强), WaterBuffer(弱), Plaza(强) }
```
对每个受影响格 p：
```
num = Σ_i weight_i · EvalFalloff(curve_i, t_i(p)) · plane_i.heightAt(p.xz)
den = Σ_i weight_i · EvalFalloff(curve_i, t_i(p))
if den > ε:  h = (h · Wfree + num) / (Wfree + den)     // Wfree = 1 常量"自由权重"
```
- `Wfree` 是"保留自然地形"的权重，`den → ∞` 时完全贴合约束平面；`Wfree` 进 `TerrainProfile`（默认 0.6）。
- 只遍历约束包围盒内的格（不扫全城），复杂度 `O(Σ约束面积/cellSize²)`。
- **非迭代**（上限 1 次，D-04）：迭代松弛会让改动在空间上传播，破坏局部稳定性与 A-2 类断言。
- 道路带用各向异性权重（剖面方向 w 大、沿线方向 w 小）→ 路是"横平"的但可以"纵有缓坡"，这是山路城市的观感来源。

### 3.5 地块目标平面怎么定
1. 取地块多边形内格点高度，**中位数**为基准（抗单点噪声/角落极值拉偏）；
2. 最小二乘拟合平面 `n·p=d`；若拟合坡度 ≤ `maxParcelSlope` 用拟合面（保留轻微坡向，利于排水与观感），
   否则退化为**水平面**（用中位数）并记 warning（地块过坡）。
3. 平滑过渡半径 = `constraintOuter`（默认内切 +12m），保证相邻地块的平地之间有谷而不是台阶。
- 中位数而非均值：地块角落常被道路红线切掉一角，均值对这种残缺多边形敏感（实测会整块偏 0.3m 级）。

## 4. 操作与交互

### 4.1 参数实时性（AGENTS §0.1 原则1）
| 参数 | 生效方式 |
|---|---|
| 噪声类（幅度/波长/八度/warp） | 拖动中：脏 bounds 内低分辨率预览（重新采样显示网格即可，<8ms）；松手：S1a→S1b→(S5,S6 脏) 全链 Commit |
| `urbanFlatness` | 同上，且**预览必须显示城内/城外对比**（叠加城心圆环 overlay） |
| 约束类（`Wfree`/内外半径/曲线） | 拖动中：仅重算约束融合（跳过 S1a，快），实时；松手 Commit |
| `displayStep` | 立即重建 mesh（无生成逻辑变化），不需 Commit |

### 4.2 生成入口
- 「新建城…」（New City，高危）：给 seed + 尺寸 → 建立新意图层（旧意图层先自动快照）→ 跑 S0/S1a → 立刻可见基底地形。
  确认流程见 08 §3.5，**禁止与「重新生成」合并为同一按钮**（H-16）。
- 「重新生成」（Rebuild）= 用当前意图层重跑全链并**保留人工层**（§3.2 次序保证，A-7 验收），无需确认。
- 「只重算平整」按钮（S1b）：改地块大小后不必重跑噪声，秒级。

### 4.3 高度可视化（调地形的眼睛）
SceneView overlay 三档（互不叠加，主面板视图模式控制）：
`灰度高程`（0~maxParcelSlope 以上渐变红）、`坡度方向`（法线 XY 映射到 RG）、`用途合成`（读 05）。
另在面板显示光标处读数：高度 / 坡度° / 掩码位。

### 4.4 与城心的耦合：**独立锚点，不实时读场域**（Modules/09 H-7 修订）
> v2 曾设计"S1a 实时取金融场域位置为城心"。推演证明它会让"拖动一个场域"变成**全城重算**
> （城心变 → 压幅变 → 地块几何变 → parcel id 变 → 建筑全重排，还连带 D8 与纵坡），
> 同时摧毁手感（05 A-5-5）与局部稳定性（00 D-10）。故降格为显式意图对象。

```csharp
// 进 CityProject（人工层，可撤销、可 diff、持久化）
struct CityCenterAnchor { Vector2 pos; float radius; string sourceFieldStableId; int setByVersion; }
```
- S1a 压幅中心、07 `distanceDecay` 中心**只读锚点**；`TerrainProfile.urbanCenter/Radius` 作为未设置时的默认值。
- 场域面板/右键场域 →「设为城心」= **一次显式提交**（把锚点写为该场域当前中心与 outerRadius），不是隐式跟随。
- 锚点变更 = **高危操作**：影响地形与全城形态 → 必过 08 §6.2 影响预览 + 自动快照。
- 来源必须可见：面板黄字「城心来自锚点（由场域 CBD 于 v37 设置）」，可点击定位到场域。

### 4.5 笔刷与手柄
- SceneView 内一个可拖动的圆（笔刷轮廓）+ 半径/强度用键盘修饰：`Shift`=半径、`Alt`=吸强度（吸取当前处坡度）。
- 拖拽 = Preview（只更新受影响格与脏 chunk mesh）；松手 = Commit（写 delta 资产 + 标脏 S6）。

## 5. 雕刻工具（TerrainSculptTool，EditorTool）

### 5.1 操作表
| 操作 | 输入 | 行为 | 落数据 |
|---|---|---|---|
| 抬升/下压 | 左键拖 + 强度 | `delta += strength · falloff(r) · dt`（`dt` 与帧时无关——用固定笔速积分，见 D-01-3） | delta 层 |
| 压平到中心高度 | 快捷键 P | 笔刷内 `delta = h(中心) − h_base`，mode=Absolute | delta 层 |
| 压平到指定值 | 输入数值 | `delta = target − h_base` | delta 层 |
| 平滑 | 左键拖 | 邻域加权平均的**增量** | delta 层 |
| 复制形态 | Ctrl+点源、点目标 | 搬运源处的 delta（相对基底） | delta 层 |
| 反色/求反 | 按钮 | `delta = −delta`（同中心） | delta 层 |
| 清除人工 | 框选 + Delete | 删除 delta 条目 | delta 层 |
- **笔速积分用固定步长**：`strength · 笔刷速度 · 固定步数` 决定增量，与帧率无关；
  否则"高帧率机器抬得慢、低帧率抬得快"，破坏跨机器一致性（A-1 的环境面）。

### 5.2 撤销与保持
- 一次连续笔划 = 一个 undo 记录（起笔 `Undo.RecordObject(deltaAsset)`，落笔合并），禁止每个 tick 建记录（undo 栈爆炸）。
- delta 资产是**资产不是场景对象**：撤销要走 `Undo.RecordObject(deltaAsset, ...)` + 标脏保存；
  工具需 `EditorApplication.update` 兜底把未保存改动标 dirty（`EditorUtility.SetDirty`）。

### 5.3 与约束融合的关系（不冲突的实现要点）
雕刻写入前把目标格从约束带中**可选豁免**：工具栏「雕刻优先」开关
- 开（默认）：笔刷范围自动置 `ManualLocked`，S1b 跳过这些格 → 人工改动不被自动平整抹回来；
- 关：人工值只作为叠加项，仍可被平整约束拉回（用于"我只是想加一点纹理"的场景）。
不写掩码而是写资产标记（避免撤销时掩码状态不同步）——记入 §7 决策。

### 5.4 性能
- 单次笔划影响格数上限 `maxCellsPerStroke`（默认 8000 格 ≈ 半径 90m），超出截断并提示"笔刷过大，建议按区域分次编辑"；
  超大笔刷一次改全城会让 Preview 掉帧，反而让用户以为卡死。
- 脏 chunk mesh 重建走 `Mesh.MarkDynamic + UpdateVertexData`，且**同帧内多次改动合并**（00 §4.2）。

## 6. 数据编辑与持久化
| 数据 | 编辑入口 | 存储 | 撤销 | 版本 |
|---|---|---|---|---|
| `TerrainProfile` | 面板 Profile 区 / 资产 Inspector | SO 入 git | ✅ | schemaVersion |
| 基底高度 | 不可直接编辑（只由参数决定） | ❌ 不落盘（派生） | 参数撤销 | — |
| 雕刻 delta | SculptTool | SO 稀疏 chunk | ✅ 笔划级 | mode 字段 |
| 显示 mesh | 不可编辑 | ❌ | ❌ | — |
| 导出高度图（调试） | 面板按钮 | `.tmp-inspect/height.png` | — | 不入库 |

## 7. 模块内决策
| 编号 | 决策 | 备选 | 理由 |
|---|---|---|---|
| D-01-1 | 人工层存 delta（含 Additive/Absolute 两模式），不存绝对高度 | 存绝对高度 | 改噪声后人工意图以"相对改动"存活；绝对模式单独保证"设为指定值"语义 |
| D-01-2 | 重放次序固定 4 步且每步可单独关闭 | 单一高度函数 | 次序固定=可解释；可关=可定位问题来源 |
| D-01-3 | 笔刷积分用固定步长，与帧率无关 | `dt` 乘帧时 | 跨机器可复现 |
| D-01-4 | 目标平面用中位数 + 条件最小二乘 | 纯均值/纯水平 | 抗残缺多边形极值；保留轻微坡向更自然 |
| D-01-5 | 融合公式引入常数 `Wfree` 自由权重 | 只用距离衰减 | 控制"自然地形保留度"这一独立手感维度 |
| D-01-6 | 城心 = 独立锚点对象 `CityCenterAnchor`，场域经「设为城心」显式提交；**S1a 不实时读场域位置** | v2 旧案：实时派生（已废，H-7） | 保局部稳定与拖拽手感；来源仍单一真源且可见 |
| D-01-7 | 雕刻优先用 delta 内标记而非掩码 | 掩码位 | 撤销一致性 |

## 8. 验收
| # | 事实 | 证据 |
|---|---|---|
| A-1-1 | `urbanFlatness=0` 时城内标准差 ≤0.2m；=1 时接近裸噪声标准差（±20%） | 城内格高度统计 |
| A-1-2 | 改噪声参数后重生成：delta 与 Absolute 模式的人工高度**逐点保持不变**（Additive 保持相对增量） | 重生成前后逐点比对 |
| A-1-3 | 压平地块：地块内最大坡度 ≤ `maxParcelSlope`（±0.5° 容差），且相邻地块过渡带无 >1m 台阶 | 采样断言 + 梯度场统计 |
| A-1-4 | 道路纵向允许坡度而横向平整（各向异性生效） | 沿程/剖面坡度对比 |
| A-1-5 | 单一笔划在 60Hz 与 144Hz 机器上产生相同 delta（固定步长） | 模拟不同帧时的单元测试 |
| A-1-6 | 一次连续笔划只产生 1 条 undo 记录；Ctrl+Z 回到起笔前 | undo 栈计数 |
| A-1-7 | S1b 非迭代：改动 1 格约束不影响 3×constraintOuter 之外的任何格高度（逐位相同） | 远端格哈希比对（支撑 A-2） |
| A-1-8 | 城心只读锚点：拖动场域**不触发 S1a**（面板脏集合仅 S5/S6）；「设为城心」提交后地形压幅中心随之改变，且面板显式标注锚点来源与 setByVersion | 拖动帧耗时断言 + 提交前后城内高度标准差比对 |

## 9. 风险
- 约束带相互重叠处的加权平均可能产生"碗状"（两个相邻大平地之间的谷被抬平）→ 缓解：约束 `outerR` 有上限
  （`constraintOuter` 不超过 2×cellSize 之外的固定 12m 量级），且 `Wfree` 不为 0。
- `GetBaseHeight` 被误用于"真高度"场合（如道路纵坡）会导致人工雕刻不生效 → 两个 API 在代码里加 `<remarks>` 并在 03 模块明确引用哪个。
- delta 资产膨胀（用户反复大笔刷编辑）→ 面板显示 delta 非零 chunk 数与估算内存，提供「压缩：删除 <0.01m 的条目」。
