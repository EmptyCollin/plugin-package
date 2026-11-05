using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Collin.StealthPlanner.EditorTooling
{
    /// <summary>
    /// 一次烘焙的<b>会话</b>：持有可见性源、网格与驱动，可跨帧续跑，也可一次跑完。
    /// <para>拆成会话（而不是「一个静态 Run 函数」）的理由：分帧推进必须<b>复用同一份</b>
    /// footprint 与驱动状态。若每帧都重跑一遍 <see cref="VisibilityBakeService.EnumerateWalkableCells"/>，
    /// 等于把「烘焙一次」变成「每帧重枚举一次」，光射线就把预算吃光。</para>
    /// <para>两条使用路径共用本类：①<b>交互窗口</b>逐帧 <see cref="Pump"/>（不卡编辑器）；
    /// ②<b>脚本/离线</b>用 <see cref="RunToEnd"/>（不依赖帧回调——本工程实测失焦时编辑器不推进泵，
    /// 故自动化取证必须有这条路）。footprint 判定全工程只有 <see cref="VisibilityBakeService"/> 一处实现。</para>
    /// </summary>
    internal sealed class VisibilityBakeSession
    {
        public VisibilityTable Table { get; }
        public IReadOnlyList<string> Skipped => _runner.SkippedGuards;
        public long Queries => _runner.Queries;
        public long TotalQueries => _runner.TotalQueries;
        public float Progress => _runner.Progress;
        public bool Done => _runner.Done;
        public int RectCells { get; }
        public int WalkableCells { get; }
        public int GuardCount { get; }
        public int GuardsWithoutDistance { get; }
        public long Prefiltered => _source.PrefilteredQueries;
        public long Forwarded => _source.ForwardedQueries;
        public double FootprintSeconds { get; }

        readonly ContractVisibilitySource _source;
        readonly VisibilityBakeRunner _runner;

        /// <summary>建会话：解析契约服务 + 枚举 footprint。<paramref name="fail"/> 非空表示失败原因。</summary>
        public static VisibilityBakeSession TryBegin(Vector3 areaMin, Vector3 areaMax,
                                                     PlannerBakeSettings settings,
                                                     float groundProbeDown, float standClearance,
                                                     out string fail)
        {
            fail = null;
            if (settings == null) { fail = "缺烘焙设置资产"; return null; }

            var source = ContractVisibilitySource.TryCreate();
            if (source == null)
            {
                fail = "未接入玩法包（IPatrolWorld / IVisionWorld 未注册）";
                return null;
            }
            source.InvalidateCaches();

            // 必须同步物理：刚 OpenScene / 刚移动过掩体时，碰撞体位置还没进 PhysX。
            // 实测不补这一步，「紧跟 OpenScene 的第一次烘焙」会与后续烘焙逐格不一致
            // （读到未沉淀的观察原点），表资产每次重存都产生虚假 diff、确定性也不成立。
            Physics.SyncTransforms();

            // 必须预热位姿求值：玩法包的弧长查找表是<b>惰性构建</b>的。域重载后第一次
            // PoseAt/TryPoseAtWorld 可能落在「表还没建」的分支上，于是<b>冷启动首烘</b>给出
            // 与后续不同的位姿——实测表现为「重开场景后第 1 次烘焙的位图 ≠ 第 2、3 次」，
            // 且全程不报错。先让适配器把每个单位探一遍（它内部走 PoseAt），逼表建起来。
            source.WarmUpPoses();
            Physics.SyncTransforms();     // 预热可能触发写回，再同步一遍收尾

            var t0 = EditorApplication.timeSinceStartup;
            var cells = VisibilityBakeService.EnumerateWalkableCells(
                areaMin, areaMax, settings.CellSize, groundProbeDown, standClearance,
                out var dimX, out var dimZ, out var origin);
            var secs = EditorApplication.timeSinceStartup - t0;

            if (cells.Count == 0)
            {
                fail = "footprint 为空：范围内无可站立地面（检查范围与检测层）";
                return null;
            }

            var table = new VisibilityTable();
            table.ConfigureGrid(origin, settings.CellSize, dimX, dimZ, settings.TimeStep);
            return new VisibilityBakeSession(table, source, cells, settings.TimeStep,
                dimX * dimZ, cells.Count, secs);
        }

        VisibilityBakeSession(VisibilityTable table, ContractVisibilitySource source,
                              List<int> cells, float timeStep, int rectCells, int walkableCells,
                              double footprintSeconds)
        {
            Table = table;
            _source = source;
            _runner = new VisibilityBakeRunner(table, source, cells, timeStep);
            RectCells = rectCells;
            WalkableCells = walkableCells;
            GuardCount = source.Guards.Count;
            GuardsWithoutDistance = source.GuardsWithoutDistance;
            FootprintSeconds = footprintSeconds;
        }

        /// <summary>推进最多 <paramref name="maxQueries"/> 次查询（窗口每帧调一次）。返回是否完成。</summary>
        public bool Pump(long maxQueries) => _runner.Step(maxQueries);

        /// <summary>一次跑到结束（占住主线程，仅供脚本/离线）。返回是否完成。</summary>
        public bool RunToEnd(Action<VisibilityBakeSession> onProgress = null, long chunk = 20000)
        {
            var guard = 0;
            while (!_runner.Done)
            {
                _runner.Step(chunk);
                if (++guard > 100000)
                {
                    Debug.LogError("[StealthPlanner] 烘焙推进未收敛，强制停止（queries=" + _runner.Queries + "）");
                    return false;
                }
                if ((guard & 15) == 0) onProgress?.Invoke(this);
            }
            return true;
        }
    }

    /// <summary>
    /// 烘焙的<b>无窗口</b>入口（脚本 / 命令行 / M8 并行实跑用）与 <b>footprint 枚举唯一实现</b>。
    /// <para>存在理由：窗口的分帧泵挂在 <c>EditorApplication.update</c> 上，而本工程实测
    /// <b>编辑器窗口失焦时不推进泵</b>（<c>set_auto_tick</c> 只保活 UI）。缺这条路就只能人工点窗口，
    /// M1 的验收读数也拿不到。</para>
    /// </summary>
    internal static class VisibilityBakeService
    {
        /// <summary>一次跑完（会占主线程）。失败时 <paramref name="fail"/> 给原因。</summary>
        internal static VisibilityBakeSession RunOnce(Vector3 areaMin, Vector3 areaMax,
                                                      PlannerBakeSettings settings,
                                                      float groundProbeDown,
                                                      float standClearance,
                                                      Action<VisibilityBakeSession> onProgress,
                                                      out string fail)
        {
            var s = VisibilityBakeSession.TryBegin(areaMin, areaMax, settings,
                groundProbeDown, standClearance, out fail);
            if (s == null) return null;
            if (!s.RunToEnd(onProgress)) { fail = "推进未收敛"; return null; }
            return s;
        }

        /// <summary>无出参版本（脚本/取证用，默认探测参数）。失败原因看返回 null + 本类日志。</summary>
        internal static VisibilityBakeSession RunOnce(Vector3 areaMin, Vector3 areaMax,
                                                      PlannerBakeSettings settings,
                                                      float groundProbeDown = 3f,
                                                      float standClearance = 1.5f)
        {
            return RunOnce(areaMin, areaMax, settings, groundProbeDown, standClearance, null, out _);
        }

        /// <summary>
        /// 枚举矩形内可活动格（<b>全工程唯一实现</b>）：格心<b>向下</b>有朝上地面，
        /// 且从该地面<b>向上 standClearance</b> 不被实体占据。
        /// <para>两条射线缺一不可：只查地面会把<b>掩体内部</b>当可走格（求解器会规划穿箱路线）；
        /// 只查站高会把悬空平台算进来。</para>
        /// </summary>
        internal static List<int> EnumerateWalkableCells(Vector3 areaMin, Vector3 areaMax, float cell,
            float probeDown, float standClearance, out int dimX, out int dimZ, out Vector3 origin)
        {
            var minX = Mathf.Min(areaMin.x, areaMax.x);
            var minZ = Mathf.Min(areaMin.z, areaMax.z);
            cell = Mathf.Max(0.01f, cell);
            // Ceil 而非 Floor：整除时 Floor 会少一列一行，范围边缘的格被无声丢掉
            dimX = Mathf.Max(1, Mathf.CeilToInt(Mathf.Abs(areaMax.x - areaMin.x) / cell));
            dimZ = Mathf.Max(1, Mathf.CeilToInt(Mathf.Abs(areaMax.z - areaMin.z) / cell));
            origin = new Vector3(minX, Mathf.Min(areaMin.y, areaMax.y), minZ);

            const int mask = ~0;                                  // 全层，与视野遮挡同口径
            var list = new List<int>(dimX * dimZ);
            var ground = new RaycastHit();

            for (var z = 0; z < dimZ; z++)
            {
                for (var x = 0; x < dimX; x++)
                {
                    var cx = minX + (x + 0.5f) * cell;
                    var cz = minZ + (z + 0.5f) * cell;

                    var probeTop = origin.y + probeDown * 0.5f;
                    if (!Physics.Raycast(new Vector3(cx, probeTop, cz), Vector3.down, out ground,
                            probeDown, mask, QueryTriggerInteraction.Ignore))
                    {
                        continue;
                    }
                    if (ground.normal.y < 0.7f) continue;          // 侧壁/天花板不算地面

                    var feet = ground.point + Vector3.up * 0.1f;
                    var room = Mathf.Max(0.01f, standClearance - 0.1f);
                    if (Physics.Raycast(feet, Vector3.up, room, mask, QueryTriggerInteraction.Ignore))
                    {
                        continue;                                   // 站立体被占（掩体内部）
                    }

                    list.Add(z * dimX + x);
                }
            }
            return list;
        }
    }
}
