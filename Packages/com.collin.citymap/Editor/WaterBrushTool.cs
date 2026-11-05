using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Collin.CityMap.EditorTooling
{
    /// <summary>
    /// 水工具（02 改版）：场景视图<b>点一格</b>添加／移除水体种子。
    /// <para>与山水笔刷同构：静态挂 <c>SceneView.duringSceneGui</c>、
    /// 接管判据读场景实例的序列化字段（<see cref="CityTerrain.WaterToolActive"/>，D-34），
    /// 地面落点用 y=0 平面几何求交（派生 mesh 不入库、没有碰撞体，H-53 同族纪律）。</para>
    /// <para>交互口径（用户 2026-11-05 裁定的四步）：
    /// ①点一格 → ②该格<b>生效地表</b>高程若高于海平面 ⇒ 弹窗"是否生成陆上水体" →
    /// ③确认后建种子（局部水位=高程+水深）进种子列表；低于海平面则直接建（无局部水位）。
    /// ④种子列表在面板编辑（坐标/高度/启停/删除），这里只管"点"。</para>
    /// <para><b>判高程必须用与 S2 相同的口径</b>（场+已叠加的滤镜终态）：
    /// 面板上看着低、点下去判成高（或反之）就是"所见非所得"，用户完全无法归因。</para>
    /// </summary>
    internal static class WaterBrushTool
    {
        static CityTerrain _terrain;
        static CityTerrainView _view;
        static CityGrid _grid;
        static bool _hasGrid;
        static CityTerrain _resolvedFor;

        /// <summary>阻断原因（面板黄字呈现，H-51 纪律：静默阻断＝把归因成本推给用户）。</summary>
        internal static string BlockReason;

        /// <summary>悬停格读数（面板光标显示用；纯呈现，不参与任何判定）。</summary>
        static Vector2Int _hoverCell = new Vector2Int(-1, -1);
        static float _hoverHeight;

        static WaterBrushTool()
        {
            SceneView.duringSceneGui += OnSceneGUI;
        }

        /// <summary>
        /// 水工具是否接管场景视图（真源=实例序列化字段，D-34，理由同笔刷 H-50）。
        /// <para>⚠️ 必须走 <see cref="IntentRegistry"/> 而非 <c>FindObjectsByType</c>，两个理由：
        /// ① 带 <c>FindObjectsSortMode</c> 的重载在 Unity 6 已废弃（CS0618 ⇒ 自造 warning 顶爆 §1.6b 归零门禁）；
        /// ② 更本质的是<b>与 <c>SculptBrushTool.ToolActive</c> 同一口径</b> —— 两个抢同一颗左键的工具
        /// 若一个按注册表选、另一个按场景物体顺序选，多实例时会出现"面板显示 A 接管、笔刷操作 B"。
        /// 注册表要求 <c>enabled</c>（不要求 activeInHierarchy）：编辑态下未打开的场景里的实例仍是有效意图对象。</para>
        /// </summary>
        static bool ToolActive(out CityTerrain t)
        {
            t = null;
            foreach (var o in IntentRegistry.All)
            {
                var cand = o as CityTerrain;
                if (cand == null || !cand.enabled || !cand.WaterToolActive) continue;
                t = cand;
                return true;
            }
            return false;
        }

        static void OnSceneGUI(SceneView sv)
        {
            if (!ToolActive(out var t))
            {
                if (_resolvedFor != null) { _resolvedFor = null; _hasGrid = false; _view = null; }
                return;
            }
            _terrain = t;
            if (_resolvedFor != t) { _resolvedFor = t; TryResolveGrid(); }

            var evt = Event.current;
            if (evt == null) return;

            // 悬停读数（MouseMove 每帧一次采样，只为面板光标显示；不参与判定）
            if (evt.type == EventType.MouseMove && _hasGrid)
                if (TryGroundPoint(evt.mousePosition, out var g)) UpdateHover(g);

            if (evt.type == EventType.MouseDown && evt.button == 0 && !evt.alt)
            {
                if (!_hasGrid)
                {
                    BlockReason = "无法取格：先「重新生成」产出地形";
                    evt.Use();
                    return;
                }
                if (!TryGroundPoint(evt.mousePosition, out var g)) { evt.Use(); return; }
                HandleClick(g);
                evt.Use();                    // 接管左键：不留给框选（H-50 的教训）
                sv.Repaint();
            }
        }

        static void HandleClick(Vector3 ground)
        {
            var cx = Mathf.FloorToInt((ground.x - _grid.Origin.x) / _grid.CellSize);
            var cz = Mathf.FloorToInt((ground.z - _grid.Origin.y) / _grid.CellSize);
            if (cx < 0 || cz < 0 || cx >= _grid.Columns || cz >= _grid.Rows)
            {
                BlockReason = "点击在图幅外";
                return;
            }

            var water = _terrain.Water;
            if (_terrain.WaterMode == CityTerrain.WaterToolMode.Remove)
            {
                RemoveNearestSeed(water, cx, cz);
                return;
            }

            if (water == null)
            {
                BlockReason = "缺水体资产：点面板「新建水体列表」";
                return;
            }

            // 02 二次改版（用户 2026-11-05 裁定「海平面不用单独做了」）：
            // 点一格 = 建一颗"高程 = 该格地面 + 默认水深、带扩散半径"的种子，<b>什么都不问</b>。
            // 原先那套"高于海平面先询问是否生成陆上水体 + 内联确认条"整块删除：
            // ① 没有全局基准就没有"高/低"之分，询问的前提不成立；
            // ② 半径已经把"点山脊淹山谷"这个风险兜住了，不需要再用警告来防。
            var h = EffectiveSurfaceHeight(cx, cz);
            CommitSeed(water, cx, cz, h + _terrain.WaterDepth, _terrain.WaterSeedRadius, h);
        }

        /// <summary>
        /// 落种子的<b>唯一</b>提交点：撤销登记 → 写种子 → 存盘 → 提交一次脏位（记账落在<b>种子资产</b>上）。
        /// <para>存盘放这里而不是让调用方各自补：否则"场景里点了水、重开工程没了"与
        /// "面板按钮点了两遍 bump ⇒ 白跑一次全图洪泛"会同时出现（两条路径都曾有此问题）。</para>
        /// <para><b>不再调 <c>_terrain.MarkWaterChanged()</c></b>：那是"记在场景组件上"的旧写法，
        /// 撤销只还原本资产 ⇒ 组件账本不动 ⇒ Tracker 看不见。现在由
        /// <see cref="WaterNetworkAsset.CommitChange"/> 直接把脏位记在被撤销的那个对象上。</para>
        /// </summary>
        static void CommitSeed(WaterNetworkAsset water, int cx, int cz, float level, float radiusMeters, float height)
        {
            Undo.RecordObject(water, "添加水");
            var added = water.AddSeed(cx, cz, level, radiusMeters, out _);
            EditorUtility.SetDirty(water);
            AssetDatabase.SaveAssets();
            water.CommitChange(WaterNetworkAsset.SeedBounds(_grid, cx, cz));
            BlockReason = null;
            Debug.Log($"{PackageIdentity.LogPrefix} {(added ? "加水" : "改水")}种子 ({cx},{cz})" +
                      $" 地面 {height:0.##}m → 水面 {level:0.##}m 半径 {(radiusMeters <= 0f ? "不限" : radiusMeters.ToString("0.#") + "m")}");
        }

        static void RemoveNearestSeed(WaterNetworkAsset water, int cx, int cz)
        {
            if (water == null || water.IsEmpty) { BlockReason = "没有可移除的水体种子"; return; }
            var best = -1; var bestD = float.MaxValue; string bestId = null;
            for (var i = 0; i < water.Count; i++)
            {
                var s = water[i];
                var d = Mathf.Max(Mathf.Abs(s.cx - cx), Mathf.Abs(s.cz - cz));   // 切比雪夫：点"附近"
                if (d < bestD) { bestD = d; best = i; bestId = s.stableId; }
            }
            if (best < 0) return;
            const int MaxReach = 16;                    // 16 格 ≈ 32~64m：太远的一键就"删不到"
            if (bestD > MaxReach)
            {
                BlockReason = $"最近种子在 {bestD} 格外（超出 {MaxReach} 格触及范围）——移除水请点到种子附近";
                return;
            }
            Undo.RecordObject(water, "移除水");
            var gone = water[best];
            water.RemoveSeed(bestId);
            EditorUtility.SetDirty(water);
            AssetDatabase.SaveAssets();                   // 与 CommitSeed 一致：只 SetDirty 不保证落盘
            water.CommitChange(WaterNetworkAsset.SeedBounds(_grid, gone.cx, gone.cz));
            BlockReason = null;
            Debug.Log($"{PackageIdentity.LogPrefix} 移除水种子（原格 ({gone.cx},{gone.cz})，点处格距 {bestD}）");
        }

        // ───────────────────────── 取场与落点（与笔刷同口径） ─────────────────────────

        /// <summary>
        /// 生效地表高程：读显示层的场。面板驱动路径下 S1b 已把滤镜叠进 <c>Heights</c> ⇒ 这就是终态，
        /// 与 S2 洪泛的判据口径一致（所见即所得）。
        /// <para>残余风险（如实记录）：若滤镜刚烘焙完、管线尚未重放到这张场，此处读到的仍是旧值。
        /// 但用户"点一格"前画面已经刷过一轮，实际窗口极小；真出现"看着低、判成高"再说。</para>
        /// </summary>
        static float EffectiveSurfaceHeight(int cx, int cz)
        {
            var f = _view != null ? _view.BoundField : null;
            if (f == null) f = CityRuntime.ActiveContext != null
                ? TerrainBaseStage.EnsureField(CityRuntime.ActiveContext) : null;
            if (f == null) return 0f;
            var w = _grid.CellCenterWorld(cx, cz);
            return f.GetHeight(w.x, w.y);
        }

        static void TryResolveGrid()
        {
            _hasGrid = false;
            _view = _terrain != null ? _terrain.GetComponent<CityTerrainView>() : null;
            var g = _view != null ? _view.BoundGrid : (CityGrid?)null;
            if (g.HasValue) { _grid = g.Value; _hasGrid = true; }
            else
            {
                var ctx = CityRuntime.ActiveContext;
                if (ctx != null)
                {
                    var cg = ctx.Grid;
                    if (cg.CellCount > 0 && cg.CellSize > 1e-5f) { _grid = cg; _hasGrid = true; }
                }
            }
            // ⚠️ 这里**不缓存任何项目/水位**（原注释记着上一版的教训，语义已随 02 二次改版变化，
            // 但纪律本身不变，所以留下来）：我第一版在此存过 `_project = CityWindow.ActiveProject`，
            // 而 TryResolveGrid 只在"实例身份变化"时跑 ⇒ 换项目后缓存过期，水工具读着旧基准做判断。
            // 现在每颗种子自带水面高程，点击时只读<b>该格地面高程</b>（与 S2 同一口径，见 EffectiveSurfaceHeight），
            // 不再需要任何全局基准 —— 也就没有第二份状态可以分叉（§0.1 原则2）。
            // CityWindow.ActiveProject 这个静态入口已于 2026-11-06 删除，别加回来。
        }

        /// <summary>鼠标 → y=0 世界平面落点（纯几何，不依赖碰撞体，同笔刷 H-53 纪律）。</summary>
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

        static void UpdateHover(Vector3 g)
        {
            var cx = Mathf.FloorToInt((g.x - _grid.Origin.x) / _grid.CellSize);
            var cz = Mathf.FloorToInt((g.z - _grid.Origin.y) / _grid.CellSize);
            if (cx < 0 || cz < 0 || cx >= _grid.Columns || cz >= _grid.Rows) return;
            _hoverCell = new Vector2Int(cx, cz);
            _hoverHeight = EffectiveSurfaceHeight(cx, cz);
        }

        /// <summary>面板悬停读数（光标处生效高程），返回 false=无有效悬停。</summary>
        internal static bool TryGetHover(out Vector2Int cell, out float height)
        {
            cell = _hoverCell; height = _hoverHeight;
            return _hoverCell.x >= 0;
        }
    }
}
