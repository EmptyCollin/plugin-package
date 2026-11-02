using UnityEditor;
using UnityEngine;

namespace Collin.Stretch.EditorTooling
{
    /// <summary>
    /// 创建入口：选中含 MeshFilter 的对象 → 生成 Profile 资产（同目录）+ 挂 StretchPreview。
    /// 菜单：Tools/Collin/Stretch 拉伸 — 创建参数资产。
    /// </summary>
    public static class StretchCreateCommand
    {
        [MenuItem("Tools/Collin/Stretch 拉伸/新建拉伸参数（选中模型）", false, 1)]
        public static void CreateProfile()
        {
            var go = Selection.activeGameObject;
            if (go == null)
            {
                EditorUtility.DisplayDialog("Collin Stretch", "请先在Hierarchy选中一个含 MeshFilter 的模型对象。", "好");
                return;
            }
            var filter = go.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null)
            {
                EditorUtility.DisplayDialog("Collin Stretch", "选中对象没有 MeshFilter 或网格为空，无法创建拉伸参数。", "好");
                return;
            }
            var mesh = filter.sharedMesh;
            if (!StretchMeshBuilder.CanProcess(mesh, out var err))
            {
                if (err.Contains("不可读"))
                {
                    if (EditorUtility.DisplayDialog("Collin Stretch",
                            "模型网格不可读（Read/Write 未开启）。可在 Project 中选中模型文件 → Inspector → 勾 Read/Write → Apply。要现在尝试自动开启吗？",
                            "尝试开启", "稍后再说"))
                    {
                        TryEnableReadWrite(mesh);
                    }
                }
                else
                {
                    EditorUtility.DisplayDialog("Collin Stretch", err, "好");
                }
                return;
            }

            var existing = go.GetComponent<StretchPreview>();
            if (existing != null && existing.profile != null)
            {
                Selection.activeObject = existing.profile;
                EditorGUIUtility.PingObject(existing.profile);
                return;
            }

            var profile = ScriptableObject.CreateInstance<StretchProfile>();
            profile.InitFromBounds(
                StretchMeshBuilder.BoundsMin(mesh, StretchAxis.Y),
                StretchMeshBuilder.BoundsMax(mesh, StretchAxis.Y));
            profile.axis = StretchAxis.Y;

            // 资产落在模型旁：取模型 prefab 路径目录，取不到则 Assets/
            string dir = "Assets";
            var prefabPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(go);
            if (!string.IsNullOrEmpty(prefabPath))
            {
                dir = System.IO.Path.GetDirectoryName(prefabPath).Replace('\\', '/');
            }
            string assetPath = AssetDatabase.GenerateUniqueAssetPath(
                $"{dir}/{go.name}_stretchProfile.asset");
            AssetDatabase.CreateAsset(profile, assetPath);

            var preview = go.GetComponent<StretchPreview>() ?? go.AddComponent<StretchPreview>();
            preview.SetProfile(profile);
            EditorUtility.SetDirty(go);
            AssetDatabase.SaveAssets();

            Selection.activeObject = profile;
            EditorGUIUtility.PingObject(profile);
        }

        private static void TryEnableReadWrite(Mesh mesh)
        {
            // fbx/obj 子资产的 GetAssetPath 返回其源模型文件路径
            string path = AssetDatabase.GetAssetPath(mesh);
            bool isModel = path.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase) ||
                           path.EndsWith(".obj", System.StringComparison.OrdinalIgnoreCase);
            if (!isModel)
            {
                Debug.LogWarning($"[Stretch] 网格源文件不可识别（{path}），请手动在模型导入设置开启 Read/Write。");
                return;
            }
            var mi = AssetImporter.GetAtPath(path) as ModelImporter;
            if (mi != null && !mi.isReadable)
            {
                mi.isReadable = true;
                mi.SaveAndReimport();
                Debug.Log($"[Stretch] 已开启 Read/Write: {path}，可重新执行创建。");
            }
        }
    }
}
