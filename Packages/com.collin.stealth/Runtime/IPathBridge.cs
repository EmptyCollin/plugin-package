using UnityEngine;

namespace Collin.Stealth
{
    /// <summary>
    /// 寻路接缝（DESIGN §3.6，M3 交付项）：把叠加层的移动目标吸附到导航可支撑的表面点。
    /// <para>纪律：本包<b>不引用任何导航包</b>（不装 com.unity.ai.navigation，见 D-06/R-2）。
    /// 工程若接入导航方案，由可选子程序集 <c>com.collin.stealth.Navigation</c> 实现本接口
    /// 并注册进 <c>Services</c>；未注册时叠加移动走直线（默认 <see cref="NullPathBridge"/> 语义）。
    /// 接缝只作用于<b>事件叠加</b>（走向可疑点/追击/搜索），绝不作用于巡逻基线——
    /// 基线位姿必须保持纯函数，否则辅助台的回溯预览失去参照。</para>
    /// </summary>
    public interface IPathBridge
    {
        /// <summary>
        /// 尝试把期望世界点 <paramref name="desiredWorld"/> 吸附为可到达点。
        /// 返回 false 表示不做吸附（调用方保持原目标）。
        /// </summary>
        bool TrySnap(Vector3 fromWorld, Vector3 desiredWorld, out Vector3 navWorld);
    }

    /// <summary>
    /// 默认实现：永不吸附（纯直线叠加）。存在意义是让「未接导航」成为显式的、
    /// 可注册的行为，而不是散落在各处的判空分支。
    /// </summary>
    public sealed class NullPathBridge : IPathBridge
    {
        public bool TrySnap(Vector3 fromWorld, Vector3 desiredWorld, out Vector3 navWorld)
        {
            navWorld = desiredWorld;
            return false;
        }
    }
}
