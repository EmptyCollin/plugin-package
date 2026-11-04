# Sample — com.collin.stealth

本目录是包内试用演示（AGENTS.md §0：showcase = 包内演示，展板只消费 Showcase.json，不引用插件代码与场景）。

## 实际落位（M5）

```
Sample/
├── Scenes/
│   └── StealthSample.unity     # 白盒保底演示：打开即可 Play
├── Prefabs/
│   ├── StealthPlayer.prefab    # Player 根（CharacterController + DetectionState + PlayerStealthController）
│   │                           #   ├ CapsuleVisual（视觉胶囊，碰撞交给 CharacterController）
│   │                           #   ├ FollowCamera（tag=MainCamera，第三人称跟随 + 右键环绕 + 防穿墙）
│   │                           #   └ Hud（察觉度条 + 各哨兵档位 + 操作提示）
│   └── StealthGuard.prefab     # 哨兵单元根
│                               #   ├ RouteRoot（PatrolRouteRoot，静态参考系，引用路线 SO）
│                               #   └ Body（PatrolAgent + VisionVolume + GuardBrain + CapsuleVisual）
└── Profiles/
    ├── WarehouseLoop.asset     # PatrolRoute：4 点方环（局部坐标，Loop，默认速度 2 m/s）
    ├── CrossSweep.asset        # PatrolRoute：3 点横掠（PingPong，2.4 m/s）
    └── GuardNormal.asset       # VisionProfile：110° / 14 m / 眼高 1.7 m / 疑惑 0.35、警觉 0.8
```

## 三条使用路径

1. **重生成整场景**：菜单 `Tools ▸ Collin ▸ Stealth ▸ 生成可玩示例场景`
   （会重建 `Scenes/` 与 `Profiles/` 三资产；采用「打开目标场景原地保存」，不删文件后重写）。
2. **沉淀/更新预制件**：菜单 `Tools ▸ Collin ▸ Stealth ▸ 沉淀示例预制件（玩家+哨兵）`
   烘焙后自带**引用完整性自检**（Body 三件套、路线/视野资产引用、T 周期 > 0、沿周期求值有位移）。
3. **验证拆箱即用**：菜单 `Tools ▸ Collin ▸ Stealth ▸ 验证预制件自洽（内存临时场景）`
   在内存临时场景里<b>只放预制件 + 一个时钟</b>（不建地面/掩体），检查实例连接状态与跨包枚举，
   结束即丢弃、不落盘。

## 两点约定

1. **路点为相对 `RouteRoot` 的局部坐标**（DESIGN D-09）：拖动预制件的 `RouteRoot` 子物体
   即整条路线搬移；SO 不含世界坐标，可跨场景复用。
2. **哨兵单元刻意分三层**（根 / RouteRoot / Body）：若把 `PatrolRouteRoot` 挂在被驱动的本体上，
   本体的每帧位姿会带动路线根、路线又反过来决定位姿，形成循环；兄弟分层既断环，
   又让整个单元能原样烘进预制件。

刻意保持白盒、不使用真实美术资产（DESIGN D-08）：本示例的唯一职责是证明
「A 包脱离任何设计辅助工具也能独立跑起来」。
