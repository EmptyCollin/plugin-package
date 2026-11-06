using UnityEditor;
using UnityEngine;

namespace Collin.Stretch.EditorTooling
{
    /// <summary>
    /// 创建入口（AGENTS.md §0.1 原则1）：选中模型 → 生成共享结构 Profile + 挂 StretchInstance，
    /// 面板直接呈现在该实例的 Inspector 上（不必跳去资产文件）。
    /// 全路径空守卫 + 异常捕获：禁止裸 NRE 污染控制台门禁（§1.6b）。
    /// </summary>
    public static class StretchCreateCommand
    {
        [MenuItem("Tools/Collin/Stretch 拉伸/新建拉伸参数（选中模型）", false, 1)]
        public static void CreateProfile()
        {
            try
            {
                Run();
            }
            catch (System.Exception ex)
            {
                // §2.1.5：禁止弹窗阻塞指令通道。异常仍记 warning（真实故障信号，不降级为 Log），
                // 但绝不允许抛裸异常在控制台留 error。
                Debug.LogWarning($"[Stretch] 创建失败: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private static void Run()
        {
            var go = Selection.activeGameObject;
            if (go == null)
            {
                Debug.Log("[Stretch] 请先在 Hierarchy 选中一个含 MeshFilter 的模型对象。");
                return;
            }
            var filter = go.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null)
            {
                Debug.Log("[Stretch] 选中对象没有 MeshFilter 或网格为空，无法创建拉伸参数。");
                return;
            }
            var mesh = filter.sharedMesh;

            // 已装配过：直接选中实例组件（所见即所得：面板就在场景物体上）
            var existing = go.GetComponent<StretchInstance>();
            if (existing != null && existing.profile != null)
            {
                Selection.activeGameObject = go;
                EditorGUIUtility.PingObject(go);
                return;
            }

            if (!StretchMeshBuilder.CanProcess(mesh, out var err))
            {
                if (err != null && err.Contains("不可读"))
                {
                    // §2.1.5：禁止 DisplayDialog 阻塞主线程 = 卡死指令通道。
                    // 原"要现在尝试自动开启吗？"的二选一确认，拆成独立菜单命令，由用户自行触发。
                    Debug.Log($"[Stretch] 模型网格不可读（Read/Write 未开启）：{mesh.name}。\n"
                        + "请执行菜单 Tools/Collin/Stretch 拉伸/开启选中模型的 Read/Write，然后重新创建拉伸参数。");
                    return;
                }
                Debug.Log("[Stretch] 无法创建拉伸参数：" + err);
                return;
            }

            var profile = ScriptableObject.CreateInstance<StretchProfile>();
            profile.InitFromMesh(mesh, StretchAxis.Y);

            // 落点优先级（AGENTS.md §0：Assets/ 不放插件产物）：
            // 1) 模型是 prefab 实例 → prefab 同目录；2) 当前场景在包内 → 该包的 Sample/Profiles；
            // 3) 兜底 → 场景所在目录。绝不写 Assets 根。
            string dir = null;
            string prefabPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(go);
            if (!string.IsNullOrEmpty(prefabPath))
            {
                dir = System.IO.Path.GetDirectoryName(prefabPath).Replace('\\', '/');
            }
            if (string.IsNullOrEmpty(dir))
            {
                var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
                string sp = scene.path.Replace('\\', '/');
                if (sp.StartsWith("Packages/"))
                {
                    int slash = sp.IndexOf('/');
                    int next = sp.IndexOf('/', slash + 1);
                    string pkgRoot = next > 0 ? sp.Substring(0, next) : sp;
                    dir = pkgRoot + "/Sample/Profiles";
                    System.IO.Directory.CreateDirectory(dir);
                }
                else if (!string.IsNullOrEmpty(sp))
                {
                    dir = System.IO.Path.GetDirectoryName(sp).Replace('\\', '/');
                }
            }
            if (string.IsNullOrEmpty(dir))
            {
                Debug.Log("无法确定参数资产存放位置：模型不是 prefab 实例，且当前场景未保存。\n请先保存场景后重试。");
                Object.DestroyImmediate(profile);
                return;
            }
            string assetPath = AssetDatabase.GenerateUniqueAssetPath(
                $"{dir}/{go.name}_stretchProfile.asset");
            AssetDatabase.CreateAsset(profile, assetPath);

            var instance = go.GetComponent<StretchInstance>() ?? go.AddComponent<StretchInstance>();
            if (instance == null)
            {
                Debug.LogWarning($"[Stretch] 无法为 {go.name} 添加 StretchInstance（可能已有互斥组件）。");
                return;
            }
            instance.SetProfile(profile);
            EditorUtility.SetDirty(go);
            AssetDatabase.SaveAssets();

            Selection.activeGameObject = go;   // 面板即在该物体的 Inspector 上呈现
            EditorGUIUtility.PingObject(go);
        }

        /// <summary>
        /// 独立菜单：开启选中模型网格的 Read/Write。
        /// 由 §2.1.5 弹窗改造而来——原"创建时询问是否自动开启"的模态确认会卡死指令通道，
        /// 现拆成用户主动触发的命令，能力不减。
        /// </summary>
        [MenuItem("Tools/Collin/Stretch 拉伸/开启选中模型的 Read/Write", false, 3)]
        public static void EnableReadWriteMenu()
        {
            var go = Selection.activeGameObject;
            if (go == null)
            {
                Debug.Log("[Stretch] 请先在 Hierarchy 选中一个含 MeshFilter 的模型对象。");
                return;
            }
            var filter = go.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null)
            {
                Debug.Log("[Stretch] 选中对象没有 MeshFilter 或网格为空，无 Read/Write 可开。");
                return;
            }
            TryEnableReadWrite(filter.sharedMesh);
        }

        private static void TryEnableReadWrite(Mesh mesh)
        {
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
