using System;
using System.Collections.Generic;
using UnityEngine;

namespace Collin.StealthPlanner
{
    /// <summary>
    /// <b>玩家路点数据集</b>（静态设计 4「导出前 M 条解」的产物，亦是动态设计 1「注入实跑」的输入）。
    /// <para>DESIGN D-07 定了<b>双形态</b>：同一份数据既能 <see cref="ToJson"/> 成 JSON（可 diff、可版本化、
    /// 静态4 注入走这条），又能作为 <see cref="ScriptableObject"/> 进 Inspector（用户手配走这条）。
    /// 本类就是那个 SO，JSON 是它的序列化视图。</para>
    ///
    /// <para><b>canonical 表示 = 每层站位时间线 <c>cellsPerLayer</c></b>（下标=全局时间桶，值=该桶玩家所在格）。
    /// 为什么存这个、而不是只存"移动路点"：<c>PhaseKShortest.Route</c> 里相邻层同格=原地等待，
    /// 一旦折叠成稀疏路点，"在第几层等了多久"就依赖注入端如何用速度反推——那会造出第二套时间口径
    /// （本包反复踩的漂移教训）。存满时间线是<b>无损</b>的：复现器（M5）逐层读站位即可，不猜。
    /// 需要稀疏路点时再经 <see cref="MovementWaypoints"/> 派生，那条才是给策划看/给巡逻路点用的视图。</para>
    ///
    /// <para><b>网格快照</b>（origin/格长/Δt/维度）：数据集里的格是<b>线性索引</b>，换一张表就失去含义。
    /// 注入端必须先 <see cref="GridMatches"/> 校验，不匹配就<b>拒绝</b>，而不是拿错网格硬解出乱坐标。</para>
    ///
    /// <para>纯数据 + 纯函数，不引 UnityEditor ⇒ 引擎侧可加载同一份 JSON 复现。</para>
    /// </summary>
    [CreateAssetMenu(menuName = "Collin/Stealth Planner/玩家路点数据集", fileName = "NewPlayerRouteDataset")]
    public sealed class PlayerRouteDataset : ScriptableObject
    {
        /// <summary>数据格式版本。字段布局变更必须抬它，注入端遇不匹配一律拒绝（同 D-22 的教训）。</summary>
        public const int SchemaVersion = 1;

        [SerializeField] int _schemaVersion = SchemaVersion;
        [SerializeField] List<RouteEntry> _routes = new List<RouteEntry>();
        [SerializeField] int _dimX;
        [SerializeField] int _dimZ;
        [SerializeField] float _cellSize;
        [SerializeField] float _timeStep;
        [SerializeField] Vector3 _origin;

        /// <summary>单条路线：一条玩家动线。</summary>
        [Serializable]
        public struct RouteEntry
        {
            /// <summary>每层站位：下标=全局时间桶（0=起点层），值=格线性索引。相邻两项相等=原地等待。</summary>
            public int[] cellsPerLayer;
            public int arrivalBucket;      // 抵达撤离点的层
            public float arrivalSeconds;   // 抵达时刻（秒）
            public int goalCell;           // 撤离点格（= cellsPerLayer 末项，冗余存以便校验与展示）
            public int startCell;          // 起点格（= cellsPerLayer[0]）
        }

        /// <summary>数据集内的路线数。</summary>
        public int Count => _routes.Count;
        public int DimX => _dimX;
        public int DimZ => _dimZ;
        public float CellSize => _cellSize;
        public float TimeStep => _timeStep;
        public Vector3 Origin => _origin;
        public int Schema => _schemaVersion;

        /// <summary>只读某条路线（越界返回 false）。</summary>
        public bool TryGetRoute(int index, out RouteEntry route)
        {
            if (index < 0 || index >= _routes.Count) { route = default; return false; }
            route = _routes[index];
            return true;
        }

        /// <summary>
        /// 从 <see cref="PhaseKShortest"/> 产出的路线集合构建数据集，写入网格快照（取自 <paramref name="table"/>）。
        /// <paramref name="table"/> 为 null 或路线为空 ⇒ 返回 false、数据集保持空（不产出半成品）。
        /// </summary>
        public bool BuildFromRoutes(VisibilityTable table, IReadOnlyList<PhaseKShortest.Route> routes)
        {
            if (table == null || routes == null || routes.Count == 0) return false;

            _schemaVersion = SchemaVersion;
            _dimX = table.DimX;
            _dimZ = table.DimZ;
            _cellSize = table.CellSize;
            _timeStep = table.TimeStep;
            _origin = table.Origin;

            _routes.Clear();
            for (var i = 0; i < routes.Count; i++)
            {
                var r = routes[i];
                if (r == null || r.CellsPerLayer.Count == 0) continue;   // 跳过空路线，但整批仍非空才成功
                _routes.Add(new RouteEntry
                {
                    cellsPerLayer = r.CellsPerLayer.ToArray(),
                    arrivalBucket = r.ArrivalBucket,
                    arrivalSeconds = r.ArrivalSeconds,
                    startCell = r.CellsPerLayer[0],
                    goalCell = r.CellsPerLayer[r.CellsPerLayer.Count - 1],
                });
            }
            return _routes.Count > 0;
        }

        /// <summary>序列化为 JSON（可 diff / 落盘 / 注入）。空数据集返回空串并给原因。</summary>
        public string ToJson(out string fail)
        {
            fail = null;
            if (_routes.Count == 0) { fail = "数据集为空：先求解并加入路线再导出"; return string.Empty; }
            var dto = Snapshot();
            var s = JsonUtility.ToJson(dto, true);
            if (string.IsNullOrEmpty(s)) fail = "JSON 序列化失败（Unity JsonUtility 返回空）";
            return s ?? string.Empty;
        }

        /// <summary>从 JSON 载入（覆盖当前内容）。<paramref name="fail"/> 非空表示失败、内容保持原样。</summary>
        public bool FromJson(string json, out string fail)
        {
            fail = null;
            if (string.IsNullOrWhiteSpace(json)) { fail = "JSON 为空"; return false; }
            FileSnapshot dto;
            try { dto = JsonUtility.FromJson<FileSnapshot>(json); }
            catch (Exception e) { fail = "JSON 解析失败：" + e.Message; return false; }
            if (dto == null) { fail = "JSON 解析得到 null（结构不符）"; return false; }
            if (dto.schemaVersion != SchemaVersion)
            {
                fail = "数据格式版本 " + dto.schemaVersion + " ≠ 当前 " + SchemaVersion + "（旧数据集请用新版本重新导出）";
                return false;
            }
            if (dto.routes == null || dto.routes.Length == 0) { fail = "数据集内无路线"; return false; }

            _schemaVersion = dto.schemaVersion;
            _dimX = dto.dimX; _dimZ = dto.dimZ; _cellSize = dto.cellSize;
            _timeStep = dto.timeStep; _origin = dto.origin;
            _routes.Clear();
            for (var i = 0; i < dto.routes.Length; i++)
            {
                var e = dto.routes[i];
                if (e == null || e.cellsPerLayer == null || e.cellsPerLayer.Length == 0) continue;
                _routes.Add(new RouteEntry
                {
                    cellsPerLayer = (int[])e.cellsPerLayer.Clone(),
                    arrivalBucket = e.arrivalBucket,
                    arrivalSeconds = e.arrivalSeconds,
                    startCell = e.startCell,
                    goalCell = e.goalCell,
                });
            }
            if (_routes.Count == 0) { fail = "所有路线条目均为空，未载入任何路线"; return false; }
            return true;
        }

        /// <summary>网格快照是否与这张表一致（注入端用；不一致说明数据集与表不配套，必须拒绝）。</summary>
        public bool GridMatches(VisibilityTable table)
        {
            if (table == null) return false;
            // 逐字段比对，浮点用极小容差防序列化尾数差误判
            const float eps = 1e-4f;
            return _dimX == table.DimX && _dimZ == table.DimZ
                   && Mathf.Abs(_cellSize - table.CellSize) < eps
                   && Mathf.Abs(_timeStep - table.TimeStep) < eps
                   && Vector3.Distance(_origin, table.Origin) < eps;
        }

        /// <summary>把某条路线折叠成稀疏"移动路点"（相邻层同格合并），供展示 / 巡逻路点派生。</summary>
        public bool MovementWaypoints(int index, out int[] cells, out int[] arrivalLayers)
        {
            cells = null; arrivalLayers = null;
            if (index < 0 || index >= _routes.Count) return false;
            var tl = _routes[index].cellsPerLayer;
            var cs = new List<int>(); var ls = new List<int>();
            cs.Add(tl[0]); ls.Add(0);
            for (var b = 1; b < tl.Length; b++)
                if (tl[b] != tl[b - 1]) { cs.Add(tl[b]); ls.Add(b); }
            cells = cs.ToArray(); arrivalLayers = ls.ToArray();
            return true;
        }

        // ── JSON 视图（字段名是对外契约的一部分，改名即破坏旧数据集，见测试钉住）──────

        [Serializable]
        sealed class RouteDto
        {
            public int[] cellsPerLayer;
            public int arrivalBucket;
            public float arrivalSeconds;
            public int startCell;
            public int goalCell;
        }

        [Serializable]
        sealed class FileSnapshot
        {
            public int schemaVersion;
            public int dimX;
            public int dimZ;
            public float cellSize;
            public float timeStep;
            public Vector3 origin;
            public RouteDto[] routes;
        }

        FileSnapshot Snapshot()
        {
            var dto = new FileSnapshot
            {
                schemaVersion = _schemaVersion,
                dimX = _dimX, dimZ = _dimZ, cellSize = _cellSize, timeStep = _timeStep, origin = _origin,
                routes = new RouteDto[_routes.Count],
            };
            for (var i = 0; i < _routes.Count; i++)
            {
                var e = _routes[i];
                dto.routes[i] = new RouteDto
                {
                    cellsPerLayer = (int[])e.cellsPerLayer.Clone(),
                    arrivalBucket = e.arrivalBucket, arrivalSeconds = e.arrivalSeconds,
                    startCell = e.startCell, goalCell = e.goalCell,
                };
            }
            return dto;
        }
    }
}
