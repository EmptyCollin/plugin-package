# HANDOFF — com.collin.stretch

> 覆写式交接单（AGENTS.md §4）。只写可观测事实。

最后交接：qianwen-work-assistant（千问工作助理）/ 2026-11-02 / v0.4.0（提交见 git log）

## 1. 已完成
- 0.3.0/0.3.1/0.3.2 全部内容（边界裁剪通用化、碰撞/刚体同步、多材质子网格保持、CC0 真实栈桥、越界自动重排）；
- **0.3.3 面板布局泄漏修复**（用户报"点插入没反应+边界数据挤一行"）：根因=0.3.2 段行循环
  `break` 在 `BeginHorizontal` 未闭合时跳出 → GUILayout 组泄漏，后续控件全被吞进泄漏行。
  重写为**布局纪律版**：全行 `using HorizontalScope`、0 break、数据变更延迟到绘制结束统一应用；
  每条边界独立一行（起点/终点锁定灰显、中间行内联删除），插入/端点对齐独立一行；
- 验证：反射 `ApplyEdits(doInsert)` 楼A 实例 `bounds 4→5、repeats 同步补齐、段数匹配`，
  快照还原零污染（git diff 空）；静态配平 `BeginHorizontal=0/using×5/ScrollView 1:1/break=0`；
  归零门禁 errors=0 warnings=0；校验器绿灯。
- **0.4.0 性能路线 P1–P4 全落地**：`StretchMeshCache`(源,profile,repeats 键共享+LRU)/面板一键烘焙/
  `renderMode=Chunked`+`StretchChunkManager`(DrawMeshInstanced，50 实例×10 份=3 组)/`StretchScheduler`
  预算队列+双泵；`syncMeshCollider` 默认 false。实测证据见 ACCEPTANCE 63-68；限制见 DESIGN §8.4。
- **0.3.4 共享顶点去重 + 滑条**（用户质疑 14 万顶点）：Builder (源顶点,段,份) 缓存去重（楼A 492→328）；
  pier_long 重做=顶点表拼接+索引重映射（147600→32326=源三 section 之和，场景已重接保存）；
  边界行=滑条(源包围盒范围)+数值框、每条独立成行；倍数 1..32 滑条；
  回归 W1-W6 全绿（恒等=源12顶点、桥×3 膨胀率2.04、双槽、Box=14.97、还原32326）。

## 2. 未完成
- 倍数拖拽交互（仍整数输入框）；接缝焊接（R-2）；README；演示视频换录屏；
- pier_long 合并未工具化；
- 菜单"新建拉伸参数"把 Profile 落在 `Assets/` 根（用户现场已有 3 个 `桥_栈桥_stretchProfile*.asset`
  未跟踪残留，去留由用户定）——应改为询问存放位置或默认包内，待用户拍板。

## 3. 已知坏
- Chunked 无阴影/不接收阴影、不进深度预通道（§8.4，BRG 为后续升级位）；同提交器材质数组须一致。
- 无运行缺陷已知。0.3.5 修：切轴边界脱节（端点重设新轴+clamp+重排）、0.3.3 丢失的 ApplyEdits→NormalizeBounds。
  约束记录：重排后段索引会变（外部缓存旧索引须经 map 转换）；把手只画中间边界；
  `game_view --save_path` 路径重定位 Assets/.locks（用后清）；**eval 环境不能反射调 OnInspectorGUI**
  （"You can only call GUI functions from inside OnGUI"，引擎限制）——面板验证只能走
  "反射数据方法 + 静态配平"路线，渲染冒烟不可行。

## 4. 下一步
- 用户在 TestPlayground 复验新面板（边界逐行、插入、拖拽把手、越界重排），反馈定优先级；
- 菜单 Profile 落点策略（询问 or 包内默认）；README/录屏/焊接按反馈排。

## 5. 勿碰项
- **IMGUI 布局纪律**（面板内：任何行必须 `using HorizontalScope`；OnInspectorGUI 内禁止在
  Begin/End 配对之间 return/break；数据变更一律延迟到绘制完成后应用）——0.3.2→0.3.3 的教训，
  写进代码注释头部；
- 越界→NormalizeOrder 重排语义；把手位移换算（lossyScale×repBelow）；
- 逐子网格+材质槽保持（0.3.1）；裁剪分片+切口插值（D-3）；数据归属（结构=Profile、倍数=实例）；
- sharedMesh 读写约定；端点对齐必须用 `Inst.OriginalMesh`（源网格）；
- **共享顶点缓存语义（(v,段,份) 键）与合并=顶点表拼接**（退回逐三角形摊平即复现顶点暴涨，用户已质疑过一次）；
- Sample CC0 资产与 CREDITS.md；桥保持裸模型无 Profile（用户明令）；AGENTS.md §0/§0.1；core（另锁范围）。
