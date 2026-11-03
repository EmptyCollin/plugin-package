using System;

namespace Collin.Stealth
{
    /// <summary>
    /// L4 察觉修正掩码（DESIGN §3.3 位序固定，禁止重排——辅助台与预留功能都按位序对接）。
    /// 与跨包查询掩码 <see cref="Collin.Core.Stealth.StealthQueryLayers"/> 是两回事：
    /// 后者选「判到哪一层」，本掩码标「哪些修正当前生效」，且只在包内使用。
    /// </summary>
    [Flags]
    public enum StealthMask
    {
        /// <summary>无修正。</summary>
        None = 0,

        /// <summary>几何遮挡位：可见性本身由查询层级判定，此位供外部标注用途。</summary>
        LineOfSight = 1,

        /// <summary>蹲伏/掩体：累积速率 × crouchExposure。</summary>
        CoverOrCrouch = 2,

        /// <summary>预留：声音/事件转移注意力（D-12 本版本不启用，留位避免将来重排位序）。</summary>
        DistractionIgnore = 4,

        /// <summary>安全岛内（数据由设计辅助台经 ISafeIslandProvider 提供）：累积速率 = 0。</summary>
        SafeIsland = 8,

        /// <summary>预留位。</summary>
        Reserved16 = 16,

        /// <summary>预留位。</summary>
        Reserved32 = 32,

        /// <summary>预留位。</summary>
        Reserved64 = 64,
    }
}
