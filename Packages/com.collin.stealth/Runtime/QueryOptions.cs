using System;

namespace Collin.Stealth
{
    /// <summary>
    /// 察觉判定的层级选择（DESIGN §3.3：四层必须可分层取结果，辅助台按层可视化依赖此）。
    /// 值序与 core 的 <see cref="Collin.Core.Stealth.StealthQueryLayers"/> 严格一致
    /// （有专门用例守卫），适配层可直接数值转换；消费方永远只见 core 的那份，本包内部用这份。
    /// </summary>
    [Flags]
    public enum QueryOptions
    {
        /// <summary>不启用任何检查（仅通过距离粗筛后直接返回可见，供工具探测用途）。</summary>
        None = 0,

        /// <summary>仅距离。</summary>
        DistanceOnly = 1,

        /// <summary>距离 + 水平/垂直视角锥。</summary>
        ConeOnly = 2,

        /// <summary>距离 + 视角锥 + 视线（射线遮挡）。</summary>
        LineOfSight = 4,

        /// <summary>全开。</summary>
        Full = DistanceOnly | ConeOnly | LineOfSight,
    }
}
