using UnityEditor;

namespace Collin.Stretch.EditorTooling
{
    /// <summary>编辑期调度泵（DESIGN §8.2 P4）：域重载/场景加载后继续消费重建队列；
    /// 面板交互走 RebuildNow 即时路径不受预算影响（原则1 所见即所得对"正在编辑的对象"兑现）。</summary>
    [InitializeOnLoad]
    public static class StretchEditorPump
    {
        static StretchEditorPump()
        {
            EditorApplication.update -= Pump;
            EditorApplication.update += Pump;
            EditorApplication.playModeStateChanged -= OnModeChange;
            EditorApplication.playModeStateChanged += OnModeChange;
        }

        private static void OnModeChange(PlayModeStateChange c)
        {
            // 退出 Play 还原材料 instancing 临时开关（编辑期不留痕，§8.2 材质纪律）
            if (c == PlayModeStateChange.EnteredEditMode) StretchChunkManager.RestoreTouchedMaterials();
        }

        private static void Pump()
        {
            if (StretchScheduler.Pending > 0) StretchScheduler.Pump();
        }
    }
}
