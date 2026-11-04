# Sample — com.collin.stealth

本目录是包内试用演示（AGENTS.md §0：showcase = 包内演示，展板只消费 Showcase.json，不引用插件代码与场景）。

## 实际落位（M5）

```
Sample/
├── Scenes/
│   ├── StealthSample.unity     # 白盒保底演示：打开即可 Play
│   └── StealthSample/
│       └── NavMesh.asset       # 导航烘焙产物（由生成器产出，勿手改勿删：删了哨兵又穿模）
├── Prefabs/
│   ├── StealthPlayer.prefab    # Player 根（CharacterController + DetectionState + PlayerStealthController）
│   │                           #   ├ CapsuleVisual（视觉胶囊，碰撞交给 CharacterController）
│   │                           #   ├ FollowCamera（tag=MainCamera，第三人称跟随 + 右键环绕 + 防穿墙）
│   │                           #   ├ Hud（察觉度条 + 各哨兵档位 + 撤离指引 + 操作提示）
│   │                           #   └ ResultPanel（胜利/失败结算卡片，IMGUI）
│   └── StealthGuard.prefab     # 哨兵单元根
│                               #   ├ RouteRoot（PatrolRouteRoot，静态参考系，引用路线 SO）
│                               #   └ Body（PatrolAgent + VisionVolume + GuardBrain
│                               #            + StealthGroundProjection + CapsuleVisual）
└── Profiles/
    ├── WarehouseLoop.asset     # PatrolRoute：4 点方环（局部坐标，Loop，默认速度 2 m/s）
    ├── CrossSweep.asset        # PatrolRoute：3 点横掠（PingPong，2.4 m/s）
    ├── DepotRing.asset         # PatrolRoute：4 点环绕东北角仓库（Loop，1.8 m/s）
    └── GuardNormal.asset       # VisionProfile：110° / 14 m / 眼高 1.7 m / 疑惑 0.35、警觉 0.8
                                #   / 近身察觉 1.8 m（修「贴脸反而看不见」）
```

## 场地构成（生成器搭出来的东西）

- **外侧围栏四面**（内腔 54×54 m、高 4 m）：全部标静态**参与导航烘焙**——
  墙不参与烘焙的话导航面会照常铺到墙外，NPC 会穿墙追人。
- **场地硬边界**（`StealthArena`）：围栏之外再兜一层，穿模或掩体被删也不会溜出地图。
  预制件里**默认关闭**（半宽 0），示例场景由生成器显式开启为 27。
- **实体占位共 20 个**（生成器自检日志的口径）：围栏 4 + 中部掩体 3 + 南侧掩体群 4 +
  西侧长墙 1 + 仓库 6（四墙 + 门洞两侧 + 箱内杂物）+ 撤离区立柱 2。
  门洞不能做窄：legacy 烘焙按角色半径腐蚀可行走面，1.4 m 的缺口会被封死。
- **撤离点两处**：北侧主点（驻留 0.6 s）与仓库内备用点（驻留 1.2 s），任达其一即通关。
- **哨兵 3 名**，分别使用上面三条路线。

## 玩法与操作

| 操作 | 效果 |
|---|---|
| W/A/S/D | 相机相对四向移动（A 向左前进、D 向右前进，不是转镜头） |
| Shift / Ctrl | 跑 / 蹲伏（蹲伏降低曝光，更难被发现） |
| 按住鼠标右键拖拽 | 转视角（镜头角与模型朝向各自独立） |
| R 或结算卡片按钮 | 重开一局（**全量复位**：玩家回出生点、察觉与哨兵档位清零、巡逻时钟归零） |
| Esc 或「关闭本界面」 | 收起结算卡片 |

出结果（通关或被抓）时冻结玩家输入并弹结算卡片，含用时、察觉峰值、惊动哨兵数与撤离点归因。

## 三条使用路径

1. **重生成整场景**：菜单 `Tools ▸ Collin ▸ Stealth ▸ 生成可玩示例场景`
   （重建 `Scenes/` 与 `Profiles/` 四资产，并在存盘后烘焙导航网格、跑绕行与路线间隙两项自检；
   采用「打开目标场景原地保存」，不删文件后重写）。
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
