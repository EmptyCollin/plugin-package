# Modules/02 — 河流：笔刷、剖面下切、自动水系、下游耦合

> 依赖：Modules/00、01（高度重放次序）。消费方：03（桥）、04（水缓冲边界）、05（临水场）、07（临水偏好）。
> 对应里程碑：M3。状态：草案。

## 1. 需求与定位
用户要"河流笔刷"。真实需求拆开是三件事：**画得出（交互）、下得去（与地形闭环）、断不了（水系拓扑正确）**。
其中第三件事最容易被忽略——画完不检查连通性，就会出现"悬在坡上的半截河"，且它会连锁毁掉地块与桥的生成。

三种模式（默认 A，用户可在面板切 B/C）：
| 模式 | 谁主导 | 算法核心 |
|---|---|---|
| **A 人工主导** | 画的线就是河 | 剖面下切 + 沿线洼地连通修补 |
| **B 程序主导** | 只给源头与出口方向 | D8 流向 + 汇流累积阈值提取 |
| **C 混合（推荐）** | 人工主干 + 自动支流 | A 结果作为先验，B 在两侧生长支流并强制汇入 |

## 2. 数据模型

### 2.1 Model：`RiverProfile`（SO）
| 组 | 字段 | 默认 | 语义 |
|---|---|---|---|
| 剖面 | `bedWidth` | 14m | 河床平底宽 |
| | `depth` | 4m | 河床相对两岸地面的下切深度 |
| | `bankSlope` | 1.8 | 边坡（水平/垂直），>1 为缓岸 |
| | `shoulderWidth` | 6m | 岸顶过渡带（下切到自然地形的缓冲） |
| | `profileCurve` | EaseIn | 横剖面形状（V 形/U 形） |
| 笔刷 | `minSpacing` | 2m | 折点最小间距（重采样目标） |
| | `smoothPasses` | 2 | Chaikin 平滑次数 |
| | `snapToValley` | 0.35 | 吸附谷地强度（0=纯手画 1=强吸附） |
| | `autoTaper` | true | 上游自动收窄（宽度随汇流等级变化） |
| 自动 | `accumulationThreshold` | 220 格 | B/C 模式的"成河"阈值 |
| | `tributaryMaxOrder` | 2 | 支流最大阶 |
| 缓冲 | `waterBuffer` | 10m | 河道两侧不可建筑带 |
| | `floodExtend` | 0m | 汛期扩展（预留，首版不实现） |

### 2.2 人工层：`RiverNetworkAsset`（SO，持久化）
```csharp
struct RiverStroke { string strokeId;           // 稳定 id（adopt 后不变）
                    Vector2[] controlPoints;    // 原始手画折点（不存平滑结果！）
                    float widthScale;           // 实例级覆盖（÷Model，§0.1 原则2）
                    int order;                  // 汇流阶（派生写入，可缓存）
                    RiverRole role;             // Manual / Adopted / Auto
                    bool isTidal; }             // 是否接外水（出口）
```
**存原始点线、派生结果每次重算**（D-15 同路网）：平滑、重采样、下切剖面全是确定性函数，
落盘只会带来"真源与派生不一致"的风险，且改 `smoothPasses` 后旧结果无法刷新。
`RiverNetworkAsset` 与 01 的 delta 一样是 Additive 层：参与高度重放次序第 2 步。

### 2.3 派生层（内存）
```csharp
class RiverField {
  float[] channelDist;      // 每格到水道中心距离（Flood Fill，两遍扫描）
  byte[]  streamOrder;      // Strahler 阶（决定宽度与是否可被路跨越的方式）
  byte[]  waterMask;        // 水面格（00 §2.4 的 Water 位来源）
  Polygon[] riverPolygon;   // 面状水系（分叉已合并）→ 04 用
}
```

## 3. 技术路线

### 3.1 笔画处理流水
```
原始折点 → 重采样(≥minSpacing) → Chaikin 平滑 ×N → 谷地吸附 → 生成中心线 polyline → 剖面下切
```
**谷地吸附**（让手绘的河"像本来就在那"）：
对每个折点取半径 `snapRadius=8m` 内的最低点 `low`，`p' = lerp(p, low, snapToValley · w)`；
反复做 2 遍（第二遍半径减半，避免把河拉到别的水道里去）。吸附强度 0 时完全手动画。

### 3.2 剖面下切（写入高度重放的第 2 步）
对中心线每个采样点 p（沿线步长 = cellSize）：
```
d = 到中心线的距离（用格到线段的精确距离，非"格中心距离"近似，否则宽河边缘呈锯齿）
d ≤ bedWidth/2              → target = groundAt(p) − depth                    // 河床
bedWidth/2 < d ≤ outer      → target = lerp(河床, 自然高, EvalFalloff(profileCurve, t))   // 边坡+shoulder
d > outer                   → 不改动
```
- `groundAt(p)` 取**下切前**的基底 + delta（第 1、3 步的中间值）。
- 写入的是**下切量 delta**（`target − h_base`），与 01 的 Additive 语义一致；不直接改 delta 资产（两套人工层必须分开文件，否则撤销互相污染），而是 S1 重放时叠加。
- **宽度随 Strahler 阶**：`effectiveWidth = bedWidth · 0.75^max(0, orderRef − order) · widthScale`（上游窄、下游宽）；
  下游变宽让"河口"自然成为城市形态的锚点（港口、桥位密度）。
- **不能倒流**：下切完成后做一次"沿线单调下降"检查（§3.4），不满足时抬升或开挖局部（修平）而不是报错。

### 3.3 水面与岸线（View）
- 水面 = `riverPolygon` 三角化后的条带 mesh，顶点在 `waterLevel = 河床高 + waterFreeboard(默认 depth·0.55)`；
  首版水面用纯色/简单 shader（不引外部包，见 01 §3.3）。
- `riverPolygon` 生成：每条 polyline 按 `effectiveWidth` 偏移出左右岸 → 半平面交裁剪 → 同阶/父子水道做多边形 union（网格求交，`Clipper` 式算法自带实现，不引第三方）。

### 3.4 水系拓扑与"无死胡同"（核心正确性）
D8 处理链（B/C 模式与 A 模式的修补都复用）：
```
1 填洼（Planchon-Darboux 或优先级队列填洼）→ 无坑高度场
2 流向：每格 D8 指向最低下坡邻居（走第 1 步结果）
3 累积：acc = 1 + Σ(上游 acc)，按拓扑序累加（BFS from pits）
4 提取：acc ≥ threshold → stream
5 连通：每条 stream 必须满足「入水（湖/海/边界） 或 汇入更高阶 stream」
        否则沿流向继续强制开挖到满足 → A-6 验收依据
6 Strahler 阶：叶=1，两同阶汇入=阶+1
```
- A 模式也要跑 5：手绘河流若终点在坡上，第 5 步沿 D8 流向自动补下游到边界/汇入，
  这条自动补的段标 `role=Auto`，面板用虚线显示，用户可右键「接受/删掉」。
- **人工优先**：若 A 与 B 提取结果冲突，人工笔画所在格强制为 stream（人工覆盖自动），
  并把它下游的阈值临时降到 1（保证这条河连续）。

### 3.5 湖泊与河口（低成本扩展，避免"所有水都是细长条"）
- `LakeStamp`：同一笔刷按住 `Alt` 画闭合圈 → 湖 polygon，`depth` 按下切、边界=岸线；
- 湖与河相交 → 并入同一 `riverPolygon` union，河在湖边即终止（`isTidal=true`），下游检查通过。
不做真实水文：湖的"水面高度"取湖盆最低溢出口，算不出则用 `waterFreeboard`（面板标注"非物理"）。

## 4. 下游耦合（最容易漏，必须做）
| 消费方 | 规则 | 接口 |
|---|---|---|
| 03 路网 | 道路与 `riverPolygon` 相交 ⇒ 必须插桥/涵洞；桥面 ≥ `waterLevel + bridgeClearance`（默认 5m）；桥位优先落在既有路口附近 | `TryGetCrossings(roadSegment)` |
| 03 路网 | 禁止路段与中心线夹角 <20° 且距离 <`waterBuffer`（贴河"伪平行路"极难看） | `IsTooParallelToRiver()` |
| 04 地块 | `waterBuffer` 为不可建带；河道边缘**直接作为地块边界**（自然形成临水岸线，是城市形态可信度的关键） | 掩码 `WaterBuffer` + polygon 参与细分 |
| 05 场域 | 提供 `WaterProximity` 派生场（距离衰减）；住宅/金融通常正相关、工业负相关 | 场域可新增 `type=Water`？见 §7 D-02-4 |
| 07 建筑 | 每行 `waterPreference` 参与打分；湖/河首排建筑禁止背对水（朝向=朝心线法线反向） | `slot.frontEdgeKind=Waterfront` |
| 01 地形 | 下切层（§3.2）；`snapToValley` 需读第 1 步高度 | 重放次序第 2 步 |
| 洪水（预留） | `floodExtend` 扩展掩码位 `FloodRisk`，首版不实现，留字段与决策位 | — |

## 5. 操作与交互（`RiverBrushTool`，EditorTool）

### 5.1 画河
| 输入 | 行为 | 呈现 |
|---|---|---|
| 左键按下并拖动 | 连续记录折点（间距 ≥`minSpacing` 才加） | 实时中心线 + 半透明河带（宽度预览） |
| 松开 | **Preview→Commit**：跑 §3.1~3.4 + 下切 → 标脏 S1a(可选)/S3/S4/S5/S6 | 河床真正成型、地形刷新 |
| `Alt` + 拖动 | 画闭合湖 | 湖盆预览 |
| 单击已有点 | 进入"点编辑"，拖点 | 点 handle + 邻段重算 |
| 双击中心线 | 插入点 | — |
| 选中线按 `Del` | 删除该 stroke（并 Undo） | — |
| 右键 stroke | 菜单：设为主干/改宽度/接受自动段/转为人工（adopt） | — |

### 5.2 实时性与性能约束
- 拖动中**只更新中心线与河带 overlay**（不重算地形）：地形下切重放每帧跑会在宽河上掉帧；
  但 `snapToValley>0` 时吸附必须实时（否则落笔位置和所见不符），吸附 O(折点×9格)，够快。
- 松手才 Commit 下切 + 拓扑检查（几百 ms 可接受）；Commit 前若拓扑检查失败，**先弹提示再询问是否自动补下游**，
  不静默改动用户画的线（改用户的线必须可见、可撤销）。
- 撤销：一笔划 = 1 条 undo（01 §5.2 同规）；`RiverNetworkAsset` 为资产，撤销走 `Undo.RecordObject` + SetDirty。

### 5.3 模式切换
B/C 模式：面板「自动水系」区给 `accumulationThreshold` 滑杆 + 「预览候选支流」按钮。
阈值滑杆拖动 = 实时显示候选支流（只算 acc，不建资产，秒级）；
点「采纳」才把选中的支流转为 `role=Auto` 的 stroke（可再转 Adopted 变人工）。
**阈值不能直接写进下切**（重算代价高、且滑杆每步都会改全城）——这是唯一一处允许"预览→采纳"两步的例外，面板需注明原因。

## 6. 数据编辑与持久化
| 数据 | 载体 | 入 git | 撤销 | 备注 |
|---|---|---|---|---|
| `RiverProfile` | SO | ✅ | ✅ | 剖面/笔刷/阈值/缓冲 |
| 笔画（原始点线） | `RiverNetworkAsset` | ✅ | 笔划级 | `strokeId` 稳定 |
| 平滑/重采样结果 | ❌ 派生 | — | — | 改 `smoothPasses` 立即全量重算 |
| 下切量 | ❌ 派生（重放第 2 步） | — | — | 与 01 delta **分文件** |
| `channelDist/streamOrder/riverPolygon` | ❌ 内存 | — | — | 调试可导出 `.tmp-inspect/` |
| 水面 mesh | ❌ 派生 | — | — | 可选烘焙为静态 mesh（P1，性能兜底） |

## 7. 模块内决策
| 编号 | 决策 | 备选 | 理由 |
|---|---|---|---|
| D-02-1 | 只存原始点线，派生全重算 | 存平滑结果 | 改剖面/平滑参数能立即生效；消除真源与派生不一致 |
| D-02-2 | 下切作为 Additive 层参与固定重放次序，且与雕刻 delta 分文件 | 直接改 delta | 人工层互相隔离，撤销与版本迁移各自独立 |
| D-02-3 | 三种模式共用 D8 链，人工笔画作为先验强制成河 | A/B 两套实现 | 一份拓扑正确性代码；人工优先避免"画的河被算法判定不成河" |
| D-02-4 | `WaterProximity` 作为**派生场**而非场域类型 | 新增 `type=Water` | 场域表达"用户意图"，派生场表达"客观事实"，混一起会让用户以为能用笔刷造湖；临水偏好在建筑行配置（07） |
| D-02-5 | 自动补下游必须经用户确认（可撤销），不静默改线 | 自动静默补 | 改动用户输入是高风险动作 |
| D-02-6 | 宽度随 Strahler 阶自动变窄 | 全河等宽 | 上游等宽是最明显的"假河"特征 |
| D-02-7 | 拖动中只算吸附与 overlay，松手才下切 | 每帧下切 | 交互不掉帧；Commit 语义一致 |

## 8. 验收
| # | 事实 | 证据 |
|---|---|---|
| A-2-1 | 手绘河流终点在坡上时，拓扑检查报告"需补下游"，接受后并查集校验 0 死胡同（A-6 落地） | 自动化脚本：造断头河 → 接受 → 校验 |
| A-2-2 | 改 `depth`/`bankSlope`/`profileCurve` 后，河床剖面逐点符合公式（容差 0.02m），岸外 `shoulderWidth` 之外高度未变 | 剖面采样断言（支撑 A-2 局部性） |
| A-2-3 | 改 `smoothPasses` 从 1→3，笔画原始点线不变（资产逐字节对比只改版本字段） | 资产 diff |
| A-2-4 | 道路与河交点 100% 生成桥/涵洞（03 A-5 联合）；桥面 ≥ `waterLevel+bridgeClearance` | 交叉清单 |
| A-2-5 | `waterBuffer` 内无任何建筑、无任何 slot | 空间查询全量统计 |
| A-2-6 | B 模式阈值滑杆拖动时不产生 GameObject 与资产写盘 | Profiler + 资产 mtime |
| A-2-7 | 河宽随阶单调变化：order=1 处宽度 < order=4 处宽度（同 widthScale） | 宽度采样对比 |
| A-2-8 | 一笔画 = 1 条 undo；Ctrl+Z 后 stroke 与地形 delta 同时回滚 | undo 走查 |

## 9. 风险
- **多边形 union 是工作量黑洞**（河-河、河-湖、河与道路红线求交）→ 缓解：首版用"格网栅格化 + 距离场"近似表达水系面（与 00 §2.4 掩码同源），只在需要光滑岸线处做 marching-squares 轮廓提取；矢量 union 留 P1。
- 填洼与 D8 在人工下切后的地形上跑，可能把人工河"抢走"（改向到更低的自然谷）→ 缓解：人工 stroke 覆盖格强制为 stream 且冻结其 acc（§3.4 末）。
- 河与城市正交网格的"斜切"观感：细窄河斜切地块会产生极钝角地块 → 缓解：04 中 `riverPolygon` 边作为**不可分裂边界**（细分时沿河边界优先平行）。
