namespace Collin.Core.Stealth
{
    /// <summary>
    /// 察觉事件通道（core 提供具体子类：泛型 <see cref="EventChannel{T}"/> 无法被 Unity
    /// 直接 <c>CreateInstance</c>，实测返回 null，故消费方必须使用/声明具体子类）。
    /// <para>用法：玩法包在察觉状态跃迁时 <c>Raise</c>；设计辅助台 <c>Subscribe</c>
    /// 后把事件画到时间轴上（<see cref="StealthDetectedPayload.timeIndex"/> 用于对齐 scrub 位置）。
    /// 通道资产由使用者在项目中创建（CreateAssetMenu），跨包双方引用同一资产即可通信。</para>
    /// </summary>
    [UnityEngine.CreateAssetMenu(menuName = "Collin/Stealth/Detected Channel", fileName = "NewStealthDetectedChannel")]
    public class StealthDetectedChannel : EventChannel<StealthDetectedPayload>
    {
    }

    /// <summary>
    /// 时间索引变更通道。载荷为新的 t（秒）。
    /// <para>用途：编辑态回溯时，若由玩法包侧（Inspector 手改 ManualTime）驱动 t，
    /// 辅助台通过本通道对齐自己的滑动条，避免两个 UI 各持一份时间。</para>
    /// <para>注意与 <see cref="IStealthScrubDriver.Changed"/> 的分工：
    /// 本通道承载「t 的值」，适合需要精确时刻的消费方；<c>Changed</c> 事件只表示「重绘」，
    /// 适合只关心刷新的消费方。二者可由同一变更同时触发，但**不得互相调用**造成环路。</para>
    /// </summary>
    [UnityEngine.CreateAssetMenu(menuName = "Collin/Stealth/Time Index Channel", fileName = "NewStealthTimeChannel")]
    public class StealthTimeIndexChannel : EventChannel<float>
    {
    }

    /// <summary>
    /// 世界数据结构性变更通道（路点增删、单位增减、路线资产替换）。
    /// <para>用途：辅助台缓存了轨迹采样与热区网格，一旦路线拓扑变了必须整体作废重算。
    /// 这类变更频率低，用无载荷广播即可，不必逐条上报。</para>
    /// </summary>
    [UnityEngine.CreateAssetMenu(menuName = "Collin/Stealth/World Changed Channel", fileName = "NewStealthWorldChannel")]
    public class StealthWorldChangedChannel : EventChannel<int>
    {
        /// <summary>变更类型。作为 int 载荷传出（<see cref="EventChannel{T}"/> 的具体子类需要非泛型载荷）。</summary>
        public enum ChangeKind
        {
            /// <summary>路点增删或坐标改变。</summary>
            Waypoints = 0,

            /// <summary>巡逻单位增删。</summary>
            Actors = 1,

            /// <summary>视野体增删或参数改变。</summary>
            Vision = 2,

            /// <summary>安全岛数据改变（由辅助台发出，玩法包可忽略）。</summary>
            SafeIsland = 3,
        }

        /// <summary>类型安全的广播入口，避免调用方手写 (int) 强转写错位。</summary>
        public void Raise(ChangeKind kind) => Raise((int)kind);
    }
}
