using UnityEngine;

namespace Collin.Stealth
{
    /// <summary>
    /// 视线射线的帧预算与错峰（DESIGN §3.3 L3「预算制」）。
    /// <para>必须由外部显式 <see cref="BeginFrame"/> 报帧号，而不是自己读
    /// <c>Time.frameCount</c>：编辑态也有编辑器帧在递增，自读会让「这一帧还能不能发射线」
    /// 变成不可控的隐式状态，测试也无法稳定断言。</para>
    /// </summary>
    public static class StealthPerformance
    {
        /// <summary>每帧允许发出的视线射线数。&lt;=0 表示不限制。</summary>
        public static int raycastBudgetPerFrame = 64;

        /// <summary>全局默认视线检测间隔帧数（<see cref="VisionProfile.LineOfSightCheckInterval"/> 为 1 时采用）。</summary>
        public static int defaultCheckIntervalFrames = 1;

        static int _used;
        static int _frame = -1;

        /// <summary>帧起点：同一 <paramref name="frameIndex"/> 内多次调用只重置一次预算。</summary>
        public static void BeginFrame(int frameIndex)
        {
            if (frameIndex == _frame) return;
            _frame = frameIndex;
            _used = 0;
        }

        /// <summary>申请一条射线配额。预算耗尽时返回 false，调用方应复用上帧结果而不是静默判错。</summary>
        public static bool TryAcquireRaycast()
        {
            if (raycastBudgetPerFrame <= 0) return true;
            if (_used >= raycastBudgetPerFrame) return false;
            _used++;
            return true;
        }

        /// <summary>本帧已用配额（诊断用）。</summary>
        public static int UsedThisFrame => _used;

        /// <summary>
        /// 判断某单位本帧是否该做视线检测。用单位 id 与间隔取模实现<b>错峰</b>：
        /// 若所有单位都按同一相位检查，预算会在同帧被同一批单位吃满，其余单位整帧拿不到结果。
        /// </summary>
        public static bool ShouldCheckThisFrame(int actorId, int intervalFrames)
        {
            if (intervalFrames <= 1) return true;
            var phase = actorId % intervalFrames;
            if (phase < 0) phase += intervalFrames;
            return (_frame % intervalFrames) == phase;
        }

        /// <summary>测试/装配复位。</summary>
        public static void ResetForTests()
        {
            _used = 0;
            _frame = -1;
            raycastBudgetPerFrame = 64;
            defaultCheckIntervalFrames = 1;
        }
    }
}
