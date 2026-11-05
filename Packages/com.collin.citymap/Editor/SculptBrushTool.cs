using UnityEditor;
using UnityEngine;

namespace Collin.CityMap.EditorTooling
{
    /// <summary>
    /// 山水雕刻笔刷的场景交互壳（Modules/01 §5.1/§5.2，A-1-6「一笔一撤销」）。
    /// <para>职责边界：本类<b>只做输入与可视化</b>——把鼠标轨迹换算成世界落点、驱动
    /// <see cref="SculptBrush"/> 的纯数学、把结果写进 <see cref="SculptDeltaAsset"/>，
    /// 再调 <see cref="CityTerrain.MarkDeltaChanged"/>。<b>不在这里重算地形或重建 mesh</b>：
    /// 落笔后由既有 D-31 链路（面板合并窗口 → S1b 重算 → 显示层按脏区增量重建）统一刷新，
    /// 保证"编辑态所见"与"重新生成所得"是同一条路径（§0.1 原则3）。</para>
    /// <para>接管方式：<see cref="SceneView.duringSceneGui"/> 静态挂接，判据读
    /// <see cref="CityTerrain.SculptToolActive"/>（<b>场景实例的序列化字段</b>，H-50／D-34）；
    /// 未接管时一律原样放行鼠标。本轮只接**有输入来源**的动作（抬/压/平滑/定高到起笔点），
    /// 反色/清除/定高到指定值需要先给面板数值框，留待后续，未接线动作会明确提示而非静默。</para>
    /// <para>⚠️ 曾经的判据是 <c>CityWindow.CurrentMode</c>（EditorWindow 实例字段的静态投影），
    /// 它随域重载归零 ⇒ 出现「面板显示雕刻模式、笔刷却按 Inspect 放行鼠标」，
    /// 那个属性已于 2026-11-06 连同 ActiveProject／ActiveRunner 一起删除，<b>不要把它加回来</b>。</para>
    /// </summary>
    [InitializeOnLoad]
    internal static class SculptBrushTool
    {
        // ── 工具参数（面板可调，此处给保守默认） ──
        internal static float RadiusMeters = 30f;
        internal static float Strength = 1f;
        internal static SculptAction Action = SculptAction.Raise;
        /// <summary>平滑保边（bilateral）开关：开=只抹碎不削坡（陡坎样本被压权，磨不到折线）。</summary>
        internal static bool EdgePreserve = false;
        /// <summary>保边高度带宽 σ（米）：盘内与本格高差超过 ~2σ 的样本基本不计权。</summary>
        internal static float SigmaMeters = 3f;

        static readonly SculptStroke _stroke = new SculptStroke();
        /// <summary>本帧解析到的地形实例（含其接管开关）；null = 场景里没有可用实例。</summary>
        static CityTerrain _terrain;
        static CityGrid _grid;
        static bool _hasGrid;
        /// <summary>本帧解析到的显示层，用于取贴地高度（H-53）。</summary>
        static CityTerrainView _view;
        /// <summary>上次解析所对应的激活实例；变了才重解析（避免每事件全表扫描）。</summary>
        static CityTerrain _resolvedFor;

        static bool _stroking;
        static bool _blocked;
        static Vector2 _lastCenter;
        static float _carry;

        /// <summary>
        /// 本笔已写入的增量累计（格线性号 → 米）。H-65 的核心修复载体。
        /// <para>存在理由：场（<c>_view.BoundField</c>）在**整个笔划期间不更新**——刷新要等松手提交后
        /// 走 S1b→显示层链路。于是每个 MouseDrag 事件读到的 <c>field.GetHeight</c> 都是**同一份旧值**，
        /// 平滑增量 <c>(mean − self)·λ·fall</c> 每事件被重复算出同一个数并 <c>AddDelta</c> 累加。
        /// 实测：±1m 棋盘磨 20 个事件 → 累积 delta = <b>−21 / +21 m</b>（单次上限本应 ≈2m）
        /// ⇒ 平滑把噪音放大了 20 倍，正是用户说的「噪音反而变大」。
        /// 抬升/下压不受影响（其增量语义本就是"位移积分"，累加是正确行为）。</para>
        /// <para>解法：平滑与定高读取的"有效面"= 场 + 本笔累计，使同一格在同一笔内**收敛**而非发散。
        /// 用字典而非预分配窗口：范围随笔划自然增长，不必猜笔划长度，也不吃大块内存。</para>
        /// </summary>
        static readonly System.Collections.Generic.Dictionary<int, float> _strokeAcc
            = new System.Collections.Generic.Dictionary<int, float>();

        /// <summary>取"有效面"高度：场当前值 + 本笔已写入的累计增量。</summary>
        static float EffectiveHeight(TerrainField f, int cellIndex, float wx, float wz)
        {
            var v = f.GetHeight(wx, wz);
            float add;
            return _strokeAcc.TryGetValue(cellIndex, out add) ? v + add : v;
        }

        /// <summary>阻断原因（面板可见）：H-51 实测「提示只打在控制台 Log，用户看不到 ⇒ 表现为拖了没反应」。</summary>
        internal static string BlockReason;

        static SculptBrushTool()
        {
            SceneView.duringSceneGui += OnSceneGUI;
        }

        // ─────────── M4-m：局部积分图（非保边平滑的精确窗口均值） ───────────
        // 32 点盘采样实测在大半径下混叠（R=200 对 λ=60 残余 0.51，理论≈0）⇒ 该抹的没抹掉、
        // 且相邻格样本相位不齐可能出斑块。积分图每格 O(1) 且任意半径精确；矩形窗口关于格中心
        // 对称 ⇒ 保住"任意斜面是精确不动点"（只吃曲率不推坡）。
        // 保边模式仍用点采样（bilateral 依赖局部高差门控，点采样是其标准实现）。
        // 缓冲静态复用：绝不逐事件新建 —— 那正是 M4-j 修掉的 LOH/GC 读条根因。

        static TerrainField _stampField;
        static float[] _prefixBuf = new float[4096];
        static Vector2 _prefixOrigin;                       // 图幅左下角世界 XZ
        static int _prefixCols, _prefixRows, _prefixBaseCx, _prefixBaseCz;
        static int _prefixHalf;                             // 窗口半径（格）
        static bool _prefixOk;                              // 本次落笔建图是否成功（失败 ⇒ 平滑退路走点采样）

        /// <summary>有效面采样（场 + 本笔累计），H-65 口径。</summary>
        static float SampleEff(float x, float z) => EffectiveHeightAt(_stampField, x, z);

        /// <summary>图内局部格号 → 有效面高度。</summary>
        static float PrefixCellHeight(int gx, int gz)
        {
            var cell = _grid.CellSize;
            var wx = _prefixOrigin.x + (_prefixBaseCx + gx + 0.5f) * cell;
            var wz = _prefixOrigin.y + (_prefixBaseCz + gz + 0.5f) * cell;
            return EffectiveHeightAt(_stampField, wx, wz);
        }

        /// <summary>
        /// 为<b>本次落笔</b>构建局部积分图。必须逐事件重建：<c>_strokeAcc</c> 每事件都在变（H-65），
        /// 缓存会读到过期的本笔累计。覆盖"事件中心 ± (影响半径 + 半窗)"，成本随笔刷半径、与全图无关。
        /// </summary>
        static bool BuildStampPrefix(Vector2 center)
        {
            var cell = _grid.CellSize;
            var reach = Mathf.CeilToInt(RadiusMeters / cell);
            _prefixHalf = reach;
            var bcx = Mathf.FloorToInt((center.x - _grid.Origin.x) / cell);
            var bcz = Mathf.FloorToInt((center.y - _grid.Origin.y) / cell);
            var x0 = Mathf.Max(0, bcx - 2 * reach);
            var z0 = Mathf.Max(0, bcz - 2 * reach);
            var x1 = Mathf.Min(_grid.Columns - 1, bcx + 2 * reach);
            var z1 = Mathf.Min(_grid.Rows - 1, bcz + 2 * reach);
            _prefixCols = x1 - x0 + 1;
            _prefixRows = z1 - z0 + 1;
            if (_prefixCols < 3 || _prefixRows < 3) return false;

            var need = (_prefixCols + 1) * (_prefixRows + 1);
            if (_prefixBuf.Length < need)
            {
                var cap = _prefixBuf.Length;
                while (cap < need) cap *= 2;
                _prefixBuf = new float[cap];
            }
            _prefixBaseCx = x0; _prefixBaseCz = z0;
            _prefixOrigin = _grid.Origin;
            SculptBrush.BuildLocalPrefix(PrefixCellHeight, _prefixCols, _prefixRows, _prefixBuf);
            return true;
        }

        /// <summary>取格 (cx,cz) 的窗口均值（图内局部号换算 + 边缘钳制由 LocalBoxMean 负责）。</summary>
        static float WindowMean(int cx, int cz)
            => SculptBrush.LocalBoxMean(_prefixBuf, _prefixCols, _prefixRows,
                                        cx - _prefixBaseCx - _prefixHalf, cz - _prefixBaseCz - _prefixHalf,
                                        cx - _prefixBaseCx + _prefixHalf, cz - _prefixBaseCz + _prefixHalf);

        /// <summary>
        /// 笔刷是否接管场景视图。<b>真源 = 场景实例上的 <see cref="CityTerrain.SculptToolActive"/></b>（H-50）。
        /// <para>绝不能再读 <c>CityWindow.CurrentMode</c>：那是 EditorWindow 的<b>静态</b>字段，
        /// 域重载（改代码 / 重开场景 / 进 Play）后归零、且窗口实例由布局恢复时不会回填它 ⇒
        /// 面板显示在雕刻模式、这里却读到 Inspect，鼠标事件原样放行给默认工具，
        /// 用户看到的就是「左键变框选 + 光圈消失」。序列化字段能跨域重载存活，这才是真源。</para>
        /// </summary>
        static bool ToolActive(out CityTerrain t)
        {
            t = null;
            foreach (var o in IntentRegistry.All)
            {
                var ct = o as CityTerrain;
                if (ct != null && ct.enabled && ct.SculptToolActive) { t = ct; return true; }
            }
            return false;
        }

        static void OnSceneGUI(SceneView sv)
        {
            if (!ToolActive(out _terrain)) return;

            var evt = Event.current;

            // 只在「激活实例变了」或「还没解析到格网」时重解析：
            // OnSceneGUI 每个事件都会跑（Layout/Repaint/Mouse…），每事件全表扫描是 H-10 反模式。
            // 数据出现后 _hasGrid 由 false→true，靠这个条件自然补上；_view 的场每帧现读，无需重解析。
            if (!ReferenceEquals(_resolvedFor, _terrain) || !_hasGrid)
            {
                TryResolveGrid();
                _resolvedFor = _terrain;
            }

            Vector3 ground;
            var havePoint = TryGroundPoint(evt.mousePosition, out ground);

            // Layout 阶段声明被动焦点，保证笔刷激活时仍收到 MouseDown（即便无选中物体）
            if (evt.type == EventType.Layout)
                HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));

            // 范围指示<b>先于</b>落笔前置条件绘制：只要 hover 到地面就画出<b>贴地表</b>轮廓。
            // 旧版把环画在 y=0 平面、且在数据不全时画环前就 return —— 结果 ±18m 起伏把环埋住，
            // 用户完全看不出笔刷有多大（用户反馈①）。采样当前高度场逐段描边，真正贴着地形走。
            if (havePoint && evt.type == EventType.Repaint)
            {
                var pvField = CurrentField();
                void Ring(float r, Color col)
                {
                    if (r <= 0f) return;
                    const int Seg = 48;
                    Vector3 Edge(int i)
                    {
                        var a = i * Mathf.PI * 2f / Seg;
                        var x = ground.x + Mathf.Cos(a) * r;
                        var z = ground.z + Mathf.Sin(a) * r;
                        var y = pvField != null ? pvField.GetHeight(x, z) + 0.08f : 0f;   // 略抬防 z-fighting
                        return new Vector3(x, y, z);
                    }
                    Handles.color = col;
                    var prev = Edge(0);
                    for (var i = 1; i <= Seg; i++) { var cur = Edge(i); Handles.DrawAAPolyLine(2f, prev, cur); prev = cur; }
                }
                Ring(RadiusMeters, _stroking ? new Color(1f, 0.55f, 0.2f) : new Color(0.3f, 0.8f, 1f));
                Ring(RadiusMeters * CoreRatio, new Color(0.3f, 0.8f, 1f, 0.5f));   // 核心半径提示
            }

            if (!_hasGrid || _terrain == null) return;
            if (_terrain.Delta == null)
            {
                // H-51：缺雕刻层时把原因写进面板可见字段，不再只打控制台 Log
                BlockReason = "该地形实例还没有雕刻层：点面板「② 雕山水」会自动创建并绑定。";
                if (evt.type == EventType.MouseDown && evt.button == 0) { _blocked = true; evt.Use(); }
                return;
            }
            BlockReason = null;

            switch (evt.type)
            {
                case EventType.MouseDown:
                    if (evt.button != 0 || !havePoint) break;
                    BeginStroke(ground);
                    evt.Use();
                    break;
                case EventType.MouseDrag:
                    if (evt.button != 0 || !_stroking || _blocked || !havePoint) break;
                    ApplyDrag(ground);
                    evt.Use();
                    break;
                case EventType.MouseUp:
                    if (evt.button != 0 || !_stroking) break;
                    EndStroke();
                    evt.Use();
                    break;
            }
        }

        // 核心半径占笔刷半径的比例（与衰减核 core 参数一致，画在预览环上）
        const float CoreRatio = 0.35f;

        /// <summary>鼠标 → y=0 世界平面落点（纯几何，不依赖碰撞体：派生 mesh 是不入库隐藏物体）。</summary>
        static bool TryGroundPoint(Vector2 mousePx, out Vector3 point)
        {
            point = default;
            var ray = HandleUtility.GUIPointToWorldRay(mousePx);
            if (Mathf.Abs(ray.direction.y) < 1e-6f) return false;
            var d = -ray.origin.y / ray.direction.y;
            if (d <= 0f) return false;
            point = ray.origin + ray.direction * d;
            return true;
        }

        /// <summary>
        /// 解析格网与显示层。<b>不在此重设 _terrain</b>：接管实例由 <see cref="ToolActive"/> 选定
        /// （只有把开关打开的那个实例才是操作对象），这里若再扫一遍注册表，会取到"第一个"实例
        /// 而非"被激活"的那个 —— 多城场景下就是雕 A 城改 B 城。
        /// </summary>
        static void TryResolveGrid()
        {
            _hasGrid = false;
            _view = null;
            if (_terrain == null) return;

            // 显示层与意图组件同物体（CreateTerrainInstance 一起挂的），直接 GetComponent，
            // 不用 FindObjectsByType 全场景扫（H-10 反模式）。
            _view = _terrain.GetComponent<CityTerrainView>();
            var g = _view != null ? _view.BoundGrid : (CityGrid?)null;
            if (g.HasValue) { _grid = g.Value; _hasGrid = true; return; }

            // 兜底：面板驱动路径的权威上下文。CityGrid 是**值类型**，参数不合法时返回空网格，
            // 故有效性判据是 CellCount>0，而不是引用类型的 null 判断。
            var ctx = CityRuntime.ActiveContext;
            if (ctx != null)
            {
                var cg = ctx.Grid;
                if (cg.CellCount > 0 && cg.CellSize > 1e-5f) { _grid = cg; _hasGrid = true; }
            }
        }

        /// <summary>
        /// 当前高度场（贴地光圈与平滑/定高的取样源）。
        /// <para>H-53：优先取显示层的场 —— 它在<b>面板驱动</b>与<b>自建路径</b>（Play / 无面板）
        /// 两条分支里都会被解析出来，而 <c>CityRuntime.ActiveContext</c> 只在面板驱动时有值。
        /// 原先只读 ActiveContext，域重载后面板还没跑过 Commit ⇒ 场为 null ⇒ 光圈高度退化成 0，
        /// 环又被 ±18m 的起伏埋掉（反馈①的另一半根因）。
        /// 与画面同源还保证「光圈贴的是你看到的那张皮」。</para>
        /// </summary>
        static TerrainField CurrentField()
        {
            var f = _view != null ? _view.BoundField : null;
            if (f != null) return f;
            var ctx = CityRuntime.ActiveContext;
            return ctx != null ? ctx.GetSlot<TerrainField>(StageId.S1a_TerrainBase) : null;
        }

        static void BeginStroke(Vector3 ground)
        {
            _blocked = false;
            if (_terrain == null || _terrain.Delta == null || !_hasGrid)
            {
                _blocked = true;
                BlockReason = "无法落笔：需" + (_terrain == null ? "激活的地形实例" : _terrain.Delta == null
                    ? "雕刻层（点「② 雕山水」自动创建）" : "已生成的地形（先「重新生成」）");
                return;
            }
            if (!ActionSupported(Action))
            {
                _blocked = true;
                BlockReason = "该动作（" + Action + "）本轮未接线：抬升/下压/平滑/定高到起笔点可用";
                return;
            }

            Undo.RecordObject(_terrain.Delta, "Sculpt Stroke");   // 一笔 = 一个 undo（A-1-6），捕获改前态
            _stroking = true;
            _carry = 0f;
            // H-65：每笔开始清空本笔累计表。残留会让下一笔读到上一笔的增量 ⇒ 平滑跨笔互相放大，
            // 且撤销上一笔后本笔的"有效面"仍是错的（表比 delta 多算了一份）。
            _strokeAcc.Clear();
            _lastCenter = new Vector2(ground.x, ground.z);
            _stroke.Begin(_lastCenter);
            StampRadius(_lastCenter, SculptBrush.StepDistanceM);    // 单击也有起笔量，避免"点了没反应"
        }

        static void ApplyDrag(Vector3 ground)
        {
            var center = new Vector2(ground.x, ground.z);
            var moved = Vector2.Distance(center, _lastCenter);
            _lastCenter = center;
            var steps = SculptBrush.AccumulateSteps(moved, ref _carry);
            if (steps == 0) return;   // 不足一步：carry 累到下一事件（慢速精细雕刻靠这个）
            StampRadius(center, steps * SculptBrush.StepDistanceM);
        }

        /// <summary>按衰减核把走了 distanceMeters 的量刷进半径内的逻辑格（帧率无关：位移驱动，不乘帧时）。</summary>
        static void StampRadius(Vector2 center, float distanceMeters)
        {
            if (!_hasGrid || _terrain == null || _terrain.Delta == null) return;

            TerrainField field = null;
            if (NeedsField(Action))
            {
                field = CurrentField();
                if (field == null)
                {
                    _blocked = true;
                    BlockReason = "平滑/定高需当前地形场数据——先「重新生成」一次";
                    return;
                }
            }

            var cell = _grid.CellSize;
            if (cell <= 1e-5f) return;
            var span = Mathf.CeilToInt(RadiusMeters / cell) + 1;

            // H-63：登记本步的**实际影响面**（笔刷中心 ± 半径，半径外衰减核恒为 0）。
            // 放在所有早退之后、循环之前：真正要写格了才计入，避免无写入却虚报脏区。
            // 累积整条笔划的范围，松手时按它提交脏区 —— 只报终点会让中段改了却不重建。
            _stroke.AccumulateExtent(
                new Vector2(center.x - RadiusMeters, center.y - RadiusMeters),
                new Vector2(center.x + RadiusMeters, center.y + RadiusMeters));

            // M4-m：非保边平滑的窗口均值走局部积分图。**必须逐事件重建**——_strokeAcc 每事件都在变
            // （H-65），缓存会读到过期的本笔累计。保边模式用点采样，不建图（省一整个窗口的构建成本）。
            _prefixOk = false;
            if (Action == SculptAction.Smooth)
            {
                _stampField = field;
                if (!EdgePreserve) _prefixOk = BuildStampPrefix(center);
            }

            // 从世界中心反推起始格（内联 floor，避免 center 略越界时 CellIndex 返回错序 index）
            var bcx = Mathf.FloorToInt((center.x - _grid.Origin.x) / cell);
            var bcz = Mathf.FloorToInt((center.y - _grid.Origin.y) / cell);
            var core = RadiusMeters * CoreRatio;
            var baseH = NeedsField(Action) ? field.GetHeight(center.x, center.y) : 0f;

            for (var dz = -span; dz <= span; dz++)
            for (var dx = -span; dx <= span; dx++)
            {
                var cx = bcx + dx;
                var cz = bcz + dz;
                if (cx < 0 || cz < 0 || cx >= _grid.Columns || cz >= _grid.Rows) continue;

                var w = _grid.CellCenterWorld(cx, cz);
                var fall = SculptBrush.FalloffAt(Vector2.Distance(w, center), RadiusMeters, core);
                if (fall <= 1e-4f) continue;

                _stroke.Collect(cz * _grid.Columns + cx);   // 稳定线性格号，HashSet 去重

                switch (Action)
                {
                    case SculptAction.Raise:
                        _terrain.Delta.AddDelta(_grid, cx, cz, SculptBrush.DeltaFor(distanceMeters, Strength, fall));
                        break;
                    case SculptAction.Lower:
                        _terrain.Delta.AddDelta(_grid, cx, cz, -SculptBrush.DeltaFor(distanceMeters, Strength, fall));
                        break;
                    case SculptAction.FlattenToCenter:
                        // 定高到起笔点：按衰减把本格拉向 baseH（绝对写入，与 Additive 正交）
                        _terrain.Delta.SetAbsolute(_grid, cx, cz,
                            Mathf.Lerp(field.GetHeight(w.x, w.y), baseH, fall));
                        break;
                    case SculptAction.Smooth:
                        // H-65：self 与目标都必须读**有效面**（场 + 本笔累计），否则每个拖拽事件
                        // 对着笔划期间不更新的旧场重复算出同一增量并累加 ⇒ 噪音被放大（实测 20 倍）。
                        // 定高分支不改：SetAbsolute 是覆盖语义，重复写同一目标值天然幂等。
                        // M4-m（D-40）：平滑尺度 = 笔刷半径。两条核：
                        //   非保边 → 局部积分图的精确窗口均值（O(1)/格、任意半径无混叠）；
                        //   保边   → 32 点盘采样 + 高差 σ 加权（bilateral，点采样是标准实现）。
                        var sIdx = cz * _grid.Columns + cx;
                        var sh = EffectiveHeight(field, sIdx, w.x, w.y);
                        var mean = (EdgePreserve || !_prefixOk)
                            // 保边模式，或建图失败（图幅过小等）⇒ 退路走 32 点盘采样，绝不让平滑静默变 0
                            ? SculptBrush.DiskMean(SampleEff, w.x, w.y, RadiusMeters,
                                                   EdgePreserve, sh, SigmaMeters)
                            : WindowMean(cx, cz);
                        var sd = SculptBrush.SmoothDelta(sh, mean, Strength, fall);
                        _terrain.Delta.AddDelta(_grid, cx, cz, sd);
                        float sAcc;
                        _strokeAcc[sIdx] = _strokeAcc.TryGetValue(sIdx, out sAcc) ? sAcc + sd : sd;
                        break;
                }
            }
        }

        /// <summary>
        /// 盘样本的<b>有效面</b>采样（世界 XZ 版，M4-m）：格号按 floor 反推，界内才查本笔累计表
        /// （越界的线性格号会绕到别的行，绝不能拿它查表）；界外只读场，<c>Sample</c> 自带 clamp。
        /// <para>H-65 语义原样保留：读"场 + 本笔累计"而非旧场，同一格在同一笔内才收敛不发散。</para>
        /// </summary>
        static float EffectiveHeightAt(TerrainField f, float wx, float wz)
        {
            var cx = Mathf.FloorToInt((wx - _grid.Origin.x) / _grid.CellSize);
            var cz = Mathf.FloorToInt((wz - _grid.Origin.y) / _grid.CellSize);
            if (cx < 0 || cz < 0 || cx >= _grid.Columns || cz >= _grid.Rows) return f.GetHeight(wx, wz);
            return EffectiveHeight(f, cz * _grid.Columns + cx, wx, wz);
        }

        static void EndStroke()
        {
            _stroking = false;
            if (_terrain == null) { _stroke.End(); return; }

            // H-63：按**整条笔划的轨迹包围盒**提交脏区。
            // 旧口径 MarkDeltaChanged(落点, 半径) 只报终点圆 —— 长笔划中段已写进 delta 的格
            // 落在脏盒外 ⇒ S1b 与显示层都不重算 ⇒ 用户看到的"只有最后一截拖拽生效"。
            // 必须在 End() 之前取范围（End 之后语义上这笔已结束，不该再改它的记账）。
            Vector2 emin, emax;
            if (_stroke.TryGetExtentWorld(out emin, out emax))
                _terrain.MarkDeltaChanged(new Bounds(
                    new Vector3((emin.x + emax.x) * 0.5f, 0f, (emin.y + emax.y) * 0.5f),
                    new Vector3(emax.x - emin.x, 1f, emax.y - emin.y)),
                    // 每边再加一档笔刷半径：与旧圆心口径的侧向外扩**逐轴等价**（50.5 → 边缘外 50.5）。
                    // 只报包围盒而不给外扩，会让平整开启时的侧向约束带漏重建（自查发现的退化，见 D-38）。
                    RadiusMeters);
            else
                _terrain.MarkDeltaChanged(_lastCenter, RadiusMeters);   // 没刷到任何格（被阻断）：退回点口径

            _stroke.End();
            EditorUtility.SetDirty(_terrain.Delta);           // 雕刻资产是 ScriptableObject，改动须标脏才持久化
            if (_stroke.Truncated)
                BlockReason = "笔刷过大：单笔已截断于 " + _stroke.CellCount + " 格，建议缩小半径或分次雕刻";
            SceneView.RepaintAll();
        }

        static bool ActionSupported(SculptAction a)
            => a == SculptAction.Raise || a == SculptAction.Lower
               || a == SculptAction.Smooth || a == SculptAction.FlattenToCenter;

        static bool NeedsField(SculptAction a)
            => a == SculptAction.Smooth || a == SculptAction.FlattenToCenter;
    }
}
