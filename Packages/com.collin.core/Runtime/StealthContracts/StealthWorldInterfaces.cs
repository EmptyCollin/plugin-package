using System.Collections.Generic;
using UnityEngine;

namespace Collin.Core.Stealth
{
    /// <summary>
    /// 巡逻世界的跨包只读 + 写入面（提供方 = com.collin.stealth，经 Services 注册；
    /// 消费方 = 设计辅助台等，只经接口访问，AGENTS.md §5）。
    /// <para>时间语义：所有 <c>t</c> 均为「时间索引」，单位秒，含义由提供方的
    /// <c>StealthClockDto.mode</c> 决定；<c>t</c> 会被自动取模到 [0, T)。
    /// 位姿求值必须是纯函数（同 (路线, t) 恒得同结果），这是消费方在**编辑态**
    /// （非 Play、不依赖帧循环）任意 scrub 的前提。</para>
    /// </summary>
    public interface IPatrolWorld
    {
        /// <summary>场景中全部巡逻单位。</summary>
        IReadOnlyList<StealthActorInfo> Actors { get; }

        /// <summary>全部单位里最大的 T 周期（秒）；无单位时为 0。
        /// 消费方用它做全局回溯滑动条的量程上界。</summary>
        float MaxCycleTime { get; }

        /// <summary>当前时间索引状态（模式 + t）。消费方绘制时应以此为准，而不是自估。</summary>
        StealthClockDto ClockState { get; }

        /// <summary>取某单位在时刻 t 的局部位姿。actorId 不存在时返回 default 且 <see cref="StealthPoseDto.HasLeg"/> 为 false。</summary>
        StealthPoseDto PoseAt(int actorId, float t);

        /// <summary>取某单位在时刻 t 的世界位姿（提供方用路线根换算好）。
        /// <paramref name="ok"/> 为 false 时两个输出不可信。</summary>
        bool TryPoseAtWorld(int actorId, float t, out Vector3 worldPosition, out float worldYawDegrees);

        /// <summary>把某单位的整条周期按近似等弧长采样成轨迹（局部坐标）。
        /// 消费方绘制轨迹线用这一条，保证与 NPC 实际走位**同一求值路径**，不许自己另算折线。</summary>
        void BuildTrajectory(int actorId, float approxSpacingMeters, List<StealthTrajectorySampleDto> output);

        /// <summary>读某单位路线的路点序列（局部坐标）。无路线时返回 false。</summary>
        bool TryGetWaypoints(int actorId, out IReadOnlyList<StealthWaypointDto> waypoints, out StealthLoopMode loopMode, out float defaultSpeed);

        /// <summary>写某单位路线的单个路点（局部坐标 + 停留 + 段速），提供方须使 T 周期与派生数据失效重建。
        /// 这是「玩家线路反推 NPC 巡逻路点」（特性 3）的落点：反推结果通过本方法写回。</summary>
        bool TrySetWaypoint(int actorId, int index, StealthWaypointDto waypoint);

        /// <summary>写循环方式与默认速度（反推时可能需要把 Loop 改 PingPong 以匹配玩家动线方向）。</summary>
        bool TrySetRouteTuning(int actorId, StealthLoopMode loopMode, float defaultSpeed);
    }

    /// <summary>
    /// 视野世界：给消费方做「分层查看器」（NPC 视野可达区、遮蔽物、玩家视角）的数据源。
    /// 判定结果与提供方内部四层管线**同一实现**，禁止消费方复制判定逻辑造成两套口径。
    /// </summary>
    public interface IVisionWorld
    {
        /// <summary>场景内全部视野体（含被禁用者，由 <see cref="StealthVisionInfo.enabled"/> 标记）。</summary>
        IReadOnlyList<StealthVisionInfo> Volumes { get; }

        /// <summary>某单位此刻（其当前位姿）能否看到世界点 p，只启用指定层级。</summary>
        bool CanSee(int actorId, Vector3 worldPoint, StealthQueryLayers layers);

        /// <summary>某单位在时刻 t 的位姿下能否看到世界点 p（纯函数口径，编辑态 scrub 可用）。</summary>
        bool CanSeeAt(int actorId, float t, Vector3 worldPoint, StealthQueryLayers layers);

        /// <summary>
        /// 点 p 在时刻 t 被多少个视野以哪些层级看到。用于「视野可达区域」热区统计。
        /// 输出为层级掩码按人计数，消费方自行聚合渲染。
        /// </summary>
        int CountSeers(int actorIdFilter, Vector3 worldPoint, float t, StealthQueryLayers layers);
    }

    /// <summary>
    /// 察觉世界：累积察觉度与档位的只读面 + 掩码设置。
    /// 消费方（设计辅助台）用它把「哪里容易被发现」量化显示，不直接写 NPC 内部状态。
    /// </summary>
    public interface IDetectionWorld
    {
        /// <summary>某目标当前的累积察觉度 [0,1]。未知目标返回 0。</summary>
        float SuspicionOf(int targetId);

        /// <summary>某目标当前档位。</summary>
        StealthAlertStage StageOf(int targetId);

        /// <summary>把所有目标的察觉状态清零（「复原初始状态」，特性 2 的 t 定义即以此为锚）。</summary>
        void ResetAll();
    }

    /// <summary>
    /// 安全岛提供方接口（特性 1「全局安全岛」）。
    /// <para>方向与前述接口相反：**实现方 = 设计辅助台包（数据生产者）**，
    /// <b>消费方 = 玩法包</b>（判定 L4 遮蔽修正时查询「玩家此刻是否在安全岛内」）。
    /// 这正是方案 P 的价值：feature 包之间不引用，靠 core 的接口反向注入。
    /// 玩法包在未注册该服务时必须按「没有安全岛」处理（TryGet 失败即降级）。</para>
    /// </summary>
    public interface ISafeIslandProvider
    {
        /// <summary>安全岛数量（0 表示当前场景无安全岛数据）。</summary>
        int Count { get; }

        /// <summary>第 i 个安全岛的包围盒（世界坐标，轴对齐）。i 越界返回 false。</summary>
        bool TryGetBounds(int index, out Bounds worldBounds, out string displayName);

        /// <summary>世界点 p 是否位于任一安全岛内。玩法包每帧/每次判定调用，实现须廉价。</summary>
        bool Contains(Vector3 worldPoint);
    }

    /// <summary>
    /// 时间回溯驱动器（特性 2「t 以内滑动条实时查看」的驱动面）。
    /// <para>实现方 = 玩法包（它拥有时钟）；消费方 = 设计辅助台的滑动条。
    /// 手动 scrub 时 NPC 位姿、视野扇形、察觉热区必须**同时**响应，所以只有时钟能改 t。</para>
    /// </summary>
    public interface IStealthScrubDriver
    {
        /// <summary>切换到手动模式并把全局时间索引设为 t（秒）。t 会被取模到各路线的 [0,T)。</summary>
        void ScrubTo(float t);

        /// <summary>切回自动模式（恢复每帧推进）。</summary>
        void ResumeAuto();

        /// <summary>当前是否手动模式。</summary>
        bool IsManual { get; }

        /// <summary>请求消费方（辅助台）重绘。提供方在 t 或数据变更后调用；
        /// 编辑态没有帧循环，必须显式通知。</summary>
        event System.Action Changed;
    }
}
