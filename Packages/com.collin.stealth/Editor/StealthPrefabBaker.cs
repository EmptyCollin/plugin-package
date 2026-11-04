using System.Collections.Generic;
using System.IO;
using Collin.Stealth;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Collin.Stealth.EditorTooling
{
    /// <summary>
    /// 预制件烘焙（M5 / DESIGN D-13）：把示例场景里的玩家与哨兵单元沉淀为包内 <c>.prefab</c>，
    /// 让「拆箱即用」有真正的载体——新场景直接拖预制件即可玩，不必再跑一键生成。
    /// <para>纪律：预制件只带<b>实例级数据</b>（组件参数、父子结构、碰撞体尺寸）与
    /// 对<b>共享 Model 的引用</b>（路线 SO、视野 SO），不在预制件里复制资产（AGENTS.md §0.1 原则 2）。</para>
    /// <para>共享编辑器纪律同生成器：不弹模态窗、目标场景已打开时不重复 Additive。</para>
    /// </summary>
    public static class StealthPrefabBaker
    {
        const string SampleScenePath = "Packages/com.collin.stealth/Sample/Scenes/StealthSample.unity";
        const string PrefabDir = "Packages/com.collin.stealth/Sample/Prefabs";
        const string PlayerPath = PrefabDir + "/StealthPlayer.prefab";
        const string GuardPath = PrefabDir + "/StealthGuard.prefab";

        [MenuItem("Tools/Collin/Stealth/沉淀示例预制件（玩家+哨兵）", false, 40)]
        public static void BakeFromSampleScene()
        {
            if (!File.Exists(SampleScenePath))
            {
                Debug.LogWarning("[Stealth][Prefab] 示例场景不存在，先执行菜单 Tools/Collin/Stealth/生成可玩示例场景");
                return;
            }

            // 目标场景可能已经是当前场景（生成器执行后就是），重复 Additive 会报路径占用。
            var scene = FindOrOpen(SampleScenePath);
            var ownScene = !scene.isLoaded || !string.Equals(scene.path, SampleScenePath)
                ? false : WasAlreadyOpen(SampleScenePath);

            var player = FindRoot(scene, "Player");
            var guard = FindRoot(scene, "Guard_Patrol");

            if (player == null || guard == null)
            {
                Debug.LogError("[Stealth][Prefab] 场景内缺物体：player=" + (player != null)
                               + " guard=" + (guard != null) + "，先重新生成示例场景");
                return;
            }

            EnsureDir();

            var savedPlayer = PrefabUtility.SaveAsPrefabAsset(player, PlayerPath, out var err1);
            if (savedPlayer == null)
            {
                Debug.LogError("[Stealth][Prefab] 玩家预制件保存失败：" + err1);
            }

            var savedGuard = PrefabUtility.SaveAsPrefabAsset(guard, GuardPath, out var err2);
            if (savedGuard == null)
            {
                Debug.LogError("[Stealth][Prefab] 哨兵预制件保存失败：" + err2);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            // 烘焙后自检：预制件根上的组件与共享资产引用必须齐全，否则「拆箱即用」是空话。
            // 玩家单元：三个组件都在<b>根</b>上（相机/HUD 是子物体）。
            Verify(PlayerPath, new[] { typeof(CharacterController), typeof(DetectionState), typeof(PlayerStealthController) });
            // 哨兵单元：根只是容器，组件分布在 RouteRoot / Body 两个子物体上 → 交给专项自检。
            VerifyBody(GuardPath);

            if (!ownScene) EditorSceneManager.CloseScene(scene, true);

            Debug.Log("[Stealth][Prefab] 沉淀完成 -> " + PlayerPath + " / " + GuardPath
                      + "（player=" + (savedPlayer != null) + " guard=" + (savedGuard != null) + "）");
        }

        static bool WasAlreadyOpen(string path)
        {
            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var s = SceneManager.GetSceneAt(i);
                if (s.isLoaded && string.Equals(s.path, path, System.StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        static UnityEngine.SceneManagement.Scene FindOrOpen(string path)
        {
            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var s = SceneManager.GetSceneAt(i);
                if (s.isLoaded && string.Equals(s.path, path, System.StringComparison.OrdinalIgnoreCase))
                {
                    EditorSceneManager.SetActiveScene(s);
                    return s;
                }
            }
            return EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
        }

        static GameObject FindRoot(UnityEngine.SceneManagement.Scene scene, string name)
        {
            var roots = scene.GetRootGameObjects();
            for (var i = 0; i < roots.Length; i++)
            {
                if (roots[i].name == name) return roots[i];
            }
            return null;
        }

        static void Verify(string prefabPath, System.Type[] requiredOnRoot)
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (go == null)
            {
                Debug.LogError("[Stealth][Prefab] 自检失败：读不到 " + prefabPath);
                return;
            }

            var missing = new List<string>();
            for (var i = 0; i < requiredOnRoot.Length; i++)
            {
                if (go.GetComponent(requiredOnRoot[i]) == null) missing.Add(requiredOnRoot[i].Name);
            }

            if (missing.Count > 0)
            {
                Debug.LogError("[Stealth][Prefab] 自检失败：" + prefabPath + " 根上缺组件 "
                               + string.Join(", ", missing));
            }
        }

        /// <summary>哨兵预制件专项自检：Body 上的三件套 + 路线/视野资产引用非空。</summary>
        static void VerifyBody(string guardPath)
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(guardPath);
            if (go == null) return;

            var body = go.transform.Find("Body");
            if (body == null)
            {
                Debug.LogError("[Stealth][Prefab] 自检失败：哨兵预制件下找不到 Body 子物体");
                return;
            }

            var agent = body.GetComponent<PatrolAgent>();
            var vision = body.GetComponent<VisionVolume>();
            var brain = body.GetComponent<GuardBrain>();
            var rootTf = go.transform.Find("RouteRoot");

            var problems = new List<string>();
            if (agent == null) problems.Add("Body 缺 PatrolAgent");
            if (vision == null) problems.Add("Body 缺 VisionVolume");
            if (brain == null) problems.Add("Body 缺 GuardBrain");
            if (rootTf == null || rootTf.GetComponent<PatrolRouteRoot>() == null) problems.Add("缺 RouteRoot 或其组件");
            if (agent != null && agent.RouteRoot == null) problems.Add("PatrolAgent 未引用路线根（出场景即失效）");
            if (vision != null && vision.Profile == null) problems.Add("VisionVolume 未引用 VisionProfile");
            if (rootTf != null)
            {
                var rr = rootTf.GetComponent<PatrolRouteRoot>();
                if (rr != null && rr.Route == null) problems.Add("PatrolRouteRoot 未引用路线资产");
            }

            if (problems.Count > 0)
            {
                Debug.LogError("[Stealth][Prefab] 自检失败：" + guardPath + " -> " + string.Join("；", problems));
            }
            else
            {
                Debug.Log("[Stealth][Prefab] 自检通过：" + guardPath
                          + " 携带路线 " + (rootTf.GetComponent<PatrolRouteRoot>().Route.name)
                          + " 与视野 " + vision.Profile.name
                          + "，可直接拖进任意空场景使用");
            }
        }

        static void EnsureDir()
        {
            if (!AssetDatabase.IsValidFolder("Packages/com.collin.stealth/Sample/Prefabs"))
            {
                AssetDatabase.CreateFolder("Packages/com.collin.stealth/Sample", "Prefabs");
            }
        }
    }
}
