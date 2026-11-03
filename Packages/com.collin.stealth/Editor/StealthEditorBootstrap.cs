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

            // Inspector 改完路点/速度后必须立刻见效（AGENTS.md §0.1 原则 1 所见即所得）。
            // 编辑态没有帧循环，所以由撤销回调主动推一次位姿，而不是等下一帧。
            Undo.postprocessModifications += OnModificationsApplied;
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
