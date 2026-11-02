# CHANGELOG — com.collin.core

格式：Keep a Changelog；版本号：semver。

## [Unreleased]

## [0.1.0] - 2026-11-02
### Added
- 首个版本：`EventChannel`（ScriptableObject 广播信号）、抽象泛型基类 `EventChannel<T>`
  （泛型 SO 不可被 Unity 实例化，消费方声明具体子类）与样板 `IntEventChannel`、
  `Services`（接口注册表）、`ShowcaseEntry` / `ShowcaseSorter`（拓扑+order+名称排序、依赖闭包，纯函数）、
  `ShowcaseScanner`（Editor 侧包发现与 Showcase.json 解析）。
