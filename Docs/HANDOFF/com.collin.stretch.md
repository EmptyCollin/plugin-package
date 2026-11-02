# HANDOFF — com.collin.stretch

> 覆写式交接单（AGENTS.md §4）。只写可观测事实。

最后交接：qianwen-work-assistant（千问工作助理）/ 2026-11-02 / v0.3.0（提交见 git log）

## 1. 已完成
- **0.3.0 算法通用化**（用户指出"模型迁就工具=本末倒置"）：跨界三角形 SH 半空间裁剪分片、
  位置/UV/法线/切线/色切口插值、逐片刚性归段；不跨界走快速路径（旧用例逐顶点数不退化，R1/R2）；
  G1 实证：窗户正中切穿 ×3 复制 UV 仍全 [0,1]；
- **碰撞/物理同步**（用户要求）：BoxCollider 尺寸/中心、MeshCollider 结果网格（Restore 还原源）、
  Rigidbody 运行期 ResetCenterOfMass/ResetInertiaTensor；面板开关 syncBoxCollider/syncMeshCollider；
- **桥重做**：自包含节段装配式（引桥单元=箱梁+双柱墩+盖梁+护栏、主跨双柱墩+抛物拱肋、桥台收头、跨间纯空气缝），
  636 三角形/1900 顶点、绕序断言 0 异常；场景中为**裸原型**（无组件无 Profile——按用户要求不建桥的 profile，
  教学步骤已端到端实测：InitFromMesh→插空气缝→标 Repeatable→×4 → lenX 25.16、box 同步 25.16、还原回 18.56）；
- 面板新增**边界数值精调**（中间边界直接输入局部坐标）；`InitFromMesh(mesh,axis)` 轴边界同源初始化；
- 修三个真实缺陷：端点对齐读结果网格污染边界（楼A 曾 3→8，已复原+锁死源网格）、
  轴/包围盒不同源静默失败、MeshCollider 采集时序（重开后漏同步→每次 Rebuild 前重采集）。
- ACCEPTANCE 26-36 + 缺陷记录；§1.6b 归零 errors=0 warnings=0；runtime-purity/assets-layout 绿灯。

## 2. 未完成
- 拖拽把手（D-4）；接缝焊接（R-2）；裁剪分片后可选顶点焊接优化（R-4 规模可控，暂不做）；
- README 用户文档、真实录屏替换占位演示视频；
- 楼B/楼C 无 Repeater 交付形态演示（Preview 语义已并入 Instance，体验场景打开即拉伸态）。

## 3. 已知坏
- 无运行缺陷已知。约束记录：源网格须 Read/Write+UV0；Validate 拒绝时**静默保留原网格**（原因在面板 HelpBox，
  不写 error——§1.6b 友好）；`game_view/scene_view --save_path` 会把路径重定位到 Assets/.locks（CLI 怪癖，用后清）。

## 4. 下一步
- 用户在 TestPlayground 选中桥_原型，按教学步骤自建拉伸（步骤见本包 README 缺位期间的交接说明/或问 AI）；
- 反馈定优先级：把手 / README+录屏 / 焊接。

## 5. 勿碰项
- **裁剪分片+切口插值**（D-3 通用性根基）——退回"重心归段"即重新要求模型迁就边界；
- 数据归属（结构=Profile、倍数=实例，§0.1 原则2）；sharedMesh 读写约定；DrawStructure 必须传**源网格**
  （传 MeshFilter.sharedMesh 即复现"端点对齐污染边界"缺陷）；
- 桥_原型保持裸模型（用户明令不建桥 Profile）；Sample OBJ 生成约定（三角形直出不写 vn）；
- AGENTS.md §0/§0.1；com.collin.core（另锁范围）。

## 附：桥的手动创建步骤（教学实测过，G6/BridgeShots 证据）
1. 选中 `桥_原型` → 菜单 `Tools/Collin/Stretch 拉伸/新建拉伸参数`（自动开 Read/Write、生成 Profile、挂实例）；
2. 实例 Inspector → 拉伸轴 = **X**；
3. 结构区"边界精调"输入两个空气缝局部坐标：**-12.98** 与 **-10.78**（对应生成器切点 5.58/7.78，
   局部坐标=生成值-18.56；任意缝皆可，裁剪算法保证切哪都对）；
4. 把 [-12.98,-10.78] 这段类型改为 **Repeatable**，倍数填 **4** → 桥长 18.56→25.16，BoxCollider 自动同尺寸；
5. 定稿要静态网格就 `烘焙选中物体的结果`；撤销全部效果点面板"还原原网格"。
