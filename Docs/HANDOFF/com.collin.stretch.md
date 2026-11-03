# HANDOFF — com.collin.stretch

> 覆写式交接单（AGENTS.md §4）。只写可观测事实。

最后交接：qianwen-work-assistant（千问工作助理）/ 2026-11-02 / v0.4.1（提交见 git log）

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
- **0.4.1 真实演示视频**：396 帧程序化渲染（楼生长/桥延长五段）合成 16.5s mp4 替换占位；
  展板链路验证到 URL 注入（ACCEPTANCE 69-72），最终解码起播待用户前台目视。
- **0.3.4 共享顶点去重 + 滑条**（用户质疑 14 万顶点）：Builder (源顶点,段,份) 缓存去重（楼A 492→328）；
  pier_long 重做=顶点表拼接+索引重映射（147600→32326=源三 section 之和，场景已重接保存）；
  边界行=滑条(源包围盒范围)+数值框、每条独立成行；倍数 1..32 滑条；
  回归 W1-W6 全绿（恒等=源12顶点、桥×3 膨胀率2.04、双槽、Box=14.97、还原32326）。

- **0.4.2 展板交互与视频修复**（用户两轮反馈）：
  ① **Canvas 缺 GraphicRaycaster** —— 展板所有控件代码构建但从未挂射线组件，点击在派发第一环被丢弃，
  整个界面对鼠标是聋的（这才是"点不了/看不了视频"的首要根因）。补挂后实测 `EventSystem.RaycastAll`
  对 4 个按钮中心 `clickable=4/4`；② `file://` + 绝对路径把盘符解析成主机名（netloc='D:'）导致
  VideoPlayer prepare 永不完成，改直传绝对路径 + 15s 超时 + 可见占位提示；③ RT 960×360→960×540、
  音频 Direct→None；④ 演示视频重编码 Constrained Baseline+bt709+faststart，消除 Unity 导入告警；
  ⑤ **Profile 落点修复**：原兜底写 `Assets` 根（用户 7 次实验产生 7 个孤儿），改为
  prefab 同目录 → 包内 `Sample/Profiles/` → 场景目录，绝不进 Assets 根；
  ⑥ **校验器虚假绿灯漏洞**：Unity 控制台级别字段是 `warn` 而非 `warning`，归因循环匹配不上，
  把未豁免告警静默放行还输出"全部命中豁免清单"——已修（修复后立刻正确判出 citymap 红灯）。
  证据见 ACCEPTANCE 73-84。

## 2. 未完成
- 展板视频"画面真出"待用户前台 Play 目视（无头环境异步解码不推帧，见 ACCEPTANCE 82 后说明）；
  射线组件与命中链已实测贯通，用户现在应当**能点到**「播放演示」。
- 倍数拖拽交互（仍整数输入框）；接缝焊接（R-2）；README；
- pier_long 合并未工具化；
- Chunked 模式待用户实测反馈后再决定是否升级 BRG（P3.1，补齐阴影/深度预通道）。

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
