# CHANGELOG — com.collin.stretch

格式：Keep a Changelog；版本号：semver。

## [Unreleased]

## [0.4.4] - 2026-11-06
### Changed（通道纪律合规：阻塞式弹窗全部改为日志，AGENTS.md §2.1.5）
- `StretchBakeCommand`：菜单入口与面板按钮共 4 处 `EditorUtility.DisplayDialog` → `Debug.Log`。
  模态等待会占住 Unity 主线程 = 卡死所有 AI 的指令通道；失败原因仍逐字可见
  （`未生成子资产：<原因>` / `烘焙失败：<原因>`）。
- `StretchCreateCommand`：6 处 `DisplayDialog` 全部去弹窗化 —— 异常兜底那处**保留**
  `Debug.LogWarning`（真实故障信号不降级为 Log，只删掉伴随的弹窗），其余 5 处提示改 `Debug.Log`。
- **「模型网格不可读 → 是否现在自动开启 Read/Write」二选一确认框**拆成独立菜单命令
  `Tools/Collin/Stretch 拉伸/开启选中模型的 Read/Write`（`EnableReadWriteMenu`）：
  把"创建流程中途询问"换成"用户主动触发的命令"，能力不减、通道不堵。
- 空守卫分支一并核验：`BakeOne` 仍返回 `null + 原因`（供测试与面板复用，无副作用）。
- 证据见 ACCEPTANCE 89–92。门禁终态：控制台 0 error / 0 warning、`compilationFailed=False`、
  校验器 channel-discipline 两项 FAIL 转 0（整体结论绿灯）。

## [0.4.3] - 2026-11-03
### Changed（演示视频按 §6 两点硬性要求重制）
- `ShowcaseMedia/stretch_demo.mp4` 替换为**真实编辑器实录剪辑**（源=用户手动录屏
  `屏幕录制 2026-11-03 173638.mp4`，103.1s @3352×1376）。成片 55.6s / 1920×1080 /
  Constrained Baseline + bt709 + faststart / 去音轨（源音轨为 -43dB 环境噪声）。
- 剪除三类无效内容（逐帧定位 + 剪点抽帧复验）：
  ①12.2–13.6s 拖出零长度段触发"结构校验未通过"报错；②65.4–67.6s 选中裸桥弹出警告框；
  ③27–54s 与成片 12–23s 两段纯相机调镜头（Inspector 数值全程未动，抽帧核验）。
- 成片内容满足 §6 两段式：**怎么用**（选中物体→拖边界滑条→切段类型→拉倍数滑条→
  菜单建参数→Add Component 添加组件）；**用了之后的效果**（楼实时增减层、窗户贴图
  拉近特写、栈桥单跨→多跨逐段延长、拉远全景改前/改后对照）。
- 源录屏保留于包根目录（未入库），本条替换仅覆盖 ShowcaseMedia 成品。

## [0.4.2] - 2026-11-03
### Fixed（用户反馈：展板所有按钮点不动）
- **Canvas 缺 `GraphicRaycaster`**：展板全部控件由代码构建（DESIGN D-6），但从未挂过射线组件，
  场景文件里也是 0 个。EventSystem 与输入模块（InputSystemUIInputModule，与工程
  `activeInputHandler=1` 匹配）均正常，点击在派发第一环即被丢弃 → 整个界面对鼠标是聋的。
  修复：`Start()` 里补挂 `GraphicRaycaster`。实测修复前 `ray=NONE / buttons=3 live=3`，
  修复后 `ray=enabled=True`，且用 `EventSystem.RaycastAll` 对 4 个按钮中心逐一检测
  `self=True clickable=4/4`（命中链贯通）。
- **重要归因修正**：本条是「展板看不了视频」的**首要**根因——0.4.2 先前修的 `file://` 盘符
  URL 缺陷真实存在且必要，但即便修好，用户也从未点到过「播放演示」。教训：
  交互失效先证命中链（EventSystem 是否收到点击），再查末端逻辑。
- **画面中央 "No cameras rendering" 占位框压住视频区**：展板场景无相机。
  补一台 `cullingMask=0`、只负责清屏的相机（depth=-1，Overlay 画布仍绘于其上）消除该占位框。

### Fixed（用户反馈：展板看不了视频 + profile 落在 Assets 根）
- **展板视频 URL 缺陷**：`"file://" + "D:/..."` 会把盘符 `D:` 解析成**主机名**
  （urllib 静态证明 netloc='D:'），VideoPlayer 找不到该主机 → prepare 永不完成、点播放无反应。
  改为直接传绝对路径（Unity 原生支持）。
- **prepare 失败不再静默黑屏**：加 15s 超时 + 「视频准备中…/解码失败」占位提示，
  原实现 `while(!isPrepared) yield return null` 会无限自旋且界面无任何反馈。
- **渲染纹理比例**：960×360 → 960×540（与视频一致，消除拉伸变形）。
- **音频模式**：`Direct` → `None`（合成静音轨在编辑器下会让异步 prepare 挂起）。
- **演示视频重编码**：High → **Constrained Baseline** + faststart + bt709 色彩标签，
  消除 Unity 导入的 `Color primaries 0` 与 H.264 时间戳告警。
- **参数资产落点**：原逻辑兜底写 `Assets` 根（用户 7 次实验产生 7 个孤儿 profile）。
  改为 prefab 同目录 → 包内 `Sample/Profiles/` → 场景目录，**绝不进 Assets 根**；
  无法定位时弹窗提示而非静默落 Assets。实测 `inPackages=True`。

## [0.4.1] - 2026-11-02
### Changed
- 演示视频换为**真实功能录屏**（程序化逐帧渲染，非人工操作）：396 帧@24fps/16.5s，
  五段=楼群广角→楼A 1→10 层生长→摇镜→栈桥 1→5 跨延长→拉远收尾；h264+aac 960×540，
  替换 0.3.x 的文字占位视频（`ShowcaseMedia/stretch_demo.mp4`，LFS）。

## [0.4.0] - 2026-11-02
### Added（DESIGN §8.2 P1–P4 全量落地）
- **P1 `StretchMeshCache`**：结果网格按 (源网格, Profile, repeats) 共享（引用计数+LRU64，
  源/profile 销毁即作废）；实测 5 个同参实例命中同一 Mesh（hits=58/misses=3），不同 repeats 自动分键；
- **P2 一键烘焙**：面板"烘焙为静态网格"按钮（`BakeOneAndReport`），份数固定场景走普通渲染器吃 static batching；
- **P3 分块实例化**：`renderMode=Chunked` → 块几何（段×材质槽，缓存共享）+ 份数退化为矩阵，
  `StretchChunkManager` 以 `Graphics.DrawMeshInstanced` 提交；实测 50 实例×10 份=500 矩阵仅 **3 个 Draw 组**
  （=块数，与实例/份数解耦）；MeshRenderer 自动关闭防重影，材质自动开 GPU Instancing（退出 Play 还原）；
- **P4 `StretchScheduler`**：重建入队+每帧预算（默认16），运行期泵（RuntimeInitializeOnLoad）+编辑期泵
  （EditorApplication.update）；面板交互保持 RebuildNow 即时（原则1）；外部热改份数用 `RequestRebuild` 防抖；
  `syncMeshCollider` 默认改 **false**（万级规模 MeshCollider 是隐形放大器，DESIGN §8.2-P4 物理建议）。
### Verified
- 场景重开→泵数帧内恢复拉伸态（104→328，pending 0）；缓存路径下核心回归全绿（3→12 层 tris=24 maxY=12；恒等=源12）；
- 归零门禁 errors=0 warnings=0；runtime-purity 绿灯。限制如实记录于 DESIGN §8.4。

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
