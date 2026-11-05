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

        /// <summary>水工具是否接管场景视图（真源=实例序列化字段，D-34，理由同笔刷 H-50）。</summary>
        static bool ToolActive(out CityTerrain t)
        {
            t = null;
            foreach (var cand in Object.FindObjectsByType<CityTerrain>(
                         FindObjectsSortMode.None))          // Editor 里低频事件才跑，规模可接受
            {
                if (cand == null || !cand.WaterToolActive) continue;
                if (!cand.gameObject.activeInHierarchy) continue;
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

            // 海平面<b>即时</b>读面板单源：不缓存到本地字段。缓存会在"面板换了项目、
            // 实例身份却没变"时过期（TryResolveGrid 只在实例身份变化时跑）⇒ 又是一处两份状态。
            var proj = CityWindow.ActiveProject;
            var sea = proj != null ? proj.SeaLevel : 0f;
            var h = EffectiveSurfaceHeight(cx, cz);

            // 用户裁定四步：高于海平面 ⇒ 先问，不静默做主（改用户输入语义的高风险动作，同 D-02-5）。
            // <b>但绝不能用 EditorUtility.DisplayDialog</b>（AGENTS §2.1.5：阻塞主线程 = 卡死指令通道，
            // 校验器 channel-discipline 项直接判 FAIL）。改成<b>面板内联确认</b>：这里只把待确认内容挂起来，
            // 由 CityWindow.DrawWaterPanel 画一条「生成陆上水体 / 取消」的确认条。
            // 语义与用户裁定的第 2、3 步一致（仍是"问过才建"），只是 UI 形态从模态框换成面板内联 ——
            // 与本包既有风格统一（H-51 的 BlockReason 黄字就是内联提示）。
            if (h >= sea && _terrain.AskBeforeLandlocked)
            {
                // 新点击<b>作废</b>上一次的待确认（否则面板确认条会指向已经不是用户意图的旧格子）
                Pending = new PendingLandlocked { cx = cx, cz = cz, height = h, sea = sea, Valid = true };
                BlockReason = null;
                return;                     // <b>不建种子</b>：等用户在面板确认
            }

            Pending = default;              // 这次是"直接建"或"取消询问"路径，清掉可能挂着的旧待确认
            float localLevel = WaterSeed.NoLocalLevel;
            if (h >= sea)
            {
                localLevel = h + _terrain.LandlockedDepth;
                Debug.Log($"{PackageIdentity.LogPrefix} 高于海平面的点击按默认水深自动生成陆上水体" +
                          $"（局部水位 {localLevel:0.##}m；面板可打开「高处点击先询问」改为手动确认）");
            }
            CommitSeed(water, cx, cz, localLevel, h);
        }

        /// <summary>待确认的陆上水体（点高处产生，用户在面板确认后才落种子）。</summary>
        internal struct PendingLandlocked
        {
            public int cx, cz;
            public float height, sea;
            public bool Valid;
        }

        /// <summary>
        /// 待确认态存在<b>工具静态字段</b>。这<b>不违反 D-34</b>：D-34 管的是「接管态必须跨域重载存活」，
        /// 而这里是一次点击的瞬时中间态 —— 域重载丢掉只是"这次确认作废、用户重点一次"，无数据风险。
        /// 若把它做成序列化字段，反而会把"我刚点了哪格"持久化进场景，污染用户文件。
        /// </summary>
        internal static PendingLandlocked Pending;

        internal static bool HasPending => Pending.Valid;

        /// <summary>取消待确认（面板「取消」按钮 / 重新点击他处时调用）。</summary>
        internal static void CancelPending() => Pending = default;

        /// <summary>确认生成：以局部水位落种子（面板「生成陆上水体」按钮调用）。</summary>
        internal static void ConfirmPending()
        {
            var p = Pending;
            Pending = default;
            if (!p.Valid || _terrain == null) return;
            var water = _terrain.Water;
            if (water == null) { BlockReason = "缺水体资产：点面板「新建水体种子列表」"; return; }
            CommitSeed(water, p.cx, p.cz, p.height + _terrain.LandlockedDepth, p.height);
        }

        /// <summary>
        /// 落种子的<b>唯一</b>提交点：撤销登记 → 写种子 → 存盘 → bump 一次 River。
        /// <para>存盘放这里而不是让调用方各自补：否则"场景里点了水、重开工程没了"与
        /// "面板按钮点了两遍 bump ⇒ 白跑一次全图洪泛"会同时出现（两条路径都曾有此问题）。</para>
        /// </summary>
        static void CommitSeed(WaterNetworkAsset water, int cx, int cz, float localLevel, float height)
        {
            Undo.RecordObject(water, "添加水");
            water.AddSeed(cx, cz, localLevel, out _);
            EditorUtility.SetDirty(water);
            AssetDatabase.SaveAssets();
            _terrain.MarkWaterChanged();              // bump River ⇒ S2 重洪泛（全程只此一次）
            BlockReason = null;
            Debug.Log($"{PackageIdentity.LogPrefix} 加水种子 ({cx},{cz}) 高程 {height:0.##}m" +
                      (float.IsPositiveInfinity(localLevel)
                          ? " 用全局海平面" : $" 局部水位 {localLevel:0.##}m（陆上水体）"));
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
            water.RemoveSeed(bestId);
            EditorUtility.SetDirty(water);
            AssetDatabase.SaveAssets();                   // 与 CommitSeed 一致：只 SetDirty 不保证落盘
            _terrain.MarkWaterChanged();
            BlockReason = null;
            Debug.Log($"{PackageIdentity.LogPrefix} 移除水种子（原格距 {bestD}）");
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
            // 海平面<b>不</b>在此缓存：项目只从面板的单一公开属性即时读（见 HandleClick）。
            // 我第一版在这里存了 `_project = CityWindow.ActiveProject` 且另立了一个从未赋值的
            // `ActiveProject` 字段 ⇒ 海平面恒读 0 ⇒ 该问的"陆上水体"不问。教训：
            // 同一状态存两份必然分叉（§0.1），且 TryResolveGrid 只在实例身份变化时才跑，缓存会过期。
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
