# Modules/05 — 场域（LandUseField）：意图 → 四维强度场

> 依赖：00。消费方：01（城心派生）、04（parcel.landUse）、07（准入门槛 + 打分）、03（可达场修正）。
> 对应里程碑：M6。状态：草案。
> **本包的设计枢纽**：把"我想要城中心是金融区"这类意图，翻译成每格可计算的金融/工业/住宅/农业强度。

## 1. 语义（用户定义 + Q-1 裁决）
一个场域 = **两个同心圆柱体**（XZ 平面内两个同心圆，Y 方向默认无限）：

| 参数 | 用户叫法 | 类型/范围 | 语义 |
|---|---|---|---|
| `type` | 类型 | `Finance/Industrial/Residential/Agricultural` | 影响哪个维度 |
| `innerRadius` | 内径 | 米，>0 | 内圆柱半径（"绝对区"） |
| `outerRadius` | 外径 | 米，> innerRadius | 外圆柱半径（影响区） |
| `innerValue` | 内径值 | 0~100 | 内圆柱边界处的强度 |
| `outerValue` | 外径值 | 0~100 | 外圆柱边界处的强度 |
| `falloff` | 衰减曲线 | `AnimationCurve`（可引用共享 `FalloffCurveAsset`） | 内→外的进度映射 |
| `weight` | 强度权重 | 0~2 | 多场域叠合时的相对权重（`combineMode=Add/Multiply` 时使用） |
| `enabled` | 启用 | bool | 关闭后不参与烘焙（保留数据） |
| `minY/maxY` | 高度带 | 默认无限（Q-9 / R-08） | 三维场预留 |

**求值**（`r` = 场域中心到采样点的 XZ 距离）：
```
r ≤ innerR           → v = innerValue                                  # 平台（Plateau）
innerR < r ≤ outerR  → t = (r-innerR)/(outerR-innerR)
                       v = lerp(innerValue, outerValue, EvalFalloff(falloff, t))
r > outerR           → v = 0
```
`EvalFalloff` 走 00 §3.3 的统一求值器（与地形平整约束同一口径）。
`outerValue` 允许 > 0（"这个影响区到边缘还剩 20 分"），默认 0。

**Q-1 已确认**：`innerRadius`（米）与 `innerValue`（0~100）是**两个独立参数**。
用户原句「内径设为 10 ⇒ 内径范围内只从金融性 ≥ 10 的建筑中选取」的两种读法都成立：
- 半径 10m + 值 10 → 小范围弱门槛；
- 半径 200m + 值 80 → CBD 强门槛。
文档 §4.6.2（v1）的解释延续为定稿。

## 2. 数据模型

### 2.1 Instance：`LandUseField`（场景 MonoBehaviour，D-00-6）
```csharp
[ExecuteAlways, DisallowMultipleComponent]
public sealed class LandUseField : MonoBehaviour {
    [SerializeField] string stableId;      // 创建时生成，永不复用（增量/diff/引用）
    [SerializeField] LandUseType type;
    [SerializeField] float innerRadius, outerRadius;
    [SerializeField] int innerValue, outerValue;   // 0~100 整数（D-02）
    [SerializeField] bool useSharedCurve;          // 二选一：共享曲线资产 or 内联曲线
    [SerializeField] FalloffCurveAsset sharedCurve;
    [SerializeField] AnimationCurve inlineCurve;
    [SerializeField] float weight = 1f;
    [SerializeField] bool enabled = true;
    public float Sample(Vector2 xz);               // §1 公式
    public Bounds GetAffectedBounds();             // 外径外接盒（脏区域用）
}
```
曲线归属判据（AGENTS §0.1 原则2）：**被 ≥2 个场域复用的曲线才进 `FalloffCurveAsset`**（Model 层，改一处全城同步）；
独享曲线留在组件内联。`useSharedCurve` 二选一，禁止"共享曲线又被就地编辑"的双真源。

### 2.2 Model：`FieldPreset`（SO）
提供一键创建：`CBD 金融核心`、`工业带`、`住宅铺底`、`农业环带`、`行政硬边界(Step)`、`滨水住宅`。
每档预设 = type + 半径比例（相对 worldSize）+ 内外值 + 曲线 + 配色（面板显示用途）。

### 2.3 派生层：`LandUseGrid`（内存，D-13）
```csharp
class LandUseGrid {
    float[] fin, ind, res, agr;      // SoA（四张平面数组，便于 Job 与 SIMD）
    int columns, rows; float cellSize;
    void BakeDirty(Rect[] affected); // 只重烘脏区域
    float4 Sample(Vector2 world);    // 四通道双线性
    LandUseType Dominant(int cell);  // argmax
    int Threshold(int cell, LandUseType t);  // → 07 的准入门槛（0~100，向上取整）
}
```

## 3. 技术路线

### 3.1 烘焙
```
for 每格 p:
  for 每个维度 d: v_d = 0
  for 每个 enabled 场域 f（在 p 的外接盒索引内）:
      v = f.Sample(p)
      if v > 0: v_d[f.type] = Combine(v_d[f.type], v, f.weight, f.combineMode)
```
- 加速：**圆外接盒 + 距离平方比较**（不开方）；按维度分桶场域列表。
- 场域 ≤8：主线程直接算（<5ms，拖动跟手）；>8：`IJobParallelFor`，脏区域按场域 bounds 取并集。
- 复杂度 `O(cells × fields)` 只在脏 bounds 内发生（拖场域 = 只烘圆环）→ A-2 的实现依据。

### 3.2 跨场域叠合（D-03 定稿）
| 模式 | 公式 | 适用 |
|---|---|---|
| **Max（默认）** | `v' = max(v, f·weight)` | 保持"这是金融区"的语义纯净，门槛不被稀释 |
| Add | `v' = min(100, v + f·weight)` | 想要"多片郊区叠加成一个大区" |
| Multiply | `v' = clamp(v · (f/100))` | 行政/限高类"共同满足"语义（少用） |

选默认 Max 的理由（写在 v1 D-03，保留）：Add 会让重叠场域把值顶到 100，
使门槛失去约束力（07 里所有建筑都不合格 → 全城掏空，R-02）。
**注意**：`Max` 模式下 `weight` 语义为"该场域强度的乘性增益"（`f·weight` 后截断到 100），需在 tooltip 写清，避免用户以为权重能突破 100。

### 3.3 两个派生场（非场域，由系统生成）
```csharp
Accessibility  = Σ over 道路边: w(grade) · EvalFalloff(Linear, r/accRadius)   // 距主干/快路的可达度，r = 到最近边距离（栅格 EDT）
WaterProximity = EvalFalloff(curve, d/waterRadius)                              // 距水岸越近越高（02 channelDist 直接取）
```
为什么不是场域（D-02-4）：场域表达**用户意图**，派生场表达**客观事实**。
把"临水"做成可画的场域，用户会以为画个圈就能造出河。二者在 07 的打分里汇合，但来源与 UI 分区严格分开。

### 3.4 用途合成（parcel 与格两级）
- 格级：`LandUseGrid` 原样（供采样、可视化热力）。
- parcel 级（04 消费）：对地块内格做**面积加权平均** + 记录 `dominant`；小地块（<20 格）用质心采样（避免统计噪声）。
- 全局先验（`CityProject.landUsePrior`，可选 SO）：径向权重（城外环偏农）+ 少量噪声（±5 分）打破硬边界；
  **先验必须可以在面板一键关掉**，否则用户调了场域看到"没反应"会怀疑场域失效。

## 4. 操作与交互（`FieldTool`，EditorTool）

### 4.1 创建
| 入口 | 行为 |
|---|---|
| 面板预设区拖出 | 落点 = 鼠标在场景的落点（射线打地面）；半径按预设相对 worldSize |
| 菜单 `Collin/CityMap/Field/…` | 在 SceneView 中心创建 |
| 多选 + 复制 | `Ctrl+D`，保持相对布局 |
| 批量 | 面板「按现有城心新建金融场」等按钮（读 `CityCenterAnchor`，01 §4.4）；反向按钮「设为城心」见 01 §4.4（H-7） |

### 4.2 编辑（SceneView handle，必须全可视化）
| 操作 | 交互 | 约束 |
|---|---|---|
| 移动 | 拖中心球 | 实时 Preview：脏圆环重烘 + 用途 overlay 实时变 |
| 改 `innerRadius` | 拖内圈手柄（按住 `Shift` 等比改外圈） | 强制 `inner ≤ outer − minGap(2m)`，越界手柄停住并提示 |
| 改 `outerRadius` | 拖外圈手柄 | `outer ≥ inner + minGap` |
| 改 `innerValue/outerValue` | 面板滑杆 / 手柄上滚轮（0~100 整数步进） | 数值显示在手柄旁（HUD） |
| 改曲线 | 面板曲线编辑器（`AnimationCurveField`） | 曲线改动 → 全部引用者实时刷新 |
| 改 type | 面板下拉 / 快捷键 1-4 | 改类型 = 一次完整脏（可能两个维度都变） |
| 删除 | 选中 `Del` | Undo |
| 多选批量 | 框选后统一改半径/值/曲线/权重 | 批量操作 = 1 条 Undo（01 §5.2 同规） |

**手柄必须区分"拖中/拖完"**：拖中只做 Preview（脏环重烘 + overlay，禁止重建任何 GameObject、禁止改 07 的建筑实例）；
松手 Commit（04 parcel 字段更新 → 07 脏 parcel 重排）。这是拖场域能"跟手"的实现纪律（00 D-00-4）。

### 4.3 可视化（场域看不见就没法调）
| 图层 | 做法 |
|---|---|
| 同心圆柱 | 内圈实心亮环线 + 内圆柱半透明矮柱；外圈细虚线环；选中时高亮 |
| 强度圆盘 | 地面 patch mesh 顶点色 = 类型色 × 归一化强度，`alpha ≤ 0.35`；只更新脏圆环内顶点 |
| 合成用途图 | 主面板视图「用途」：每格 argmax 维度上色（一眼确认"城心确实是金融区"）|
| 门槛等值线 | 可选：把 `Threshold(cell)=t` 画成等值线（调 innerValue 时判断"到底影响了多大范围"） |
| 探针 HUD | 光标处显示四维强度、门槛、当前地块 dominant、**该点会通过哪些建筑准入（前 5 行）** —— 排查"为什么这儿放了工厂"的唯一手段（R-02 缓解） |
| 类型配色 | 金融=蓝、工业=洋红、住宅=绿、农业=土黄（全工程唯一配色表，写在 05 并在 07 引用） |

### 4.4 面板（`LandUseFieldEditor`，建在场景实例 Inspector）
遵循 §0.1 原则1：面板在**场景对象的 Inspector** 上，不要求用户跳去资产文件。
分组：`标识(stableId 只读/type)`｜`几何(inner/outer + 迷你剖面图)`｜`强度(inner/outer value + 权重)`｜`曲线(共享 or 内联)`｜`高度带(默认隐藏)`。
**迷你剖面图**：横轴半径、纵轴 0~100 的实时曲线预览（内平台 + 过渡 + 外截止一眼看清）——这个 20 行的自绘控件对可用性贡献极大。

## 5. 数据与持久化
| 数据 | 载体 | 入 git | 撤销 | 备注 |
|---|---|---|---|---|
| 场域实例（全部参数） | 场景内 `LandUseField` 组件 | ✅ 随场景 | ✅ 对象级 + 批量合并 | `stableId` 序列化字段 |
| 预设 `FieldPreset` | SO | ✅ | ✅ | 只影响新建，不影响已放场域 |
| 共享曲线 `FalloffCurveAsset` | SO | ✅ | ✅ | 多处引用（Model 层） |
| `LandUseGrid` / `Accessibility` / `WaterProximity` | ❌ 内存 | — | — | 调试导出 `.tmp-inspect/usagew.png` |
| parcel 的 `dominant/landUse` | ❌（04 派生字段） | — | — | 场域改动只刷这两个字段（D-04-6） |

**外部消费契约**（core `ILandUseSampler`，总纲 §6.3）：
`float4 Sample(world)`、`int Threshold(world, type)`、`LandUseType Dominant(parcel)`；
变化广播 `LandUseFieldChangedChannel`（携带受影响 bounds），让后续美化/植被包跟随。

## 6. 模块内决策
| 编号 | 决策 | 备选 | 理由 |
|---|---|---|---|
| D-05-1 | 场域是场景组件 + `stableId`（Q-1 双参数定稿） | SO 列表 | 用户心智=摆在地上；原生 undo；跨包可发现；id 稳定供 diff |
| D-05-2 | 求值 = 内平台 + 曲线过渡 + 外截止；曲线口径全工程唯一 | 单一高斯核 | 内平台才能表达"绝对区"（用户的 ≥10 用例依赖平台语义） |
| D-05-3 | 跨场域默认 Max；权重为乘性增益且截断 100 | Add | Add 会顶满导致门槛失效（R-02） |
| D-05-4 | 共享曲线仅在"≥2 处复用"时成立 | 全部共享 | AGENTS §0.1 原则2 判据 |
| D-05-5 | `Accessibility/WaterProximity` 为派生场，不进场域类型 | 加 `type=Water` | 意图 vs 事实必须分层（D-02-4） |
| D-05-6 | 拖拽期只烘脏环 + overlay，禁碰 GameObject | 每帧全量重算 | 跟手且不假死（00 D-00-4） |
| D-05-7 | 探针 HUD 显示"该点将通过哪些建筑准入" | 只显数值 | 门槛+加权随机的组合结果不可直觉，必须可解释 |

## 7. 验收
| # | 事实 | 证据 |
|---|---|---|
| A-5-1 | 内平台：`r ≤ innerR` 处 `v == innerValue`（四舍五入容差 0.5）；`r > outerR` 处 `v == 0` | 半径扫描采样断言 |
| A-5-2 | 曲线单调性：`EaseIn/EaseOut/Linear/Step` 四种预设下 `v(r)` 单调不增 | 断言 |
| A-5-3 | **A-3 用例的地基**：`type=Finance, innerRadius=10m, innerValue=10` ⇒ 内径内 `Threshold=10`；半径/数值独立改动互不影响对方语义 | 面板设值 + `Threshold()` 断言 |
| A-5-4 | 拖动场域：外径范围外的格四通道逐位不变（A-2） | 前后哈希比对 |
| A-5-5 | 拖动场域 100 帧内无 `Instantiate/Destroy` 调用；松手后才有 | Profiler |
| A-5-6 | 改共享曲线：所有 `useSharedCurve=true` 的场域同步刷新；内联者不变 | 双场域用例 |
| A-5-7 | 手柄越界（inner≥outer）被阻止并提示；批量改 = 1 条 undo | 交互走查 |
| A-5-8 | `enabled=false` 的场域不参与烘焙（结果与删除它一致，但数据保留） | 前后 grid 比对 |
| A-5-9 | 探针 HUD 的门槛与 `Threshold()` 返回值一致（UI 与内核同真源） | 手动核对 3 处 |

## 8. 风险
- **权重 × Max 的语义含混**（用户以为权重能超 100）→ tooltip + 面板显示"有效上限"。
- 场域数量多（>100）时脏环并集覆盖全城，Preview 退化 → 缓解：>50 个场域时 Preview 只烘"被拖动的那个"的圆环，Commit 才并集；面板显示场域数与预估耗时。
- `stableId` 若被复制粘贴复用（`Ctrl+D` 必须重新生成）会导致 diff 错乱 → 复制钩子强制重发 id，并有校验工具扫描重复。
- 高度带（`minY/maxY`）打开后与山坡交互复杂（R-08/Q-9）→ 首版 UI 隐藏该组，仅保留字段与序列化能力（前向兼容）。
