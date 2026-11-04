# 交接单 — com.collin.stealth

> 覆写式交接单（AGENTS §4）。最近更新：2026-11-04（会话 `stealth-video-2026-11-04`）
> **包版本 0.5.0**（§6 两段式演示视频已由用户实录入库、`Showcase.json.video` 已填充 ⇒ M5 全部达成）。
> 本轮：收版 + 展板接入 + **清理工程内所有手录原始视频**（释放约 157 MB，含已入库的两个，走 `git rm`）。
> 上一轮（`stealth-ux-2026-11-04`）的四条成品修正见 §1「上一轮」与 CHANGELOG [0.5.0]。

## 1. 已完成（本轮 = 会话 `stealth-video-2026-11-04`）

### A. §6 演示视频交付 + 展板接入 + 收版 0.5.0
- 用户手动实录 44.7 s 完整演示（源片在 `C:\Users\Empty\Videos\屏幕录制\`），本包用 ffmpeg
  转码为 `ShowcaseMedia/stealth_demo.mp4`（1616×880，CRF 25，**保留音轨** —— 首版误加 `-an`
  把声音去掉了，已重压覆盖）。
- `Showcase.json.video` 由空串填为 `ShowcaseMedia/stealth_demo.mp4`；`summary` 同步补入
  近身察觉 / 地面投影同源 / 导航绕行 / 场地硬边界 / 双撤离点 / 胜负结算（原摘要落后两轮交付）。
- **展板无需改场景文件**（§6 禁止）：`Assets/Showcase/ShowcaseUI.cs` 经 `ShowcaseRuntime.Load()`
  运行时枚举 `Packages/com.collin.*/Showcase.json`，视频由 `ResolveVideoPath()` 按
  「工程根 + Packages + packageId + 相对路径」解析 ⇒ 填字段即完成接入。
- `package.json` version **0.4.0 → 0.5.0**（§8 钉死版本未动；只改版本串、无依赖变化，
  内嵌包在资源刷新时自动重读清单）。

### B. 清理工程内所有手录原始视频（用户指示：无用的空间浪费）
- `Packages/com.collin.stretch/屏幕录制 2026-11-03 173638.mp4`（107.9 MB，**已入库** ⇒ `git rm`）
  与 `屏幕录制 2026-11-04 220853.mp4`（49.2 MB，未入库 ⇒ 直接删），连同各自 `.meta`，
  共释放 **约 157 MB**。删前核验：两文件 GUID 在全工程 `.asset/.unity/.prefab/.cs/.json` 中**零引用**。
- 为 stretch 包临时取锁（该包当时无锁），完成后释放。
- 附带收益：控制台那条 `Color primaries 0 ... WindowsMediaFoundation` warning
  正是 VideoPlayer 解码 stretch 包内源片产生的 ⇒ 源片清除后该告警随之消失。

### C. 本轮约定（务必延续）
- **用户手录的视频内容不做抽帧验证**，除非用户提出修改要求。只核验交付事实：
  文件存在、时长/编码、展板路径可解析。
- **包内禁止存放未压制的手录源片**，只留 `ShowcaseMedia/*_demo.mp4` 成片。

---

### 上一轮（会话 `stealth-ux-2026-11-04`）四条成品修正 —— 已提交 `03c372e`，摘要保留在此

#### ① 出生埋地（根因不在坐标，在公式）
- **真根因**：`PlayerStealthController.ApplyCapsuleShape()` 的 `localPosition.y = (h − 站高) × 0.5`
  在**站立时恰好算出 0** ⇒ 整根胶囊中心落到脚底。场景里 `CapsuleVisual` 摆的 y=0.9 是对的，
  但 Awake 时被这个公式**覆盖成 0**——所以「改场景坐标」是错方向，改公式才是对的。
- 新增 `ThirdPersonRig.CapsuleVisualPose(h)` 纯函数：`scale.y = 中心 = h/2`（Unity Capsule 基元
  `scale.y=1 ⇔ 总高 2m`），底边 = 中心 − scale.y = 0 ⇒ 贴脚底。x/z 保留作者摆的粗细（所见即所得）。
- 证据：单测 `CapsulePose_*` 3 条 + `Controller_AppliesCapsulePose_AndPreservesAuthorXZ`；
  Play 截图（验收 118）蓝色胶囊完整立于地面、底部投影清晰。

### ② 结算界面（IMGUI，不用 uGUI）
- `StealthResultModel`（纯函数数据层）+ `StealthResultPanel`（IMGUI 绘制）。
  **不用 uGUI 的理由（D-14）**：uGUI 要场景里有 `Canvas + EventSystem`，会破坏 D-13
  「预制件拖进空场景即可玩」——空场景里表现为**有 UI 但按钮点不动**。
- 卡片含：压暗遮罩、强调条、标题、**四行归因**（用时 / 察觉峰值 / 惊动哨兵 / 撤离点）、
  两个按钮、底部按键提示。字号与卡片尺寸统一按 `Screen.height/1080` 缩放（IMGUI 无 Canvas Scaler）。
- **HUD 撤掉胜负横幅与 R 键处理**：两处同帧响应重开会**重复触发复位**，且横幅与卡片同时出现自相矛盾。
- 证据：Play 截图（验收 103）卡片实际弹出；单测 `Result_*` 5 条（含失败分支文案与 `ClockOf` 边界）。

### ③ 按 R 重开没效果（复位不完整，不是按键没读到）
- 旧 `ResetRound()` **只清本组件计时** ⇒ 玩家仍站被抓处、哨兵仍保持警觉、巡逻时钟继续累加，
  观感等于「什么都没发生」。现**全量复位六处**并按序：本组件门控 → 撤离区驻留 → 玩家察觉 →
  所有哨兵叠加层 → 巡逻时钟归零 → 玩家回出生点。
- 两个必须照做的细节：
  ① 写 transform 前**先关 `CharacterController`**（启用态改 transform 会被物理层按旧碰撞结果拉回）；
  ② `Outcome` **最先**改回 Playing（按钮在绘制回调里调用本方法，否则同帧 Update 早退、残留清不掉）。
- 出生点**不写死坐标**：由 `Awake` 捕获场景摆好的位姿 + 公开 `RecordSpawnPoint()`。
  写死会让预制件拖到新场景仍在示例坐标出生（违反 §0.1.1 所见即所得）。
- 出结果即 `PlayerStealthController.Frozen = true` 冻结输入（否则玩家在遮罩后继续跑）。
- 引用改**惰性解析**（`Resolve()`）：EditMode/预制件场景不保证走 Awake，
  漏一次解析就静默漏掉玩家 ⇒ 正是「按 R 没反应」的另一条路径。
- 证据：Play 探针（验收 100）复位前 (0,0.05,**24**)/时钟 42.5 → 复位后 (0,0.05,**−22**)/时钟 **0.0**/
  三哨兵全 Patrol/两撤离区 reached 全 False/frozen False。

### ④ 围栏 + 布局复杂化
- **四面围栏**（54×54 内腔、高 4m），**全部标静态参与导航烘焙**——墙不烘焙则导航面铺到墙外、
  NPC 穿墙追人（= 上一轮反馈换个位置复现）。东西墙让出转角厚度，避免转角 z-fighting。
- **双层边界（D-15）**：可见墙 + `StealthArena` 纯函数硬钳制（走 `_cc.Move` 而非写 transform）。
  `StealthArena` **默认关闭**（半宽 0 ⇒ 恒放行），预制件拖进小场景不会凭空出现隐形墙。
- 掩体 3 → **20 个实体占位**：南侧掩体群 ×4、西侧长墙、东北角仓库（四墙 + 净宽 **3.8m** 门洞）、立柱 ×2。
  门洞原设计 1.4m，因 legacy 烘焙按角色半径**腐蚀可行走面**会封死缺口而加宽。
- 哨兵 2 → **3 名**（新增路线资产 `Sample/Profiles/DepotRing.asset`）；撤离点 1 → **2 处**
  （北侧驻留 0.6s / 仓库内 1.2s），逼出直线冲北与绕东穿仓库的取舍。
- **新增路线间隙自检** `VerifyRouteClearance()`：路点连线按 0.5m 采样查是否落进实体 XZ 矩形。
  巡逻基线**完全不参与碰撞**（D-01），摆错位置不报错不卡住、只安静穿模，肉眼在 Play 里撞见成本极高。
  PingPong 路线**不检查**末点→首点闭合段（否则把正常往返误报成穿墙）。
- **绕行校验改按场景真实物体取起终点**（重要自查）：旧版硬编码 (0,±14)，而出生点在 (0,−22)、
  撤离点在 (0,24)/(16,16) ⇒ **布局一改，校验量的是地图上不存在的位置却照样"通过"，等于没校验**。
  现逐撤离区校验可达；绕行判据只对主点强制（仓库点走东走廊直线本就通畅，要求它绕行会把好布局误报成失败）。
- 证据：`[Clearance] 三条路线与 20 个实体占位无交叉`；
  `ExitZone_North 拐点=3 直线=46.00 路径=46.06 横向偏离=1.17`；`ExitZone_Depot 拐点=4 横向偏离=0.72`。

### 其他
- 新增守卫用例 `Tests/Editor/GameRuleResetTests.cs` **22 条**（本包 99 → **121**）。
- 文档同步：DESIGN 组件表 +3 行、D-14/D-15 两条决策、§3.5 数据流图重画；
  ACCEPTANCE 新增条目 **97-120** 与未通过项 **U-6(更新)/U-7/U-8/U-9**；CHANGELOG 记在 **Unreleased**。
- **门禁**（21:20）：编译 `failed:false`；`console clear` 后重跑 recompile + test run →
  `consoleErrors:0 / consoleWarnings:0`；全量 **250/250**（本包 121 + citymap 129，
  计数经逐文件 `grep -c "^\s*\[Test\]"` 实测，**非估算**）。
  ⚠️ 校验器仍有一条 `assets-layout` FAIL，是另一会话留在 `Assets/Scenes/testscene.unity` 的
  未跟踪文件（无提交记录、citymap 锁当时仍活动），**不属本包、未代修**（U-8）。
- 长门禁期间**自己锁的心跳会超时**（本轮又遇到，597s）：续期即转绿，跑门禁中途记得续。

## 2. 实测得到的环境事实（新会话直接照做）

### 本轮修正的一条旧结论（重要）
- **上一轮记录「Play 态 `script eval_file` 会整体静默失败」是错的**，本轮多次成功执行并读到输出。
  真实原因有两个，都不是 Play 模式的问题：
  ① 探针脚本里写了 `return;`——`eval_file` 的内容被嵌进**方法体**，方法体里 `return` 合法但
     我那次是写在**顶层语句**风格里混用了类声明，导致 Roslyn 报 `CS1001 Identifier expected`；
  ② 用 `Object.FindAllObjectsByType`（**不存在的 API**，凭记忆臆造）。
  ⇒ 正解：**纯语句体 + `FindAnyObjectByType` / `FindObjectsByType(..., FindObjectsSortMode.None)`
  + `Debug.Log`，再从 `console get` 读**。别放弃探针这条路。

### 本轮新增的硬限制（取证必须知道）
- **窗口失焦时 Play 循环完全不推进**：间隔 4s 两次取样 `Time.frameCount` / `Time.time` /
  `StealthClock.Current` **三值全冻结**（实测 13229 / 41.1 / 42.5 一模一样）。
  ⇒ 凡依赖时间累积的玩法（驻留通关、察觉顶格）取证必须「探针 + `focus` + 立即截图」连做。
- **`unity command "focus"` 能恢复推进**（实测 13229 → 13602），但**每次 CLI 调用都会把焦点抢回终端**，
  所以不能并行发指令、也不能 focus 之后再慢慢做别的。
- **`set_auto_tick` 不驱动 Play 循环**（只保活编辑器 UI）：开启后帧号仍纹丝不动，别指望它。
- **`game_view --save_path` 会把绝对 Windows 路径改写到 `Assets/` 下**：
  传 `D:/.../.tmp-inspect/x.png` 实际落 `Assets/.tmp-inspect/x.png`（**未被 gitignore，属违规临时产物**）。
  ⇒ 直接用 `--save_path "Assets/.tmp-inspect/xxx.png"`，取完证据**立刻** `rm -rf Assets/.tmp-inspect`
  并确认没留 `.meta`。
- **控制台会被 AI 助手网络日志刷屏**（`Error after 1 attempt(s): UnavailableForLegalReasons ... generators.ai.unity.com`），
  `--tail N` 可能把自己刚打的日志挤出去；`--level warn` 过滤在本实例返回空。用大 tail + 自己 grep 标签。
- **退出 Play 的瞬间会短暂出现若干 warning**（实测 5 条），几秒后自行归零 ⇒ 读门禁计数前先 `sleep` 再取。
- **Unity 6 已把 `FindFirstObjectByType` 与 `FindObjectsByType(…, FindObjectsSortMode)` 标为废弃**
  （eval 诊断报 obsolete，推荐 `FindAnyObjectByType`）。本包源码尚有 3 处 `FindFirstObjectByType`
  （`StealthClock:38`、`StealthGameRule:243`、`StealthHud:142`）：**项目编译未计入控制台 warning**
  （门禁实测 0/0），属技术债，M6 前统一换掉，新代码别再写旧 API。

### 仍然有效的旧事实（未复验但无矛盾）
- Play 模式跑 `test run` 会刷 `This cannot be used during play mode` 且 `Total` 返回 0 ⇒ 跑门禁前确认已停 Play。
- 导航烘焙：`UnityEditor.AI.NavMeshBuilder.BuildNavMesh()`（legacy 内置路径），产物
  `Sample/Scenes/<场景名>/NavMesh.asset` **必须与场景一起入库**；只收 static；角色/相机**递归**取消 static。
- **`URP/Unlit` 不能当半透覆盖层**（`Queue=Geometry` + `ZWrite On`）；贴地半透须
  `Queue=Transparent` + `ZWrite Off` + `Offset` 或几何抬升。URP `LightMode` 只认 `SRPDefaultUnlit`。
- **探针度量锚点会算错**：投影半径按世界原点量得 22.08 假值，按**脚底**量才得 13.98 吻合值。
- **绕行的几何直觉要核验**：1.6m 宽障碍对 28m 直线只带来 ~0.13m 路程增量 ⇒
  用「总长变长」判导航生效是**错的**，必须看拐点横向偏离。
- Unity 不允许覆盖同路径的已打开场景、也不允许卸载最后一个已加载场景 ⇒ 打开目标场景后**原地 `SaveScene`**。
- 编辑态访问 `renderer.material` 即时报 error 并泄漏材质 ⇒ `new Material(sharedMaterials[0])` 写回 `sharedMaterials`。
- **EditMode 不执行 MonoBehaviour 生命周期**（本包已撞 5 次）：静态单例兜底 `FindFirstObjectByType`、
  `GuardBrain.All` 剪枝+空则重建、注册表手动 `MarkDirty()`、组件引用**在方法内惰性解析**
  （本轮 `StealthGameRule.Resolve()` / `ApplyCapsuleShape` 都是为此而加）。
- 跨包 id 必须非负（`StealthIds.Of`）；跨包单位枚举断言用**实例化前后差值**（工程里可能还加载着别人的场景）。
- 测试故意 `LogError` 会破归零门禁，`LogAssert.Expect` **不回退计数** ⇒ `logEnabled=false` + `finally` 复位。
- 工具：`test run --assembly X` 过滤被服务端忽略；`unity command` 输出是「JSON + 制表符表格」须 `raw_decode`；
  `scene open` 只认 `Assets/` ⇒ 包内场景必须 `EditorSceneManager.OpenScene`；
  Game 视图非前台时 `screenshot --view game` **抓不到 IMGUI**，验 HUD/结算界面用
  `game_view --source screen`（本轮两张证据图都是这么拿到的）。
- **CLI 超时不取消服务端执行**：连发 `test run` 会串行叠加并把所有指令顶到超时。一次提交一次等待。
- 共享编辑器纪律：禁 `NewScene(Single)`、禁 `DisplayDialog`；切场景前查 `isDirty`；临时场景 Additive + 用完 `CloseScene`。
- **自动录屏不可行**（已四次确认）：gdigrab 按 title 匹配失败、抓 desktop 得到用户正在用的应用、
  按 hwnd 抓黑帧、`SetForegroundWindow` 被 Windows 前台锁挡掉。**不要重试这条路。**
- 锁会被其他会话清掉（已 6 次）/自己心跳超时（已 3 次）：重新原子取锁或续心跳即可。
- 并行实况：另一会话持续在改 citymap。**不替他修、不因他人红灯停手。**

## 3. 未完成

- **U-7（本轮新增）**：**失败（被抓）结算卡片没有目视证据**。受阻于「失焦不推进帧」——被抓需数秒累积。
  与胜利分支共用同一段绘制代码（只差 `Accent`/`Title`/行数），胜利卡片已实测弹出，风险较低，
  但**不据此声称已验证**。⇒ 用户实机玩一次即可确认。
- **U-6（更新）**：地面色块「不闪」已有**静态截图**证据（验收 119 投影边界清晰无破碎），
  但「拖动视角/切档时不闪」是动态结论，单张截图证不了 ⇒ 仍待用户眼睛确认。
- **U-8**：校验器 `assets-layout` FAIL = 他包未跟踪文件（见上），需那个会话自己归位。
- ~~M5 收版项~~ ⇒ **已完成（本轮）**：视频入库 + `Showcase.json.video` 填充 + 展板自动接入 + 收版 0.5.0。
  见 §1。
- **M6**：冻结对外契约 `Docs/Modules/00-Contract.md`。
  ⚠️ `IPathBridge` 签名已变（新增 `TryFindPath`）；本轮**未**改对外契约，但 `StealthGameRule`
  新增 `ElapsedSeconds/PeakSuspicion/GuardsAlerted/ExitReached` 等公开读数——若 B 包要显示胜负状态，
  需先在 core 加接口（走 M6 冻结窗口）。
- **技术债**：3 处 `FindFirstObjectByType` 换 `FindAnyObjectByType`（Unity 6 已废弃前者）。
- **待补证据**：编辑态回溯「拖滑块 NPC 连续移动」截图；50 NPC ≥ 55 FPS 性能验收；蹲伏态切换单独截图。
- **已知未接线**：`StealthWorldChangedChannel` / `StealthTimeIndexChannel` 已定义但无资产实例、无 Raise 调用点。
- 搜索环绕阶段仍是水平面 `desired.y = 0`；导航折线只在**反应位移**里用，巡逻基线的**上下楼梯**未接。
- 导航只在**烘焙过的场景**有效。用户自己的场景需自行烘焙并标 static（包文档未写这条使用说明，M6 前补）。

## 4. 下一步（建议顺序）

1. **请用户实机确认三点**：①被抓时结算卡片是否正常弹出（U-7）；②地面色块还闪不闪（U-6）；
   ③新围栏布局的游玩难度是否合适。若色块仍闪，先试 `_clearance` 0.05m，**不要**回去改队列配置。
2. ~~等用户录屏 → 剪两段式 → 填 `video` → 收版 0.5.0~~ ⇒ **本轮已全部完成**（见 §1 A）。
   后续若用户提出**画面内容修改要求**才重录/重剪；交付事实核验方式：
   `Showcase.json.video` 非空 + `ResolveVideoPath()` 指向的文件存在。
3. **M6 契约冻结**（含 `IPathBridge` 新签名对齐 + 胜负读数是否上提 core）→ 交付 B 包消费。
4. B 包（设计辅助台）开工：安全岛、T 周期回溯面板、分层查看器、按玩家线路反推路点。`Showcase.json` order 拟 31。
5. 性能验收（50 NPC ≥ 55 FPS）与两项截图补采；清 3 处废弃 API；包文档补「自有场景需烘焙导航」说明。

## 5. 勿碰项

- **`Packages/com.collin.citymap/**`**：另一会话在迭代（其编译错误多次让全项目 `failed=true`，均自行修好）。
- **`Assets/Scenes/testscene.unity`、`Assets/test.asset`、`Assets/CityProject.asset`**：
  另一会话的未跟踪/进行中文件，**不要代其归位或删除**。
- **`Packages/com.collin.core/**`**：契约层红线（core DESIGN D-7）——只允许值类型/接口/通道子类，
  **禁止玩法逻辑与判定实现**；core 版本保持 **0.2.0**（用户裁定）。
- **`Packages/manifest.json`、`ProjectSettings/**`**：§7 共享文件。本轮**没有**动它们。
  `activeInputHandler: 1`（仅新 Input System）**不要改回 Legacy**。
- **`Assets/Showcase/**`、`Assets/Scenes/SampleScene.unity`**：展板纯 UI，禁止为单插件改场景文件（§6）。
- **§8 钉死版本**：Unity 6000.6.7f1 / pipeline 0.9.1-exp.1 / ai.assistant 2.21.0-pre.2 / ai.inference 2.6.1。
- **不要引入 `com.unity.ai.navigation`**（D-06/R-2）：付费 HAVOK，逐帧积分会摧毁「位姿为时间纯函数」地基。
- **结算界面不要改成 uGUI**（D-14）：会破坏 D-13 拆箱即用（空场景无 `EventSystem` ⇒ 按钮点不动）。
- **`StealthArena` 默认值保持关闭**（半宽 0）：预制件拖进小场景不该凭空出现隐形墙；
  示例场景由生成器显式写 27，**不要**把 27 做成默认值。
- **出生点不要写死坐标**：由 `Awake` 捕获场景摆好的位姿（§0.1.1 所见即所得）。
- **`Sample/Scenes/StealthSample/NavMesh.asset` 勿手改、勿删**：删了绕行立刻失效（表现为「又穿模了」）。
- **地基纪律勿破坏**：①基线位姿必须是纯函数，反应（含导航绕行）只走 `IPatrolModifier` 叠加；
  ②判定与投影**只有一条实现**（`GroundVisionProjection` / `VisionGeometry`）；
  ③档位配色只有一份（`StealthStageVisual`）；④地面半透必须用 `Resources/StealthGroundProjection.shader`。
- **胜负呈现只有一处（`StealthResultPanel`）**：勿在 HUD 恢复横幅或 R 键处理（会重复触发复位）。
- **操控语义勿回退**：镜头角与模型角分离；相机位置与朝向共用同一阻尼。
- **胶囊贴合公式勿回退**：`CapsuleVisualPose`（中心 = scale.y = h/2），
  旧公式 `(h − 站高)/2` 在站立时算出 0 = 埋地（单测 `CapsulePose_LegacyFormulaWasTheBug` 专门钉住它）。
- **哨兵单元三层结构（根 / RouteRoot / Body）勿合并**；`SpawnGuard` 的 `bodyOffset` 对 `DepotRing` **必须为 0**
  （正偏移会把环线平移出围栏 ⇒ 哨兵穿东墙）。
- **`GuardBrain` 重路由的两个坑勿回退**：追踪点必须钉在当前位置；必须有 `_repathInterval` 节流。
- **已定案勿重议**：D-01~D-15、Q-A1~Q-A5、方案 P、core 0.2.0、**stealth 已收版 0.5.0**（M1~M5 全部达成）。
- **包内禁止存放未压制的手录原始视频**（用户 11-04 明确：「无用的空间浪费」）：
  `Packages/com.collin.*` 下只留 `ShowcaseMedia/<包名>_demo.mp4` 这类**转码成片**。
  本轮已清掉 stretch 包内两个源片（157 MB，其中一个已入库 ⇒ 走 `git rm`）。
  录屏始终由**用户手动录制**（自动录屏已四次确认不可行），交付后本包只转码、**不验证画面内容**。
