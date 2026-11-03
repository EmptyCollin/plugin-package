# CHANGELOG — com.collin.core

格式：Keep a Changelog；版本号：semver。

## [Unreleased]

### Added
- **潜行跨包契约层** `Runtime/StealthContracts/`（3 文件，2026-11-03，DESIGN D-7 / 方案 P）：
  - `StealthDtos.cs`：位姿（含 `valid` 有效性位）、路点、轨迹采样、单位信息、视野快照、时钟状态、察觉事件载荷，
    以及 `StealthLoopMode` / `StealthAlertStage` / `StealthQueryLayers` 三组镜像枚举；
  - `StealthWorldInterfaces.cs`：`IPatrolWorld`（读 + 写路点，供「按玩家线路反推巡逻路点」）、
    `IVisionWorld`、`IDetectionWorld`、`ISafeIslandProvider`（**反向注入**：实现方=设计辅助台、消费方=玩法包）、
    `IStealthScrubDriver`（编辑态回溯驱动）；
  - `StealthChannels.cs`：`StealthDetectedChannel` / `StealthTimeIndexChannel` / `StealthWorldChangedChannel`
    三个 `EventChannel<T>` 具体子类（泛型通道不可被 Unity 实例化，见 0.1.0 验收第 8 行）。

### Notes
- **版本号仍为 0.2.0**：本包新增对外契约本应升 minor 版本，但 citymap / stealth / stretch 三处
  `package.json` 均写死 `"com.collin.core": "0.2.0"`，Unity 对 0.x 版本按**精确匹配**解析，
  单改 core 版本会让三个包解析失败；而那三处声明属于其他 AI 的包工作区（§2 互斥）。
  → 升版动作待用户批准「同一次改动里同时更新三处依赖声明」后执行（属 §7 共享文件申报范畴）。

## [0.2.0] - 2026-11-02
### Added
- `ShowcaseRuntime`：运行时从 `Packages/` 磁盘枚举 com.collin.* 的 Showcase.json 并排序
  （展板 Play 模式数据源，DESIGN D-5）；`ResolveVideoPath` 视频绝对路径解析。

## [0.1.0] - 2026-11-02
### Added
- 首个版本：`EventChannel`（ScriptableObject 广播信号）、抽象泛型基类 `EventChannel<T>`
  （泛型 SO 不可被 Unity 实例化，消费方声明具体子类）与样板 `IntEventChannel`、
  `Services`（接口注册表）、`ShowcaseEntry` / `ShowcaseSorter`（拓扑+order+名称排序、依赖闭包，纯函数）、
  `ShowcaseScanner`（Editor 侧包发现与 Showcase.json 解析）。
