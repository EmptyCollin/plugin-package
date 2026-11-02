using UnityEditor;
using UnityEngine;

namespace Collin.Stretch.EditorTooling
{
    /// <summary>
    /// 烘焙定稿：把当前预览的拉伸结果写成磁盘网格子资产（挂在 Profile 资产下），
    /// 供不挂组件的普通流程使用（导出/静态场景/其他工具消费）。
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
                EditorUtility.DisplayDialog("Collin Stretch", "请先选中含 StretchPreview 或 StretchRepeater 的物体。", "好");
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
            var preview = go.GetComponent<StretchPreview>();
            var repeater = go.GetComponent<StretchRepeater>();
            var profile = preview != null && preview.profile != null
                ? preview.profile
                : (repeater != null ? repeater.profile : null);
            if (profile == null)
            {
                error = "物体上没有 StretchPreview/StretchRepeater，或其 profile 为空";
                return null;
            }

            // 源网格必须是未经拉伸的原始网格
            var src = preview != null && preview.OriginalMesh != null ? preview.OriginalMesh : filter.sharedMesh;

            string err2 = profile.Validate(
                StretchMeshBuilder.BoundsMin(src, profile.axis),
                StretchMeshBuilder.BoundsMax(src, profile.axis));
            if (err2 != null)
            {
                error = "参数校验未通过: " + err2;
                return null;
            }
            var built = StretchMeshBuilder.Build(src, profile);
            if (built == null)
            {
                error = "网格构建失败（源不可读/缺 UV0）";
                return null;
            }

            string path = AssetDatabase.GetAssetPath(profile);
            if (string.IsNullOrEmpty(path))
            {
                Object.DestroyImmediate(built);
                error = "Profile 不是磁盘资产，无法挂子资产";
                return null;
            }

            // 重复烘焙：先清掉旧同名子资产
            string assetName = go.name + "_baked";
            foreach (var sub in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (sub is Mesh m && m.name == assetName)
                {
                    AssetDatabase.RemoveObjectFromAsset(m);
                }
            }

            // built 是场景外临时对象，直接 AddObjectToAsset 持久化
            built.name = assetName;
            AssetDatabase.AddObjectToAsset(built, profile);
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

        /// <summary>在 Profile 资产文件里找同名烘焙网格。</summary>
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
