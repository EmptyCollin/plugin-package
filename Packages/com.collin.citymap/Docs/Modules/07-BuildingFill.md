# Modules/07 — 程序化建筑落位（S6）：硬过滤 → 加权随机 → 落位 → 拉伸

> 依赖：00、04（slot）、05（门槛与强度）、06（表 + 拉伸服务）。消费方：01（沿坡校正回环，只读高度）。
> 对应里程碑：M8（降级路径 + 全图语义）、M9（接 stretch）。状态：草案。

## 1. Q-2 裁决后的定稿规则（最重要的一节）
**两步法适用于全地图每一个 slot，不是"内径范围专用"规则。**

```
对每个 slot s：
① 硬过滤（筛掉不合格）
   F(s) = LandUseGrid.Sample(s.pos)                 // 四维强度（05）
   D    = argmax(F(s))                              // 该处主导维度
   Th   = Threshold(s.pos, D) ∈ [0,100]             // 该维度在 s 处的强度值即门槛
   C    = { B ∈ BuildingProfile | B.v_D ≥ Th }      // 只看数值达标，不看建筑"类别"
   C 为空 → 放宽：Th ← Th × relaxFactor（≤relaxSteps 次）
              仍空 → C = { argmax_B B.v_D }，并 warnings["候选掏空"] += 1
② 加权随机（幸存者里选）
   score(B) = 见 §3
   w(B)     = softmax(β · score(B))
   B        = Sample(c, w, seed = objectSeed(stage=6, s.id))    // 局部稳定（00 §3.1）
```

**为什么这样才对（用户理由）**：金融区不可能全是金融建筑；"生活类但金融值足够高的咖啡馆"
（`residential=45, finance=70`）在金融门槛 `Th=60` 处应当合格入选。
所以：
- 过滤只看**主导维度的数值是否达标**，绝不做"用途=住宅所以不能在金融区"的类别排除；
- `dominant` 只用来决定"按哪个维度设门槛"，不限制建筑的其他高维度；
- 过渡带（内径~外径）`Th` 随曲线连续下降 ⇒ **同一条规则天然覆盖"核心区严格、边缘宽松"**，
  不需要任何"区内/区外"分支（这是 Q-2 把规则提到全图后带来的最大简化）。

对照用户原始示例（验收 A-3 的断言形式）：
`type=Finance, innerRadius=10m, innerValue=10` ⇒ 内径内每个 slot 的 `Th=10` ⇒ 选中建筑必有 `finance ≥ 10`；
而外径外 `Th=0` ⇒ 全部行合格（退化为纯加权随机）；过渡带里 `Th` 介于 0~10。

## 2. 打分项（软排序，决定"更像"而不只是"够格"）
```
score(B) = λ_cosine · cosSim(F̂(s), V̂(B))            // 归一化四维余弦：整体性格贴合
         + λ_frontage · gradeMatch(B.preferredGrade, s.grade)
         + λ_water    · B.waterPreference · WaterProximity(s)
         + λ_size     · sizeFit(B.footprint, s.size)   // 1 − |Δ|/s.size，钳 [0,1]
         − λ_repeat    · sameBuildingInParcel(B, parcel)   // 同地块重复惩罚（多样性）
         + λ_cluster  · neighborTypeAffinity(B, 已放邻居)   // 同主导维度奖励（聚簇）
         + log(B.weightBias)                          // 表内偏好乘子（06 §2.2）
         − minParcelPenalty(B, parcel.area)           // B.minParcelArea 不满足 → −∞（视作硬约束）
```
说明与纪律：
- `cosSim` 用四维**归一化向量**（`v/‖v‖`），而门槛用**原始整数**——两件事各用各的度量，别混（混了会导致"全 90 分的楼"永远压过"恰好合适的楼"）。
- `λ_repeat` 与 `λ_cluster` 方向相反、必须同时存在：前者保地块内多样性，后者保街区一致性；
  实测只有 cluster 会整街一栋楼重复，只有 repeat 会斑马纹（R-09 的具体形态）。
- `neighborTypeAffinity` 读已放建筑（同 parcel 优先、跨 parcel 用邻域哈希）→ **落位顺序会影响结果**！
  为守住确定性（A-1），顺序必须是**确定性顺序**：`parcel.id` 升序 → parcel 内 slot 按"沿临街边顺序"排列；
  禁止用字典序或 List 插入序以外的隐式顺序。
- 混合地块（`F` 前两维差 < `mixEpsilon · max(F)`）：主导维度仍取 argmax，但过滤放宽为
  `C = { B | B.v_D ≥ Th_D − mixSlack }`（`mixSlack = 15` 默认）；
  这避免金融/住宅交界带因 argmax 抖动出现硬折线（v1 遗留问题在此定稿）。

## 3. 落位（摆放）
```
for parcel in 确定性顺序:
  if parcel.flags & (Invalid|Reserve): continue
  slots = parcel.slots（Perimeter 模式按"主临街 → 次临街"顺序）
  cursor = 0
  for s in slots:
      (B, Th_used, score) = PickAndScore(s)          // §1+§2
      place = SolvePlacement(s, B)                   // §3.1
      if place == null: continue 且记 skip           // 跳过不阻断
      Instantiate(prefab, place) → CityBuildingTag
      cursor 按 B.footprint 宽度推进（两遍法，04 §3.3）
```

### 3.1 位置求解（冲突与朝向）
1. 中心 = slot 中心；朝向 = `slot.facingYaw`（临街边法线，背面朝地块内，正面用 06 §3.3 的 `frontVector`）；
2. 碰撞检测：与该 parcel 及相邻 parcel 已放建筑的 **OBB** 重叠检查（空间哈希，桶边长 = 平均建筑宽）；
   **`orphan=true` 与 adopted/手刷对象一律视为已占位**（其 footprint 来自 `OccupiedHard` 掩码，00 §2.4/H-2），
   自动流程绝不与其重叠；
3. 失败重试序列：`沿边滑动 ±step` → `旋转 90°` → `缩小 footprint（仅 footprint 覆盖值，不动几何）` → `跳过该 slot`；
   重试次数上限 4，超限跳过并计数（跳过率进面板，>15% 提示"slot 与表尺寸不匹配"）。
   **H-26 定稿要求**：第 3 步产生的覆盖值必须写入 `BuildingRecord.footWQ/DQ`（0=用表值，Modules/11 §2/§7）。
   否则表是共享 Model（不能存实例差异，§0.1 原则2），跨会话重算后 footprint 回到表值 →
   已让位的建筑重新重叠，**A-8 静默失效且无报错**。认回与 `OccupiedHard` 烘焙一律**先取记录覆盖值、无覆盖才 join 表**。
4. 越界检查：不得进入 `Road/Water/WaterBuffer/OutOfBounds` 掩码格（A-8 断言对象）；
   `needsStilt=true` 的 slot（04 §3.4）首版跳过。
5. 沿河 waterfront slot：朝向强制朝水（04 A-4-6），若与表内建筑正面约定冲突则允许 ±180° 翻转。

### 3.2 拉伸与高度（场域 → 形态的闭环）
```
targetH = lerp(B.heightMin, B.heightMax, normalize(F_D(s))) × distanceDecay(dist 到城心)

if B.stretchable and 拉伸服务可用:                          // 情形一：stretch 分段复制
    seg   = 首选可重复段（GetRepeatableSegmentCount>0）
    if GetPreferredRepeat(go, targetH, out n): SetRepeat(go, seg, n)
    record.heightValue = n            // = repeat（整数 ≥1）
    record.flags.approximated = 0
elif B.stretchable:                                        // 情形二：降级等比缩放（06 §4.4）
    localScale.y = targetH / 原始高
    record.heightValue = round(100 · targetH / 原始高)   // = scale 百分数（有效区 50~800）
    record.flags.approximated = 1
else:                                                    // 情形三：非拉伸行
    几何与缩放恒等
    record.heightValue = 1            // 哨兵值（11 §4.3 表第一列 false+0）
    record.flags.approximated = 0     // false+1 为非法组合，校验 error（A-51）
    若 targetH 与原始高差异 >10% → 记 "尺寸与形态不匹配" 建议（仅提示，不改）
```
> `heightValue` 是**双义字段**，读侧一律经 `RecordGeometry.Resolve(record, row)` 单一入口解释（11 §4.3 与 §13 风险第 1 条），
> 禁止在业务代码里手写 `heightValue/100` 这类推断。段号 `seg` 不持久化（11 §5：段模型属提供方）。
- `distanceDecay`：城心高、外围矮（默认 `1 → 0.55`，半径外），中心**只读 `CityCenterAnchor`（01 §4.4，H-7 修订）**
  ——**不实时读场域位置**，否则"拖场域"会升级成全城重排。**无需新增参数**即可得到"CBD 塔楼群 + 外围低层"的城市轮廓。
- 农业/工业地块：`targetH` 区间用表内该行的 `heightMin/Max`，不参与城心衰减（`decay=false` 的 tag 组，P1）。
- **拉伸只作用于高度轴**（默认 Y），与 stretch 的"单轴分段复制"语义天然一致。

### 3.3 认回与增量重算（跨会话正确性的关键，Modules/09 H-10 修订；**v6 起主方案见 10 §4**）

> **v6 修订（D-25）+ 定稿收敛（Modules/11）**：v5 的"扫场景 tag 建表"在 8km/10 万栋下不可行
> （遍历 10 万对象且多数未加载）；主方案 = **`BuildingRecordAsset` + `intentVersion` 指纹**，
> tag 反查仅作首次兜底（限工作集）。旧文中的 `slotId`/`profileRowId` 统一为定稿的 **`slotKey`/`rowId`**，
> 且判定一律读**记录字段**（`record.flags.*`），不再读组件字段（组件仅剩 `slotKey` 反向指针，§5）。

**认回（reclaim）流程**
```
S6 启动前：取记录集合 → 工作集内的投影按 record.slotKey join 现有 CityBuildingTag（O(工作集实例数)）
① record.flags.(adopted|userModified|orphan|inLockRegion) 任一为真 → 进"不可替换"集合；
   其 footprint = resolveRecordFootprint(record, row)（覆盖值优先，11 §7 H-26）→ 烘焙 OccupiedHard
② 同 slotKey 的重算结果 rowId 相同 且 xQ/zQ/yawQ 一致 → 保留投影实例（省一次 Instantiate，不闪）
③ 不一致 → 销毁旧投影 + 按新记录物化（正常重排）
④ 记录里的 slotKey 在本次派生结果中已无对应 slot（地块消失）→ flags.orphan=1，记录保留、不删（走 08 三选一）
⑤ 无 tag、也无记录匹配的场景物体 → 永不触碰（可能是用户自己的东西）
```
```
会话内增量：prev = { slotKey: (rowId, placeHash) }  ← 纯优化缓存（少做一次 diff）
跨会话正确性由【记录 + intentVersion】承担 ⇒ D-13（派生层不落盘）得以成立。
```
- 幂等要求：**重开编辑器→保存，记录 payload 逐字节不变、投影无净增实例**（A-30/A-46）。
- **rowId 失效同判（H-15）**：认回第②步前检查 `record.rowId` 在当前表中是否存在；不存在 → `flags.orphan=1`
  并进"改绑/删除/保留为固化"三选一条目；`rowId` 与 `slotKey` 均**永不复用**（同 03 边 id 规则，冲突协议见 11 §4.2）。
- 表行被删但记录还在 = "无主 auto 物"：既不占位也不被 diff 认领，重排会叠楼——A-8 的静默破坏源，必须按上述转 orphan。
- 不在"next"集合中的 parcel：一行代码都不执行（A-5-4 的落点）。
- 表行被删但实例还在 = "无主 auto 物"：既不占位也不被 diff 认领，重排会叠楼——本条是 A-8 的静默破坏源，必须按上述转 orphan。
- 不在"next"集合中的 parcel：一行代码都不执行（A-5-4 的落点）。

## 4. 交互

### 4.1 工具与入口
| 入口 | 行为 |
|---|---|
| 主面板「建筑」区 | 「填充全城」/「只填选中 parcel」/「清空建筑」（含二次确认，因为会删人工 adopt 项） |
| 自动跟随=on | 表/场域/路网/ρ 改动 → 脏 parcel 自动 Commit 重排（无需手点，A-10） |
| 手刷工具 `BuildBrushTool` | 单个 parcel 强制指定行（人工层，`ManualBuildingAsset`）；用于"这栋必须是这个" |
| 右键建筑 | `固化(adopt)` / `移除并禁止自动生成(Reserve)` / `查看为什么选它`（§4.3） |
| 拖动/旋转已 adopt 建筑 | 直接改 Transform + `userModified=true`，此后不再被自动替换 |

### 4.2 生成过程可见
- 分块渐进：按 parcel 批量、分帧（每帧预算默认 8ms，可调），进度条 + 「取消」（00 §4.4 事务回滚）；
- 面板显示：候选数分布（多少 slot 走了放宽）、跳过率、拉伸成功率、降级栋数、内存与实例数。

### 4.3 "为什么选它"（可解释性，R-02/R-09 的解药）
选中一栋建筑 → Inspector 显示：
```
slot: id, pos, landUse(F,I,R,A 四项 + 主导维度 + 门槛 Th)
候选: 该 slot 过滤后的行数（如 37/210）→ 被 §2 打分后的前 6 名与各自分项分解
选中: 你的建筑，score 分项条 + 抽样随机数（objectSeed 十六进制，可复现）
门槛来源: 哪个场域在起作用（若来自过渡带则给出 r 与 t）
```
没有这个面板，用户调表与调场域就是在瞎调（把 05 的探针 HUD 延伸到"单个建筑级"）。

### 4.4 撤销政策（真源见 **Modules/08**，此处遵循其 L1~L4 分层）
- 自动生成结果：**不做对象级 undo**（15k 实例的 undo 栈会卡死编辑器，且与确定性契约冲突）；
  回滚 = 改参数重生成 / 「清空建筑」/ 检查点恢复（08 §5.3）。
- 人工操作（手刷、adopt、移动、Reserve）：**必须可撤销**，对象级。
- 删除自动建筑的正确语义 = 挂 `NoBuild` 负约束（08 §4.3），Ctrl+Z 移除该约束即复活。
- 面板与帮助文本必须写明这条区别，否则用户会以为 Ctrl+Z 能撤销"重新生成"。

## 5. 数据与持久化
```csharp
// 定稿（Modules/11）：CityBuildingTag 只是【投影上的反向指针】，不承载任何持久语义。
// 旧版（v5 遗留）在此存 rowId/landUse/threshold/adopted 等字段，与 BuildingRecord 重复 ⇒ 双真源，已废除。
public sealed class CityBuildingTag : MonoBehaviour {
    [SerializeField] int slotKey;        // 唯一字段 = BuildingRecord.slotKey（认回 join 键）
    // 以下均为【投影期缓存】，回收前必须由保存钩子写回记录，读取一律以记录为准：
    //   位置/朝向/footprint 覆盖/heightValue/flags —— 全部来自记录，不在此重复存储
}
```
> **禁止**在本组件上再加回 `adopted`/`orphan`/`approximated` 等布尔位：投影可被随意销毁重建，
> 组件字段的生命周期短于记录，任何以组件为真源的判定都会在"未加载区"或跨会话后失效（10 §4、11 §5）。
> 「为什么选它」面板（§4.3）读四维与门槛时一律从记录取（`land{F,I,R,A}` + `argmax`），随机数由 `objectSeed(6, slotKey)` 现算。
> **v6 重大变更（D-25 / 10 §2）**：建筑的**真源是 `BuildingRecord` 数据（≤32B/栋，10 万栋 ≈3.2MB）**，
> 场景 GameObject 只是工作集内的**投影**，可随意销毁重建。下方 `CityBuildingTag` 因此**降级为反向指针**
> （唯一字段 `slotKey` 为反向指针，其余字段一律以记录为准，回收前必须写回）。原"实例落盘"模型在 8km/10 万栋下
> 会让场景 YAML 膨胀到数百 MB、打开与保存进入分钟级（09 H-17，阻断级）。

| 数据 | 载体 | 入 git | 说明 |
|---|---|---|---|
| **建筑真源：`BuildingRecordAsset`** | SO 元数据 + `byte[] payload`（32B/栋，按 region 分块，11 §3） | ✅（有人工改动/adopted 时必存；纯派生城可省） | 认回主依据 + `intentVersion` 指纹；**禁止自动序列化器**（11 H-28） |
| 建筑投影（prefab 实例） | 场景/内存，工作集内 | ❌ | 按需物化与 LRU 回收；**回收≠丢数据**（10 §3.1） |
| `CityBuildingTag` | 投影上的组件 | ❌（随投影回收） | 仅 `slotKey` 有持久含义（反向指针）；用户改动经保存钩子写回记录 |
| `ManualBuildingAsset`（手刷人工层） | SO | ✅ | 重生成保留（D-08） |
| 落位快照 diff 表（§3.3 prev） | ❌ 内存 | — | **纯优化缓存**；跨会话正确性由**记录资产**承担（v5 曾写"由 tag 承担"，v6 修订：10 万对象遍历不可行，见 10 §4） |
| 统计/调试 | `.tmp-inspect/` | ❌ | 不入库（AGENTS §9） |

**投影分组**：按 region 建父节点（`CityMap/region_3_5`），进出工作集时整批物化/回收；
`EditorUtility.SetStaticEditorFlags`（BatchingDynamic）按用户设置，默认关（避免和手改冲突）。

## 6. 性能纪律
| 措施 | 目标 |
|---|---|
| 表按维度预排序（`rowsByDim[d]` 降序）→ 硬过滤可用**二分找分界**，不必全表扫 | 过滤 `O(log n)` |
| 候选 >200 时先截断到 top-200 再打分 | 单 slot 打分 <20µs |
| 空间哈希 OBB 检测（桶边长=平均建筑宽） | 放置检测 <10µs/次 |
| slot 增量 diff + GameObject 保留 | 拖场域时单 parcel <300ms（00 §8） |
| 实例化走 `PrefabUtility.InstantiatePrefab`（Editor） | 保持 prefab 联系，可 adopt |
| 每帧预算分帧 + 可取消 | 全量 ≤60s，面板不卡 |

## 7. 模块内决策
| 编号 | 决策 | 备选 | 理由 |
|---|---|---|---|
| D-07-1 | 全图统一"硬过滤（只看主导维度数值达标）→ softmax 加权随机" | 内径专用规则 / 纯加权不过滤 / 类别排除 | **Q-2 裁决**；类别排除会杀死咖啡馆用例，纯加权不保证"城心纯度" |
| D-07-2 | `dominant` 仅决定门槛维度，不作类别限制 | 按类型选池 | 同上，用户语义的核心 |
| D-07-3 | 门槛随过渡带曲线连续下降，全图同一公式 | 分区 switch 逻辑 | 少一套分支 = 少一类 bug；核心区严格/边缘宽松自动成立 |
| D-07-4 | 打分用归一化余弦，门槛用原始整数 | 两者同度量 | 混用会让"全高分楼"永远压制"合适楼"（§2） |
| D-07-5 | 落位确定性顺序 = parcel.id 升序 → slot 沿边顺序 | 任意顺序 | 邻居依赖使顺序影响结果，必须钉死以保 A-1 |
| D-07-6 | `repeat 惩罚` 与 `cluster 奖励` 并存且同进 Tuning | 只留一个 | 只有前者→斑马纹；只有后者→整街重复（§2） |
| D-07-7 | 增量 diff 保留 GameObject；`userModified/adopted` 视为人工层不替换 | 全重建 | 跟手 + 人工劳动不被摧毁（D-08 延伸，A-14） |
| D-07-8 | 拉伸目标高 = f(主导维强度 × 离城心衰减) | 随机/按等级 | 无需新参数即可得到城市天际线；用户可见旋钮少但效果大 |
| D-07-9 | "为什么选它" Inspector 必备（含随机数可复现） | 只显统计 | 门槛×加权×邻居的组合不可直觉，是调参唯一出路（R-09） |
| **D-07-10** | **跨会话认回靠 `CityBuildingTag` 反查，`prev` 降级为会话内优化缓存**（09 H-10） | prev 落盘为"实例快照资产" | 落盘 prev=D-13 破功且多一份真源；tag 已在盘上、零新增资产 |
| **D-07-11** | **表行删除/引用失效 → 实例转 orphan 保留并进 `OccupiedHard`**（09 H-15） | 随表删物体 / 不管（无主物） | 删用户看得见东西违背 D-08-4；"不管"会静默叠楼违反 A-8 |

## 8. 验收
| # | 事实 | 证据 |
|---|---|---|
| **A-3** | **全图门槛语义**：`Finance/inner=10m/innerValue=10` ⇒ 内径内所有建筑 `finance ≥ 10`；外径外允许 `< 10`；过渡带随 `Th(r)` 一致 | 内径内全量枚举断言 + 过渡带抽样 100 slot 与 `Threshold()` 对比 |
| A-3b | **咖啡馆用例**：表中含 `residential=45, finance=70` 行；金融门槛 60 的区域内该栋**出现次数 > 0**，且"纯住宅行（finance<60）"未出现在该区域 | 生成后按行统计出现位置 |
| A-7-1 | `userModified=true` 或 `adopted=true` 的建筑，在"全量重生成"后原样存在（位置/行 id 不变） | 重生成前后 id 集合比对（A-14/A-7） |
| A-7-2 | 手刷人工层（`ManualBuildingAsset`）重生成后仍生效 | 用例 |
| A-8 | 建筑两两 OBB 重叠率 ≤0.5%（超出记 warning）；无建筑落在 `Road/Water/WaterBuffer/OutOfBounds` 格；无建筑中心越出所属 parcel 外扩 2m | 空间哈希全量检测 |
| A-9 | `stretchable=false` 的行：`localScale == prefab 默认`（1e-6 容差）且无 mesh 改动 | 抽样 200 栋断言 |
| A-9b | `stretchable=true` 且服务可用：`record.heightValue`=repeat 且 `flags.approximated=0`，`GetHeight(go)` 与 `RecordGeometry.Resolve` 一致；服务不可用时 `approximated=1`、`heightValue`=缩放百分数、黄标计数正确 | 两种装配核对（A-13、11 §4.3） |
| A-10 | 自动跟随=on 时，改场域/表/ρ 均无需点按钮即见结果；拖场域期间无全量重建 | 交互走查 + Profiler（配合 A-5-5） |
| A-11 | 全量生成同 seed 两次：逐 slot 的 (rowId, pos, repeat) 完全一致 | 快照哈希比对（A-1 的建筑侧） |
| A-12 | 分帧生成期间取消：无残留半成品节点，随后完整重跑与不取消结果一致 | 00 A-0-4 联合 |
| A-15 | 混合地块（前两维差 < mixEpsilon）时放宽生效，边界不出现 >20m 的连续空带 | 交界带空洞统计（§2 末） |
| A-16 | 「为什么选它」面板的 score 分项之和 = 记录总分；随机数十六进制可复现同选择 | 面板与内核同算法对照 |
| **A-29** | **删表行→引用失效实例转 orphan**：保留在场景、进 `OccupiedHard`、info 条目含三选一；选择"删除"后位置不再叠楼 | 用例：删行→重生成→OBB 全量断言（H-15） |
| **A-30** | **认回幂等**：保存→重启编辑器（或强制域重载）→自动跟随触发 S6 首跑→再次保存，场景实例集合与重启前逐位一致（数量、rowId、位置零变化，零新增 Instantiate 净增量） | 前后场景枚举比对 + Profiler Instantiate 计数（H-10） |

## 9. 风险
- **顺序依赖性 vs 局部稳定性张力**（邻居奖励使结果依赖落位顺序）→ 已用"确定性顺序 + 按 parcel 隔离"控制：
  跨 parcel 的邻居读取限制在"已完成 parcel"，且 diff 时按 parcel 整体重算（不追求 slot 级独立）。
  残留风险：改一个 parcel 可能轻微影响其下游 parcel 的 cluster 分（面板注明"建筑布局为街区级稳定，非单栋级稳定"）。
- 表规模大（>2000 行）时候选截断可能"总选同一批高分楼" → Tuning 的 `β` 调低 + `weightBias` 让策划干预；必要时 P1 引入"全局配额约束"（每种行最多出现 k 次）。
- `PrefabUtility.InstantiatePrefab` 批量 15k 次在 Editor 里较慢（实测风险点）→ 缓解：分帧 + 只在 diff 变化处实例化（§3.3）；
  极端情况提供「烘焙为静态网格」导出选项（P1，交付性能用，工具链内不默认）。
- 手改建筑后 prefab 被删 → `tag` 引用丢失、场景变粉：保存钩子检测 prefab 丢失并列出（与 06 R-2 同一修复工具入口）。
