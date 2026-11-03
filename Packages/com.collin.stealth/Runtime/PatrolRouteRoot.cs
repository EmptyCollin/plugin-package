using System.Collections.Generic;
using UnityEngine;

namespace Collin.Stealth
{
    /// <summary>
    /// 路线根：把一个 <see cref="PatrolRoute"/>（共享 Model）挂到一个场景 transform 上，
    /// 由该 transform 提供「局部坐标 → 世界坐标」的换算（DESIGN D-09）。
    /// <para>拖动本物体 = 整条巡逻路线及其全部 NPC 轨迹一起搬移；
    /// 资产本身不含世界坐标，可跨场景复用（验收 14/16）。</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PatrolRouteRoot : MonoBehaviour
    {
        [Tooltip("共享的巡逻路线资产（路点为相对本物体的局部坐标）")]
        [SerializeField] PatrolRoute _route;

        readonly List<PatrolRoute.TrajectorySample> _sampleBuffer = new List<PatrolRoute.TrajectorySample>();

        /// <summary>所引用的路线资产（可能为 null）。</summary>
        public PatrolRoute Route => _route;

        /// <summary>设置路线资产引用。</summary>
        public void SetRoute(PatrolRoute route)
        {
            _route = route;
        }

        /// <summary>T 周期（秒）；无资产时为 0。</summary>
        public float TotalCycle => _route != null ? _route.TotalCycle : 0f;

        /// <summary>把局部位姿换算到世界系。</summary>
        public PatrolPoseWorld ToWorld(in PatrolPose pose)
        {
            var result = new PatrolPoseWorld
            {
                position = transform.TransformPoint(pose.localPosition),
                rotation = Quaternion.Euler(0f, transform.eulerAngles.y + pose.localYawDegrees, 0f),
                legIndex = pose.legIndex,
                legProgress01 = pose.legProgress01,
                isDwelling = pose.isDwelling,
                cycleTime = pose.cycleTime
            };
            return result;
        }

        /// <summary>把世界坐标换算为路线局部坐标（Editor 拖手柄写回时用）。</summary>
        public Vector3 WorldToLocal(Vector3 worldPosition)
        {
            return transform.InverseTransformPoint(worldPosition);
        }

        /// <summary>把路线局部坐标换算为世界坐标。</summary>
        public Vector3 LocalToWorld(Vector3 localPosition)
        {
            return transform.TransformPoint(localPosition);
        }

        /// <summary>
        /// 反推：把一组**世界坐标**采样烘焙回本路线的局部坐标（所见即所得的「从当前位置反推路点」）。
        /// </summary>
        /// <param name="worldPoints">按顺序给出世界坐标（首个点成为 0 号路点）。</param>
        /// <returns>写入的路点数量；资产为 null 时返回 -1。</returns>
        public int BakeWaypointsFromWorld(IReadOnlyList<Vector3> worldPoints)
        {
            if (_route == null || worldPoints == null) return -1;
            for (var i = 0; i < worldPoints.Count; i++)
            {
                _route.AddWaypoint(WorldToLocal(worldPoints[i]));
            }
            return worldPoints.Count;
        }

        /// <summary>
        /// 取整条轨迹的世界采样（供 Gizmos 与 B 包分层绘制复用同一求值口径）。
        /// 返回的列表是本组件内部缓冲，**下次调用前有效**。
        /// </summary>
        public IReadOnlyList<PatrolRoute.TrajectorySample> BuildWorldTrajectory(float spacingMeters)
        {
            if (_route == null) return _sampleBuffer;
            _route.BuildTrajectorySamples(spacingMeters, _sampleBuffer);
            for (var i = 0; i < _sampleBuffer.Count; i++)
            {
                var s = _sampleBuffer[i];
                s.localPosition = LocalToWorld(s.localPosition);
                _sampleBuffer[i] = s;
            }
            return _sampleBuffer;
        }
    }

    /// <summary>位姿的世界系结果。</summary>
    public struct PatrolPoseWorld
    {
        public Vector3 position;
        public Quaternion rotation;
        public int legIndex;
        public float legProgress01;
        public bool isDwelling;
        public float cycleTime;
    }
}
