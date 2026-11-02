using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace Collin.Stretch.EditorTooling
{
    /// <summary>
    /// 场景保存守卫：StretchInstance 的预览网格是 DontSave 内存态，直接保存会把
    /// MeshFilter 引用序列化成 missing。本钩子在保存前还原源网格，保存后立即重建——
    /// 磁盘场景永远只存源网格+Profile+倍数，打开即见拉伸结果（配合 [ExecuteAlways]）。
    /// </summary>
    [InitializeOnLoad]
    public static class StretchSaveHook
    {
        private static readonly List<StretchInstance> _touched = new List<StretchInstance>();

        static StretchSaveHook()
        {
            EditorSceneManager.sceneSaving -= OnSaving;
            EditorSceneManager.sceneSaving += OnSaving;
            EditorSceneManager.sceneSaved -= OnSaved;
            EditorSceneManager.sceneSaved += OnSaved;
        }

        private static void OnSaving(Scene scene, string path)
        {
            _touched.Clear();
            if (!scene.IsValid()) return;
            foreach (var root in scene.GetRootGameObjects())
            {
                _touched.AddRange(root.GetComponentsInChildren<StretchInstance>(true));
            }
            foreach (var inst in _touched)
            {
                if (inst != null) inst.Restore();
            }
        }

        private static void OnSaved(Scene scene)
        {
            foreach (var inst in _touched)
            {
                if (inst != null) inst.Rebuild();
            }
            _touched.Clear();
        }
    }
}
