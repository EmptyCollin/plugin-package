using UnityEngine;

namespace Collin.Stealth
{
    /// <summary>
    /// 巡逻叠加器：在 M1 的<b>基线位姿</b>之上追加世界偏移与注视点（stealth DESIGN §3.6 / R-1）。
    /// <para>为什么必须有这层：M1 的承诺是「基线位姿 = 时间的纯函数」，B 包的 T 周期回溯、
    /// 轨迹绘制、视野可达区都依赖它。而状态机的反应（走向可疑点、追击、搜索）是
    /// <b>事件驱动的非确定性内容</b>，只能作为可关断的叠加层存在，绝不能回流进基线，
    /// 否则回溯预览显示的就是「上一局玩过的痕迹」，设计工具直接失真。</para>
    /// <para>驱动纪律：叠加器由 <see cref="PatrolAgent"/> 每帧统一 Tick（不自己开 LateUpdate），
    /// 保证「先决策、再合成位姿、最后写 transform」的顺序确定。
    /// 编辑态预览（<c>dt &lt;= 0</c>）时 <see cref="PatrolAgent"/> <b>完全跳过叠加层</b>，
    /// 所以 scrub 看到的始终是干净的巡逻基线。</para>
    /// </summary>
    public interface IPatrolModifier
    {
        /// <summary>本叠加器当前是否要求改写位姿。</summary>
        bool Active { get; }

        /// <summary>
        /// 每帧推进一次。
        /// </summary>
        /// <param name="dt">真实时间步长（&gt;0）。</param>
        /// <param name="baselineWorldPosition">本帧巡逻<b>基线</b>世界位置（不含任何叠加偏移）。
        /// 叠加器必须以它为基准计算偏移，否则会与基线位置形成循环依赖。</param>
        /// <param name="timeIndex">当前时间索引 t（供记录「最后看见的时刻」等）。</param>
        void Tick(float dt, Vector3 baselineWorldPosition, float timeIndex);

        /// <summary>
        /// 取叠加结果：<paramref name="worldOffset"/> 直接加到基线位置；
        /// <paramref name="worldLookAt"/> 非空时覆盖朝向，让 NPC 注视该世界点。
        /// </summary>
        void GetOverlay(out Vector3 worldOffset, out Vector3? worldLookAt);

        /// <summary>复位到「不干预」状态（辅助台复原初始状态时调用）。</summary>
        void ResetOverlay();
    }
}
