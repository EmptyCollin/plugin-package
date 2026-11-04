using System.Collections.Generic;
using UnityEngine;
#if STEALTH_NAVMESH_AVAILABLE
using UnityEngine.AI;
#endif

namespace Collin.Stealth.Navigation
{
    /// <summary>
    /// 基于 <b>legacy NavMesh</b>（内置 <c>com.unity.modules.ai</c>）的寻路桥实现。
    /// <para>刻意<b>不</b>依赖 <c>com.unity.ai.navigation</c>（DESIGN D-06：付费 HAVOK 方案 +
    /// 其 NavMeshAgent 逐帧积分会摧毁位姿纯函数地基）。这里只用 <see cref="NavMesh.CalculatePath"/>
    /// 做<b>一次性几何查询</b>，不引入任何按帧移动的组件。</para>
    /// <para>本类位于可选子程序集 <c>com.collin.stealth.Navigation</c>：不想要导航的工程
    /// 删掉该目录即可，主包不受影响；注册与否决定哨兵反应是「直线」还是「绕行」。</para>
    /// <para>注册方式（二选一）：场景里放一个 <see cref="NavMeshPathBridgeInstaller"/>，
    /// 或在你自己的装配代码里 <c>Services.Register&lt;IPathBridge&gt;(new NavMeshPathBridge())</c>。</para>
    /// </summary>
    /// <summary>
    /// 求绕行路径的实现。仅在内置 AI 模块存在时编译（<c>STEALTH_NAVMESH_AVAILABLE</c>，
    /// 由本程序集 versionDefines 注入）；缺失时走下方降级实现，行为等同「没有导航」。
    /// </summary>
#if STEALTH_NAVMESH_AVAILABLE
    public sealed class NavMeshPathBridge : IPathBridge
    {
        /// <summary>路径点离可导航面的吸附容差（米）。</summary>
        const float SampleTolerance = 0.5f;

        /// <summary>复用的路径缓冲（避免每次重路由都分配）。</summary>
        readonly NavMeshPath _path = new NavMeshPath();

        /// <summary>
        /// 求绕行路径。<c>corners</c> 保证 <c>[0]</c> 为出发点、末点为路径终点（可能早于目标：不可达时是可达边缘）。
        /// 未烘焙 NavMesh 或起终点都不在可导航面上时返回 false。
        /// </summary>
        public bool TryFindPath(Vector3 fromWorld, Vector3 desiredWorld, List<Vector3> corners)
        {
            if (corners == null) return false;
            corners.Clear();

            // 起终点都先吸到可导航面上：哨兵追击时基线点可能贴着掩体边缘，
            // 玩家则根本不在导航面上（掩体、撤离区都不该是可行走面）。
            if (!SampleSurface(fromWorld, out var start) && !SampleSurface(desiredWorld, out start))
            {
                return false;   // 完全没有导航面：交给调用方退回直线
            }

            var hasGoal = SampleSurface(desiredWorld, out var goal);
            if (!hasGoal) goal = nearestTo(desiredWorld);

            if (!NavMesh.CalculatePath(start, goal, NavMesh.AllAreas, _path)
                || _path.status == NavMeshPathStatus.PathInvalid
                || _path.corners == null || _path.corners.Length < 2)
            {
                return false;
            }

            // corners[0] 必须是出发点：调用方以「基线为锚」算叠加偏移，锚点错位会让 NPC 瞬移。
            var c = _path.corners;
            corners.Add(start);
            for (var i = 1; i < c.Length; i++) corners.Add(c[i]);
            return corners.Count >= 2;
        }

        /// <summary>单点吸附到可导航面（不可达时返回 false，调用方保持原目标）。</summary>
        public bool TrySnap(Vector3 fromWorld, Vector3 desiredWorld, out Vector3 navWorld)
        {
            if (SampleSurface(desiredWorld, out navWorld)) return true;
            navWorld = desiredWorld;
            return false;
        }

        bool SampleSurface(Vector3 p, out Vector3 hit)
        {
            var source = new NavMeshHit();
            // 有 NavMesh 数据时 nearest 才可信；未烘焙时 QueryType 会返回失败。
            if (NavMesh.SamplePosition(p, out source, SampleTolerance, NavMesh.AllAreas))
            {
                hit = source.position;
                return true;
            }
            hit = p;
            return false;
        }

        Vector3 nearestTo(Vector3 p)
        {
            var source = new NavMeshHit();
            return NavMesh.SamplePosition(p, out source, SampleTolerance * 8f, NavMesh.AllAreas)
                ? source.position
                : p;
        }
    }
#else
    /// <summary>
    /// <b>降级实现</b>：内置 AI 模块（<c>com.unity.modules.ai</c>）不存在时编译这一版。
    /// 行为与「场景没烘焙 NavMesh」完全一致——一律返回 false，由调用方退回直线叠加，
    /// 保证本程序集在缺少导航模块的工程里也能编译、且不改变主包玩法。
    /// </summary>
    public sealed class NavMeshPathBridge : IPathBridge
    {
        public bool TryFindPath(Vector3 fromWorld, Vector3 desiredWorld, List<Vector3> corners)
        {
            if (corners != null) corners.Clear();
            return false;
        }

        public bool TrySnap(Vector3 fromWorld, Vector3 desiredWorld, out Vector3 navWorld)
        {
            navWorld = desiredWorld;
            return false;
        }
    }
#endif

    /// <summary>
    /// 装配点：存在即注册（<see cref="IPathBridge"/> 是全局单例服务，故同一时刻只应有一个）。
    /// <para><b>两条注册路径，各管一侧</b>：</para>
    /// <list type="bullet">
    /// <item>Play 态：本类的 <c>RuntimeInitializeOnLoadMethod</c>（场景加载后）或挂在场景里的
    /// 本组件 <c>OnEnable</c>；</item>
    /// <item>编辑态：<b>只能</b>靠同包的 <c>StealthNavEditorBootstrap</c>
    /// （<c>[InitializeOnLoad]</c>）——本包已 4 次实测确认 <b>EditMode 不执行 MonoBehaviour
    /// 生命周期回调</b>，所以「往场景放一个组件」在编辑态是无效的。</item>
    /// </list>
    /// <para>注册幂等（先判存在），两条路径同时命中也不会报冲突。</para>
    /// <para>本程序集是<b>运行时</b>装配定义，禁止引用 UnityEditor（校验器 runtime-purity 强制）。</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NavMeshPathBridgeInstaller : MonoBehaviour
    {
        static readonly NavMeshPathBridge Shared = new NavMeshPathBridge();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void RegisterOnPlay()
        {
            Register();
        }

        /// <summary>幂等注册：已有其他实现（例如自定义桥）时不覆盖、不报冲突。</summary>
        public static void Register()
        {
            if (!Collin.Core.Services.TryGet<IPathBridge>(out _))
            {
                Collin.Core.Services.Register<IPathBridge>(Shared);
            }
        }

        void OnEnable() => Register();
    }
}
