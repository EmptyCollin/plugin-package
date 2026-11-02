using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using UnityEngine;

namespace Collin.Stretch.EditorTooling
{
    /// <summary>
    /// 场景保存守卫：StretchPreview 的预览网格是 DontSave 内存态，直接保存场景会把
    /// MeshFilter 的引用序列化成 missing。本钩子在保存前把所有预览还原为源网格，
    /// 保存后立即重建——磁盘上的场景永远只存源网格 + Profile 引用，打开即见拉伸结果（配合 [ExecuteAlways]）。
    /// </summary>
    [InitializeOnLoad]
    public static class StretchSaveHook
    {
        private static readonly List<StretchPreview> _touched = new List<StretchPreview>();

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
                _touched.AddRange(root.GetComponentsInChildren<StretchPreview>(true));
            }
            foreach (var pv in _touched)
            {
                if (pv != null) pv.Restore();
            }
        }

        private static void OnSaved(Scene scene)
        {
            foreach (var pv in _touched)
            {
                if (pv != null) pv.Rebuild();
            }
            _touched.Clear();
        }
    }
}
