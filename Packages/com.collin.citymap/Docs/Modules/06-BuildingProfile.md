# Modules/06 — 建筑配置表（BuildingProfile）与 stretch 桥接

> 依赖：00（服务消费）、core（`IStretchService` 接口，ADR-0001）。消费方：07（唯一消费者）。
> 对应里程碑：M7（表）、M9（stretch 桥接）。状态：草案。

## 1. 职责
定义"**这城市能盖哪些楼，每栋楼是什么性格，能不能拉长**"。
这是 C4 程序化建筑的唯一输入：表本身即规则的一部分（四维值决定准入与打分），不是被算法忽略的资产清单。

## 2. 行结构（用户点名的 6 项必填，其余可选）

### 2.1 必填组
| 字段 | 类型 | 语义 | 校验 |
|---|---|---|---|
| `prefabPath` | string | 预制件**路径**（用户明确要求以路径描述） | 空/不存在 → 行标红，生成时跳过并计数 |
| `stretchable` | bool | **是否支持几何拉伸**（用户明确要求） | false ⇒ 几何与缩放恒等（A-9） |
| `finance` | int 0~100 | 金融值 | 四维全 0 → 黄标（该行永远不参与主导维度准入，但仍可作陪衬） |
| `industrial` | int 0~100 | 工业值 | 同上 |
| `residential` | int 0~100 | 住宅值 | 同上 |
| `agricultural` | int 0~100 | 农业值 | 同上 |

四维值与场域 `innerValue/outerValue` **同标度、整数**（D-02）：07 的准入就是 `B.v_D ≥ Threshold(cell)` 的整数比较，无归一化歧义。
四维**不要求互斥、不要求和为 100**：这是用户 Q-2 裁决的关键前提——
"生活类但金融值足够高的咖啡馆"（`residential=45, finance=70`）必须能在金融区被合格选中。

### 2.2 可选组（全部有默认值，保证"5 分钟填表能出城"）
| 字段 | 默认 | 语义 |
|---|---|---|
| `prefabRef` | null | 项目内强引用；有则优先用它实例化（`prefabPath` 仍保留用于导出/外部资产库） |
| `footprintW/D` | 0=自动测量 | 占地宽×深；**默认由编辑器测量 prefab 包围盒**（D-06），覆盖仅用于"逻辑占地比视觉小"的情形 |
| `heightMin/heightMax` | 自动 / ×2.5 | 可拉伸行的目标高度区间（米）；`stretchable=false` 时 `max==min==原始高` |
| `stretchAxis` | `Y` | 拉伸轴（`Y/X/Z`），与 stretch 段模型的"单轴"一致（06 §4.2） |
| `preferredGrade` | Local | 偏好临街等级（07 打分项，不是硬过滤） |
| `waterPreference` | 0 | −1~1 临水偏好（07 打分项） |
| `minParcelArea` | 0 | 该楼要求的最小地块（把"摩天楼不该在碎地块"变成数据而非代码） |
| `tags` | 空 | 分组标签；供街区模板/抽样权重（P1），不影响准入 |
| `weightBias` | 1.0 | 抽样权重乘子（想让某类楼更常见，改这里而不是改四维值） |
| `note` | — | 备注列（团队协作时用） |

## 3. 表资产与编辑器

### 3.1 `BuildingProfile`（SO）
```csharp
public sealed class BuildingProfile : ScriptableObject {
    [SerializeField] int schemaVersion;
    [SerializeField] List<BuildingRow> rows;
    [SerializeField] Tuning tuning;             // λ 系数组（07 §3.4）——放在表里而非代码（R-09）
    [SerializeField] TuningPreset preset;       // Tidy / Organic / Chaotic 三档
    // 派生缓存（不序列化）：按维度的排序索引、四维 argmax 表、总量统计
}
```
`dominantType` **不存字段**（派生只读：argmax of 四维）——存了就有"改了四维忘了改类型"的双真源风险。

### 3.2 表格编辑器（`BuildingProfileEditor`，EditorWindow/CustomEditor）
| 功能 | 说明 |
|---|---|
| 拖文件夹批量建行 | 从 Project 窗口拖 prefab/文件夹 → 自动填 `prefabPath/prefabRef` + 测量 footprint 与原始高 |
| 内联编辑 | 路径（带对象选择框 + 存在性图标）、四维（数字框 + 滑杆二选一显示）、bool |
| **四维可视化** | 每行右侧堆叠迷你条（4 色，复用 05 的类型配色）；选中行的雷达小图 |
| 排序/筛选 | 按任一维度排序、按"主导类型"筛选、按路径前缀筛选（表几百行时必须能筛） |
| 校验面板 | 路径不存在 / 四维全 0 / `heightMax<heightMin` / 重复行（同路径）/ **场景存在引用已删行的实例**（H-15，可一键转 orphan）→ 分级列出，**阻断生成但不阻断编辑** |
| 统计摘要 | 各主导类型的行数与占比（一眼看出"这张表 90% 是住宅，难怪城里全是住宅"）；四维分布直方图 |
| 表导入/导出 | CSV 导出/导入（策划批量改值最省事的通道，字段名与列头固定） |
| Undo | 行增删、批量改值各为 1 条 undo；资产保存走 `EditorUtility.SetDirty` |

校验在**保存时**与**生成前**各跑一次（后者阻断并给出"跳到第一处问题"按钮）。

### 3.3 footprint 自动测量的细节（D-06）
```
meshWorldBounds = union(renderers.bounds)                     // 含子物体，考虑旋转与 prefab 缩放
footprintW = size.x, footprintD = size.z                       // 以 prefab 前向 = +Z 为建筑正面
原始高度 = size.y（Y 轴拉伸时用）
缓存 key = (assetPath, assetGUID+localVersionIdentifier)       // prefab 改动后自动重测
```
- 用世界包围盒而非 mesh bounds（prefab 根上常有缩放）；
- 非轴对齐模型（斜向）会量到偏大的 footprint → 面板提供"按 OBB 重测"按钮（用 `Renderer.bounds` 与 `localToWorld` 组合自算），
  这是数据编辑入口之一，不能让用户只能手填；
- `frontVector` 记录 prefab 的"正面"约定（默认 +Z），07 朝向计算依赖它（否则楼会背对街道）。

## 4. stretch 桥接（Q-3 裁决落地，ADR-0001）

### 4.1 为什么不能直接引用
`com.collin.stretch` 是 feature 包，AGENTS §5 明文禁止 feature→feature，校验器 `dep-direction` 会 FAIL；
asmdef 层 `validateReferences:true` 也编不过。而 stretch 本身设计良好（分段复制、UV 不形变、碰撞同步），
重新实现一遍既浪费又会造成"两套拉伸语义"。**用户裁决=就用 stretch**，所以走 §5 自己给出的合法通道：core 接口 + `Services` 注册表。

### 4.2 接口（落在 `com.collin.core`，通用命名，不含 stretch/citymap 专有类型）
```csharp
namespace Collin.Core {
    /// 几何拉伸服务：由具备拉伸能力的包注册实现（当前为 com.collin.stretch）。
    public interface IStretchService {
        bool IsAvailable { get; }
        bool SupportsBatch { get; }                       // 批量能力探测（H-13）
        void BeginBatch();  void EndBatch();              // 提供方内部合并分配/延迟重建（H-13）
        /// 为实例启用拉伸；成功返回 true。失败/不支持返回 false（调用方降级）。
        bool TryBind(GameObject instance, StretchAxis axis);
        void Unbind(GameObject instance);
        int  GetRepeatableSegmentCount(GameObject go);         // 可重复段数（0 = 无法拉伸）
        float GetSegmentSpan(GameObject go, int seg);          // 该段原始跨度（米）
        bool  SetRepeat(GameObject go, int seg, int repeat);   // 整数复制份数（stretch 的段模型语义）
        float GetHeight(GameObject go);                        // 当前总高（米）
        bool  GetPreferredRepeat(GameObject go, float targetHeight, out int repeat); // 换算建议（向上取整）
    }
}
```
- **批量语义是性能必需，不是可选项（Modules/09 H-13）**：07 逐栋落位时调用 `SetRepeat`，9000 栋 = 9000 次
  mesh 重建与顶点数组重分配（stretch 走 SH 裁剪路径），全在 Editor 主线程 Commit 内 → 总纲 §8 的 ≤60s 预算会先炸在这里。
  `BeginBatch/EndBatch` 由提供方合并分配与延迟重建；`SupportsBatch=false` 时 citymap 侧兜底"分帧摊销"（每帧 8ms，同 07 §6）。
  此项属接口扩项，与 R-13 同通道：ADR-0001 定稿签名时一并并入（需用户批准）。
分工理由：
- **换算（目标高度→份数）放在提供方**（`GetPreferredRepeat`）：只有 stretch 知道段的划分与是否允许非整数；
- **策略（该拉到多高）放在 citymap**（07 §3.5）：只有 citymap 知道场域强度与城市形态；
- 这样接口里没有"楼/城市"概念，未来别的包（如桥梁延展、围墙）可复用同一服务（符合"stretch 有独立复用潜力"的定位）。

### 4.3 装配与生命周期（Modules/09 H-11/H-14b 修订版）
```
stretch 侧：[RuntimeInitializeOnLoadMethod(SubsystemRegistration)] → Services.Register<IStretchService>(单例)（幂等守卫）
citymap 侧：**惰性解析**——首次需要拉伸时 Services.TryGet(...)，结果缓存；
            缓存随域重载自然失效（静态字段被重置），**禁止跨域持有、禁止依赖"双方同阶段先后"的顺序假设**
```
- **顺序假设是假的**：不同程序集的初始化回调执行顺序不保证，citymap 若跑在 stretch 前会**永远误判为降级**（R3-H-11）。
  惰性解析 + 面板"拉伸源"实时 `TryGet`（不用缓存值）根治此问题。
- 可用性 false→true 的切换**不追溯重改已有实例**：只影响新实例；面板黄标"降级期间放置 N 栋"并给显式动作
  「按 stretch 重建这些高度」（显式提交，入 journal）。
- `Services.Register` 对同接口重复注册会 `LogError`（保留先注册者）→ 注册必须幂等（静态 bool 守卫），
  否则域重载后第二次注册产生 error **直接违反控制台归零门禁（AGENTS §1.6b）**。写进 M9 实施清单。
- **注册唯一性两条硬检查（H-14b，防并行事故）**：① 全项目 `Register<IStretchService>` **有且仅有一处**（在 stretch 包内）；
  ② citymap **永不注册**该接口、只 `TryGet`——"为了让降级路径跑通而在 citymap 里注册假实现"是并行开发最可能出现的坏法，
  会造成重复注册 error。提交前 grep 断言 + ACCEPTANCE 记证据。

### 4.4 降级路径（缺包/注册失败时）
| 项 | 行为 |
|---|---|
| 几何 | `Transform.localScale` 在 `stretchAxis` 上等比缩放（会拉伸窗户，观感次优） |
| 标记 | 实例上 `CityBuildingTag.approximated = true`；面板黄色角标统计"降级 N 栋" |
| 不静默 | 主面板启动时若 `IStretchService` 不可用，显式提示「未检测到拉伸服务，已降级为等比缩放」 |

### 4.5 两段式合并（Modules/09 H-12 修订版，替代 v3 的笼统"申报"）
本桥接要同时改 **core**（加接口）与 **stretch**（注册实现）。一次性提交三段改动的问题：stretch 可能被其他会话持锁
（**本会话内就观察到它被释放又被重新取得**，故任何"现在有没有锁"的结论都不可复用，开工前一律实时 `verify_project` 复核），
这会让提交要么等锁要么冲突，
失败时回滚面横跨三个包，且改 core 编译红会进 Safe Mode 切断所有 AI 通道（AGENTS §1.6c）。

**拆两段，各自独立可回滚：**
- **段 1（低风险，先合）**：只往 core 新增一个文件（`IStretchService` + `StretchAxis` + 批量成员），
  不改 core 任何既有 API、不碰 stretch/citymap 代码 → 编译风险≈0。合入后 citymap 走降级路径、stretch 无感知
  → **两个包都不被阻塞**。取 core 锁前按 AGENTS §2 申报；回滚 = 删该文件。
- **段 2（待 stretch 空闲再合）**：取 stretch 锁 → 写 `StretchService` 实现与注册 → 取 citymap 锁 → 接上消费与面板。
  失败只回滚两包，core 留着空接口无害（无实现 ⇒ 自动降级，已验证路径）。
- 段 2 久等不到锁：功能不受阻，但 **Showcase 演示视频必须录段 2 完成后的拉伸效果**（否则 A-13 演示的是降级态，属误导）。
- 接口扩项（批量）与语义不匹配同走 R-13 通道：**ADR-0001 定稿时一次并齐，段 1 就带上批量成员**，避免改两次 core。
- 旧替代路线（citymap 侧反射绑定 stretch）**仍作废**：违反 §5 精神，且会与 H-14b 的"注册唯一性"检查冲突。

## 5. 调参组 `Tuning`（07 的权重，全数据化）
放在表资产里（R-09：调参不该改代码），三档预设 + 可自定义：
| 参数 | Tidy | Organic（默认） | Chaotic | 作用（07 §3.4） |
|---|---|---|---|---|
| `β`（softmax 温度） | 14 | 8 | 4 | 越大越"只选最好的"，越小越随机 |
| `λ_cosine` | 1.0 | 1.0 | 1.0 | 四维余弦相似 |
| `λ_frontage` | 0.8 | 0.5 | 0.2 | 临街等级匹配 |
| `λ_water` | 0.3 | 0.5 | 0.4 | 临水偏好 |
| `λ_size` | 0.6 | 0.4 | 0.3 | 尺寸适配 |
| `λ_repeat` | 1.2 | 0.6 | 0.2 | 同地块重复惩罚 |
| `λ_cluster` | 0.4 | 1.0 | 1.4 | 同类型邻居奖励（聚簇） |
| `mixEpsilon` | 0.05 | 0.08 | 0.15 | 混合用途判定阈值 |
| `relaxSteps/relaxFactor` | 3 / 0.7 | 3 / 0.7 | 2 / 0.8 | 候选掏空时的放宽（07 §3.3） |

## 6. 数据与持久化
| 数据 | 载体 | 入 git | 备注 |
|---|---|---|---|
| `BuildingProfile`（行 + Tuning） | SO | ✅ | 可 CSV 导出；`schemaVersion` 迁移 |
| 测量缓存 footprint/高度 | SO 内序列化（带 prefab 版本戳） | ✅ | 避免每次生成重测（大表几百 prefab 会慢）；版本戳失配即重测 |
| 行内 `prefabRef` | SO 引用 | ✅ | 依赖丢失时降级到 `prefabPath` 解析 |
| 表校验报告 | `.tmp-inspect/` | ❌ | — |

## 7. 模块内决策
| 编号 | 决策 | 备选 | 理由 |
|---|---|---|---|
| D-06-1 | 四维用 0~100 整数、不要求归一化/互斥 | 归一化浮点、单"类型"字段 | Q-2 裁决要求"跨类别按数值准入"（咖啡馆用例）；整数比较无精度歧义 |
| D-06-2 | `dominantType` 派生不落字段 | 存枚举 | 消除双真源（改四维忘改类型） |
| D-06-3 | 复用 stretch：经 core `IStretchService` + `Services`（Q-3） | 直接 asmdef 引用（违规）/自带轻量拉伸（重复实现） | 唯一合规且不重复造；接口通用化后可被他包复用 |
| D-06-4 | 高度换算放提供方、形态策略放调用方 | 全放 citymap | 段模型细节属于 stretch；citymap 不该知道"段" |
| D-06-5 | 降级为等比缩放但必须可见（`approximated`） | 静默降级/直接报错 | 工具可用性优先，但不能让用户以为用了 stretch |
| D-06-6 | footprint/高度自动测量并缓存（带 prefab 版本戳） | 手填 | 几十上百行的表手填是维护灾难（D-06 沿 v1） |
| D-06-7 | Tuning 全进表资产、给三档预设 | 写死在代码 | 调参是策划工作，必须可改可 diff（R-09） |

## 8. 验收
| # | 事实 | 证据 |
|---|---|---|
| A-6-1 | 拖 prefab 批量建行后，footprint/原始高与实测包围盒一致（容差 1%），且 `prefabPath` 与 GUID 对应 | 10 个样例 prefab 核对 |
| A-6-2 | 路径失效行：校验红标 + 生成时跳过并给出计数；面板可一键"按名称重新匹配" | 破坏路径用例 |
| A-6-3 | 四维全 0 行不参与主导准入，但统计摘要中可见 | 表校验 + 07 抽样日志 |
| A-6-4 | CSV 导出→改值→导入，除被改字段外逐字节一致（列头固定、顺序稳定） | 往返 diff |
| A-6-5 | `Services` 幂等注册：域重载两次后控制台**无** `IStretchService` 重复注册 error（门禁 §1.6b） | 触发域重载 + `console status` |
| A-6-6 | `IStretchService` 可用时 `CityBuildingTag.stretchSource="com.collin.stretch"`；不可用时 `"fallback"` 且面板黄标统计 | 摘除/挂上 stretch 两种装配核对 |
| A-6-7 | 改 `Tuning.preset` 立刻改变下一次生成的多样性（同 seed 下唯一性统计差异显著） | 三档预设各跑一次，统计"相邻同类比例/唯一个数" |
| A-6-8 | 表编辑器不阻断编辑、只阻断生成；`跳到第一处问题` 可定位 | 交互走查 |
| **A-6-9** | 全项目 `Register<IStretchService>` 出现次数 **恰为 1**（位于 stretch 包内）；citymap 只 `TryGet` 从不注册；`Services` 域重载两次无重复注册 error | grep 断言 + `console status` 门禁（H-14b） |
| **A-6-10** | 9000 栋批量拉伸在 `BeginBatch/EndBatch` 下总耗时计入全量 ≤60s 预算；`SupportsBatch=false` 时走分帧摊销且不超帧预算 8ms | 拉伸专项计时基准（H-13） |

## 9. 风险
- **接口签名可能需要扩**（R-13）：stretch 的段模型若与"目标高度"换算不匹配（如某楼只有一个可重复段但需要半层），
  M9 联调时允许扩接口，但扩 core 属高风险变更（AGENTS §10）→ 需 ADR + 用户批准 + 同步校验器检查项。
- 大表（>1000 行）编辑窗口卡顿 → 虚拟化列表（`ListView`）+ 分页；筛选条件持久化到 `SessionState`。
- `prefabPath` 与 `prefabRef` 同时存在且指向不同物 → 校验规则：两者都在且 GUID 不同 = error（强制单一真源，路径仅作导出用）。
- CSV 导入绕过校验（直接写 SO 不可能，但导入器必须走同一条 `Validate()`）→ 导入即校验，失败整批回滚。
