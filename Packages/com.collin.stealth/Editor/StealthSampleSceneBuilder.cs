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
        static readonly Color WallColor = new Color(0.62f, 0.64f, 0.7f);
        static readonly Color FenceColor = new Color(0.42f, 0.45f, 0.52f);
        static readonly Color PlayerColor = new Color(0.2f, 0.6f, 1f);
        static readonly Color GuardColor = new Color(0.9f, 0.35f, 0.25f);
        static readonly Color ExitColor = new Color(0.35f, 0.85f, 1f);

        // 场地尺寸（围栏内腔半宽）：玩家控制器的硬边界与此一致，改一处必须同步另一处
        internal const float ArenaHalf = 27f;
        // 出生点：必须在巡逻视距（14m）之外——实测放 (0,0,-16) 时站着不动约 30s 即被
        // 南侧路点看见并抓获，演示不公平。z=-22 到南边 waypoints 垂直距离 16>14，静置安全。
        static readonly Vector3 SpawnPos = new Vector3(0f, 0.05f, -22f);
        const float FenceHeight = 4f;
        const float FenceThickness = 1f;

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

            s_obstacles.Clear();      // 生成器可重复执行，残留旧坐标会让自检假阳性
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
            var route3 = AssetDatabase.LoadAssetAtPath<PatrolRoute>(ProfileFolder + "/DepotRing.asset");
            var profile = AssetDatabase.LoadAssetAtPath<VisionProfile>(ProfileFolder + "/GuardNormal.asset");
            if (route == null || route2 == null || route3 == null || profile == null)
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
            ground.transform.localScale = new Vector3(6f, 1f, 6f);   // 60×60m，围栏内腔 54×54
            // legacy 烘焙只收 NavigationStatic 物体：地面=可行走面，掩体=障碍（实测教训：
            // 漏标静态会得到空导航，绕行校验静默不过、追击照旧穿模）
            SetNavStatic(ground);

            BuildFence();

            // ── 中部掩体：横在出生区→撤离点的必经线上（绕行校验依赖它）──────
            // Cover_C 的 z 必须避开巡逻方框的 z=±6 边线与横扫路线（实测：原先放 z=5 时
            // 边线正好压进掩体里，哨兵会当众穿过方块——巡逻基线不参与碰撞，只能靠摆位规避）
            PlaceCube("Cover_A", new Vector3(-3f, 1.5f, 0f), new Vector3(1.6f, 3f, 1.6f));
            PlaceCube("Cover_B", new Vector3(4f, 1.5f, -2f), new Vector3(2.4f, 3f, 1.2f));
            PlaceCube("Cover_C", new Vector3(0f, 1.5f, 3f), new Vector3(1.2f, 3f, 2.4f));

            // ── 南侧掩体群：出生区到中段之间不再是空旷直路（原稿这里什么都没有，
            //    玩家只能直线冲锋，潜行玩法不成立）
            PlaceCube("Crate_S1", new Vector3(-4.5f, 1f, -16f), new Vector3(2.4f, 2f, 2.4f));
            PlaceCube("Crate_S2", new Vector3(5f, 0.7f, -13f), new Vector3(1.8f, 1.4f, 1.8f));
            PlaceCube("Crate_S3", new Vector3(-8f, 1.3f, -10f), new Vector3(1.4f, 2.6f, 3f));
            PlaceCube("Crate_S4", new Vector3(10f, 1f, -18f), new Vector3(3f, 2f, 1.6f));

            // ── 西侧长墙：切断东西向通视，逼出「沿墙摸进」这条路线
            PlaceCube("Wall_West", new Vector3(-12f, 1.5f, 4f), new Vector3(1f, 3f, 14f), WallColor);

            // ── 仓库（东北角）：四面墙 + 南向门洞，可进可守
            //    门洞 x∈[14.1,17.9]（净宽 3.8m）。原本留 1.4m 太窄：legacy 烘焙会按角色半径
            //    腐蚀可行走面，窄缺口容易被腐蚀成不连通，导致「看得见进不去」。
            PlaceCube("Shed_N", new Vector3(16f, 1.5f, 19.6f), new Vector3(9f, 3f, 0.8f), WallColor);
            PlaceCube("Shed_SL", new Vector3(12.8f, 1.5f, 12.4f), new Vector3(2.6f, 3f, 0.8f), WallColor);
            PlaceCube("Shed_SR", new Vector3(19.2f, 1.5f, 12.4f), new Vector3(2.6f, 3f, 0.8f), WallColor);
            PlaceCube("Shed_E", new Vector3(20.6f, 1.5f, 16f), new Vector3(0.8f, 3f, 7.2f), WallColor);
            PlaceCube("Shed_W", new Vector3(11.4f, 1.5f, 16f), new Vector3(0.8f, 3f, 7.2f), WallColor);
            PlaceCube("Crate_In", new Vector3(13.5f, 0.75f, 17.5f), new Vector3(2f, 1.5f, 2f));

            // ── 撤离区立柱：远处可见地标，兼作最后一段遮挡
            PlaceCube("Pillar_E", new Vector3(20f, 1.5f, 2f), new Vector3(1.2f, 3f, 1.2f));
            PlaceCube("Pillar_W", new Vector3(-6f, 1.5f, 18f), new Vector3(1.2f, 3f, 1.2f));

            SpawnGuard("Guard_Patrol", route, profile, Vector3.zero, 0f);
            SpawnGuard("Guard_Sweeper", route2, profile, new Vector3(2f, 0f, -2f), 3f);
            // 偏移必须为 0：本体位置 = 环线 + 偏移，若给正偏移会把整条环线平移到 x 33.5，
            // 直接穿出围栏内腔（半宽 27）——巡逻基线不参与碰撞，表现为哨兵穿过东墙。
            SpawnGuard("Guard_Shed", route3, profile, Vector3.zero, 1.5f);
            BuildPlayerUnit();
            BuildExitAndRule();

            var clockGo = new GameObject("StealthClock");
            clockGo.AddComponent<StealthClock>();

            // 角色与信标必须显式非静态：CreatePrimitive 出来的子物体 isStatic 默认为 true，
            // 不处理会把哨兵自己烘成障碍，路径会绕着"活人"走（实测过的坑）。
            // 统一放在全部单元建好之后，避免边建边标漏掉某个单元。
            SetNavStaticRecursively(GameObject.Find("Player"), false);
            SetNavStaticRecursively(GameObject.Find("Guard_Patrol"), false);
            SetNavStaticRecursively(GameObject.Find("Guard_Sweeper"), false);
            SetNavStaticRecursively(GameObject.Find("Guard_Shed"), false);
            SetNavStaticRecursively(clockGo, false);
            var zones = UnityEngine.Object.FindObjectsByType<StealthExitZone>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (var i = 0; i < zones.Length; i++)
            {
                if (zones[i] != null) SetNavStaticRecursively(zones[i].gameObject, false);
            }

            // 布局自检：巡逻基线不参与碰撞，路线一旦压在实体墙上就会「哨兵当众穿墙」
            //（用户上一轮反馈的穿模观感，换了个位置复现）。这里先查，别等肉眼发现。
            VerifyRouteClearance(route, route2, route3);

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
                      + "\n场地：54×54 四面围栏 + 南侧掩体群 + 西侧长墙 + 东北角仓库（南向门洞）+ 3 名巡逻哨兵"
                      + "\nPlay 后：WASD 相机相对移动 / Shift 跑 / Ctrl 蹲伏 / 右键拖拽转视角"
                      + "\n通关：抵达北侧蓝色撤离圈（驻留 0.6s）或仓库内备用撤离圈（驻留 1.2s），任达其一"
                      + "\n失败：被察觉到底格并持续注视；出结果后弹结算卡片，R 或点按钮重开、Esc 关闭"
                      + "\n出生点 " + Fmt(SpawnPos) + "，场地硬边界半宽 " + ArenaHalf
                      + "（走出围栏会被钳回，不会掉进地图外）");
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
        /// 校验：从<b>实际出生点</b>到<b>场景里每一处撤离区</b>都必须①导航可达 ②真的绕行。
        /// <para>两点原因：</para>
        /// <list type="bullet">
        /// <item>旧版把起终点写成硬编码 (0,±14)，而出生点在 (0,-22)、撤离点在 (0,24) 与 (16,16)
        /// ——布局一改，校验量的就是<b>地图上不存在的位置</b>，照样"通过"，等于没校验。
        /// 现在起终点都从场景真实物体取。</item>
        /// <item>双撤离点后，「可达」成了独立的玩法要求：仓库那处要穿门洞，
        /// 门洞被导航腐蚀封死的话玩家看得见进不去，只有逐点算路径才查得出来。</item>
        /// </list>
        /// <para><b>绕行判据只对主撤离点强制</b>（<see cref="RequireDetourName"/>）：
        /// 它正南方有掩体横在直线上，直穿说明障碍没参与烘焙。仓库撤离点走的是东侧走廊，
        /// 直线本就通畅，要求它绕行反而会把好布局误报成失败。</para>
        /// <para>每一步失败都<b>打日志说明原因</b>：早退点若静默返回 false，只能靠猜定位（本包实测教训）。</para>
        /// </summary>
        static bool VerifyDetour()
        {
            const string Tag = "[StealthSampleBuilder][Nav] ";
            const string RequireDetourName = "ExitZone_North";

            if (!UnityEngine.AI.NavMesh.SamplePosition(new Vector3(SpawnPos.x, 0.1f, SpawnPos.z),
                    out var start, 6f, UnityEngine.AI.NavMesh.AllAreas))
            {
                Debug.LogWarning(Tag + "出生点 " + Fmt(SpawnPos)
                                 + " 吸不到导航面——导航网格可能没烘出来或地面未参与烘焙");
                return false;
            }

            var zones = UnityEngine.Object.FindObjectsByType<StealthExitZone>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            if (zones.Length == 0)
            {
                Debug.LogWarning(Tag + "场景里没有 StealthExitZone，无从校验可达性");
                return false;
            }

            var allOk = true;
            for (var z = 0; z < zones.Length; z++)
            {
                var mustDetour = zones[z].name == RequireDetourName;
                if (!VerifyOneDetour(start.position, zones[z], Tag, mustDetour)) allOk = false;
            }
            return allOk;
        }

        /// <summary>单条「出生点 → 某撤离区」的可达（+可选绕行）校验。失败只报 Warning 并返回 false。</summary>
        static bool VerifyOneDetour(Vector3 from, StealthExitZone zone, string Tag, bool mustDetour)
        {
            var center = zone.Center;
            if (!UnityEngine.AI.NavMesh.SamplePosition(new Vector3(center.x, 0.1f, center.z),
                    out var goal, 6f, UnityEngine.AI.NavMesh.AllAreas))
            {
                Debug.LogWarning(Tag + zone.name + " 中心 " + Fmt(center)
                                 + " 吸不到导航面 ⇒ 该撤离点不可达（检查是否被墙围死）");
                return false;
            }

            var path = new UnityEngine.AI.NavMeshPath();
            if (!UnityEngine.AI.NavMesh.CalculatePath(from, goal.position,
                    UnityEngine.AI.NavMesh.AllAreas, path))
            {
                Debug.LogWarning(Tag + zone.name + " CalculatePath 返回 false");
                return false;
            }
            if (path.status != UnityEngine.AI.NavMeshPathStatus.PathComplete)
            {
                Debug.LogWarning(Tag + zone.name + " 路径状态=" + path.status
                                 + "（非 Complete，两点不连通）");
                return false;
            }

            var corners = path.corners;
            if (corners == null || corners.Length < 2)
            {
                Debug.LogWarning(Tag + zone.name + " 拐点数=" + (corners == null ? -1 : corners.Length));
                return false;
            }

            var direct = Vector3.Distance(from, goal.position);
            var along = 0f;
            for (var i = 1; i < corners.Length; i++) along += Vector3.Distance(corners[i - 1], corners[i]);

            // 判据用<b>横向偏离</b>而非总长增量：28m 路径绕开 1.1m 的掩体，总长只多 0.13m，
            // 用它判定会把「已经绕行」误判成「没绕行」（本轮实测踩过）。
            var line = goal.position - from;
            var len = line.magnitude;
            var maxLateral = 0f;
            if (len > 1e-3f)
            {
                var dir = line / len;
                for (var i = 1; i < corners.Length - 1; i++)   // 首尾不算
                {
                    var v = corners[i] - from;
                    var along1 = Vector3.Dot(v, dir);
                    var lateral = (v - dir * along1).magnitude;
                    if (lateral > maxLateral) maxLateral = lateral;
                }
            }

            Debug.Log(Tag + zone.name + " 起点=" + Fmt(from) + " 终点=" + Fmt(goal.position)
                      + " 拐点=" + corners.Length + " 直线=" + direct.ToString("0.00")
                      + " 路径=" + along.ToString("0.00")
                      + " 最大横向偏离=" + maxLateral.ToString("0.00"));

            // 拐点横向偏离 > 0.4m 才算真的绕开了（≈ 半个掩体宽 + 导航腐蚀）。
            if (mustDetour && maxLateral <= 0.4f)
            {
                Debug.LogWarning(Tag + zone.name + " 路径未绕行（横向偏离过小）"
                                 + "⇒ 挡在直线上的掩体没成为障碍：检查其 isStatic 与碰撞体");
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
        /// <para><b>两处撤离点</b>（本轮复杂化）：北侧主撤离点 + 仓库旁备用撤离点，
        /// 任达其一即通关。单点会让路线只有一种解法，新增的仓库与西墙结构对玩法毫无影响；
        /// 双点才逼出「直线冲北」与「绕东穿仓库」的真实取舍。</para>
        /// <para>区域本身用 Gizmos 画圈 + 立柱标示，立柱不带碰撞（不挡路）。</para>
        /// </summary>
        static void BuildExitAndRule()
        {
            // z=24.0 而不是 22：仓库环线（DepotRing）上边在 z=21.5，22 会让撤离圈压在环线正下方，
            // 哨兵走到上边线时几乎贴脸站在玩家脸上，驻留 0.6s 根本不可能完成。
            // 也不能更靠北：围栏内腔面在 z=27，圆半径 2.4 的外缘会贴到导航面具边缘被腐蚀掉。
            MakeExit("ExitZone_North", new Vector3(0f, 0f, 24f), 2.4f, 0.6f);
            MakeExit("ExitZone_Depot", new Vector3(16f, 0f, 16f), 2.0f, 1.2f);

            var ruleGo = new GameObject("StealthGameRule");
            var rule = ruleGo.AddComponent<StealthGameRule>();
            var player = GameObject.Find("Player");
            if (player != null)
            {
                SetSerializedField(rule, "_player", player.GetComponent<DetectionState>());
            }
        }

        /// <summary>建一处撤离区（圆判定 + 可见立柱）。</summary>
        static void MakeExit(string name, Vector3 center, float radius, float dwell)
        {
            var exitGo = new GameObject(name);
            exitGo.transform.position = center;
            var zone = exitGo.AddComponent<StealthExitZone>();
            SetSerializedField(zone, "_radius", radius);
            SetSerializedField(zone, "_dwellSeconds", dwell);

            // 可见标记：立柱，远处也能看见撤离点在哪
            var beacon = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            beacon.name = "Beacon";
            beacon.transform.SetParent(exitGo.transform, false);
            beacon.transform.localPosition = new Vector3(0f, 2.2f, 0f);
            beacon.transform.localScale = new Vector3(0.5f, 2.2f, 0.5f);
            Object.DestroyImmediate(beacon.GetComponent<Collider>());   // 不挡路
            Tint(beacon, ExitColor);
        }

        /// <summary>
        /// 玩家单元：Player（根） ├ CapsuleVisual ├ FollowCamera └ Hud。
        /// 相机与 HUD 做成子物体，是为了让预制件<b>拖进任意空场景就能玩</b>（D-13）：
        /// 若它们是场景平级物体，烘进预制件后引用会出场景即丢失，空场景里表现为黑屏 + 无 HUD。
        /// </summary>
        static void BuildPlayerUnit()
        {
            var player = new GameObject("Player");
            player.transform.position = SpawnPos;
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

            // 场地硬边界：与可见围栏内腔一致（墙在 27 之外，留 0.55m 裕度避免贴墙摩擦抖动）
            SetSerializedField(controller, "_arenaCenter", Vector3.zero);
            SetSerializedField(controller, "_arenaHalfSize", new Vector2(ArenaHalf, ArenaHalf));
            SetSerializedField(controller, "_arenaMargin", 0.55f);
            // 出生点<b>不写覆盖值</b>：由控制器 Awake 捕获场景里摆好的位姿（所见即所得 §0.1.1）。
            // 写死坐标会让预制件拖到新场景后仍在示例坐标出生。

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

            // 结算面板与 HUD 同层（都是 IMGUI、零场景配置），跟着玩家预制件走
            var resultGo = new GameObject("ResultPanel");
            resultGo.transform.SetParent(player.transform, false);
            resultGo.AddComponent<StealthResultPanel>();
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
            PlaceCube(name, center, scale, CoverColor);
        }

        static void PlaceCube(string name, Vector3 center, Vector3 scale, Color color)
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = name;
            cube.transform.position = center;
            cube.transform.localScale = scale;
            SetNavStatic(cube);
            Tint(cube, color);
            s_obstacles.Add(new Bounds(center, scale));   // 登记给路线间隙自检
        }

        // 本次生成摆放的所有实体占位（围栏 + 掩体 + 仓库），供 VerifyRouteClearance 检查
        static readonly System.Collections.Generic.List<Bounds> s_obstacles
            = new System.Collections.Generic.List<Bounds>();

        /// <summary>
        /// 布局自检：把每条巡逻路线的<b>相邻路点连线</b>按 0.5m 采样，检查采样点是否落进
        /// 任何实体的 XZ 矩形内。命中 ⇒ 巡逻 NPC 会<b>当众穿过墙/箱子</b>。
        /// <para>为什么必须自动查：巡逻基线是「时间的纯函数」直接写 transform、
        /// <b>完全不参与碰撞</b>（DESIGN D-01，编辑态回溯预览靠的就是这点）。
        /// 所以墙摆错了位置不会报错、不会卡住，只会安静地穿模——
        /// 用户上一轮反馈的「NPC 抓人穿模」正是这类问题，靠肉眼在 Play 里撞见成本极高。</para>
        /// <para>失败只报 Warning（不报 error）：布局问题不该破 §1.6b 控制台归零门禁。</para>
        /// </summary>
        static void VerifyRouteClearance(params PatrolRoute[] routes)
        {
            const string Tag = "[StealthSampleBuilder][Clearance] ";
            var hits = 0;

            for (var r = 0; r < routes.Length; r++)
            {
                var route = routes[r];
                if (route == null) continue;
                var n = route.WaypointCount;
                if (n < 2) continue;
                // PingPong 是原路往返，<b>没有</b> 末点→首点 这条线段；
                // 按 Loop 一样检查会把正常的往返路线误报成穿墙（假阳性会掩盖真问题）。
                var isLoop = route.LoopMode == PatrolLoopMode.Loop;
                var segCount = isLoop ? n : n - 1;

                for (var i = 0; i < segCount; i++)
                {
                    var a = route.GetWaypointPosition(i);
                    var b = route.GetWaypointPosition((i + 1) % n);
                    var seg = b - a;
                    var len = new Vector2(seg.x, seg.z).magnitude;
                    var steps = Mathf.Max(1, Mathf.CeilToInt(len / 0.5f));
                    for (var s = 0; s <= steps; s++)
                    {
                        var p = a + seg * (s / (float)steps);
                        for (var o = 0; o < s_obstacles.Count; o++)
                        {
                            var ext = s_obstacles[o].extents;
                            var c = s_obstacles[o].center;
                            if (Mathf.Abs(p.x - c.x) <= ext.x && Mathf.Abs(p.z - c.z) <= ext.z)
                            {
                                Debug.LogWarning(Tag + route.name + " 第 " + i + "→" + ((i + 1) % n)
                                                 + " 段在 (" + p.x.ToString("0.0") + "," + p.z.ToString("0.0")
                                                 + ") 处穿过实体 " + s_obstacles[o].center);
                                hits++;
                            }
                        }
                    }
                }
            }

            if (hits == 0)
            {
                Debug.Log(Tag + "三条路线与 " + s_obstacles.Count + " 个实体占位无交叉，巡逻不会穿模");
            }
            else
            {
                Debug.LogWarning(Tag + "共 " + hits + " 处路线压到实体 ⇒ 哨兵会穿墙，请调整路点或掩体位置");
            }
        }

        /// <summary>
        /// 外侧围栏（主流做法：四面实体围墙，<b>参与导航烘焙</b>）。
        /// <para>必须标静态的原因：墙若不参与烘焙，导航网格会照常铺到墙外，
        /// NPC 会「穿墙追出去」——正是用户上一轮反馈的观感问题，只是换了个位置复现。</para>
        /// <para>围栏只是<b>可见</b>层，硬边界另由玩家控制器的 <see cref="StealthArena"/> 兜底
        /// （穿模、陡峭滑行或删墙都不会让人溜出地图）。</para>
        /// </summary>
        static void BuildFence()
        {
            var t = FenceThickness;
            var h = FenceHeight;
            var outer = ArenaHalf + t;               // 外表面半宽
            var c = ArenaHalf + t * 0.5f;            // 墙中心
            // 南北墙占满整幅（含转角），东西墙让出两个转角厚度：
            // 若四面都用全长，转角处两组立方体占据同一空间，画面会有 z-fighting 闪烁。
            var runNS = outer * 2f;
            var runEW = outer * 2f - 2f * t;

            PlaceCube("Fence_North", new Vector3(0f, h * 0.5f, c), new Vector3(runNS, h, t), FenceColor);
            PlaceCube("Fence_South", new Vector3(0f, h * 0.5f, -c), new Vector3(runNS, h, t), FenceColor);
            PlaceCube("Fence_East", new Vector3(c, h * 0.5f, 0f), new Vector3(t, h, runEW), FenceColor);
            PlaceCube("Fence_West", new Vector3(-c, h * 0.5f, 0f), new Vector3(t, h, runEW), FenceColor);
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

            // 仓库外圈：离四面墙各 1.5m（哨兵基线不参与碰撞，压墙会当众穿模）
            var route3 = CreateRoute("DepotRing", ProfileFolder + "/DepotRing.asset",
                PatrolLoopMode.Loop, 1.8f,
                new[] { new Vector3(9.5f, 0f, 10.5f), new Vector3(22.5f, 0f, 10.5f),
                        new Vector3(22.5f, 0f, 21.5f), new Vector3(9.5f, 0f, 21.5f) },
                new[] { 0.8f, 0.8f, 0.8f, 0.8f });

            if (route == null) Debug.LogError("[StealthSampleBuilder] WarehouseLoop 资产加载失败");
            if (route2 == null) Debug.LogError("[StealthSampleBuilder] CrossSweep 资产加载失败");
            if (route3 == null) Debug.LogError("[StealthSampleBuilder] DepotRing 资产加载失败");

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
                case SerializedPropertyType.Vector2:
                    prop.vector2Value = (Vector2)value;
                    break;
                case SerializedPropertyType.Vector3:
                    prop.vector3Value = (Vector3)value;
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
