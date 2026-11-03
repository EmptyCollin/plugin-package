# Sample — com.collin.stealth

本目录是包内试用演示（AGENTS.md §0：showcase = 包内演示，展板只消费 Showcase.json，不引用插件代码与场景）。

当前为空（M0 只交付骨架）。M5 计划落位（依 DESIGN D-09 / D-13 定案）：

```
Sample/
├── Scenes/
│   └── StealthSample.unity     # 白盒保底演示：地面 + Cube 掩体 + 玩家预制件 + 2~3 个哨兵预制件
├── Prefabs/
│   ├── StealthPlayer.prefab    # Capsule + CharacterController + PlayerStealthController + DetectionState
│   │                           #   → 拆箱即用：拖进任意空场景按 Play，WASD 走、Ctrl 蹲伏（验收 13）
│   └── StealthGuard.prefab     # Capsule + PatrolRouteRoot + PatrolAgent + VisionVolume
│                               #   → 一个预制件 = 一个「会巡逻、有视野」的哨兵，拖进场景即可用
├── Profiles/
│   ├── WarehouseLoop.asset     # PatrolRoute（共享 Model，路点为相对路线根的局部坐标）
│   └── GuardNormal.asset       # VisionProfile（共享 Model）
└── README.md
```

两点约定：

1. **路线搬移以预制件为单位**（D-09）：`StealthGuard` 自带路线根，拖动该物体即整条巡逻路线一起搬移；
   SO 资产只存相对坐标，因此可跨场景复用而不写死世界位置。
2. **刻意保持白盒、不用真实美术资产**（D-08）：本示例的唯一职责是证明
   「A 包脱离任何设计辅助工具也能独立跑起来」。
