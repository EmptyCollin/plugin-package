namespace Collin.Stealth
{
    /// <summary>
    /// 察觉档位（包内）。值序与 core 的
    /// <see cref="Collin.Core.Stealth.StealthAlertStage"/> 严格一致（用例守卫），
    /// 适配层直接数值转换；Searching 档在 M3 守卫状态机接入前只由外部写入。
    /// </summary>
    public enum StealthDetectionStage
    {
        /// <summary>未察觉。</summary>
        Patrol = 0,

        /// <summary>越过疑惑阈值。</summary>
        Suspicious = 1,

        /// <summary>越过警觉阈值。</summary>
        Alerted = 2,

        /// <summary>搜索中（M3 状态机）。</summary>
        Searching = 3,
    }
}
