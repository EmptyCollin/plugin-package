using UnityEditor;
using UnityEngine;

namespace Collin.Stretch.EditorTooling
{
    /// <summary>
    /// 烘焙定稿：把"源结构 + 实例倍数"的结果持久化为 Profile 子资产（§0.1 原则2：
    /// 烘焙时把当前实例的倍数固化进网格，故导出物无需依赖场景组件）。
    /// 菜单：Tools/Collin/Stretch 拉伸/烘焙选中物体的结果。
    /// </summary>
    public static class StretchBakeCommand
    {
        [MenuItem("Tools/Collin/Stretch 拉伸/烘焙选中物体的结果", false, 2)]
        public static void BakeMenu()
        {
            var go = Selection.activeGameObject;
            if (go == null)
            {
                EditorUtility.DisplayDialog("Collin Stretch", "请先选中含 StretchInstance 的物体。", "好");
                return;
            }
            var mesh = BakeOne(go, out var err);
            if (mesh == null)
            {
                EditorUtility.DisplayDialog("Collin Stretch", err, "好");
                return;
            }
            EditorGUIUtility.PingObject(mesh);
            Debug.Log($"[StretchBake] 已生成子资产: {AssetDatabase.GetAssetPath(mesh)} :: {mesh.name}（{mesh.vertexCount} 顶点）");
        }

        /// <summary>核心烘焙（无 UI 弹窗，供菜单与测试复用）。失败返回 null + 原因。</summary>
        public static Mesh BakeOne(GameObject go, out string error)
        {
            error = null;
            var filter = go.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null)
            {
                error = "选中物体没有源网格";
                return null;
            }
            var inst = go.GetComponent<StretchInstance>();
            if (inst == null || inst.profile == null)
            {
                error = "物体上没有 StretchInstance，或其 profile 为空";
                return null;
            }

            var src = inst.OriginalMesh != null ? inst.OriginalMesh : filter.sharedMesh;
            var repeats = inst.EffectiveRepeats();
            string reason = StretchMeshBuilder.FailReason(src, inst.profile, repeats);
            if (reason != null)
            {
                error = reason;
                return null;
            }
            var built = StretchMeshBuilder.Build(src, inst.profile, repeats);
            if (built == null)
            {
                error = "网格构建失败（源不可读/缺 UV0）";
                return null;
            }

            string path = AssetDatabase.GetAssetPath(inst.profile);
            if (string.IsNullOrEmpty(path))
            {
                Object.DestroyImmediate(built);
                error = "Profile 不是磁盘资产，无法挂子资产";
                return null;
            }

            string assetName = go.name + "_baked";
            foreach (var sub in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (sub is Mesh m && m.name == assetName)
                {
                    AssetDatabase.RemoveObjectFromAsset(m);
                }
            }

            built.name = assetName;
            AssetDatabase.AddObjectToAsset(built, inst.profile);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            var persisted = FindBakedInAsset(path, assetName);
            if (persisted == null)
            {
                error = "写入后未能回读子资产";
                return null;
            }
            return persisted;
        }

        public static Mesh FindBakedInAsset(string profilePath, string assetName)
        {
            foreach (var sub in AssetDatabase.LoadAllAssetsAtPath(profilePath))
            {
                if (sub is Mesh m && m.name == assetName) return m;
            }
            return null;
        }
    }
}
