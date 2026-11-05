using System.Collections.Generic;
using Collin.Core;
using Collin.Core.Stealth;
using UnityEngine;

namespace Collin.StealthPlanner.EditorTooling
{
    /// <summary>
    /// 把 core 契约（<c>IVisionWorld</c> / <c>IPatrolWorld</c>）适配成 Runtime 的
    /// <see cref="IVisibilitySource"/>，并在适配层加一道<b>距离预筛</b>。
    /// <para><b>为什么必须有 <see cref="CanResolvePoseAt"/></b>：实测 <c>StealthWorld.CanSeeAt</c>
    /// 在单位没有 <c>PatrolAgent</c> 时<b>静默降级</b>为「用当前位姿判定」（内部
    /// <c>TryObservationAt</c> 返 false 即回退）。降级值一旦烘进表，等于拿某一瞬间的摆位
    /// 冒充整条周期，且<b>全程不报错</b>。故适配器先用 <c>PoseAt(actorId, 0).valid</c> 探一次
    /// （缺组件是<b>单位级</b>事实、与 t 无关），无效则由烘焙器整片跳过并上报策划。</para>
    /// <para><b>距离预筛</b>（DESIGN §7「首烘射线次数大幅降」的落地处）：每桶每格先做一次纯数学
    /// 「水平距离 &gt; maxDistance ⇒ 该处必不可见」，命中即跳过契约调用（省一条射线）。
    /// 方向是<b>保守正确</b>的：3D 距离 ≥ 水平距离，故水平已超界时不可能真看得见——
    /// <b>只会少问、绝不漏问</b>（漏问会把危险格错标成安全，不可接受）。</para>
    /// <para>本类<b>不做</b>桶号计算：Δt 与分片桶数是烘焙器的私有口径，在此重算会把 Δt 变成
    /// 跨调用的隐式状态（多窗口/改参数时会串台）。位姿求值是弧长查表、成本远低于射线，
    /// 故只按「量化后的 t」缓存一次结果，不做更多花活。</para>
    /// <para>可见性判定<b>一律转调玩法包</b>，本包不自建第二套（DESIGN §4 红线）。</para>
    /// </summary>
    sealed class ContractVisibilitySource : IVisibilitySource
    {
        readonly IPatrolWorld _patrol;
        readonly IVisionWorld _vision;
        readonly List<GuardInfo> _guards = new List<GuardInfo>();

        /// <summary>actorId → 最大视距（取不到记 0，表示「不做预筛」，保守地把查询全交给契约）。</summary>
        readonly Dictionary<int, float> _maxDist = new Dictionary<int, float>();
        /// <summary>(actorId, 量化 t) → 该时刻哨兵世界位。省掉同桶内对每格重复求值。</summary>
        readonly Dictionary<long, Vector3> _poseAt = new Dictionary<long, Vector3>();
        /// <summary>已失效的位姿查询（该单位取不到 t 时刻位姿），避免反复尝试。</summary>
        readonly HashSet<int> _poseFailed = new HashSet<int>();

        /// <summary>被预筛挡掉的查询数（= 省下的射线数，供面板读数）。</summary>
        public long PrefilteredQueries { get; private set; }
        /// <summary>真正打到契约（含射线）的查询数。</summary>
        public long ForwardedQueries { get; private set; }
        /// <summary>取不到视距参数的单位数（这些单位不做预筛，仅提醒）。</summary>
        public int GuardsWithoutDistance { get; private set; }

        public IReadOnlyList<GuardInfo> Guards => _guards;

        /// <summary>解析契约服务。缺任一服务返回 null——调用方须降级提示，不得抛异常。</summary>
        public static ContractVisibilitySource TryCreate()
        {
            if (!Services.TryGet<IPatrolWorld>(out var patrol) || patrol == null) return null;
            if (!Services.TryGet<IVisionWorld>(out var vision) || vision == null) return null;
            return new ContractVisibilitySource(patrol, vision);
        }

        ContractVisibilitySource(IPatrolWorld patrol, IVisionWorld vision)
        {
            _patrol = patrol;
            _vision = vision;

            var volumes = _vision.Volumes;
            if (volumes != null)
            {
                for (var i = 0; i < volumes.Count; i++)
                {
                    var v = volumes[i];
                    if (!_maxDist.ContainsKey(v.actorId)) _maxDist[v.actorId] = v.maxDistance;
                }
            }

            var actors = _patrol.Actors;
            if (actors == null) return;
            for (var i = 0; i < actors.Count; i++)
            {
                var a = actors[i];
                if (!a.hasRoot) continue;                       // 无路线根 ⇒ 无法按 t 求位姿
                _guards.Add(new GuardInfo
                {
                    actorId = a.actorId,
                    displayName = string.IsNullOrEmpty(a.displayName) ? "actor=" + a.actorId : a.displayName,
                    cycleSeconds = a.cycleTime
                });
                if (!_maxDist.ContainsKey(a.actorId) || _maxDist[a.actorId] <= 0f) GuardsWithoutDistance++;
            }
        }

        /// <summary>
        /// 预热：对每个单位各探一次位姿，逼玩法包<b>惰性构建</b>其弧长查找表。
        /// <para>实测动机：重开场景后「第 1 次烘焙」的位图与第 2、3 次不同（逐格有差异），
        /// 而第二次起就稳定。表没建好时 <c>PoseAt</c> 走的是未初始化路径 ⇒ 冷启动首烘不可信。
        /// 用公开查询把表先建起来，再开始正式烘焙。</para>
        /// </summary>
        public void WarmUpPoses()
        {
            for (var i = 0; i < _guards.Count; i++)
            {
                var g = _guards[i];
                _patrol.PoseAt(g.actorId, 0f);
                if (g.cycleSeconds > 0f)
                {
                    _patrol.PoseAt(g.actorId, g.cycleSeconds * 0.5f);
                    _patrol.PoseAt(g.actorId, g.cycleSeconds);
                }
                // 同时把世界位姿走一遍：CanSeeAt 用的是 TryPoseAtWorld，与 PoseAt 是两条路径
                _patrol.TryPoseAtWorld(g.actorId, 0f, out _, out _);
                if (g.cycleSeconds > 0f) _patrol.TryPoseAtWorld(g.actorId, g.cycleSeconds * 0.5f, out _, out _);
            }
        }

        public bool CanResolvePoseAt(int actorId)
        {
            // PoseAt 的 valid 是契约层唯一公开的「位姿可解析」信号。
            var pose = _patrol.PoseAt(actorId, 0f);
            if (pose.valid) return true;

            _poseFailed.Add(actorId);
            return false;
        }

        public bool CanSeeAt(int actorId, float t, Vector3 worldPoint)
        {
            if (TryPose(actorId, t, out var guardPos) &&
                _maxDist.TryGetValue(actorId, out var maxD) && maxD > 0f)
            {
                var dx = worldPoint.x - guardPos.x;
                var dz = worldPoint.z - guardPos.z;
                if (dx * dx + dz * dz > maxD * maxD)
                {
                    PrefilteredQueries++;                                     // 省掉一条射线
                    return false;
                }
            }

            ForwardedQueries++;
            return _vision.CanSeeAt(actorId, t, worldPoint, StealthQueryLayers.Full);
        }

        bool TryPose(int actorId, float t, out Vector3 pos)
        {
            pos = Vector3.zero;
            if (_poseFailed.Contains(actorId)) return false;

            // 键：actorId 高位 + 量化到微秒的 t。烘焙按「桶起点」逐桶推进，同一桶内所有格共享
            // 同一个 t，故一次求值可服务整格遍历；量化避免浮点抖动导致缓存永不命中。
            var qt = (long)Mathf.RoundToInt(t * 1000000f);
            var key = ((long)actorId << 32) ^ qt;
            if (_poseAt.TryGetValue(key, out pos)) return true;

            if (!_patrol.TryPoseAtWorld(actorId, t, out pos, out _))
            {
                _poseFailed.Add(actorId);
                return false;
            }
            _poseAt[key] = pos;
            return true;
        }

        /// <summary>重烘前清缓存（位姿与参数都可能已变；actorId 域重载后会变，不可跨会话保留）。</summary>
        public void InvalidateCaches()
        {
            _poseAt.Clear();
            _poseFailed.Clear();
        }
    }
}
