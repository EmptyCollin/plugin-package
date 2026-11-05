using System.Collections.Generic;
using UnityEngine;

namespace Collin.StealthPlanner
{
    /// <summary>
    /// 烘焙所需的<b>外部世界能力</b>（依赖倒置：Runtime 不引用玩法包类型，只认本接口；
    /// Editor 侧的适配器把它接到 core 契约 <c>IVisionWorld</c> / <c>IPatrolWorld</c>）。
    /// <para>拆接口的理由：可见性判定<b>必须</b>由玩法包提供（DESIGN §4 红线，B 不得自建第二套判定），
    /// 但烘焙算法本身要能<b>在 EditMode 用假世界断言</b>。两者只在适配器处耦合。</para>
    /// </summary>
    public interface IVisibilitySource
    {
        /// <summary>
        /// 该单位能否按<b>时刻 t</b> 解析出位姿。
        /// <para><b>必须先问过它</b>：契约的 <c>CanSeeAt</c> 在单位没有巡逻组件时
        /// <b>会静默降级成用「当前位姿」判定</b>（实测 <c>StealthWorld.TryObservationAt</c> 返 false 即降级）。
        /// 降级结果烘进表 = 拿某一瞬间的摆位冒充整条周期，数据错但全程不报错。
        /// 适配器实现应走 <c>IPatrolWorld.PoseAt(actorId, t).valid</c>——那是契约层唯一公开的有效位姿信号。</para>
        /// </summary>
        bool CanResolvePoseAt(int actorId);

        /// <summary>该单位在时刻 t 能否看见世界点（距离 + 视角锥 + 视线全开）。</summary>
        bool CanSeeAt(int actorId, float t, Vector3 worldPoint);

        /// <summary>参与烘焙的哨兵（id + 显示名 + 自身周期）。</summary>
        IReadOnlyList<GuardInfo> Guards { get; }
    }

    /// <summary>参与烘焙的单个哨兵快照（来自 <c>IPatrolWorld.Actors</c>）。</summary>
    public struct GuardInfo
    {
        public int actorId;
        public string displayName;
        public float cycleSeconds;
    }

    /// <summary>
    /// <b>烘焙驱动</b>（Runtime）：把「哪些格在哪些桶被谁看见」填进 <see cref="VisibilityTable"/>。
    /// <para>算法放 Runtime 而非 Editor 的两条硬理由：
    /// ①引擎运行态要能复用<b>同一套填桶口径</b>（AGENTS §0.1.3 双侧友好）；
    /// ②桶数取整、越界钳制、降级排除这几处最容易<b>静默</b>出错，必须能在 EditMode 用假世界钉住。</para>
    /// <para><b>分帧按「查询预算」切，不按毫秒</b>：射线判定只能主线程（D-01），一次跑完会卡死编辑器、
    /// 连带切断所有 AI 的实时通道（D-11）。按查询数切分是<b>确定性</b>的——同样的输入永远得到同样的
    /// 分片，因而可断言；用 <c>DateTime</c> 卡时间片则每次分点不同、热循环里还多一次取值开销。
    /// Editor 泵自己决定一帧喂多少预算（例如按上次耗时折算）。</para>
    /// </summary>
    public sealed class VisibilityBakeRunner
    {
        readonly VisibilityTable _table;
        readonly IVisibilitySource _source;
        readonly IReadOnlyList<int> _cells;
        readonly float _timeStep;

        int _guardCursor;
        int _sliceIndex = -1;
        int _bucketCursor;
        int _cellCursor;
        float _bucketWidth = 1f;
        bool _finalized;

        /// <summary>因「无法按时刻解析位姿」被整片跳过的哨兵名（<b>不静默降级</b>，须上报给策划）。</summary>
        public readonly List<string> SkippedGuards = new List<string>();

        /// <summary>
        /// 整条周期<b>一个格都没看见</b>的哨兵名（可疑，不等于真的盲）。
        /// <para>为什么必须单独上报：实测玩法包在<b>视野体按 actorId 解析不到</b>时
        /// （如重开场景后注册表未刷新）<c>CanSeeAt</c> 会<b>静默返回 false</b>——
        /// 于是整张表被烘成「全场永久安全」，求解器得出「随便走都行」的荒谬结论，
        /// 而烘焙过程<b>全程不报错</b>。正常关卡里「全周期零可见」几乎不可能出现，
        /// 命中即应视为上游数据缺失，而不是可信读数。</para>
        /// </summary>
        public readonly List<string> ZeroVisibilityGuards = new List<string>();

        /// <summary>已发出的可见性查询次数。</summary>
        public long Queries { get; private set; }

        public VisibilityTable Table => _table;

        /// <summary>是否已跑完全部哨兵（完成时已重建派生层）。</summary>
        public bool Done => _finalized;

        public VisibilityBakeRunner(VisibilityTable table, IVisibilitySource source,
                                    IReadOnlyList<int> cells, float timeStep)
        {
            _table = table;
            _source = source;
            _cells = cells;
            _timeStep = Mathf.Max(0.01f, timeStep);
        }

        /// <summary>某周期占多少时间桶 = ⌈c_i / Δt⌉（至少 1）。</summary>
        public static int BucketsFor(float cycleSeconds, float timeStep)
        {
            if (cycleSeconds <= 0f) return 1;
            return Mathf.Max(1, Mathf.CeilToInt(cycleSeconds / Mathf.Max(0.01f, timeStep)));
        }

        /// <summary>预计总查询数（只算可解析位姿的哨兵）。</summary>
        public long TotalQueries
        {
            get
            {
                var gs = _source.Guards;
                if (gs == null || _cells == null) return 0;
                long total = 0;
                for (var i = 0; i < gs.Count; i++)
                {
                    if (!_source.CanResolvePoseAt(gs[i].actorId)) continue;
                    total += (long)BucketsFor(gs[i].cycleSeconds, _timeStep) * _cells.Count;
                }
                return total;
            }
        }

        /// <summary>进度 [0,1]。总数未知（全被跳过）时按已完成计。</summary>
        public float Progress
        {
            get
            {
                var total = TotalQueries;
                return total <= 0f ? 1f : Mathf.Clamp01(Queries / (float)total);
            }
        }

        /// <summary>
        /// 推进烘焙，最多发出 <paramref name="maxQueries"/> 次可见性查询后让出主线程。
        /// 返回 <see cref="Done"/>。<paramref name="maxQueries"/> ≤0 视为 1（保证单步必有进展）。
        /// </summary>
        public bool Step(long maxQueries)
        {
            if (_finalized) return true;
            if (maxQueries <= 0) maxQueries = 1;

            var guards = _source.Guards;
            long spent = 0;

            while (spent < maxQueries)
            {
                if (guards == null || _guardCursor >= guards.Count) break;
                var g = guards[_guardCursor];

                // 先问「能不能按 t 解析位姿」，不能就整片跳过并登记——
                // 直接问 CanSeeAt 会拿到降级结果（当前摆位冒充全周期），那是最坏的静默错误。
                if (!_source.CanResolvePoseAt(g.actorId))
                {
                    SkippedGuards.Add(string.IsNullOrEmpty(g.displayName)
                        ? "actorId=" + g.actorId.ToString() : g.displayName);
                    _guardCursor++;
                    _sliceIndex = -1;
                    _bucketCursor = 0;
                    _cellCursor = 0;
                    continue;
                }

                // 本片首桶：建片（桶数与桶宽在此一次性定下，填桶与查询共用同口径）
                if (_sliceIndex < 0)
                {
                    var buckets = BucketsFor(g.cycleSeconds, _timeStep);
                    _sliceIndex = _table.AddSlice(g.actorId, g.displayName, g.cycleSeconds, buckets);
                    var created = _table.Slices[_sliceIndex];
                    _bucketWidth = created.cycleSeconds / created.buckets;
                    _bucketCursor = 0;
                    _cellCursor = 0;
                }

                var slice = _table.Slices[_sliceIndex];
                var t = _bucketCursor * _bucketWidth;       // 桶起点：与 BucketOf 的分母同源

                for (; _cellCursor < _cells.Count && spent < maxQueries; _cellCursor++)
                {
                    var cell = _cells[_cellCursor];
                    if (_source.CanSeeAt(g.actorId, t, _table.CellCenterWorld(cell)))
                    {
                        _table.SetSliceVisible(_sliceIndex, _bucketCursor, cell, true);
                    }
                    Queries++;
                    spent++;
                }

                if (_cellCursor >= _cells.Count)
                {
                    _cellCursor = 0;
                    _bucketCursor++;
                    if (_bucketCursor >= slice.buckets)
                    {
                        _guardCursor++;
                        _bucketCursor = 0;
                        _sliceIndex = -1;
                    }
                }
            }

            if (_guardCursor >= (guards == null ? 0 : guards.Count))
            {
                // 先留足迹再派生永久安全层：墙体/掩体内部的格从未被射线打过，
                // 若不记「哪些格真被探测过」，下游 BuildDangerMask 会把它们当安全 ⇒ 玩家穿墙。
                _table.MarkEvaluated(_cells);
                _table.SetUnknownGuardNames(SkippedGuards);
                _table.RebuildPermanentSafeLayer();
                ScanZeroVisibility();
                _finalized = true;
            }
            return _finalized;
        }

        /// <summary>
        /// 收尾扫描：把「整条周期一个格都没看见」的分片列入 <see cref="ZeroVisibilityGuards"/>。
        /// <para>放在收尾统一扫、不在填位循环里累加标志：填位跨步被打断时，逐片标志可能漏判。
        /// 成本仅一遍位表（线性于体积），相对射线开销可忽略。
        /// 判据用位表 <c>AnyBit</c>（不解析语义、不猜桶数格数的乘积布局），
        /// 空片=null/长度 0 也算零可见。</para>
        /// </summary>
        void ScanZeroVisibility()
        {
            var slices = _table.Slices;
            for (var s = 0; s < slices.Count; s++)
            {
                if (AnyBit(slices[s].bits)) continue;
                var name = slices[s].displayName;
                ZeroVisibilityGuards.Add(string.IsNullOrEmpty(name)
                    ? "actorId=" + slices[s].actorId.ToString() : name);
            }
        }

        static bool AnyBit(byte[] arr)
        {
            if (arr == null) return false;
            for (var i = 0; i < arr.Length; i++) if (arr[i] != 0) return true;
            return false;
        }
    }
}
