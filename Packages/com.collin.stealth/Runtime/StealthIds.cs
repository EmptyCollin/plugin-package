using UnityEngine;

namespace Collin.Stealth
{
    /// <summary>
    /// 跨包单位 id 的唯一算法。
    /// <para>必须只有一处实现：<see cref="StealthWorld"/>（按 <see cref="PatrolAgent"/> 枚举）、
    /// <see cref="DetectionState"/>、以及视野归属反查都用同一个函数，
    /// 否则辅助台拿着 actorId 会问不到对应单位。</para>
    /// <para>Unity 6 起 <c>GetInstanceID()</c> 被编译器当 error 报（CS0619），
    /// 而 <c>EntityId</c> 的隐式整型转换同样废弃，故取 <c>GetEntityId().GetHashCode()</c>。
    /// 该 id 在同一域内稳定，<b>域重载后会变</b>，消费方不得持久化（core 契约注释已声明）。</para>
    /// </summary>
    public static class StealthIds
    {
        /// <summary>
        /// id 掩码：哈希结果<b>必须非负</b>。
        /// 契约用 <c>-1</c> 作「不限定单位」的哨兵，而 <see cref="StealthPerformance.ShouldCheckThisFrame"/>
        /// 也按 id 取模做错峰——两者都预设 id 是非负整数，故这里抹掉符号位。
        /// </summary>
        const int NonNegative = int.MaxValue;

        /// <summary>取物体所挂组件的跨包 id（恒为非负）。</summary>
        public static int Of(GameObject gameObject)
        {
            if (gameObject == null) return 0;
            return gameObject.GetEntityId().GetHashCode() & NonNegative;
        }

        /// <summary>取组件的跨包 id（以其所在物体为准，恒为非负）。</summary>
        public static int Of(Component component)
        {
            if (component == null) return 0;
            return Of(component.gameObject);
        }
    }
}
