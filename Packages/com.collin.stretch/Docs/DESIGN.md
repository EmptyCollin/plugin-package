# DESIGN — com.collin.stretch

> 状态：草案待用户确认（DESIGN 先于代码，§4）。首个 feature 包，依赖 com.collin.core。

## 1. 目标与非目标
- 解决什么问题：对 3D 模型沿单轴做**分段复制式拉伸**——一栋 3 层楼通过复制其中一段变成 10 层楼，一座桥通过复制桥面段变长。与 spline 放样同类但轻量：不需要路径、不需要截面扫掠，只在编辑器面板选轴、划段、填倍数。
- 明确不做什么：
  - 不做非线性/弯曲拉伸（只沿单一世界轴）；
  - 不做网格焊接/法线重算接缝优化（v0.1 保持接缝原样，见风险 R-2）；
  - 不做运行时交互式手柄（v0.1 走 Inspector 数值 + 场景预览）。

## 2. 核心语义（通用化：任意多面体、任意边界位置）
- 沿选择轴把网格包围盒划成若干 **slice（切片边界）**，相邻切片之间是 **segment（段）**；
- 不跨边界的三角形整三角形刚性归段；**跨边界的三角形被边界裁剪分片**（Sutherland–Hodgman 半空间裁剪）：
  每片刚性归入所在段，位置/UV/法线/切线/色在切口线性插值——每片都是原几何的精确子集，
  **算法对任意网格、任意边界位置成立**，模型无需迁就工具；
- 每段标记 `fixed` 或 `repeatable`：
  - fixed 段：随下方延展整体刚性平移（楼基、楼顶永远不变形）；
  - repeatable 段：以该段原始跨度为周期**首尾相接复制** repeat 份（3 层一段复制 7 次 = 10 层）；
- **贴图不同步变形**（用户硬要求，杜绝巨型窗户）：每份复制体携带原位置插值后的 UV，
  被横切的窗户两半各自随段移动、在复制体中拼回原窗；窗户/层高/栏杆在任何倍数下都是标准尺寸；
- **碰撞跟随视觉**（用户要求）：重建后自动同步同物体上的 BoxCollider（尺寸/中心）与 MeshCollider（结果网格）；
  Rigidbody 在运行期重置质心/惯性张量（物理量编辑期无模拟意义）；Restore 全部还原；
- **辅助线**：Scene 视图按 slice 画分界线；repeatable 段画**周期等分虚线**（3 层楼的段里 3 条线区分 3 层），可开关。

## 3. 约束
- Unity 6000.6.7f1；Runtime asmdef 引用 com.collin.core，Editor asmdef 再加 com.collin.stretch；面板用 IMGUI（CustomEditor）；validateReferences=true（§5）；
- 输入网格：Readable 且含 UV0；FBX 需先开 Read/Write（README 级说明；工具检测到不可读时**只给日志提示 +
  独立菜单命令** `开启选中模型的 Read/Write`，不再弹窗询问——§2.1.5 禁阻塞主线程）；
- 拉伸轴语义 = 网格对象**本地空间**单轴（物体旋转不影响分段计算）；
- 不新增外部包依赖。

## 4. 方案概述（程序集 / 数据流）
| 组件 | 程序集 | 职责 |
|---|---|---|
| `StretchProfile` | Runtime | **共享结构 Model**（ScriptableObject）：轴、局部边界、每段类型；纯计算 API（位移/总长因子/结构校验）——不含倍数（实例状态，§0.1 原则2） |
| `StretchInstance` | Runtime | **实例半边**（场景组件，序列化）：引用 Profile + 每段倍数 `repeats[]`；[ExecuteAlways] 编辑期即时重建并同步碰撞体（Box/MeshCollider、Rigidbody 运行期重置），Play 态同一条构建路径（原则1/2/3） |
| `StretchMeshBuilder` | Runtime | 消费 Profile + 源 Mesh + 实例倍数 → 结果 Mesh：跨边界三角形 SH 半空间裁剪分片、逐片刚性归段，UV/法线等切口插值；**逐子网格处理并保持 subMeshCount 与材质槽对应**（FBX 多材质模型必需）；>65535 自动 uint32；倍数数组长度校验 |
| `StretchInstanceEditor` | Editor | **主面板**（建在场景实例 Inspector）：轴、段类型/边界（折叠复用 StretchStructUI）、每段倍数即时预览；OnSceneGUI 画辅助线（唯一入口） |
| `StretchProfileEditor` | Editor | 资产面板：只读结构摘要 + 定位引用实例（不设第二套编辑 UI，§0.1 原则1）；`StretchBakeCommand` 按实例倍数烘焙为 Profile 子资产 |

数据流：选中模型 → 菜单"新建拉伸参数"生成共享 Profile 并挂 StretchInstance → 该实例 Inspector 直接编辑轴/边界/段类型（写 Profile）与倍数（写实例）→ 改动即时重建预览 → 需交付静态网格时"烘焙"把当前实例倍数固化为 Profile 子资产。

## 5. 决策记录
| 编号 | 决策 | 备选 | 理由 |
|---|---|---|---|
| D-1 | 结构（轴/边界/段类型）存 Profile 共享；**倍数存实例组件序列化字段** | 全部塞进 Profile（v0.1 旧法） | v0.1 把倍数放 Profile 违反 §0.1 原则2——同一 Profile 复用于多楼时无法各自不同倍数。0.2.0 修正，验收 V1 证明同一资产 ×3/×20 独立生效 |
| D-2 | 编辑器面板即时编辑 + **内存预览网格**，定稿才落盘 | 按钮生成资产 | 迭代快；磁盘资产不脏 |
| D-3 | **边界裁剪**：跨边界三角形按 SH 半空间裁剪分片，逐片刚性归段平移/复制，属性切口插值；不跨界三角形走整三角形快速路径 | 重心归段（0.1.x 旧法）；逐顶点位移 | 旧法要求边界落在几何空白带——等于让模型迁就工具（用户指出本末倒置）。裁剪让任意边界位置确定性成立，被切的窗户在复制体中拼回原样（验收 G1/G4）；快速路径保证旧用例逐顶点数不退化（R1/R2） |
| D-4 | 倍数走面板整数输入；边界支持**面板数值输入 + Scene 拖拽把手**（0.3.2 落地），越界一律 NormalizeOrder 自动重排 | 只允许夹在相邻间的数值输入 | 用户反馈"夹死式 Clamp"表现为改了被重置；重排语义让任意输入/拖拽都成立，段类型与 repeats 按映射跟随 |
| D-5 | 主面板建在场景实例 Inspector（`StretchInstanceEditor`），资产面板降为只读+定位 | 用户在资产文件里编辑 | §0.1 原则1 所见即所得；避免两套编辑真源与辅助线重复绘制 |
| D-6 | 碰撞/物理同步内建重建路径（Box/MeshCollider 即时、Rigidbody 运行期、Restore 还原） | 用户手动调碰撞 | 拉伸对象碰撞必须与视觉同尺寸（用户要求）；编辑/运行同一条构建路径（原则3） |

## 6. 风险
- R-1（0.3.0 已由裁剪消除）：旧"跨界三角形被拉长"风险不复存在——切口插值保证每片是原几何精确子集（G1 验收：窗户正中切穿 ×3 复制 UV 仍全在 [0,1]）。边界落在几何空白带仍是最优选（接缝最干净），但不再是正确性前提。
- R-2：复制接缝处几何/光照不连续（顶点不焊接）→ 视觉可能可见硬边。接受（轻量定位），后续版本再评估焊接。
- R-3：超大网格 × 高倍数内存膨胀 → 结果顶点数超 65535 需 uint32 索引（Mesh.indexFormat），Builder 自动切换。
- R-4：裁剪分片使跨界三角形顶点数增加（每片独立顶点）→ 仅在边界切中几何时发生，规模可控（G1: 24→96 顶点级）。
- R-5：真实 DCC 模型（多材质子网格、非单位缩放、pivot 居中、readable=false）会放大实现盲区 → 缓解：示例已改用 CC0 真实资产（见 §7 资产来源），Builder 逐子网格处理，**独立菜单命令**开启 Read/Write（0.4.4 起不再弹窗询问，§2.1.5），面板端点对齐只读源网格。

## 7. 资产来源
示例模型的第三方资产登记于 `Sample/Models/CREDITS.md`（Poly Haven modular_wooden_pier，CC0）。
体验场景保持至少一个真实 DCC 资产（多材质子网格/非单位缩放/pivot 居中），作为通用性的回归标本。

## 8. 性能与规模路线（计划，未实施）

### 8.1 压力账（10⁴ 实例、份数外部驱动）
当前实现=每实例独占一份重建网格，实测参数（桥 32k 源顶点）下三个瓶颈：
- **内存**：每 unique 参数组合一份网格（顶点 ~50B/个 + CPU/GPU 双侧）；一万实例若参数不重复即万份网格，GB 级；
- **批处理**：per-object unique mesh 杀死 GPU Instancing 与 static batching；draw/剔除开销 = 实例数 × subMeshCount；
- **CPU**：`Build()` O(V) 主线程，高频外部改份数 = 每实例每次全量重建；编辑期 [ExecuteAlways] 全场重建直接失能。
另记：MeshCollider 同规模下与网格同数量级开销；影子/材质实例是次级放大器。

### 8.2 路线（按杠杆排序；P3 为目标形态，P1/P2 先行兑现）
- **P1 MeshCache（0.4.0 已实施）**：`StretchMeshBuilder` 输出经静态缓存 `(源网格, profile, repeats)` 键共享
  （引用计数 + LRU 逐出；域重载清空重建）。参数组合远少于实例数时内存骤降，且同键对象自动恢复
  GPU Instancing。语义不变，`StretchInstance.Rebuild()` 内部改造，外部 API 不动。
- **P2 Bake & Static（0.4.0 已实施，面板一键烘焙）**：份数不变的展示/城市场景 → 烘焙子资产 + 普通 MeshRenderer
  （可吃 static batching / 光照贴图），不挂组件。`StretchBakeCommand` 已支撑，写入 README 级流程。
- **P3 分块实例化（0.4.0 已实施基础版，见 8.4 限制）**：利用 `R(x)` 段内刚体平移的性质——复制份不需要复制顶点：
  Builder 新增"只切不合"输出（每段块 × 材质槽一个 mesh）；运行时每实例把块摆成
  `segOffset + k×段长` 的变换阵列，经 `Graphics.DrawMeshInstanced(Indirect)`/BRG（Unity 6 BatchRendererGroup，
  或 URP DOTS Instancing）成组提交。份数即每实例矩阵，天然承接"外部驱动"。
  draw ≈ 块数×材质组数，显存与实例数无关。已知坑：Indirect/BRG 路径的阴影与深度需自行提交、
  视锥剔除自建（BRG 可托管）；接缝归属与现裁剪一致，无新正确性问题。
- **P4 调度与降级（0.4.0 已实施）**：
  - 中央 `StretchManager` 脏队列 + 每帧重建预算（P1 期即可先做缓存命中率统计）；
    份数数组生成逻辑可 Job 化（纯数组写）；
  - LOD：远距用低份数/删面代理——`repeats` 距离衰减是预算策略，API 已支持（外部传值即可），语义归游戏层；
  - 物理：大规模推荐 Box/简单碰撞（`syncMeshCollider` 默认关的文档指导）；
  - 编辑态：仅选中实例保持 ExecuteAlways 重建，未选显示源网格（所见即所得对"正在编辑的对象"兑现）；
  - 纪律：禁止 per-object Material 实例（会炸掉刚救回的合批）；逐实例差异走 transform/BRG 属性，
    MaterialPropertyBlock 慎用（破坏 SRP Batcher）。

### 8.4 0.4.0 已知限制（如实记录）
- `Graphics.DrawMeshInstanced` 基础提交：不投影/不接收阴影、不进深度法线预通道（依赖它的效果如 SSDecal/部分雾会缺这些物体）、无 LODGroup/反射探针联动；
  升级为 BRG（BatchRendererGroup）可全部补齐——列为 P3.1，待实测规模需求再做；
- Chunked 模式下绑定材质会被自动开启 GPU Instancing（对普通渲染无副作用；退出 Play 还原运行期开关）；
- 同一 `StretchChunkManager` 绑定的实例必须共享同一 Profile 与材质数组（材质不一致者被跳过提交）；份数不同的实例可共提交器（矩阵各自算）；
- 编辑期 Chunked 预览每帧提交（Update, ExecuteAlways），万级规模的编辑期开销本身建议仅在预览选中对象上使用。

### 8.3 与数据架构的关系
Profile（共享结构）/ Instance（份数）契约在 P1–P4 全程不变：P1 缓存其产物、P3 换其渲染器。
§0.1 原则2 的分离是本路线不需破契约的前提。规模假设变更（如实例数上限、份数变化频率）须更新 8.1 的账并重排优先级。

