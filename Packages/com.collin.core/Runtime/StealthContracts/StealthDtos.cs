using System;
using UnityEngine;

namespace Collin.Core.Stealth
{
    // 潜行跨包契约的纯数据 DTO（AGENTS.md §5 / stealth DESIGN §4.4 方案 P）。
    // 硬约束：这里的类型以及 IPatrolWorld 等接口签名上出现的类型，只允许来自 core、
    // UnityEngine 或 BCL。一旦出现某个 feature 包的具体类型，消费方就必须在 asmdef 层
    // 引用提供方才能编译，方案 P（feature 包互不引用）即失效。

    /// <summary>巡逻位姿。全部为**局部量**：相对某条巡逻路线的根 transform。</summary>
    public struct StealthPoseDto
    {
        /// <summary>本结果是否由有效路线算出。<b>消费方必须先判它</b>——
        /// 结构体 default 的 <c>legIndex</c> 为 0，光看 legIndex 会把「无数据」误读成「在第 0 段」。</summary>
        public bool valid;

        /// <summary>相对路线根的位置。</summary>
        public Vector3 localPosition;

        /// <summary>相对路线根的水平朝向（绕 Y 轴欧拉角，度）。零位移段会继承邻近段朝向，故不为 NaN。</summary>
        public float localYawDegrees;

        /// <summary>当前所处段索引；-1 表示路线为空。</summary>
        public int legIndex;

        /// <summary>当前段内行程进度 [0,1]；停留阶段为 0。</summary>
        public float legProgress01;

        /// <summary>是否处于到点停留阶段。</summary>
        public bool isDwelling;

        /// <summary>所属路线的 T 周期（秒）：从初始状态到首次完全复原的最小周期。</summary>
        public float cycleTime;

        /// <summary>当前段索引是否有效（须同时满足 <see cref="valid"/>）。</summary>
        public bool HasLeg => valid && legIndex >= 0;
    }

    /// <summary>轨迹采样点（等弧长采样，供消费方绘制轨迹折线与朝向标线）。</summary>
    public struct StealthTrajectorySampleDto
    {
        public Vector3 localPosition;
        public float localYawDegrees;

        /// <summary>该采样点对应的时间索引 t（秒），消费方用它把「位置」反查成「时刻」。</summary>
        public float time;
    }

    /// <summary>单个路点的跨包视图（局部坐标 + 到点停留 + 本段速度）。</summary>
    public struct StealthWaypointDto
    {
        /// <summary>相对路线根的局部坐标（不是世界坐标）。</summary>
        public Vector3 localPosition;

        /// <summary>到点停留时长（秒）。0 表示不停留。</summary>
        public float stopDuration;

        /// <summary>驶入本路点那一段的移动速度（米/秒）。&lt;=0 表示使用路线默认速度。</summary>
        public float moveSpeed;
    }

    /// <summary>巡逻路线的循环方式。值序与玩法包内部同名枚举严格一致，禁止单独重排。</summary>
    public enum StealthLoopMode
    {
        /// <summary>首尾相连循环。</summary>
        Loop = 0,

        /// <summary>走到端点后原路折返（周期 = 去程 + 返程）。</summary>
        PingPong = 1
    }

    /// <summary>巡逻单位（NPC）的身份与根坐标系。消费方用它把 actorId 映射到场景物体。</summary>
    public struct StealthActorInfo
    {
        /// <summary>单位 id：同一场景同一时刻内唯一，路线重排不改变它。
        /// <b>但域重载（进出 Play、脚本重编译）后可能变化</b>，消费方不得把它持久化，
        /// 每次数据刷新都应重新枚举 <see cref="IPatrolWorld.Actors"/>。</summary>
        public int actorId;

        /// <summary>显示名（取场景物体 name，仅供面板标签使用，不参与逻辑判定）。</summary>
        public string displayName;

        /// <summary>该单位巡逻路线的 T 周期（秒）。无路线时为 0。</summary>
        public float cycleTime;

        /// <summary>是否成功解析到路线根（false 时下面三个字段不可信）。</summary>
        public bool hasRoot;

        public Vector3 rootPosition;
        public Quaternion rootRotation;
        public Vector3 rootScale;
    }

    /// <summary>
    /// 视野体快照（一个 NPC 的一套视野参数）。消费方据此绘制视野扇形，
    /// 不得、也无需访问提供方的组件类型。
    /// </summary>
    public struct StealthVisionInfo
    {
        /// <summary>拥有该视野的单位 id。</summary>
        public int actorId;

        /// <summary>观察原点（世界坐标，已含视线高度偏移）。</summary>
        public Vector3 originWorld;

        /// <summary>观察朝向（世界前向，单位化；无有效朝向时为零向量）。</summary>
        public Vector3 forwardWorld;

        /// <summary>水平视野角（度，全角，非半角）。</summary>
        public float horizontalAngleDegrees;

        /// <summary>垂直视野角（度，全角）。</summary>
        public float verticalAngleDegrees;

        /// <summary>最大视距（米）。</summary>
        public float maxDistance;

        /// <summary>视野是否启用。false 时消费方应跳过绘制，而不是画成零尺寸。</summary>
        public bool enabled;
    }

    /// <summary>察觉层级选择。跨包只暴露这三档；玩法包内部的遮蔽修正与预留位不在此列，
    /// 避免消费方依赖提供方的内部枚举。</summary>
    [Flags]
    public enum StealthQueryLayers
    {
        /// <summary>只判距离。</summary>
        DistanceOnly = 1,

        /// <summary>距离 + 视角锥。</summary>
        Cone = 1 << 1,

        /// <summary>距离 + 视角锥 + 视线（几何遮挡）。</summary>
        LineOfSight = 1 << 2,

        /// <summary>逐层全开（等价于完整判定）。</summary>
        Full = DistanceOnly | Cone | LineOfSight,
    }

    /// <summary>察觉档位（值序即严重程度，消费方可直接比较大小）。</summary>
    public enum StealthAlertStage
    {
        /// <summary>巡逻中，未察觉。</summary>
        Patrol = 0,

        /// <summary>有可疑迹象，前往查看。</summary>
        Suspicious = 1,

        /// <summary>已发现目标，追踪。</summary>
        Alerted = 2,

        /// <summary>丢失目标，在嫌疑区域搜索。</summary>
        Searching = 3,
    }

    /// <summary>时钟状态快照。编辑态回溯时消费方要以此为准显示当前 t 与模式，不许自估。</summary>
    public struct StealthClockDto
    {
        /// <summary>0=Auto（每帧自增），1=Manual（外部驱动，编辑态回溯用）。与玩法包内部同名枚举值序一致。</summary>
        public int mode;

        /// <summary>当前时间索引 t（秒，未取模）。</summary>
        public float time;

        /// <summary>是否处于手动模式。</summary>
        public bool IsManual => mode == 1;
    }

    /// <summary>察觉事件载荷，供 <see cref="StealthDetectedChannel"/> 广播。</summary>
    public struct StealthDetectedPayload
    {
        /// <summary>察觉者（NPC）id。</summary>
        public int actorId;

        /// <summary>被察觉目标 id（由玩法包定义，通常为玩家注册 id；无目标时为 -1）。</summary>
        public int targetId;

        /// <summary>事件发生时的时间索引 t（秒）。消费方用它把事件钉回回溯时间轴上。</summary>
        public float timeIndex;

        /// <summary>事件后的档位。</summary>
        public StealthAlertStage stage;

        /// <summary>当时的累积察觉度 [0,1]。</summary>
        public float suspicion;
    }
}
