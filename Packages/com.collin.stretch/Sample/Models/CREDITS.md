# 示例模型来源

`modular_wooden_pier.fbx`（模块化木栈桥）来自 [Poly Haven](https://polyhaven.com/a/modular_wooden_pier)，
许可 **CC0 1.0 Universal（公共领域）**，可自由使用、修改与再分发，无需署名。

- 模型：`modular_wooden_pier.fbx`（FBX 1K 版，7 个模块 + 2 材质）
- 贴图：`Sample/Textures/Pier/`（planks/poles 各 diffuse + normal，1K）
- 场景用网格：`Sample/Meshes/pier_long.asset` —— 由 section_01/02/03 三个直段按材质
  合并成的连续栈桥（双材质子网格，147600 顶点，9.36m 长），合并为一次性工具操作。

选用理由：体验场景需要一个**真实 DCC 产出的任意多面体**——正是它暴露了 Builder 只读
子网格 0 会丢多材质模型一半的缺陷（ACCEPTANCE 37），程序化网格永远测不出这类问题。

楼A/楼B/楼C 为本工程程序化生成的教学网格；旧程序化 `桥_原型.obj` 已移除，由上述真实资产取代。
