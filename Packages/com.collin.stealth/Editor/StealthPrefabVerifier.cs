using System.Collections.Generic;
using System.IO;
using Collin.Core;
using Collin.Core.Stealth;
using Collin.Stealth;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Collin.Stealth.EditorTooling
{
    /// <summary>
    /// 预制件自检（验证「拆箱即用」）：在<b>内存临时场景</b>里只放两个预制件 + 一个时钟，
    /// 不建地面、不建掩体、不手写资产，检查玩家与哨兵是否自洽可用。
    /// <para>共享编辑器纪律：用 <b>Additive</b> 建临时场景、验完 <c>CloseScene</c> 丢弃，
    /// <b>绝不 <c>NewScene(Single)</c></b>——那会丢弃其他会话未保存的现场（同生成器的理由）。
    /// 本工具不存盘，因为「无落盘的验证现场」不该进仓库，只输出一条结论日志。</para>
    /// </summary>
    internal static class StealthPrefabVerifier
    {
        const string PlayerPath = "Packages/com.collin.stealth/Sample/Prefabs/StealthPlayer.prefab";
        const string GuardPath = "Packages/com.collin.stealth/Sample/Prefabs/StealthGuard.prefab";

        [MenuItem("Tools/Collin/Stealth/验证预制件自洽（内存临时场景）")]
        public static void Verify()
        {
            if (!File.Exists(PlayerPath) || !File.Exists(GuardPath))
            {
                Debug.LogWarning("[Stealth][PrefabVerify] 预制件不存在，先执行「沉淀示例预制件」");
                return;
            }

            var playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPath);
            var guardPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(GuardPath);
            if (playerPrefab == null || guardPrefab == null)
            {
                Debug.LogError("[Stealth][PrefabVerify] 预制件资产读不出来（player="
                               + (playerPrefab != null) + " guard=" + (guardPrefab != null) + "）");
                return;
            }

            // Additive 临时场景：不替换活动场景，结束后原样关掉。
            var temp = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            var problems = new List<string>();

            try
            {
                EditorSceneManager.SetActiveScene(temp);

                var clockGo = new GameObject("StealthClock");
                clockGo.AddComponent<StealthClock>();

                var player = (GameObject)PrefabUtility.InstantiatePrefab(playerPrefab);
                var guardA = (GameObject)PrefabUtility.InstantiatePrefab(guardPrefab);
                var guardB = (GameObject)PrefabUtility.InstantiatePrefab(guardPrefab);
                guardB.transform.localPosition = new Vector3(6f, 0f, 0f);

                if (PrefabUtility.GetPrefabInstanceStatus(player) != PrefabInstanceStatus.Connected)
                {
                    problems.Add("玩家实例与预制件断开");
                }
                if (PrefabUtility.GetPrefabInstanceStatus(guardA) != PrefabInstanceStatus.Connected)
                {
                    problems.Add("哨兵实例与预制件断开");
                }

                if (player.GetComponent<DetectionState>() == null) problems.Add("玩家缺 DetectionState");
                if (player.GetComponent<PlayerStealthController>() == null) problems.Add("玩家缺 PlayerStealthController");
                if (player.GetComponent<CharacterController>() == null) problems.Add("玩家缺 CharacterController");

                var body = guardA.transform.Find("Body");
                var agent = body != null ? body.GetComponent<PatrolAgent>() : null;
                var vision = body != null ? body.GetComponent<VisionVolume>() : null;
                var brain = body != null ? body.GetComponent<GuardBrain>() : null;
                var rootTf = guardA.transform.Find("RouteRoot");
                var routeRoot = rootTf != null ? rootTf.GetComponent<PatrolRouteRoot>() : null;

                if (agent == null) problems.Add("哨兵 Body 缺 PatrolAgent");
                if (vision == null) problems.Add("哨兵 Body 缺 VisionVolume");
                if (brain == null) problems.Add("哨兵 Body 缺 GuardBrain");
                if (routeRoot == null) problems.Add("哨兵缺 RouteRoot 组件");
                if (agent != null && agent.RouteRoot == null) problems.Add("PatrolAgent 未引用路线根");
                if (routeRoot != null && routeRoot.Route == null) problems.Add("路线资产引用丢失");
                if (vision != null && vision.Profile == null) problems.Add("视野资产引用丢失");

                // 纯函数求值必须可用：拖出的预制件不依赖场景里任何其他物体。
                var cycle = routeRoot != null && routeRoot.Route != null ? routeRoot.Route.TotalCycle : 0f;
                if (cycle <= 1e-4f)
                {
                    problems.Add("T 周期为 0（路线资产未生效）");
                }
                else if (agent != null)
                {
                    var p0 = agent.EvaluateWorld(0f).position;
                    var p1 = agent.EvaluateWorld(cycle * 0.25f).position;
                    if ((p1 - p0).sqrMagnitude < 1e-6f) problems.Add("沿周期求值无位移（路线未生效）");
                }

                StealthWorld.EnsureRegistered();
                // 断言用<b>差值</b>而非绝对值：单位枚举走全工程搜索，而此刻还加载着
                // 其他会话正在编辑的场景（示例/展板），绝对值既可能误报失败也可能凑数假绿。
                var before = CountActors();
                var guardC = (GameObject)PrefabUtility.InstantiatePrefab(guardPrefab);
                var after = CountActors();
                if (after - before != 1)
                {
                    problems.Add("实例化 1 个哨兵后跨包枚举新增 " + (after - before) + " 个（预期 1）");
                }
                Object.DestroyImmediate(guardC);

                Debug.Log("[Stealth][PrefabVerify] 内存临时场景（无地面/无掩体）仅拖预制件："
                          + "actors新增=" + (after - before) + " cycle=" + cycle.ToString("0.00") + "s  "
                          + (problems.Count == 0
                             ? "结论=通过，预制件自洽、拆箱即用"
                             : "结论=失败 " + string.Join("；", problems)));
            }
            finally
            {
                var active = SceneManager.GetActiveScene();
                if (active == temp)
                {
                    // 关掉临时场景前把活动场景还给原来的现场，否则 CloseScene 会连带影响选中的场景。
                    foreach (var go in temp.GetRootGameObjects()) Object.DestroyImmediate(go);
                }
                EditorSceneManager.CloseScene(temp, true);
            }
        }
        static int CountActors()
        {
            return Services.TryGet<IPatrolWorld>(out var pw) && pw != null ? pw.Actors.Count : -1;
        }
    }
}
