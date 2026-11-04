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
    /// <para>接管方式：<see cref="SceneView.duringSceneGui"/> 静态挂接，仅在
    /// <see cref="CityWindow.CurrentMode"/> == <see cref="CityWindow.ToolMode.TerrainSculpt"/>
    /// 时处理鼠标；其它模式一律原样放行。本轮只接**有输入来源**的动作（抬/压/平滑/定高到起笔点），
    /// 反色/清除/定高到指定值需要先给面板数值框，留待后续，未接线动作会明确提示而非静默。</para>
    /// </summary>
    [InitializeOnLoad]
    internal static class SculptBrushTool
    {
        // ── 工具参数（面板可调，此处给保守默认） ──
        internal static float RadiusMeters = 30f;
        internal static float Strength = 1f;
        internal static SculptAction Action = SculptAction.Raise;

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

        /// <summary>阻断原因（面板可见）：H-51 实测「提示只打在控制台 Log，用户看不到 ⇒ 表现为拖了没反应」。</summary>
        internal static string BlockReason;

        static SculptBrushTool()
        {
            SceneView.duringSceneGui += OnSceneGUI;
        }

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
                        var h = field.GetHeight(w.x, w.y);
                        _terrain.Delta.AddDelta(_grid, cx, cz,
                            SculptBrush.SmoothDelta(h, NeighborMean(field, w, cell), Strength, fall));
                        break;
                }
            }
        }

        static float NeighborMean(TerrainField f, Vector2 w, float cell)
        {
            float sum = 0f; var n = 0;
            for (var i = -1; i <= 1; i++)
            for (var j = -1; j <= 1; j++)
            {
                if (i == 0 && j == 0) continue;
                sum += f.GetHeight(w.x + i * cell, w.y + j * cell);
                n++;
            }
            return n > 0 ? sum / n : f.GetHeight(w.x, w.y);
        }

        static void EndStroke()
        {
            _stroking = false;
            _stroke.End();
            if (_terrain == null) return;

            EditorUtility.SetDirty(_terrain.Delta);           // 雕刻资产是 ScriptableObject，改动须标脏才持久化
            // 用笔刷中心而非 transform.position 提交脏区（H-42 同族：算错影响范围→雕了别处画面不动）
            _terrain.MarkDeltaChanged(_lastCenter, RadiusMeters);
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
