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
    /// <para>产物：地面 + 3 掩体 + 2 名巡逻哨兵（路线/视野/决策三件套）+
    /// 玩家（WASD+蹲伏，自带跟随相机与 HUD）+ 光源 + 时钟，打开即可 Play。</para>
    /// <para><b>共享编辑器纪律</b>（本轮实测踩过，勿回退）：
    /// ①<b>不用模态对话框</b>（会把指令通道卡死）→ 提示一律走日志；
    /// ②<b>不在有未保存现场时切场景</b>→ 检测到脏场景就中止并提示，绝不丢弃别人的工作；
    /// ③<b>不访问 <c>renderer.material</c></b>（编辑态访问即报 error 并泄漏材质，破 §1.6b 归零门禁）
    /// → 用显式材质实例写进 <c>sharedMaterials</c>；
    /// ④<b>不用「删场景文件再向同路径存盘」</b>：Unity 既不允许卸载最后一个已加载场景，
    /// 也不允许覆盖同路径的已打开场景，两者叠加会让旧文件被删、新文件没写出来
    /// （示例场景凭空消失，本轮实测）。正解是<b>打开目标场景后原地 <c>SaveScene(scene)</c></b>。</para>
    /// </summary>
    internal static class StealthSampleSceneBuilder
    {
        const string SampleFolder = "Packages/com.collin.stealth/Sample";
        const string ProfileFolder = SampleFolder + "/Profiles";
        const string ScenePath = SampleFolder + "/Scenes/StealthSample.unity";

        static readonly Color CoverColor = new Color(0.82f, 0.82f, 0.86f);
        static readonly Color PlayerColor = new Color(0.2f, 0.6f, 1f);
        static readonly Color GuardColor = new Color(0.9f, 0.35f, 0.25f);
        static readonly Color ExitColor = new Color(0.35f, 0.85f, 1f);

        [MenuItem("Tools/Collin/Stealth/生成可玩示例场景")]
        public static void Build()
        {
            // 共享编辑器：有未保存现场时不切场景，避免丢弃其他会话的工作。
            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var s = SceneManager.GetSceneAt(i);
                if (s.isDirty && s.path != ScenePath)
                {
                    Debug.LogWarning("[StealthSampleBuilder] 场景 " + s.name
                                     + " 有未保存改动，已中止（不丢弃他人现场）。请先保存该场景再重试。");
                    return;
                }
            }

            var originalPath = SceneManager.GetActiveScene().path;

            EnsureFolders();
            BuildAssets();

            // ── 打开目标场景（不存在则新建），清空根物体后原地保存 ──
            var scene = File.Exists(ScenePath)
                ? EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single)
                : EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            foreach (var go in scene.GetRootGameObjects()) Object.DestroyImmediate(go);
            EditorSceneManager.SetActiveScene(scene);

            var route = AssetDatabase.LoadAssetAtPath<PatrolRoute>(ProfileFolder + "/WarehouseLoop.asset");
            var route2 = AssetDatabase.LoadAssetAtPath<PatrolRoute>(ProfileFolder + "/CrossSweep.asset");
            var profile = AssetDatabase.LoadAssetAtPath<VisionProfile>(ProfileFolder + "/GuardNormal.asset");
            if (route == null || route2 == null || profile == null)
            {
                Debug.LogError("[StealthSampleBuilder] 资产缺失，未搭建场景（route="
                               + (route != null) + " route2=" + (route2 != null)
                               + " profile=" + (profile != null) + "）");
                return;
            }

            var lightGo = new GameObject("Directional Light");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            lightGo.transform.rotation = Quaternion.Euler(45f, 30f, 0f);

            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.localScale = new Vector3(6f, 1f, 6f);   // 60×60m，覆盖出生点(z=-22)到撤离点(z=16)
            // legacy 烘焙只收 NavigationStatic 物体：地面=可行走面，掩体=障碍（实测教训：
            // 漏标静态会得到空导航，绕行校验静默不过、追击照旧穿模）
            SetNavStatic(ground);

            PlaceCube("Cover_A", new Vector3(-3f, 1.5f, 0f), new Vector3(1.6f, 3f, 1.6f));
            PlaceCube("Cover_B", new Vector3(4f, 1.5f, -2f), new Vector3(2.4f, 3f, 1.2f));
            PlaceCube("Cover_C", new Vector3(0f, 1.5f, 5f), new Vector3(1.2f, 3f, 2.4f));

            SpawnGuard("Guard_Patrol", route, profile, Vector3.zero, 0f);
            SpawnGuard("Guard_Sweeper", route2, profile, new Vector3(2f, 0f, -2f), 3f);
            BuildPlayerUnit();
            BuildExitAndRule();

            // 角色与信标必须显式非静态：CreatePrimitive 出来的子物体 isStatic 默认为 true，
            // 不处理会把哨兵自己烘成障碍，路径会绕着"活人"走（实测过的坑）。
            SetNavStaticRecursively(GameObject.Find("Player"), false);
            SetNavStaticRecursively(GameObject.Find("Guard_Patrol"), false);
            SetNavStaticRecursively(GameObject.Find("Guard_Sweeper"), false);
            SetNavStaticRecursively(GameObject.Find("StealthClock"), false);
            var beacon = GameObject.Find("Beacon");
            if (beacon != null) SetNavStaticRecursively(beacon.transform.parent.gameObject, false);

            var clockGo = new GameObject("StealthClock");
            clockGo.AddComponent<StealthClock>();

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene))     // 原地保存：无路径占用问题
            {
                Debug.LogError("[StealthSampleBuilder] 场景原地保存失败：" + ScenePath);
                return;
            }

            // ── 导航网格：先存盘再烘焙（legacy bake 要求场景已落盘）──────
            var navOk = BakeNavigation();
            if (!EditorSceneManager.SaveScene(scene))
            {
                Debug.LogWarning("[StealthSampleBuilder] 烘焙后二次存盘失败，导航数据可能未写入场景");
            }

            // 把活动场景还给原现场（其他会话可能正在别的场景里工作）。
            if (!string.IsNullOrEmpty(originalPath) && originalPath != ScenePath && File.Exists(originalPath))
            {
                EditorSceneManager.OpenScene(originalPath, OpenSceneMode.Single);
            }

            Debug.Log("[StealthSampleBuilder] 可玩示例已生成：" + ScenePath
                      + (navOk ? "（导航已烘焙并通过绕行校验，哨兵追击会绕开掩体）"
                                : "（导航未通过绕行校验，追击将退回直线——见上方烘焙日志）")
                      + "\nPlay 后：WASD 相机相对移动 / Shift 跑 / Ctrl 蹲伏 / 右键拖拽转视角；"
                      + "\n哨兵警戒范围以半透明色块投在地面，走到北侧蓝色撤离圈内驻留即通关，被察觉到底则失败（R 重开）。");
        }

        /// <summary>
        /// 烘焙 legacy 导航网格并校验「真的会绕行」。
        /// <para>用 <c>UnityEditor.AI.NavMeshBuilder</c> + 内置 <c>com.unity.modules.ai</c>，
        /// <b>不引入 com.unity.ai.navigation</b>（DESIGN D-06：付费 HAVOK + 其组件按帧积分会摧毁
        /// 位姿纯函数地基）。烘焙要求场景已落盘，故流程是「存盘 → 烘焙 → 再存盘」。</para>
        /// <para>烘焙不可用（场景被占用 / 无烘焙数据）时只 <b>LogWarning 退回直线叠加</b>，
        /// 绝不报 error——本包全程零自造 error，否则破 §1.6b 归零门禁。</para>
        /// </summary>
        static bool BakeNavigation()
        {
            try
            {
                // 同步烘焙（UnityEditor.AI 的 legacy 入口）：返回即完成，可直接校验。
                // 谁参与烘焙由搭建阶段的 isStatic 决定（地面/掩体=静态，角色=非静态），
                // 不再临时停用物体——那条路径在烘焙抛异常时会把物体留在停用状态。
                UnityEditor.AI.NavMeshBuilder.BuildNavMesh();
                var ok = VerifyDetour();
                Debug.Log("[StealthSampleBuilder] 导航烘焙 "
                          + (ok ? "完成并通过绕行校验，追击会绕开掩体" : "完成但绕行校验未通过"));
                return ok;
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[StealthSampleBuilder] 导航烘焙不可用，追击退回直线叠加：" + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// 校验：从南侧出生区到北侧撤离点的路径必须<b>比直线长</b>——
        /// 掩体横在中间，直穿说明障碍没参与烘焙，那和没有导航一样（用户反馈的穿模）。
        /// <para>每一步失败都<b>打日志说明原因</b>：5 个早退点若静默返回 false，
        /// 就只能靠猜来定位（本轮实测教训）。</para>
        /// </summary>
        static bool VerifyDetour()
        {
            const string Tag = "[StealthSampleBuilder][Nav] ";

            if (!UnityEngine.AI.NavMesh.SamplePosition(new Vector3(0f, 0.1f, -14f), out var start,
                    4f, UnityEngine.AI.NavMesh.AllAreas))
            {
                Debug.LogWarning(Tag + "起点吸不到导航面 (0,-14)——导航网格可能没烘出来或地面未参与烘焙");
                return false;
            }
            if (!UnityEngine.AI.NavMesh.SamplePosition(new Vector3(0f, 0.1f, 14f), out var goal,
                    4f, UnityEngine.AI.NavMesh.AllAreas))
            {
                Debug.LogWarning(Tag + "终点吸不到导航面 (0,14)——撤离区一侧不可达");
                return false;
            }

            var path = new UnityEngine.AI.NavMeshPath();
            if (!UnityEngine.AI.NavMesh.CalculatePath(start.position, goal.position,
                    UnityEngine.AI.NavMesh.AllAreas, path))
            {
                Debug.LogWarning(Tag + "CalculatePath 返回 false");
                return false;
            }
            if (path.status != UnityEngine.AI.NavMeshPathStatus.PathComplete)
            {
                Debug.LogWarning(Tag + "路径状态=" + path.status + "（非 Complete，两点不连通）");
                return false;
            }

            var corners = path.corners;
            if (corners == null || corners.Length < 2)
            {
                Debug.LogWarning(Tag + "拐点数=" + (corners == null ? -1 : corners.Length));
                return false;
            }

            var direct = Vector3.Distance(start.position, goal.position);
            var along = 0f;
            for (var i = 1; i < corners.Length; i++) along += Vector3.Distance(corners[i - 1], corners[i]);

            // 判据用<b>横向偏离</b>而非总长增量：28m 路径绕开 1.1m 的掩体，总长只多 0.13m，
            // 用它判定会把「已经绕行」误判成「没绕行」（本轮实测踩过）。
            var line = goal.position - start.position;
            var len = line.magnitude;
            var maxLateral = 0f;
            if (len > 1e-3f)
            {
                var dir = line / len;
                for (var i = 1; i < corners.Length - 1; i++)   // 首尾不算
                {
                    var v = corners[i] - start.position;
                    var along1 = Vector3.Dot(v, dir);
                    var lateral = (v - dir * along1).magnitude;
                    if (lateral > maxLateral) maxLateral = lateral;
                }
            }

            Debug.Log(Tag + "起点=" + Fmt(start.position) + " 终点=" + Fmt(goal.position)
                      + " 拐点=" + corners.Length + " 直线=" + direct.ToString("0.00")
                      + " 路径=" + along.ToString("0.00")
                      + " 最大横向偏离=" + maxLateral.ToString("0.00"));

            // 拐点横向偏离 > 0.4m 才算真的绕开了（≈ 半个掩体宽 + 导航腐蚀）。
            if (maxLateral <= 0.4f)
            {
                Debug.LogWarning(Tag + "路径未绕行（横向偏离过小）⇒ 掩体没成为障碍：检查掩体 isStatic 与碰撞体");
                return false;
            }
            return true;
        }

        static string Fmt(Vector3 v)
        {
            return "(" + v.x.ToString("0.0") + "," + v.y.ToString("0.0") + "," + v.z.ToString("0.0") + ")";
        }

        /// <summary>
        /// 标为静态：legacy 导航烘焙只收静态物体（地面=可行走面，掩体=障碍）。
        /// 漏标会得到空导航网格 ⇒ 绕行校验静默不过、追击照旧穿模（本包实测踩过的坑）。
        /// <c>GameObject.isStatic</c> 在编辑器侧即「全部静态位」，包含导航静态。
        /// </summary>
        static void SetNavStatic(GameObject go)
        {
            go.isStatic = true;
        }

        /// <summary>递归设置静态标记（角色层级含默认静态的图元子物体，必须整体关掉）。</summary>
        static void SetNavStaticRecursively(GameObject go, bool value)
        {
            if (go == null) return;
            go.isStatic = value;
            for (var i = 0; i < go.transform.childCount; i++)
            {
                SetNavStaticRecursively(go.transform.GetChild(i).gameObject, value);
            }
        }

        /// <summary>
        /// 撤离区 + 对局规则：补上「游戏有终点」这一环。
        /// 撤离点放在<b>巡逻路线之外</b>的北侧（z=+16），玩家必须绕过或穿过哨兵视野才能到，
        /// 这样潜行才有目标；区域本身用 Gizmos 画圈 + 一根立柱标示。
        /// </summary>
        static void BuildExitAndRule()
        {
            var exitGo = new GameObject("ExitZone");
            exitGo.transform.position = new Vector3(0f, 0f, 16f);
            var zone = exitGo.AddComponent<StealthExitZone>();
            SetSerializedField(zone, "_radius", 2.4f);
            SetSerializedField(zone, "_dwellSeconds", 0.6f);

            // 可见标记：半透明立柱，远处也能看见撤离点在哪
            var beacon = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            beacon.name = "Beacon";
            beacon.transform.SetParent(exitGo.transform, false);
            beacon.transform.localPosition = new Vector3(0f, 2.2f, 0f);
            beacon.transform.localScale = new Vector3(0.5f, 2.2f, 0.5f);
            Object.DestroyImmediate(beacon.GetComponent<Collider>());   // 不挡路
            Tint(beacon, ExitColor);

            var ruleGo = new GameObject("StealthGameRule");
            var rule = ruleGo.AddComponent<StealthGameRule>();
            var player = GameObject.Find("Player");
            if (player != null)
            {
                SetSerializedField(rule, "_player", player.GetComponent<DetectionState>());
            }
        }

        /// <summary>
        /// 玩家单元：Player（根） ├ CapsuleVisual ├ FollowCamera └ Hud。
        /// 相机与 HUD 做成子物体，是为了让预制件<b>拖进任意空场景就能玩</b>（D-13）：
        /// 若它们是场景平级物体，烘进预制件后引用会出场景即丢失，空场景里表现为黑屏 + 无 HUD。
        /// </summary>
        static void BuildPlayerUnit()
        {
            var player = new GameObject("Player");
            // 出生点必须在巡逻视距（14m）之外：实测放 (0,0,-16) 时站着不动约 30s 即被
            // 南侧路点看见并抓获（outcome=Caught），演示不公平。z=-22 到南边 waypoints
            // 的垂直距离 16>14，静置安全；通往北侧撤离点 (0,16) 仍要穿过巡逻区。
            player.transform.position = new Vector3(0f, 0.05f, -22f);
            var cc = player.AddComponent<CharacterController>();
            cc.height = 1.8f;
            cc.center = new Vector3(0f, 0.9f, 0f);

            var playerBody = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            playerBody.name = "CapsuleVisual";
            playerBody.transform.SetParent(player.transform, false);
            playerBody.transform.localPosition = new Vector3(0f, 0.9f, 0f);
            Object.DestroyImmediate(playerBody.GetComponent<Collider>());  // 碰撞交给 CharacterController
            Tint(playerBody, PlayerColor);

            var detection = player.AddComponent<DetectionState>();
            var controller = player.AddComponent<PlayerStealthController>();

            var camGo = new GameObject("FollowCamera");
            camGo.transform.SetParent(player.transform, false);
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.Skybox;
            camGo.AddComponent<AudioListener>();
            SetSerializedField(controller, "_followCamera", cam);

            var hudGo = new GameObject("Hud");
            hudGo.transform.SetParent(player.transform, false);
            var hud = hudGo.AddComponent<StealthHud>();
            SetSerializedField(hud, "_target", detection);
        }

        /// <summary>
        /// 哨兵单元：<c>name</c>（静态根，预制件的根） ├ <c>RouteRoot</c>（静态，路线参考系）
        /// └ <c>Body</c>（被求值驱动的本体，挂 Agent/Vision/Brain）。
        /// <para>为什么这么分层：若把 <c>PatrolRouteRoot</c> 挂在被驱动的本体上，本体的每帧位姿会带动
        /// 路线根、路线又反过来决定位姿，形成循环（M3 立「反应永不回流基线」纪律时避开的同类问题）。
        /// 让两者成为兄弟、共同挂在一个静态根下，既断环，又让整个单元可以原样烘进预制件。</para>
        /// </summary>
        static void SpawnGuard(string name, PatrolRoute route, VisionProfile profile,
                               Vector3 bodyOffset, float phase)
        {
            var go = new GameObject(name);                      // 静态单元根
            go.transform.position = Vector3.zero;

            var rootGo = new GameObject("RouteRoot");           // 静态参考系
            rootGo.transform.SetParent(go.transform, false);
            rootGo.transform.localPosition = Vector3.zero;
            var root = rootGo.AddComponent<PatrolRouteRoot>();
            root.SetRoute(route);

            var bodyGo = new GameObject("Body");                // 被驱动本体
            bodyGo.transform.SetParent(go.transform, false);
            bodyGo.transform.localPosition = bodyOffset;

            var agent = bodyGo.AddComponent<PatrolAgent>();
            SetSerializedField(agent, "_routeRoot", root);
            SetSerializedField(agent, "_phaseOffset", phase);

            var vision = bodyGo.AddComponent<VisionVolume>();
            SetSerializedField(vision, "_profile", profile);

            bodyGo.AddComponent<GuardBrain>();
            bodyGo.AddComponent<StealthGroundProjection>();

            var visual = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            visual.name = "CapsuleVisual";
            visual.transform.SetParent(bodyGo.transform, false);
            visual.transform.localPosition = new Vector3(0f, 0.9f, 0f);
            Object.DestroyImmediate(visual.GetComponent<Collider>());
            Tint(visual, GuardColor);
        }

        static void PlaceCube(string name, Vector3 center, Vector3 scale)
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = name;
            cube.transform.position = center;
            cube.transform.localScale = scale;
            SetNavStatic(cube);
            Tint(cube, CoverColor);
        }

        /// <summary>
        /// 给基元上色。<b>禁止用 <c>renderer.material.color</c></b>：编辑态访问 <c>.material</c>
        /// 会即时实例化材质并打印 error（材质泄漏 + 破 §1.6b 归零门禁）。
        /// 这里显式建一份材质写进 <c>sharedMaterials</c>，效果一致、零报错。
        /// </summary>
        static void Tint(GameObject go, Color color)
        {
            var r = go.GetComponent<Renderer>();
            if (r == null) return;

            var mats = r.sharedMaterials;
            if (mats == null || mats.Length == 0 || mats[0] == null) return;

            mats[0] = new Material(mats[0]) { color = color };
            r.sharedMaterials = mats;
        }

        /// <summary>建/重建三个共享 Model 资产（路线 ×2 + 视野参数）。</summary>
        static void BuildAssets()
        {
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

            if (route == null) Debug.LogError("[StealthSampleBuilder] WarehouseLoop 资产加载失败");
            if (route2 == null) Debug.LogError("[StealthSampleBuilder] CrossSweep 资产加载失败");

            var profilePath = ProfileFolder + "/GuardNormal.asset";
            var existing = AssetDatabase.LoadAssetAtPath<VisionProfile>(profilePath);
            var profile = existing != null ? existing : ScriptableObject.CreateInstance<VisionProfile>();
            if (existing == null)
            {
                profile.name = "GuardNormal";
                AssetDatabase.CreateAsset(profile, profilePath);
                profile = AssetDatabase.LoadAssetAtPath<VisionProfile>(profilePath);
            }

            Undo.RecordObject(profile, "初始化视野参数");
            profile.SetHorizontalAngle(110f);
            profile.SetMaxDistance(14f);
            profile.SetEyeHeight(1.7f);
            profile.SetSuspicionGain(0.45f);
            profile.SetDecayPerSecond(0.3f);
            profile.SetSuspicionThreshold(0.35f);
            profile.SetAlertThreshold(0.8f);
            profile.SetLightSensitivity(0f);
            profile.SetProximityRadius(1.8f);      // 贴脸必然察觉：修「重叠后丧失目标」
            profile.SetOccluderMask(-1);           // 全层可遮挡：掩体真的能挡视线
            EditorUtility.SetDirty(profile);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        /// <summary>建路线资产并返回<b>持久化</b>实例（已存在则原地改写，不重导 GUID）。</summary>
        static PatrolRoute CreateRoute(string assetName, string path, PatrolLoopMode mode,
                                       float speed, Vector3[] points, float[] stops)
        {
            var existing = AssetDatabase.LoadAssetAtPath<PatrolRoute>(path);
            PatrolRoute route;
            if (existing != null)
            {
                route = existing;
            }
            else
            {
                route = ScriptableObject.CreateInstance<PatrolRoute>();
                route.name = assetName;
                AssetDatabase.CreateAsset(route, path);
                route = AssetDatabase.LoadAssetAtPath<PatrolRoute>(path);
                if (route == null) return null;
            }

            Undo.RecordObject(route, "初始化路线");
            for (var i = route.WaypointCount - 1; i >= 0; i--) route.RemoveWaypointAt(i);
            route.SetDefaultSpeed(speed);
            route.SetLoopMode(mode);
            for (var i = 0; i < points.Length; i++) route.AddWaypoint(points[i], stops[i]);
            EditorUtility.SetDirty(route);
            return route;
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
    }
}
