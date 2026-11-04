using System.Collections.Generic;
using UnityEngine;

namespace Collin.Stealth
{
    /// <summary>
    /// 寻路接缝（DESIGN §3.6，M3 预留、本轮实装）：把哨兵的<b>反应位移</b>（追击/搜索）
    /// 从「直线拉向可疑点」升级为「沿导航路径绕行」，解决 NPC 抓人时穿掩体。
    /// <para>纪律（勿破坏）：接缝<b>只作用于叠加层</b>。巡逻基线永远是
    /// <see cref="PatrolRoute.EvaluatePose"/> 的纯函数结果——导航若参与基线，
    /// 位姿就变成帧历史依赖，辅助台的 T 周期回溯与轨迹预测全部失效（DESIGN D-01 / R-1）。
    /// 掩体穿模只发生在反应阶段（走向最后目击点、追击、搜索环绕），所以只需覆盖叠加层。</para>
    /// <para>主包<b>不引用任何导航类型</b>（D-06）：具体实现放在可选子程序集
    /// <c>com.collin.stealth.Navigation</c>（用内置 <c>com.unity.modules.ai</c> 的 legacy NavMesh，
    /// 不引入付费的 com.unity.ai.navigation）。未注册本服务时行为与之前一致：直线叠加。</para>
    /// </summary>
    public interface IPathBridge
    {
        /// <summary>
        /// 求从 <paramref name="fromWorld"/> 到 <paramref name="desiredWorld"/> 的绕行路径。
        /// <paramref name="corners"/> 由调用方提供、实现方<b>先 Clear 再填</b>，
        /// 且必须包含可用起点（约定：<c>corners[0]</c> 为出发点、末点为目标或路径终点）。
        /// 返回 false 表示无路径（未烘焙 / 目标不在可导航面 / 不可达），调用方应退回直线叠加。
        /// </summary>
        bool TryFindPath(Vector3 fromWorld, Vector3 desiredWorld, List<Vector3> corners);

        /// <summary>
        /// 单点吸附（保留给轻量实现与工具使用）：把期望点贴到可导航面上。
        /// 返回 false 表示不吸附，调用方保持原目标。
        /// </summary>
        bool TrySnap(Vector3 fromWorld, Vector3 desiredWorld, out Vector3 navWorld);
    }

    /// <summary>
    /// 默认实现：永不出路径、永不吸附 ⇒ 叠加层退回直线（不注册本服务时的等价行为）。
    /// 存在的意义是让「未接导航」成为显式状态，而不是散落在各处的判空分支。
    /// </summary>
    public sealed class NullPathBridge : IPathBridge
    {
        public bool TryFindPath(Vector3 fromWorld, Vector3 desiredWorld, List<Vector3> corners)
        {
            if (corners != null) corners.Clear();
            return false;
        }

        public bool TrySnap(Vector3 fromWorld, Vector3 desiredWorld, out Vector3 navWorld)
        {
            navWorld = desiredWorld;
            return false;
        }
    }
}
