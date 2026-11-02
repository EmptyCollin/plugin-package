# CHANGELOG — com.collin.stretch

格式：Keep a Changelog；版本号：semver。

## [Unreleased]

## [0.3.5] - 2026-11-02
### Fixed（用户反馈：桥切轴后再无法拉伸）
- **切轴边界脱节**：边界值是轴坐标，切换拉伸轴后首尾边界仍停在旧轴包围盒端点
  （桥 Z 轴配 Y 端点 → 校验永远不过、段长显示负数）。修复：切轴时首尾自动重设新轴端点、
  中间边界 clamp 进新范围，随后统一 NormalizeBounds 重排；
- **0.3.3 丢失的越界重排**：面板 ApplyEdits 未再调用 NormalizeBounds（0.3.2 语义回归）。补回；
- 校验失败提示补可操作出口（"点端点对齐或重选拉伸轴可自动修复"）。
### Verified
- T1（Y→Z 切轴）/T2（用户截图态点端点对齐）双路径：bounds=[-8.93,-6.97,-3.46,0.43]、
  ×6 后 lenZ=26.91 与手算一致；归零门禁 errors=0 warnings=0；场景重开零污染（inst=True v=32326）。

## [0.3.4] - 2026-11-02
### Changed（用户反馈：14万顶点异常 + 缺滑条）
- **共享顶点缓存**：Builder 未切割三角形按 (源顶点,段,份) 去重输出——不再逐三角形摊平、
  复制份间不重复拷贝（楼A 结果 492→328 顶点，三角形数不变）；
- `pier_long` 重生成：三直段按顶点表拼接+索引重映射（147600→**32326**，=源三段之和零膨胀），
  场景网格引用已重接并保存；
- 面板：**每条边界 = 滑条(源包围盒范围)+数值框**一行，独立成行；Repeatable 段倍数加 1..32 滑条；
### Verified
- 回归 W1-W6 全绿（恒等=源12顶点；×3 桥膨胀率 2.04、双槽、碰撞 14.97、还原 32326）；
  归零门禁 errors=0 warnings=0。

## [0.3.3] - 2026-11-02
### Fixed（0.3.2 面板引入的回归：插入无响应 + 控件全部挤一行）
- 根因=段行循环内 `break` 在 `BeginHorizontal` 未闭合时跳出 → GUILayout 布局组泄漏，
  其后所有控件（含插入行按钮）被挤进同一横向组并被裁剪失效；
- 面板重写为布局纪律版：全部行改用 `using HorizontalScope`（异常/跳出必闭合）、
  **数据变更延迟到绘制结束后统一应用（全函数 0 个 break）**；
- **每条边界独立一行**（起点/终点锁定灰显，中间行内联删除按钮）；
  插入/端点对齐独立一行；段行保留一行一段。
### Verified
- 反射触发插入逻辑：`doInsert bounds 4→5、repeats 同步补齐、段数匹配`（楼A 实例，快照还原零残留）；
- 静态配平：BeginHorizontal=0、HorizontalScope=5 全 using、ScrollView 1/1、break=0。

## [0.3.2] - 2026-11-02
### Fixed（用户反馈：边界精调"改了就被重置"）
- 旧实现把输入值 `Clamp` 在相邻边界之间——两边界接近时（如 -0.94 与 -0.9377）任何修改都被夹回原位，
  表现为"改一个数后另一个也被重置"。现改为**允许越界输入，NormalizeOrder() 自动升序重排**，
  段类型与实例 repeats 按映射跟随移动（验收 N1/N2/N3：越界→重排→几何正确）；
### Changed（用户反馈：面板布局难用）
- 主面板重构：整体滚动区（限高 400px 不再无限拉长）；每段合并为一行（范围+类型+倍数+删除）；
  边界一行紧凑输入；插入/端点对齐同行；
- **Scene 视图支持拖拽边界把手**（青色点，FreeMoveHandle）：位移按该轴 lossyScale 与下方段倍数
  换算回局部源坐标，拖过相邻边界同样自动重排（D-4 的把手需求就此落地）。

## [0.3.1] - 2026-11-02
### Fixed（真实 CC0 资产暴露的通用性缺陷）
- **多材质子网格丢失**：Builder 原用 `source.triangles`（仅子网格 0）——FBX 多材质模型（如栈桥的木板+桩柱）
  会被丢掉一半。改为**逐子网格处理，结果保持 subMeshCount 与材质槽对应**（验收 37：双槽 both>0、matSlots=2）；
### Added
- 体验场景的桥替换为真实 CC0 资产：Poly Haven `modular_wooden_pier`（FBX+1K 贴图，CC0 无需署名，
  来源登记 `Sample/Models/CREDITS.md`）；场景物体 `桥_栈桥` 为三直段按材质合并的连续栈桥裸模型
  （`Sample/Meshes/pier_long.asset`，双材质 147600 顶点），仍**无组件、无 Profile**（用户要求）；
### Removed
- 程序化 `桥_原型.obj`（被真实资产取代）。

## [0.3.0] - 2026-11-02
### Changed（算法通用化：模型不再迁就工具）
- **跨界三角形改为边界裁剪分片**（Sutherland–Hodgman 半空间裁剪，位置/UV/法线/切线/色切口线性插值，
  逐片刚性归段）：任意多面体、边界切在任意位置均确定性成立（用户指出旧"重心归段+空白带前提"本末倒置）。
  不跨界三角形走整三角形快速路径，旧用例逐顶点数不退化（R1/R2 回归证明）；
  验收 G1：窗户被边界从正中切穿再 ×3 复制，UV 仍全在 [0,1]、无巨型窗户；
- 体验场景桥重做为**自包含节段装配式**（引桥单跨单元=箱梁+双柱墩+盖梁+护栏，主跨双柱墩+抛物拱肋，
  桥台收头；跨间纯空气缝）；桥在场景中为**裸原型**（无组件、无 Profile——拉伸方案由用户在面板自行创建）；
- `StretchInstance` 重建时**同步碰撞体**：BoxCollider 尺寸/中心、MeshCollider 换结果网格（Restore 还原源），
  Rigidbody 运行期重置质心/惯性张量（用户要求：碰撞与视觉同尺寸）；
- `StretchProfile.InitFromMesh(mesh, axis)` 轴+边界同源初始化（防"轴与包围盒不同源"静默校验失败）；
- 面板结构区新增**边界数值精调**（中间边界直接输入局部坐标，配合 % 滑条实现精确切点）。
### Fixed
- 面板"端点对齐/初始化"从 MeshFilter 取包围盒——编辑态那是**拉伸结果**，点一次即把结果尺寸写回边界
  （楼A 示例资产曾被污染为 8.0 的根因，已复原并回归）；现锁死为实例缓存的源网格。

## [0.2.0] - 2026-11-02
### Changed（破坏性：数据归属重构，落实 AGENTS.md §0.1 三原则）
- **倍数从 Profile 迁到实例**：`StretchProfile` 只存共享结构（轴/边界/段类型 `segmentTypes`），
  每段倍数 `repeats[]` 改由场景实例 `StretchInstance` 序列化——同一 Profile 现可被多实例以不同倍数复用（原则2 MVC 解耦）；
- 主面板改建在**场景实例 Inspector**（`StretchInstanceEditor`，即时预览+辅助线唯一入口），
  Profile 资产面板降为只读摘要+实例定位（原则1 所见即所得，消除两套编辑真源与辅助线双绘）；
- `Build`/烘焙签名改为 `(source, profile, repeats)`，新增倍数数组长度校验。
### Removed
- 旧 `StretchPreview` / `StretchRepeater` 组件合并为单一 `StretchInstance`（编辑/运行同一条构建路径，原则3）。
### Fixed
- `Tools/Collin/Stretch 新建拉伸参数` 在无 Mesh/空对象触发裸 NullReferenceException——改为空守卫+异常捕获弹窗（§1.6b 门禁友好）；
- 校验器新增 `runtime-purity`（Runtime 禁引用 UnityEditor，强制原则3）。

## [0.1.1] - 2026-11-02
### Added
- 包内 `Sample/TestPlayground.unity` 体验场景：3 楼 1 桥（3层→10层 / 5层→7层复合单元 / 原样对照 / 桥面×5），
  含程序化 OBJ 模型、窗户贴图、材质与 4 份 Profile 资产；
- `ShowcaseMedia/stretch_demo.mp4` 演示视频，`Showcase.json` 指向它（展板只消费视频，§0）；
- `StretchSaveHook`（Editor）：场景保存前还原预览、保存后重建——磁盘场景永远只存源网格+Profile 引用。
### Changed
- `StretchPreview` 加 `[ExecuteAlways]`：打开场景即自动重建拉伸预览，无需手动刷新。
### Fixed
- 示例模型绕序与声明法线不自洽导致外侧被剔除"缺片"（用户反馈；根因在生成器非插件）——
  改为三角形直出+导入器重算法线，生成器加右手定则 assert；
- 桥段边界恰与塔壁三角形重心重合导致塔壁被误复制（"栅栏"现象）——边界内缩 0.002 修复。

## [0.1.0] - 2026-11-02
### Added
- `StretchProfile`：拉伸参数资产——单轴、slice 边界划段、fixed/repeatable 段、整数复制倍数，含校验与位移计算；
- `StretchMeshBuilder`：逐三角形按重心归段整体复制，UV/法线逐字保留（杜绝巨型窗户）；>65535 自动 uint32；
- `StretchPreview`（编辑期内存预览）/ `StretchRepeater`（运行时替换网格并同步 BoxCollider）；
- Editor：`Tools/Collin/Stretch 拉伸` 菜单（创建参数+挂预览 / 烘焙结果为 Profile 子资产）、
  Profile 面板（归一化边界插入、段编辑、端点对齐）、Scene 辅助线（段框线/周期线/xN 标签）。
