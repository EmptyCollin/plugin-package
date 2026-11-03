using UnityEngine;

namespace Collin.Stealth
{
    /// <summary>
    /// 巡逻位姿：<see cref="PatrolRoute.EvaluatePose"/> 的输出，全部为**局部量**（相对路线根）。
    /// 世界量由 <see cref="PatrolRouteRoot.ToWorld"/> 换算，保证同一份求值结果
    /// 在编辑态预览与 Play 态下含义一致。
    /// </summary>
    public struct PatrolPose
    {
        /// <summary>相对路线根的位置。</summary>
        public Vector3 localPosition;

        /// <summary>
        /// 相对路线根的水平朝向（绕 Y 的欧拉角，度）。
        /// 零位移段（垂直升降/重复点）在重建表时继承邻近段朝向，故此处不会是 NaN。
        /// </summary>
        public float localYawDegrees;

        /// <summary>当前所处段索引；-1 表示空路线。</summary>
        public int legIndex;

        /// <summary>当前段内的行程进度 [0,1]（停留阶段为 0）。</summary>
        public float legProgress01;

        /// <summary>是否处于到点停留阶段。</summary>
        public bool isDwelling;

        /// <summary>所属路线的 T 周期（秒）；把位姿结果自解释，便于测试与外部消费。</summary>
        public float cycleTime;

        /// <summary>当前段索引是否有效（空路线时无效）。</summary>
        public bool HasLeg => legIndex >= 0;
    }
}
