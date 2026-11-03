using System.IO;
using Collin.Stealth;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Collin.Stealth.EditorTooling
{
    /// <summary>
    /// 一键生成可玩白盒示例（AGENTS.md §0：演示归包内 Sample/）。
    /// 菜单：Tools ▸ Collin ▸ Stealth ▸ 生成可玩示例场景
    /// <para>产物：地面 + 掩体 + 2 名巡逻哨兵（路线/视野/决策三件套）+
    /// 玩家（WASD+蹲伏）+ 第三人称相机 + 光源 + HUD。
    /// 资产落在 <c>Packages/com.collin.stealth/Sample/</c> 下，场景可直接 Play。</para>
    /// <para>幂等：重复执行会先删除旧产物再重建，保证「生成 → 玩」这条路径随时可复现。</para>
    /// </summary>
    internal static class StealthSampleSceneBuilder
    {
        const string SampleFolder = "Packages/com.collin.stealth/Sample";
        const string ProfileFolder = SampleFolder + "/Profiles";
        const string ScenePath = SampleFolder + "/Scenes/StealthSample.unity";

        [MenuItem("Tools/Collin/Stealth/生成可玩示例场景")]
        public static void Build()
        {
            // 共享编辑器纪律：
            // ①不用模态对话框（会一直等人点确定并把指令通道卡死）；
            // ②不用 NewScene(Single)（会丢弃其他会话未保存的现场）。
            // 改用 Additive 建一个内存场景，建完存盘再关掉——全程不碰别人的场景。
            var originalActive = SceneManager.GetActiveScene();

            EnsureFolders();
            CleanupOldAssets();
            AssetDatabase.Refresh();

            // ── 资产：路线 + 视野参数（建完必须重新 Load 拿<b>持久化</b>实例，
            //    否则赋进场景的是临时对象，保存后引用会变成 None）────────
            var route = CreateRoute("WarehouseLoop", ProfileFolder + "/WarehouseLoop.asset",
                PatrolLoopMode.Loop, 2f,
                new[] { new Vector3(-6f, 0f, -6f), new Vector3(6f, 0f, -6f),
                        new Vector3(6f, 0f, 6f), new Vector3(-6f, 0f, 6f) },
                new[] { 1f, 1f, 1f, 1f });

            var route2 = CreateRoute("CrossSweep", ProfileFolder + "/CrossSweep.asset",
                PatrolLoopMode.PingPong, 2.4f,
                new[] { new Vector3(-9f, 0f, 2f), new Vector3(0f, 0f, 8f),
                        new Vector3(9f, 0f, 2f) },
                new[] { 1.5f, 0.5f, 1.5f });

            var profilePath = ProfileFolder + "/GuardNormal.asset";
            var profileInstance = ScriptableObject.CreateInstance<VisionProfile>();
            profileInstance.name = "GuardNormal";
            profileInstance.SetHorizontalAngle(110f);
            profileInstance.SetMaxDistance(14f);
            profileInstance.SetEyeHeight(1.7f);
            profileInstance.SetSuspicionGain(0.45f);
            profileInstance.SetDecayPerSecond(0.3f);
            profileInstance.SetSuspicionThreshold(0.35f);
            profileInstance.SetAlertThreshold(0.8f);
            profileInstance.SetLightSensitivity(0f);
            AssetDatabase.CreateAsset(profileInstance, profilePath);

            var profile = AssetDatabase.LoadAssetAtPath<VisionProfile>(profilePath);
            AssetDatabase.SaveAssets();

            // ── 场景（Additive 空场景，只存在于内存，存盘后关闭）────
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            EditorSceneManager.SetActiveScene(scene);

            // 光
            var lightGo = new GameObject("Directional Light");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            lightGo.transform.rotation = Quaternion.Euler(45f, 30f, 0f);

            // 地面
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.localScale = new Vector3(4f, 1f, 4f);

            // 掩体（Default 层即可——遮挡层掩码默认全层）
            PlaceCube("Cover_A", new Vector3(-3f, 1.5f, 0f), new Vector3(1.6f, 3f, 1.6f));
            PlaceCube("Cover_B", new Vector3(4f, 1.5f, -2f), new Vector3(2.4f, 3f, 1.2f));
            PlaceCube("Cover_C", new Vector3(0f, 1.5f, 5f), new Vector3(1.2f, 3f, 2.4f));

            // 哨兵 ×2
            SpawnGuard("Guard_Patrol", route, profile, Vector3.zero, 0f, 0f);
            SpawnGuard("Guard_Sweeper", route2, profile, new Vector3(2f, 0f, -2f), 3f, 4f);

            // 玩家 + 相机 + HUD
            var player = new GameObject("Player");
            player.transform.position = new Vector3(0f, 0.05f, -16f);
            var cc = player.AddComponent<CharacterController>();
            cc.height = 1.8f;
            cc.center = new Vector3(0f, 0.9f, 0f);
            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "CapsuleVisual";
            body.transform.SetParent(player.transform, false);
            body.transform.localPosition = new Vector3(0f, 0.9f, 0f);
            Object.DestroyImmediate(body.GetComponent<Collider>());   // 碰撞交给 CharacterController
            body.GetComponent<Renderer>().material.color = new Color(0.2f, 0.6f, 1f);

            var detection = player.AddComponent<DetectionState>();
            var controller = player.AddComponent<PlayerStealthController>();

            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.Skybox;

            var hudGo = new GameObject("StealthHud");
            var hud = hudGo.AddComponent<StealthHud>();
            SetSerializedField(hud, "_target", detection);

            // 时钟（全系统唯一时间源）
            var clockGo = new GameObject("StealthClock");
            clockGo.AddComponent<StealthClock>();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);

            // 收尾：关掉临时场景、把活动场景还给原现场，编辑器状态与执行前一致。
            EditorSceneManager.CloseScene(scene, true);
            if (originalActive.IsValid()) EditorSceneManager.SetActiveScene(originalActive);

            Debug.Log("[StealthSampleBuilder] 可玩示例已生成：" + ScenePath +
                      "\n打开它按 Play 即可：WASD 移动、Ctrl 蹲伏，走进哨兵视野会被发现。");
        }

        static void SpawnGuard(string name, PatrolRoute route, VisionProfile profile,
                               Vector3 bodyOffset, float phase, float extraDelay)
        {
            var go = new GameObject(name);
            go.transform.position = bodyOffset;

            var rootGo = new GameObject(name + "_RouteRoot");
            rootGo.transform.position = Vector3.zero;
            var root = rootGo.AddComponent<PatrolRouteRoot>();
            root.SetRoute(route);

            var agent = go.AddComponent<PatrolAgent>();
            SetSerializedField(agent, "_routeRoot", root);
            SetSerializedField(agent, "_phaseOffset", phase);

            var vision = go.AddComponent<VisionVolume>();
            SetSerializedField(vision, "_profile", profile);

            var brain = go.AddComponent<GuardBrain>();

            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "CapsuleVisual";
            body.transform.SetParent(go.transform, false);
            body.transform.localPosition = new Vector3(0f, 0.9f, 0f);
            Object.DestroyImmediate(body.GetComponent<Collider>());
            body.GetComponent<Renderer>().material.color = new Color(0.9f, 0.35f, 0.25f);
        }

        static void PlaceCube(string name, Vector3 center, Vector3 scale)
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = name;
            cube.transform.position = center;
            cube.transform.localScale = scale;
        }

        /// <summary>建路线资产并返回<b>持久化</b>实例。</summary>
        static PatrolRoute CreateRoute(string assetName, string path, PatrolLoopMode mode,
                                       float speed, Vector3[] points, float[] stops)
        {
            var instance = ScriptableObject.CreateInstance<PatrolRoute>();
            instance.name = assetName;
            instance.SetDefaultSpeed(speed);
            instance.SetLoopMode(mode);
            for (var i = 0; i < points.Length; i++)
            {
                instance.AddWaypoint(points[i], stops[i]);
            }
            AssetDatabase.CreateAsset(instance, path);
            return AssetDatabase.LoadAssetAtPath<PatrolRoute>(path);
        }

        /// <summary>
        /// 写仅存在于编辑器序列化字段的引用（这些字段是 private，无公开 setter）。
        /// 用 SerializedObject 而非反射：走 Unity 序列化通道，字段名写错会直接报错而不是静默丢引用。
        /// </summary>
        static void SetSerializedField(Component targetComponent, string fieldName, object value)
        {
            var so = new SerializedObject(targetComponent);
            var prop = so.FindProperty(fieldName);
            if (prop == null)
            {
                Debug.LogError("[StealthSampleBuilder] " + targetComponent.GetType().Name
                               + " 上找不到序列化字段 " + fieldName);
                return;
            }

            switch (prop.propertyType)
            {
                case SerializedPropertyType.ObjectReference:
                    prop.objectReferenceValue = value as Object;
                    break;
                case SerializedPropertyType.Float:
                    prop.floatValue = System.Convert.ToSingle(value);
                    break;
                case SerializedPropertyType.Integer:
                    prop.intValue = System.Convert.ToInt32(value);
                    break;
                case SerializedPropertyType.Boolean:
                    prop.boolValue = System.Convert.ToBoolean(value);
                    break;
                default:
                    Debug.LogError("[StealthSampleBuilder] 字段 " + fieldName
                                   + " 类型未支持：" + prop.propertyType);
                    return;
            }

            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void EnsureFolders()
        {
            if (!AssetDatabase.IsValidFolder(SampleFolder))
            {
                Directory.CreateDirectory(
                    Path.Combine(
                        Directory.GetParent(Application.dataPath).FullName,
                        "Packages/com.collin.stealth/Sample"));
                AssetDatabase.Refresh();
            }
            if (!AssetDatabase.IsValidFolder(SampleFolder + "/Scenes"))
                AssetDatabase.CreateFolder(SampleFolder, "Scenes");
            if (!AssetDatabase.IsValidFolder(ProfileFolder))
                AssetDatabase.CreateFolder(SampleFolder, "Profiles");
        }

        static void CleanupOldAssets()
        {
            DeleteIfExists(ProfileFolder + "/WarehouseLoop.asset");
            DeleteIfExists(ProfileFolder + "/CrossSweep.asset");
            DeleteIfExists(ProfileFolder + "/GuardNormal.asset");
            DeleteIfExists(ScenePath);
        }

        static void DeleteIfExists(string path)
        {
            if (AssetDatabase.LoadMainAssetAtPath(path) != null) AssetDatabase.DeleteAsset(path);
        }
    }
}
