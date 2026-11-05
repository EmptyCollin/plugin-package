using Collin.Core;
using Collin.Core.Stealth;
using UnityEditor;

namespace Collin.Stealth.EditorTooling
{
    /// <summary>
    /// 编辑器装配钩子：让本包的跨包契约在<b>编辑态（非 Play）</b>也可用。
    /// <para>为什么必须由本包自己做：B 包（设计辅助台）只允许引用 core，
    /// 不能为了「取到 IPatrolWorld」而去引用本包类型；而 Play 态的注册点是
    /// <see cref="RuntimeInitializeOnLoadMethod"/>，它在编辑态根本不执行。
    /// 于是编辑态回溯（特性 2「t 以内滑动条实时查看 NPC 位置」）会拿不到服务而静默降级。</para>
    /// <para><c>[InitializeOnLoad]</c> 在每次编辑器域重载后都会跑一次，正好补上
    /// core 的 <see cref="Services"/>「域重载即清空」这一已知语义（core DESIGN §6 风险 1）。
    /// 本钩子只做注册与预览刷新，不创建场景物体、不改任何资产。</para>
    /// </summary>
    [InitializeOnLoad]
    static class StealthEditorBootstrap
    {
        static StealthEditorBootstrap()
        {
            StealthWorld.EnsureRegistered();

            // U-8 收口：编辑态切换场景后，组件 OnDisable 不保证触发 ⇒ VisionRegistry / DetectionRegistry
            // 的缓存可能滞留在已销毁对象上。Runtime 侧那道 HasDestroyed 自愈判据对「重建结果为空列表
            // （切到没有视野体的场景）」这一路径判不出来 ⇒ 切回原场景会永远返回 0（实测复现）。
            // 唯一可靠且不打扰烘焙热路径的补法：在<b>能拿到切场景事件的一侧（Editor）</b>订阅并置脏，
            // 下次读取自然重建。刻意不放 Runtime —— 那会破 §0.1 原则 3（Runtime 禁引 UnityEditor）。
            UnityEditor.SceneManagement.EditorSceneManager.activeSceneChangedInEditMode += OnEditorActiveSceneChanged;

            // Inspector 改完路点/速度后必须立刻见效（AGENTS.md §0.1 原则 1 所见即所得）。
            // 编辑态没有帧循环，所以由撤销回调主动推一次位姿，而不是等下一帧。
            Undo.postprocessModifications += OnModificationsApplied;
        }

        /// <summary>
        /// 编辑态活动场景切换 ⇒ 两个注册表都置脏（下次读取重建）。
        /// <para>参数不用取——事件本身即「场景里的视野体/察觉目标全换了一批」的信号。</para>
        /// </summary>
        static void OnEditorActiveSceneChanged(
            UnityEngine.SceneManagement.Scene from, UnityEngine.SceneManagement.Scene to)
        {
            VisionRegistry.MarkDirty();
            DetectionRegistry.MarkDirty();
        }

        static UndoPropertyModification[] OnModificationsApplied(UndoPropertyModification[] modifications)
        {
            if (modifications == null || modifications.Length == 0) return modifications;

            var world = StealthWorld.Instance;
            if (world == null) return modifications;

            var touchesStealth = false;
            for (var i = 0; i < modifications.Length; i++)
            {
                var value = modifications[i].currentValue;
                if (IsStealthTarget(value != null ? value.target : null))
                {
                    touchesStealth = true;
                    break;
                }
            }

            if (touchesStealth)
            {
                // 只刷新预览：不改时钟模式、不改 t。用户在 Play 里撤销时不该被强行拽进手动回溯。
                world.RefreshPreview();
            }

            return modifications;
        }

        static bool IsStealthTarget(UnityEngine.Object target)
        {
            if (target == null) return false;
            return target is PatrolRoute
                || target is PatrolRouteRoot
                || target is PatrolAgent
                || target is StealthClock;
        }
    }
}
