# CHANGELOG — com.collin.stealth

格式：Keep a Changelog（https://keepachangelog.com）；版本号：semver。

## [Unreleased]

## [0.5.0] - 2026-11-04

> **收版依据**：§6 两段式演示视频已由用户手动实录并入库
> （`ShowcaseMedia/stealth_demo.mp4`，44.7 s，压至 1616×880 / 保留音轨），
> `Showcase.json.video` 已填充。此前刻意停在 0.4.0 的唯一原因（演示视频缺失）已消除。

### Added（收版项：演示视频 + 展板接入）
- `ShowcaseMedia/stealth_demo.mp4`：用户实录的完整版演示，覆盖 §6 规定的两段且顺序正确
  ——①怎么用（编辑器全景 + Inspector 里的 Agent/Vision/Brain/投影参数面板）
  ②用了之后的效果（地面警戒投影随哨兵档位变色、潜行绕行、抵达撤离圈弹结算卡片）。
  源片由用户录制，内容按要求**不做抽帧验证**；转码只降分辨率与码率、保留音轨与完整时长。
- `Showcase.json`：`video` 由空串填为 `ShowcaseMedia/stealth_demo.mp4`；`summary` 同步补入
  近身察觉、地面投影同源、导航绕行、场地硬边界、双撤离点与胜负结算界面（原摘要落后两轮交付）。
- **展板接入无需改展板场景文件**：`Assets/Showcase/ShowcaseUI.cs` 经
  `ShowcaseRuntime.Load()` 运行时枚举 `Packages/com.collin.*/Showcase.json`，
  视频路径由 `ShowcaseRuntime.ResolveVideoPath()` 按「工程根 + Packages + packageId + 相对路径」解析
  ——填 `video` 字段即完成接入（AGENTS §6：禁止为单个插件改展板场景文件）。

### Removed
- **清掉工程内所有手录原始视频**（用户指示：无用的空间浪费）：
  `Packages/com.collin.stretch/屏幕录制 2026-11-03 173638.mp4`（107.9 MB，**已入库** ⇒ 走 `git rm`）
  与 `屏幕录制 2026-11-04 220853.mp4`（49.2 MB，未入库 ⇒ 直接删），共释放 **约 157 MB**。
  删前已核验：两个文件的资源 GUID 在全工程 `.asset/.unity/.prefab/.cs/.json` 中**零引用**，
  且 stretch 包当时无锁；`Packages/com.collin.*` 下不再有任何「屏幕录制」文件。
  附带收益：控制台那条 `Color primaries 0 ... WindowsMediaFoundation` warning
  正是 VideoPlayer 解码 stretch 包内源片产生的，源片清除后该告警随之消失。

### Chores
- 按 AGENTS §3 为 stretch 包临时取锁（`repo-cleanup-2026-11-04`）执行清理，完成后释放。

### Fixed（成品质量修正，session stealth-ux-2026-11-04）
- **玩家出生半截埋地**（用户实机反馈）：根因在 `PlayerStealthController.ApplyCapsuleShape()`——
  旧公式 `localPosition.y = (h − 站高) × 0.5` 在**站立时算出 0**，把整根胶囊中心压到脚底。
  新增 `ThirdPersonRig.CapsuleVisualPose(h)`（纯函数：`scale.y = 中心 = h/2`，底边贴 y=0）接管纵向，
  x/z 仍保留作者摆好的粗细。场景与预制件经重新生成/沉淀后 `m_LocalPosition.y` = 0.9。
- **按 R 重开没效果**（用户实机反馈）：`ResetRound()` 原先只清本组件计时。现**全量复位六处**：
  本组件结果与计时 → 撤离区驻留 → 玩家察觉状态 → 所有哨兵叠加层 → 巡逻时钟归零 → 玩家回出生点。
  出生点由 `RecordSpawnPoint()` / `Awake` 捕获（**不写死坐标**，保住 D-13 拆箱即用）；
  复位先关 `CharacterController` 再写 transform（否则被物理层按旧碰撞结果拉回）。
- 出结果即**冻结玩家输入**（`PlayerStealthController.Frozen`），结算画面不再被遮罩后的跑动污染。
- **HUD 与结算面板职责切分**：HUD 撤掉胜负横幅与 R 键处理（两处同帧响应重开会重复触发复位）。

### Added（结算界面 + 场地边界，同上）
- `Runtime/StealthResultModel.cs`：结算卡片**数据层纯函数**（标题/归因行/配色/操作提示），
  `Playing` 返回 null。拆出来是为了脱离 Play 态断言文案与数值格式（Play 探针本会话实测不可靠）。
- `Runtime/StealthResultPanel.cs`：胜利/失败结算界面（IMGUI：压暗遮罩 + 强调条 + 标题 +
  用时/察觉峰值/惊动哨兵/撤离点四行归因 + 两个按钮）。**刻意不用 uGUI**——uGUI 需要场景里的
  `Canvas + EventSystem`，会破坏 D-13「预制件拖进空场景即可玩」（空场景无 EventSystem 时按钮失灵）。
  字号与卡片尺寸统一按 `Screen.height/1080` 缩放，分辨率变化时重建样式。
- `Runtime/StealthArena.cs`：场地矩形边界纯函数（`Clamp`/`Contains`/`Inset`，半宽 ≤0 即关闭）。
  玩家控制器接其做**硬钳制**（走 `_cc.Move` 而非直接写 transform，避免碰撞状态与实际位置脱节）。
  **默认关闭**：预制件拖进任意小场景不该凭空出现隐形墙，示例场景由生成器显式开启。

### Added（示例场景复杂化 + 外侧围栏，同上）
- **四面围栏**（54×54 内腔、高 4m、转角不重叠以免 z-fighting），全部标静态**参与导航烘焙**——
  墙若不烘焙，NPC 会穿墙追出去，等于把上一轮的穿模问题换个位置复现。
- 掩体从 3 个扩到 **18 个实体占位**：南侧掩体群 ×4、西侧长墙、东北角仓库（四面墙 + 净宽 3.8m 门洞）、
  撤离区立柱 ×2。门洞原设计 1.4m，因 legacy 烘焙按角色半径腐蚀可行走面会封死缺口而加宽。
- 哨兵 2 → **3 名**（新增 `DepotRing` 环绕仓库），路线资产 `Sample/Profiles/DepotRing.asset`。
- 撤离点 1 → **2 处**（北侧主点驻留 0.6s / 仓库内备用点驻留 1.2s），逼出直线冲北与绕东穿仓库的取舍。
- 新增**路线间隙自检** `VerifyRouteClearance()`：巡逻基线不参与碰撞，路点连线压到实体就会当众穿墙，
  且不会报错——按 0.5m 采样逐段检查，PingPong 路线不检查不存在的闭合段（否则假阳性）。
- **绕行校验改为按场景真实物体取起终点**：旧版硬编码 (0,±14)，而出生点在 (0,−22)、撤离点在
  (0,24)/(16,16)——布局一改，校验量的是**地图上不存在的位置**却照样"通过"，等于没校验。
  现逐撤离区校验可达性，绕行判据只对主点强制（仓库点走东走廊，要求它绕行会把好布局误报成失败）。

### Verified（2026-11-04 21:20 实测）
- 编译 `failed:false`；`console clear` 后重跑 recompile + test run → `consoleErrors:0 / consoleWarnings:0`；
  全量 **250/250**（本包 **121** = 原 99 + 新增 `GameRuleResetTests` **22**；余 129 属 citymap，
  由并行会话新增 1 条）。用例数按 `grep -c "^\s*\[Test\]"` 逐文件实测，非估算。
- Play 截图取证：蓝色胶囊**完整立于地面**（底部投影可见），四面围栏入画，地面警戒投影正常。
- Play 截图取证：胜利结算卡片实际弹出——绿色强调条 + "撤离成功" + 四行归因 + 两按钮 + 底部按键提示。
- Play 探针取证：`ResetRound()` 后 玩家 (0,0.05,24) → **(0,0.05,−22)**（=出生点）、
  时钟 42.5 → **0.0**、用时 → 0.00、三哨兵 stage 全回 Patrol、两撤离区 reached 全 False、frozen False。
- 生成器日志：`[Clearance] 三条路线与 20 个实体占位无交叉`；
  `ExitZone_North 拐点=3 直线=46.00 路径=46.06 横向偏离=1.17`；`ExitZone_Depot 拐点=4 横向偏离=0.72`（两点均可达）。

### 已知环境限制（本会话实测，非本包缺陷）
- **共享编辑器后台不推进 Play 循环**：窗口失焦时 `frameCount`/`Time.time` 完全冻结
  （实测 4 秒间隔两次取样均为 13229/41.1）；`set_auto_tick` 只保活编辑器 UI、**不驱动 Play 循环**。
  ⇒ 驻留/察觉这类**需要时间累积**的玩法只能靠 `focus` 抢前台取证，且每次 CLI 调用都会抢回焦点。
- **失败（被抓）分支未取到目视截图**：与胜利分支共用同一段绘制代码，数据层已由单测覆盖
  （`Result_CaughtAttributesToPeakSuspicion`），但"卡片实际弹出"缺画面证据 ⇒ 记入未通过项 U-7。
- 校验器 `assets-layout` 红灯来自另一会话留在 `Assets/Scenes/testscene.unity` 的未跟踪文件
  （无提交记录、citymap 锁仍在活动），与本包改动无关，未代修。

### Added（M5 部分：预制件沉淀 + 拆箱即用验证）
- `Editor/StealthPrefabBaker.cs`：把示例场景的玩家与哨兵单元沉淀为
  `Sample/Prefabs/StealthPlayer.prefab` / `StealthGuard.prefab`，内置**引用完整性自检**
  （Body 三件套、路线根引用、路线/视野资产非空、T 周期 > 0、沿周期求值有位移）。
  菜单 `Tools ▸ Collin ▸ Stealth ▸ 沉淀示例预制件（玩家+哨兵）`。
- `Editor/StealthPrefabVerifier.cs`：**内存临时场景只放预制件 + 一个时钟**（不建地面、不建掩体、
  不手写资产）验证拆箱即用；检查 `PrefabInstanceStatus.Connected` 与跨包枚举**新增数量差值**；
  验完 `CloseScene` 丢弃、不落盘。菜单 `验证预制件自洽（内存临时场景）`。
- `Sample/README.md`：由 M0 规划改写为实际落位 + 三条使用路径。
- 实测：两个预制件已落盘（9393 B / 7766 B），GUID 级引用比对全通过，
  验证日志 `actors新增=1 cycle=28.00s 结论=通过`；门禁三项全绿（编译 0 错、
  归零后 `consoleErrors:0 / consoleWarnings:0`、全量 210/210、校验器绿灯）。
- **M5 余项（已于本轮 0.5.0 完成）**：`ShowcaseMedia/stealth_demo.mp4` 两段式实录视频 + 展板接入。
  由用户手动实录（自动录屏此前四次确认不可行），本包工具负责转码入库与 `Showcase.json.video` 填充。
- M6：冻结对外契约 `Docs/Modules/00-Contract.md`（逐签名核验 DTO 全部来自 core），交付给 B 包消费。
- 待补证据：性能验收（50 NPC ≥ 55 FPS）；编辑态回溯「拖滑块 NPC 连续移动」的截图（条目 71 部分通过）。

### Fixed（M5 实现轮自查，均为自造）
- **示例场景凭空消失**：原先 `DeleteAsset` 场景文件后再向同一路径 `SaveScene`。Unity 既不允许覆盖
  同路径的已打开场景、也不允许卸载最后一个已加载场景 ⇒ 旧文件被删、新文件没写出来
  （已入库场景一度需 `git checkout` 恢复）。改为**打开目标场景 → 清空根物体 → 原地 `SaveScene`**。
- **编辑态材质泄漏刷 3 条 error**（破 §1.6b 归零）：上色用 `renderer.material.color`；
  改为显式 `new Material(sharedMaterials[0])` 写回 `sharedMaterials`。
- **自检断言写错**：断言「哨兵预制件根上应有 `PatrolRouteRoot`」，而该组件按分层挂在 `RouteRoot`
  子物体上 ⇒ 必失败并报自造 error；改由专项 `VerifyBody` 负责。
- **计数断言方式错**：跨包枚举走全工程搜索，此刻还加载着其他会话的场景 ⇒ 绝对值既会误报也可能凑数假绿；
  改为实例化前后的**差值**。
- 生成器加两道保护：他人场景有未保存改动时中止并日志提示（不弹模态窗）；存盘失败输出明确 error。

### 环境事实（新增）
- 编辑器处于 Play 模式时执行 `test run`：TestRunner 因无法建/还原场景刷出一批
  `This cannot be used during play mode` error，**且 `Summary.Total` 返回 0**——不是「工程里没有测试」。
  跑门禁前先 `editor status` 确认 `playMode: stopped`。
- ~~`script eval_file` 在 **Play 模式下可能整体静默失败**~~ ⇒ **本轮（stealth-ux）证伪**：
  Play 态探针可正常执行。当时失败的两个真因是①顶层语句里混写类声明（`CS1001`）、
  ②用了不存在的 `FindAllObjectsByType`。正解：纯语句体 + `FindAnyObjectByType` + `Debug.Log` 再读控制台。
- **真正的限制是「窗口失焦时 Play 循环不推进」**（实测 `frameCount`/`Time.time`/时钟值三值冻结）；
  `set_auto_tick` 不驱动播放循环，`focus` 能恢复但每次 CLI 调用都会抢回焦点
  ⇒ 依赖时间累积的取证必须「探针 + focus + 立即截图」连做。

> 以下条目同样归入 **[0.5.0]**（本轮视频已入库，不再挂 Unreleased）。

### Added（导航网格 + 地面投影专用着色器，session stealth-nav-2026-11-04）
- **可选子程序集 `com.collin.stealth.Navigation`**：基于**内置 `com.unity.modules.ai` 的 legacy NavMesh**
  实装寻路（`NavMeshPathBridge`：`NavMesh.SamplePosition` 吸附起终点 + `NavMesh.CalculatePath` 出折线）。
  **刻意不引入 `com.unity.ai.navigation`**（DESIGN D-06：付费 HAVOK、且其 Agent 按帧积分会摧毁位姿纯函数地基）；
  只用**一次性几何查询**，不引入任何按帧移动组件。
  - `NavMeshPathBridgeInstaller`：Play 态经 `RuntimeInitializeOnLoadMethod` 注册（幂等，先判存在不撞 core 冲突报错）；
  - `Navigation/Editor/StealthNavEditorBootstrap`：`[InitializeOnLoad]` 补**编辑态**注册
    ——注册点自带在可选程序集内，主包 Editor 不引用它，**删掉该目录主包零影响**（可选性成立）。
- **绕行只作用于叠加层**（地基纪律）：`GuardBrain` 反应位移改为**沿导航折线逐段推进**
  （`AdvanceAlongPath`），巡逻基线仍是纯函数结果；不可达/无桥时**退回直线叠加**，绝不原地停住。
  重建路径时把追踪点**钉在当前位置**（否则每次重路由都会把 NPC 拽回路径起点 = 抖动/瞬移）；
  目标移动 >0.6m 或超过 `_repathInterval` 才重路由（追击时玩家会跑、掩体会变）。
- **`IPathBridge` 扩展为路径接口**（`TryFindPath(from,to,List<Vector3> corners)` + 保留 `TrySnap`），
  并写明"corners[0] 必须是出发点"的锚点契约；`NullPathBridge` 同步实现。
- **版本宏守卫补成真的**（自查）：asmdef 的 `versionDefines` 声明了 `STEALTH_NAVMESH_AVAILABLE`
  但代码里**没有任何 `#if` 用到它**——宏是虚设的，真缺内置 AI 模块时该程序集会整体编译失败，
  「可选」承诺失效。现改为 `#if` 包住 `using UnityEngine.AI` 与整个 NavMesh 实现，
  并在 `#else` 提供同名**降级实现**（一律返回 false ⇒ 行为等同「没烘焙 NavMesh」，退回直线叠加）。
- `Resources/StealthGroundProjection.shader`：**地面投影专用半透着色器**
  ——`Queue=Transparent` + `ZWrite Off` + `ZTest LEqual` + `Offset -1,-1` + `Blend SrcAlpha OneMinusSrcAlpha`。
  `LightMode` 用 `SRPDefaultUnlit`（已在 `DrawObjectsPass.cs:87` 核实 URP 的 shaderTagIds 首位即它，非凭记忆）。
- 示例场景接入导航：地面与三块掩体标 `isStatic`（烘焙只收静态物体），
  玩家/两哨兵/时钟/信标**递归取消**静态（`CreatePrimitive` 的子物体默认是静态，不处理会把角色自己烘成障碍）。
  烘焙流程 = 存盘 → `UnityEditor.AI.NavMeshBuilder.BuildNavMesh()` → 绕行校验 → **再存盘**，
  产物 `Sample/Scenes/StealthSample/NavMesh.asset` 随场景入库。

### Fixed
- **NPC 追击穿掩体**（用户反馈）：反应位移改走导航折线，实测绕行成立（下条证据）。
- **地面色块剧烈闪烁**（用户反馈「要么改 shader，要么半透 + Z 略高于地面」）：**两条一起做**。
  根因是复用 `URP/Unlit`——它 `Queue=Geometry` + `ZWrite On`，即使 alpha<1 也没进透明队列，
  与地面争深度；且多边形 y 与地面**完全共面**。修：专用着色器（透明队列 + 关深度写入 + 深度偏移）
  **加** 可配 `_clearance`（默认 0.02m）抬升，二者叠加才稳定不闪。找不到专用着色器时兜底并**一次性警告**。
- **绕行校验判据本身是错的**（自查）：原用「路径总长 > 直线 + 0.3m」。28m 直线绕 1.6m 宽的掩体，
  几何上总长增量仅 0.13m ⇒ **该判据在物理上不可能满足**，把已经生效的导航误判为失败。
  改用「拐点相对直线的**最大横向偏离** > 0.4m」。同时给 5 个早退分支各补**诊断日志**
  （静默 false 只能靠猜定位，本轮实测教训）。

### Verified（2026-11-04 18:13 实测，含宏守卫改动后复跑）
- 绕行：`[Nav] 起点=(0.0,0.1,-14.0) 终点=(0.0,0.1,14.0) 拐点=4 直线=28.00 路径=28.13 最大横向偏离=1.17`
  ⇒ 「导航烘焙 完成并通过绕行校验」；4 个拐点 + 1.17m 横向偏离即掩体已成障碍的直接证据。
- 门禁三项全绿（末次 18:13，即宏守卫改动**之后**复跑）：编译 `failed:false`；`console clear` 后重跑 recompile + test run
  → `consoleErrors:0 / consoleWarnings:0`；全量 **227/227**（CityMap 128 / **Stealth 99**）；
  校验器 **结论: 绿灯**。

### Changed
- 2026-11-03 用户拍板 Q-A1~Q-A5 后调整设计：新增 `com.unity.inputsystem` 依赖；路点改为相对坐标 + 预制件承载；**听觉/声音侦测移出本包范围**（最小可用子集），`StealthMask` 对应位改为预留位不启用；玩家移动控制器由「交给外部工程」改为「包内自带、拆箱即用」。

### Added（成品质量修正：用户三条实机反馈，session stealth-fix-2026-11-04）
- `ThirdPersonRig.cs`：操控数学纯函数层——`MoveDirection`（**相机相对**四向：A/D=侧移不是转身）、
  `SmoothAngle/SmoothValue`（帧率无关的 `1-e^(-k·dt)` 阻尼）、`ClampPitch`（上下限填反自动交换）。
- `StealthStageVisual.cs`：档位配色与中文名统一口径（HUD 与地面投影共用，杜绝「HUD 说红地上画黄」）。
- `GroundVisionProjection.cs`：**与判定同源地面投影**（纯函数）：外缘 = sqrt(maxDist²−Δh²)、
  内缘 = 俯仰盲区 Δh/tan(半垂直角)，近身豁免盖住盲区时收合成连续扇形；遮挡射线裁边。
- `StealthGroundProjection.cs`：Play 态半透明填充（`Graphics.DrawMesh`，不污染预制件层级、不碰 `.material`），
  随哨兵档位变色；着色器 URP→Sprites→内置兜底、失败只警告一次。
- `StealthExitZone.cs`：撤离区（纯几何圆判定 + 驻留计时，不依赖触发器/Rigidbody）。
- `StealthGameRule.cs`：对局规则——抵达撤离区=通关，察觉度顶格持续=被抓；R 重开；Manual 预览态不推进。
- `Tests/Editor/GroundProjectionTests.cs`：**17 条**修正守卫（投影半径与判定逐位吻合、
  盲区环口收合、贴脸可见且仍受遮挡、A/D 严格反向、平滑帧率无关、撤离区驻留/零驻留/高度窗）。

### Changed
- **玩家操控语义重写**：镜头角与模型角彻底分离（旧实现共用 `_yaw` ⇒ 按 A/D 变转镜头）；
  相机位置与朝向共用同一阻尼并移到 `LateUpdate`（旧实现朝向瞬时、位置单独 Lerp ⇒ 拖拽抖动）；
  新增 Shift 跑。`PlayerStealthController` 公开 `CameraYaw/MoveYaw`。
- **判定点从脚底改为身体中部**：`DetectionState.BodyPoint`（默认 0.95m、蹲伏下移），
  `GuardBrain` 感知、自身感知、安全岛掩码与投影全部改用同一口径。
- 示例场景：玩家出生点 (0,-16)→(0,-22)（实测出生 30 秒即被抓获，演示不公平）、地面扩到 60×60m、
  北侧 (0,16) 放撤离区 + 信标柱、哨兵 Body 挂投影组件、`GuardNormal.asset` 显式配 proximityRadius=1.8
  与遮挡层全开；HUD 增加撤离指引/被抓倒计时/胜负横幅与 R 重开。

### Fixed
- 重叠丧失目标（越近越瞎）、A/D 转镜头、拖拽抖动、投影边界与实际不符（旧实现把俯仰角当射程上限，
  14m 视距只画 ~4.9m——正是用户截图里的现象）、无通关点，共五条实机反馈全部闭环。
- 探针算法错误（外缘半径按世界原点量得 22.08 假值）→ 按脚底量后 13.98 与期望**逐位吻合**（V3 探针）。

### 门禁（末次实测 17:19）
编译 `failed:false`；`console clear` 后重跑全部验证动作 `consoleErrors:0 / consoleWarnings:0`；
全量 **227/227**（Stealth **99**）；校验器 **结论: 绿灯**。Play 探针实测：
`outerR=13.98=expectR`、`D=(1,0) A=(-1,0)`、`overlapFoot=True overlapBody=True`、
`exits=1 rules=1 projections=2 outcome=Playing`。

## [0.4.0] - 2026-11-04
### Added
- **M3 哨兵决策与反应层**：
  - `IPatrolModifier`：叠加层接口（`Tick(dt, baselineWorldPosition, timeIndex)` / `GetOverlay` / `ResetOverlay`）。
    设计要点——状态机的反应**只能作为基线之上的可关断偏移**，绝不回流进巡逻基线，
    否则辅助台的 T 周期回溯显示的就是「上一局玩过的痕迹」（DESIGN R-1）。
  - `GuardBrain`：四档状态机（巡逻→疑惑→警觉→搜索→巡逻）、可疑点记忆、驻留/环绕计时、
    同伴通报（Alerted 时把半径内同伴拉进疑惑并同步可疑点）、察觉事件经 core 的
    `StealthDetectedChannel` 广播（载荷含 `timeIndex`，供辅助台钉回时间轴）。
  - `IPathBridge` + `NullPathBridge`：寻路接缝（DESIGN D-06）。默认不注册 = 纯直线叠加；
    工程接入导航时由可选子程序集实现并注册，**本包不引用任何导航类型**。
  - `PatrolAgent` 成为唯一位姿合成点：`ApplyBaselineAt(t, dt)` 先驱动叠加器决策、
    再合成偏移与注视朝向、最后写 transform；`dt <= 0` 时**完全跳过叠加层**（编辑态预览语义）。
  - `StealthWorld.ResetAll()` 一并复原哨兵反应状态（档位 + 叠加偏移）。
- **M4 编辑器工具与可玩成品**：
  - `PatrolRouteRootEditor`：Scene 视图直接拖路点（世界显示、局部写回，D-09）、
    Inspector 逐点改停留/段速、追加/删除、从子物体烘焙路点、T 周期与朝向指示线。
  - `StealthScrubWindow`（Tools ▸ Collin ▸ Stealth ▸ 巡逻回溯窗口）：T 周期滑动条实时推送 NPC 位姿，
    自动播放 / 回到 t=0 / 恢复 Auto，并列出各单位在该时刻的世界坐标与朝向。
    **编辑态（非 Play）可用**——这是 M1「位姿为时间纯函数」换来的核心能力。
  - `StealthGizmos`：巡逻折线（路线根常显）+ 视野扇形（选中时绘制），与判定共用同一数据源。
  - `PlayerStealthController`（**拆箱即用**，D-13）：WASD/方向键移动、Ctrl 蹲伏（切换或按住两种模式）、
    第三人称跟随相机（右键环绕、防穿墙收距）、蹲伏自动改胶囊高度并向 L4 上报曝光度。
    输入后端按 `ENABLE_INPUT_SYSTEM` / `ENABLE_LEGACY_INPUT_MANAGER` 三档自动选择，
    两路皆不可用时输出一次性明确 warning。
  - `StealthHud`：IMGUI 显示察觉度条、各哨兵档位与可疑点、操作提示（零依赖、零场景配置）。
  - `StealthSampleSceneBuilder`（Tools ▸ Collin ▸ Stealth ▸ 生成可玩示例场景）：
    一键产出 `Sample/Scenes/StealthSample.unity` + 3 个资产（两条路线 + 一份视野参数）；
    场景含地面、3 个掩体、2 名哨兵、玩家、相机、HUD、时钟，**打开即可 Play**。
- `Tests/Editor/GuardBrainTests.cs`：**10 条** M3 用例（档位流转、回落链、纯预览不改状态、
  叠加不污染基线、事件广播、同伴通报、接缝生效、复原联动）。

### Fixed
- **玩家初始朝向写死 180°**：与场景摆放无关，导致打开示例后画面里看不到掩体与哨兵
  （第一张 Play 截图实证）。改为取 `transform.eulerAngles.y`。
- **视线遮挡判据的两难**：终点余量收 0.5m 会让贴身掩体被跳过（隔墙看得见），
  收 0.1m 又会让玩家自身胶囊被当成遮挡（哨兵永远看不见玩家）。
  正解 = 全长射线 + 按身份排除：`CanSee` / `CanSeeFrom` 新增 `targetOwner`，
  `DetectionState` 与 `GuardBrain` 均传入目标 transform。
- **`GuardBrain.All` 在编辑态恒空**：只靠 `OnEnable` 登记会让 HUD 与 `ResetAll` 静默失效
  （EditMode 不跑生命周期回调，本包第三次撞同一族坑）。改为剪枝 + 空则按场景重建。
- **回溯窗口三处失效写法**（自查发现）：`readonly` 字段被赋值、计时基准在 `OnGUI` 内被每次重绘重置
  （滑块永远不动）、编辑态提前 return（「不 Play 也能看」直接落空）。
- **共享编辑器纪律**：示例生成工具原用 `NewScene(Single)` + 模态对话框——前者会丢弃其他会话
  未保存的现场，后者会把指令通道卡死（本轮实测确实卡住一次）。
  改为 Additive 建内存场景 → 存盘 → 关闭，并把所有 `DisplayDialog` 换成日志。
- **状态机驻留计时被重复累加**：同一帧内 `_stageTime` 在两个分支各加一次，驻留时长实际减半且互相覆盖。
- **叠加偏移以当前 transform 为锚**：与巡逻基线构成循环依赖；改为以基线世界位置为锚收敛。

## [0.3.0] - 2026-11-03
### Added
- **M2 四层察觉管线**（Runtime 11 文件）：
  - `VisionProfile`（SO，共享 Model）：水平/垂直视野角（全角）、视距、视线高度、遮挡 LayerMask、
    察觉上升与回落速率、疑惑/警觉双阈值、光照敏感度、视线检测间隔帧数；
    配 `Set*` 写入面（§0.1 原则 2 的 Controller 入口），并保证疑惑阈值不会倒挂过警觉阈值。
  - `VisionGeometry`（静态纯函数）：L1 球检 → L2 水平锥 + 俯仰锥 → L3 视线委托注入；
    零向量/重合点等退化输入安全。**独立成纯函数的理由与 M1 同源**——辅助台要按 t 时刻的 NPC 位姿
    在编辑态问「这一点看不看得到」，绑在 transform 上就只剩「当前位置」一种问法。
  - `VisionVolume`（实例）：启用开关、视距倍率、视线高度/水平角覆盖（均为实例级差异）；
    L3 内部处理遮挡层过滤 + 自身与子节点排除 + 射线预算 + 错峰缓存；
    新增 `LineOfSightOverride` 静态注入点，使遮挡翻转与预算分支可在 EditMode 确定性断言。
  - `QueryOptions` / `StealthMask`：前者是四层可选掩码（值序与 core 的 `StealthQueryLayers` 对齐，有守卫用例）；
    后者是包内 L4 修正位（`SafeIsland=8` 消费辅助台数据，`4/16/32/64` 预留不启用）。
  - `AwarenessModel`（纯函数）：察觉度积分、档位导出、L4 曝光倍率合成（蹲伏 ×0.35、光照线性、安全岛归零）。
  - `DetectionState`（实例）：累积察觉度与档位；`EvaluateOnce(dt<=0)` 为**纯预览语义**——
    只更新可见性、绝不推进真实累积，故辅助台回溯 scrub 不会污染玩法状态。
  - `VisionRegistry` / `DetectionRegistry`：读时重建的保守失效注册表。
  - `StealthPerformance`：射线帧预算 + 按 actorId 取模错峰（帧号由外部显式报告，不自读 `Time.frameCount`）。
  - `StealthIds`：跨包单位 id 的**唯一算法**入口。
  - `StealthWorld` 补实现并注册 `IVisionWorld` / `IDetectionWorld`；`StealthClock.Update` 每帧复位射线预算。
- `Tests/Editor/VisionDetectionTests.cs`：**30 条** EditMode 用例（本包合计 72 条）。

### Fixed
- **察觉度永不回落**：未被看见时误用 `default` 阈值组（回落速率 0）⇒ 察觉度永久钉住、玩家无法脱身。
  改为取场上任一启用视野的 Profile 参数（ACCEPTANCE F-7）。
- **跨包 id 可为负**：`GetEntityId().GetHashCode()` 会为负，撞上契约 `-1`「不限定单位」哨兵，
  且巡逻侧与视野侧算法分叉会让 `CanSeeAt` **静默退回当前位姿**。统一走 `StealthIds.Of`（内部强制非负）
  （ACCEPTANCE F-8）。

## [0.2.0] - 2026-11-03
### Added
- **M0.5 跨包契约落地（方案 P 闭环）**：
  - `Runtime/StealthWorld.cs`：实现 core 的 `IPatrolWorld` + `IStealthScrubDriver` 并经 `Services` 注册；
    位姿/路点/轨迹/单位枚举全部以 core 的 DTO 出入（不外泄本包类型）；
    `ScrubTo(t)` **显式推送**全部位姿（编辑态无帧循环，B 包滑动条依赖此）；
    `RefreshPreview()` 供 Inspector 改完路点即时无帧刷新；`TrySetWaypoint` 是「按玩家线路反推巡逻路点」的落点。
    `IVisionWorld` / `IDetectionWorld` **故意不注册**（M2/M3 未完成，B 侧走优雅降级）。
  - `Editor/StealthEditorBootstrap.cs`：`[InitializeOnLoad]` 编辑态注册钩子 + 撤销回调触发预览刷新
    （补上「生命周期回调在编辑态不执行」的缺口，见 ACCEPTANCE F-5）。
  - `Tests/Editor/StealthContractConsumerTests.cs`：**消费方视角** 16 条用例——4 个 Fake 实现只用
    `Collin.Core.Stealth` 符号，证明 B 包不引用 A 也能实现与取用契约；含跨包镜像枚举值序一致性守卫。
- `Docs/ACCEPTANCE.md`：M0.5 增量条目 27-33 + 实现轮自查记录。

### Fixed
- `StealthWorld` 用 `Object.GetInstanceID()` 触发 Unity 6 的 CS0619（该 API 被当 error 报）；
  改用 `GetEntityId().GetHashCode()` 作 actorId，并同步契约注释声明「域重载后会变，消费方不得持久化」。
- `StealthWorld` 同时 using `System` 与 `UnityEngine` 时 `Object.FindObjectsByType` 二义性（CS0104）；改全限定写法。
- 适配层原以成员列表复用返回 `Actors` / 路点集合，会让消费方持有的旧引用被下次访问改写；改为每次新建容器。
- core 契约 `StealthPoseDto.HasLeg` 仅判 `legIndex >= 0`，而 `default` 的 `legIndex` 恰为 0
  ⇒「无数据」被误读成「位于第 0 段」；配合 core 新增 `valid` 位收紧判据。

## [0.1.0] - 2026-11-03
### Added
- **M1 巡逻求值层**（Runtime，5 个源文件）：
  - `PatrolRoute`（ScriptableObject，共享 Model）：路点存**相对路线根的局部坐标**（D-09）；
    每点可设停留秒数与段速度（0 表示沿用默认速度）；支持 Loop / PingPong；
    内部维护「分段累计长度表 + 布局哈希」，改路点后自动失效重建；
    `EvaluatePose(t)` 为**时间的纯函数**（D-01）——弧长定位 + 段内线性插值，不做逐帧积分；
    `BuildTrajectorySamples()` 提供等弧长采样（B 包分层绘制复用同一口径）；
    `WrapTime()` 把任意时间折算到 `[0, T)`。
  - `PatrolPose`（struct）：局部位姿 + 段索引 + 段内进度 + 停留标志 + 所属周期，结果自解释。
  - `PatrolRouteRoot`（MonoBehaviour）：路线根，提供局部↔世界换算、`ToWorld(pose)`、
    `BakeWaypointsFromWorld()`（从世界位置反推路点）、`BuildWorldTrajectory()`。
  - `PatrolAgent`（MonoBehaviour）：实例级差异数据（相位偏移、速度倍数、朝向跟随速度）；
    位置**硬贴**保持折线精确、朝向单独缓动（D-11）；`LateUpdate` 由 `StealthClock` 驱动。
  - `StealthClock`（MonoBehaviour）：`Auto` / `Manual` 双模式时间索引，`Manual` 是编辑态回溯的唯一入口。
- **Tests/Editor**：`com.collin.stealth.Tests` 程序集 + **24 条 EditMode 用例**。
- `Docs/ACCEPTANCE.md`：条目 1-17 全部补齐实测证据。

### Fixed
- `PatrolRoute` 段朝向把 `Mathf.Atan2` 的弧度乘了 `1f/Mathf.Rad2Deg`，朝向被压缩（+X 段应 90°、实得 0.027°）；改乘 `Mathf.Rad2Deg`（ACCEPTANCE F-4）。
- `StealthClock` 仅在 `Awake/OnEnable` 登记静态单例，**EditMode 下生命周期回调不执行 ⇒ `Instance` 恒 null**，会让 B 包的 T 周期回溯在 M4 才暴露走不通；`Instance` 增加 `FindFirstObjectByType` 兜底（ACCEPTANCE F-5）。
- `PatrolAgent.BuildWorldTrajectory` 重复调用根组件换算，导致世界坐标二次变换；改为单次取用。

## [0.0.1] - 2026-11-03
### Added
- 首个版本：**仅包骨架**，不含功能代码（AGENTS.md §4「DESIGN 先于代码」）。
- `package.json`（依赖 `com.collin.core@0.2.0` + `com.unity.inputsystem@1.20.1`，符合 §5 单向依赖）。
- `Runtime/com.collin.stealth.asmdef`（引用 core + Unity.InputSystem，`versionDefines` 探测输入包，`validateReferences: true`）。
- `Editor/com.collin.stealth.Editor.asmdef`（includePlatforms=Editor，引用 core + 本包 Runtime）。
- `Showcase.json`（category=玩法组件，order=30.0；`video` 暂空，M5 补 `ShowcaseMedia/stealth_demo.mp4`）。
- `Docs/DESIGN.md`（**已评审**，含 13 条决策与 Q-A1~Q-A5 决议表）、`Docs/ACCEPTANCE.md`、本文件。
- `Sample/`（README 规划预制件落位）、`ShowcaseMedia/` 占位目录。

### Changed
- 无（首版）。

### Removed
- 无（首版）。
