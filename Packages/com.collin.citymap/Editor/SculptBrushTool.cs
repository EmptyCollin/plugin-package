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
        static CityTerrain _terrain;
        static CityGrid _grid;
        static bool _hasGrid;

        static bool _stroking;
        static bool _blocked;
        static Vector2 _lastCenter;
        static float _carry;

        static SculptBrushTool()
        {
            SceneView.duringSceneGui += OnSceneGUI;
        }

        static void OnSceneGUI(SceneView sv)
        {
            if (CityWindow.CurrentMode != CityWindow.ToolMode.TerrainSculpt) return;

            var evt = Event.current;

            // 每帧重解析格网：重生成/换档位后 CityGrid 尺寸会变，缓存旧值会算错笔刷覆盖格
            if (!_hasGrid) TryResolveGrid();

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
                TryCurrentField(out var pvField);
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

            if (!_hasGrid || _terrain == null || _terrain.Delta == null) return;

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

        static void TryResolveGrid()
        {
            _hasGrid = false;
            _terrain = FindTerrain();
            if (_terrain == null) return;
            foreach (var v in Object.FindObjectsByType<CityTerrainView>(FindObjectsInactive.Exclude))
            {
                var g = v.BoundGrid;
                if (g.HasValue) { _grid = g.Value; _hasGrid = true; return; }
            }
            // CityGrid 是**值类型**：走 ctx.Grid（Project.GetGrid()），参数不合法时返回空网格，
            // 故有效性判据是 CellCount>0，而不是引用类型的 null 判断。
            var ctx = CityRuntime.ActiveContext;
            if (ctx != null)
            {
                var g = ctx.Grid;
                if (g.CellCount > 0 && g.CellSize > 1e-5f) { _grid = g; _hasGrid = true; }
            }
        }

        // 走意图注册表而非每帧全场景扫描（H-10 反模式），与面板 FindTerrainInstance 同口径
        static CityTerrain FindTerrain()
        {
            foreach (var t in IntentRegistry.All)
            {
                var ct = t as CityTerrain;
                if (ct != null && ct.enabled) return ct;
            }
            return null;
        }

        static void BeginStroke(Vector3 ground)
        {
            _blocked = false;
            if (_terrain == null || _terrain.Delta == null || !_hasGrid)
            {
                _blocked = true;
                Hud("需场景内 CityTerrain + 雕刻层 + 已生成地形（先「创建地形实例」或重新生成）");
                return;
            }
            if (!ActionSupported(Action))
            {
                _blocked = true;
                Hud("该动作（" + Action + "）本轮未接线：抬升/下压/平滑/定高到起笔点可用");
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
            if (NeedsField(Action) && !TryCurrentField(out field))
            {
                _blocked = true;
                Hud("平滑/定高需当前地形场数据——先重新生成一次");
                return;
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
                Hud("笔刷过大：单笔已截断于 " + _stroke.CellCount + " 格，建议缩小半径或分次雕刻");
            SceneView.RepaintAll();
        }

        static bool ActionSupported(SculptAction a)
            => a == SculptAction.Raise || a == SculptAction.Lower
               || a == SculptAction.Smooth || a == SculptAction.FlattenToCenter;

        static bool NeedsField(SculptAction a)
            => a == SculptAction.Smooth || a == SculptAction.FlattenToCenter;

        static bool TryCurrentField(out TerrainField field)
        {
            field = CityRuntime.ActiveContext != null
                    ? CityRuntime.ActiveContext.GetSlot<TerrainField>(StageId.S1a_TerrainBase)
                    : null;
            return field != null;
        }

        static void Hud(string msg) => Debug.Log("[SculptBrush] " + msg);
    }
}
