using System.Collections.Generic;
using Collin.Core;
using Collin.Core.Stealth;
using Collin.Stealth;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Collin.Stealth.Tests
{
    /// <summary>
    /// M3 验收：四档状态机（巡逻→疑惑→警觉→搜索→巡逻）、同伴通报、察觉事件广播、寻路接缝。
    /// <para>纪律与 M1/M2 同源：全部断言写在<b>数据层</b>，不依赖帧循环——
    /// EditMode 不执行 Update/LateUpdate（本包实测坑），所以测试显式驱动
    /// <see cref="DetectionState.EvaluateOnce"/> 与 <see cref="GuardBrain.Tick"/>。</para>
    /// <para>最硬的一条守卫：<b>叠加层不得污染巡逻基线</b>（DESIGN R-1）。
    /// 反应位移只存在于 transform 合成结果里，
    /// <see cref="PatrolAgent.EvaluateWorld"/> 在任何时刻都必须返回纯基线位姿——
    /// 否则辅助台的回溯预览显示的就是「上一局玩过的痕迹」，设计工具直接失真。</para>
    /// </summary>
    public class GuardBrainTests
    {
        readonly List<GameObject> _games = new List<GameObject>();
        readonly List<Object> _assets = new List<Object>();

        [SetUp]
        public void SetUp()
        {
            Services.Clear();
            VisionRegistry.ResetForTests();
            DetectionRegistry.ResetForTests();
            StealthPerformance.ResetForTests();
            VisionVolume.LineOfSightOverride = null;
        }

        [TearDown]
        public void TearDown()
        {
            VisionVolume.LineOfSightOverride = null;
            StealthPerformance.ResetForTests();
            Services.Clear();

            for (var i = _games.Count - 1; i >= 0; i--)
            {
                var go = _games[i];
                if (go == null) continue;
                go.SetActive(false);
                Object.DestroyImmediate(go, true);
            }
            _games.Clear();

            for (var i = _assets.Count - 1; i >= 0; i--)
            {
                var o = _assets[i];
                if (o != null) Object.DestroyImmediate(o, true);
            }
            _assets.Clear();

            VisionRegistry.ResetForTests();
            DetectionRegistry.ResetForTests();
        }

        VisionProfile NewProfile(float gain = 0.5f, float decay = 0.2f,
                                 float sus = 0.35f, float alert = 0.8f)
        {
            var p = ScriptableObject.CreateInstance<VisionProfile>();
            _assets.Add(p);
            p.SetHorizontalAngle(360f);      // 全向：让档位断言与朝向解耦
            p.SetVerticalAngle(180f);
            p.SetMaxDistance(20f);
            p.SetEyeHeight(0f);
            p.SetOccluderMask(0);            // 无物理遮挡：L3 恒放行，测的是状态机不是射线
            p.SetSuspicionGain(gain);
            p.SetDecayPerSecond(decay);
            p.SetSuspicionThreshold(sus);
            p.SetAlertThreshold(alert);
            p.SetLightSensitivity(0f);
            return p;
        }

        /// <summary>建一个「视野 + 决策」哨兵；决策不依赖巡逻也能测。</summary>
        GuardBrain NewGuard(Vector3 position, VisionProfile profile, out VisionVolume volume)
        {
            var go = new GameObject("guard");
            _games.Add(go);
            go.transform.position = position;
            volume = go.AddComponent<VisionVolume>();
            volume.Profile = profile;
            return go.AddComponent<GuardBrain>();
        }

        DetectionState NewPlayer(Vector3 position)
        {
            var go = new GameObject("player");
            _games.Add(go);
            go.transform.position = position;
            return go.AddComponent<DetectionState>();
        }

        // ── 档位流转 ────────────────────────────────────────────────

        [Test]
        public void SuspicionCrossesThreshold_EscalatesToSuspicious()
        {
            var brain = NewGuard(Vector3.zero, NewProfile(gain: 0.5f), out _);
            var player = NewPlayer(new Vector3(0f, 0f, 5f));

            Assert.That(brain.Stage, Is.EqualTo(StealthDetectionStage.Patrol));

            player.EvaluateOnce(1f);                       // 0 → 0.5（越过 0.35）
            brain.Tick(0.016f, Vector3.zero, 1f);

            Assert.That(brain.Stage, Is.EqualTo(StealthDetectionStage.Suspicious));
            Assert.That(brain.Active, Is.True);
            AssertVec(new Vector3(0f, 0.95f, 5f), brain.LastSeenPoint, 1e-4f,
                      "可疑点必须是最后看见玩家的位置（身体判定点，非脚底）");
        }

        [Test]
        public void SuspicionCrossesAlert_EscalatesToAlerted_AndOverlayChases()
        {
            var brain = NewGuard(Vector3.zero, NewProfile(gain: 0.9f), out _);
            var player = NewPlayer(new Vector3(0f, 0f, 6f));

            player.EvaluateOnce(1f);                       // 0.9 ≥ 0.8
            brain.Tick(0.016f, Vector3.zero, 1f);

            Assert.That(brain.Stage, Is.EqualTo(StealthDetectionStage.Alerted));

            brain.GetOverlay(out var offset, out var look);
            Assert.That(offset.z, Is.GreaterThan(0f), "反应偏移应指向可疑点（+Z）");
            Assert.That(look.HasValue, Is.True, "追击时必须接管朝向");
        }

        [Test]
        public void LostTarget_HoldsThenSearchesThenReturnsToPatrol()
        {
            var profile = NewProfile(gain: 0.9f, decay: 0.5f);
            var brain = NewGuard(Vector3.zero, profile, out _);
            var player = NewPlayer(new Vector3(0f, 0f, 6f));

            player.EvaluateOnce(1f);
            brain.Tick(0.016f, Vector3.zero, 1f);
            Assert.That(brain.Stage, Is.EqualTo(StealthDetectionStage.Alerted));

            // 玩家离场：察觉度衰减归零后，状态链应是 警觉→(驻留)→搜索→(环绕)→巡逻。
            player.transform.position = new Vector3(0f, 0f, 60f);
            for (var i = 0; i < 4; i++)
            {
                player.EvaluateOnce(1f);
                brain.Tick(1f, Vector3.zero, 2f + i);
            }
            Assert.That(brain.Stage, Is.EqualTo(StealthDetectionStage.Searching),
                        "驻留期满应转搜索（环绕可疑点）");

            for (var i = 0; i < 8; i++)
            {
                player.EvaluateOnce(1f);
                brain.Tick(1f, Vector3.zero, 6f + i);
            }
            Assert.That(brain.Stage, Is.EqualTo(StealthDetectionStage.Patrol),
                        "搜索期满必须回巡逻");
        }

        [Test]
        public void PurePreview_DoesNotAdvanceStageMachine()
        {
            var brain = NewGuard(Vector3.zero, NewProfile(gain: 0.9f), out _);
            var player = NewPlayer(new Vector3(0f, 0f, 5f));
            player.EvaluateOnce(1f);                       // 察觉度已达警觉线

            // dt<=0 = 编辑态 scrub 语义：读数刷新，但状态机绝不推进。
            for (var i = 0; i < 5; i++) brain.Tick(0f, Vector3.zero, 1f);

            Assert.That(brain.Stage, Is.EqualTo(StealthDetectionStage.Patrol),
                        "纯预览不得改变档位");
            AssertVec(new Vector3(0f, 0.95f, 5f), brain.LastSeenPoint, 1e-4f,
                      "但感知读数必须刷新（辅助台要靠它画可疑点）");

            brain.Tick(0.016f, Vector3.zero, 1f);         // 对照：真实推进即升档
            Assert.That(brain.Stage, Is.EqualTo(StealthDetectionStage.Alerted));
        }

        // ── 叠加层与基线的隔离（本包地基）──────────────────────────

        [Test]
        public void OverlayMovesTransform_ButBaselineStaysPure()
        {
            // 两路点直线：局部 (0,0,0)→(10,0,0)，速度 2 ⇒ t=1s 基线在 (2,0,0)。
            var route = ScriptableObject.CreateInstance<PatrolRoute>();
            _assets.Add(route);
            route.SetDefaultSpeed(2f);
            route.AddWaypoint(Vector3.zero);
            route.AddWaypoint(new Vector3(10f, 0f, 0f));

            var rootGo = new GameObject("route-root");
            _games.Add(rootGo);
            var root = rootGo.AddComponent<PatrolRouteRoot>();
            root.SetRoute(route);

            var guard = new GameObject("guard-agent");
            _games.Add(guard);
            var agent = guard.AddComponent<PatrolAgent>();
            agent.RouteRoot = root;
            agent.ApplyToTransform = true;

            var profile = NewProfile(gain: 0.9f);
            var vision = guard.AddComponent<VisionVolume>();
            vision.Profile = profile;
            var brain = guard.AddComponent<GuardBrain>();
            agent.AddModifier(brain);

            var player = NewPlayer(new Vector3(0f, 0f, 8f));
            player.EvaluateOnce(1f);                      // 0.9 ≥ 0.8 → 会升警觉

            agent.ApplyBaselineAt(1f, 1f);                // 一帧：感知+升档+叠加收敛

            var baseline = agent.EvaluateWorld(1f).position;
            AssertVec(new Vector3(2f, 0f, 0f), baseline, 1e-3f,
                      "EvaluateWorld 必须永远是纯基线，与叠加无关");
            Assert.That(agent.transform.position.z, Is.GreaterThan(0.5f),
                        "transform 应被叠加层拉向可疑点");
            Assert.That((agent.transform.position - baseline).magnitude, Is.GreaterThan(0.5f),
                        "显示位姿与基线必须分离");

            // 再求值不同 t：基线仍按路线纯函数走，不残留拉扯痕迹。
            var late = agent.EvaluateWorld(3.5f).position;
            AssertVec(new Vector3(7f, 0f, 0f), late, 1e-3f, "叠加不得回流进基线");
        }

        [Test]
        public void ResetOverlay_ClearsStageAndOffset()
        {
            var brain = NewGuard(Vector3.zero, NewProfile(gain: 0.9f), out _);
            var player = NewPlayer(new Vector3(0f, 0f, 6f));
            player.EvaluateOnce(1f);
            brain.Tick(1f, Vector3.zero, 1f);
            Assert.That(brain.Stage, Is.Not.EqualTo(StealthDetectionStage.Patrol));

            brain.ResetOverlay();
            Assert.That(brain.Stage, Is.EqualTo(StealthDetectionStage.Patrol));
            Assert.That(brain.Active, Is.False);
            brain.GetOverlay(out var off, out var look);
            Assert.That(off.magnitude, Is.EqualTo(0f).Within(1e-6f));
            Assert.That(look.HasValue, Is.False);
        }

        // ── 同伴通报 + 事件广播 ────────────────────────────────────

        [Test]
        public void Alert_BroadcastsPayload_OnDetectedChannel()
        {
            var channel = ScriptableObject.CreateInstance<StealthDetectedChannel>();
            _assets.Add(channel);

            var profile = NewProfile(gain: 0.9f);
            var brain = NewGuard(Vector3.zero, profile, out var volume);

            // _detectedChannel 是私有序列化字段：走 SerializedObject（与示例生成器同一做法）。
            var so = new SerializedObject(brain);
            var prop = so.FindProperty("_detectedChannel");
            Assert.That(prop, Is.Not.Null, "字段改名必须在这里暴露，而不是静默丢广播");
            prop.objectReferenceValue = channel;
            so.ApplyModifiedPropertiesWithoutUndo();

            StealthDetectedPayload last = default;
            var hits = 0;
            void Handler(StealthDetectedPayload p) { last = p; hits++; }
            channel.Subscribe(Handler);

            var player = NewPlayer(new Vector3(0f, 0f, 5f));
            player.EvaluateOnce(1f);
            brain.Tick(0.016f, Vector3.zero, 4.25f);      // 升警觉 ⇒ 广播一次

            Assert.That(hits, Is.EqualTo(1));
            Assert.That(last.actorId, Is.EqualTo(brain.ActorId));
            Assert.That(last.targetId, Is.EqualTo(player.TargetId));
            Assert.That(last.timeIndex, Is.EqualTo(4.25f).Within(1e-4f),
                        "事件必须带 t，辅助台才能把它钉回回溯时间轴");
            Assert.That(last.stage, Is.EqualTo(StealthAlertStage.Alerted));

            // 已在警觉态再 tick 不得重复广播（只有跃迁瞬间发）。
            player.EvaluateOnce(1f);
            brain.Tick(0.016f, Vector3.zero, 4.27f);
            Assert.That(hits, Is.EqualTo(1), "非跃迁帧不得重复广播");
        }

        [Test]
        public void Alert_PullsNearbyCompanionToSuspicious_NotFarOrAlreadyAlerted()
        {
            var profile = NewProfile(gain: 0.9f);

            // A：会警觉的哨兵；B：距离 A 可疑点 10m（<15m 通报半径）但看不见玩家；
            // C：看不见玩家且远离 A 的可疑点（>15m），不应被拉进来。
            var a = NewGuard(Vector3.zero, profile, out _);
            var b = NewGuard(new Vector3(0f, 0f, 10f), profile, out var volumeB);
            volumeB.DistanceMultiplier = 0.1f;            // 实例覆盖：视距 2m，够不到 5m 处的玩家
            var c = NewGuard(new Vector3(0f, 0f, 30f), profile, out var volumeC);
            volumeC.DistanceMultiplier = 0.1f;

            var player = NewPlayer(new Vector3(0f, 0f, 5f));
            player.EvaluateOnce(1f);
            a.Tick(0.016f, Vector3.zero, 1f);              // A 警觉 ⇒ 通报

            Assert.That(b.Stage, Is.EqualTo(StealthDetectionStage.Suspicious),
                        "半径内同伴应被拉到疑惑并同步可疑点");
            AssertVec(player.BodyPoint, b.LastSeenPoint, 1e-4f);
            Assert.That(c.Stage, Is.EqualTo(StealthDetectionStage.Patrol),
                        "半径外同伴不受影响");
        }

        // ── 寻路接缝（D-06：默认关，注册即生效）───────────────────

        [Test]
        public void PathBridge_AbsentByDefault_PresentWhenRegistered()
        {
            var brain = NewGuard(Vector3.zero, NewProfile(gain: 0.9f), out _);
            var player = NewPlayer(new Vector3(0f, 0f, 6f));

            // 默认无导航实现：叠加目标 = 可疑点直线。
            // dt 取 3：叠加按限速收敛（3.5m/s），一步只走 3.5m 时两种目标会被截成同一个值，
            // 必须给足时间让偏移真正抵达目标，断言才有区分度。
            Assert.False(Services.TryGet<IPathBridge>(out _), "NullPathBridge 不得默认注册");
            player.EvaluateOnce(1f);
            brain.Tick(3f, Vector3.zero, 1f);
            brain.GetOverlay(out var plain, out var plainLook);
            Assert.That(plain.z, Is.EqualTo(6f).Within(1e-3f), "未接桥时叠加应抵达可疑点");
            Assert.That(plainLook.Value.z, Is.EqualTo(6f).Within(1e-3f));

            // 注册接缝后：目标经 TrySnap 改写（+4m），叠加必须落在吸附后的点上。
            Services.Register<IPathBridge>(new ShiftBridge(new Vector3(0f, 0f, 4f)));
            var brain2 = NewGuard(new Vector3(20f, 0f, 0f), NewProfile(gain: 0.9f), out _);
            VisionRegistry.MarkDirty();   // EditMode 不跑 OnEnable，须手动让注册表重建
            DetectionRegistry.MarkDirty();

            player.transform.position = new Vector3(20f, 0f, 6f);
            player.EvaluateOnce(1f);
            brain2.Tick(3f, new Vector3(20f, 0f, 0f), 2f);
            brain2.GetOverlay(out var snapped, out var snapLook);
            Assert.That(snapped.z, Is.EqualTo(10f).Within(1e-3f),
                        "注册 IPathBridge 后叠加目标必须被吸附改写（6 + 4）");
            Assert.That(snapLook.Value.z, Is.EqualTo(10f).Within(1e-3f));
        }

        private sealed class ShiftBridge : IPathBridge
        {
            private readonly Vector3 _delta;
            public ShiftBridge(Vector3 delta) { _delta = delta; }
            public bool TrySnap(Vector3 fromWorld, Vector3 desiredWorld, out Vector3 navWorld)
            {
                navWorld = desiredWorld + _delta;
                return true;
            }
        }

        // ── 与跨包复原的接线（M0.5 接口 × M3 行为）────────────────

        [Test]
        public void WorldResetAll_AlsoRestoresGuardReactions()
        {
            var brain = NewGuard(Vector3.zero, NewProfile(gain: 0.9f), out _);
            var player = NewPlayer(new Vector3(0f, 0f, 6f));
            player.EvaluateOnce(1f);
            brain.Tick(1f, Vector3.zero, 1f);
            Assert.That(brain.Stage, Is.EqualTo(StealthDetectionStage.Alerted));

            StealthWorld.EnsureRegistered();
            Services.Get<IDetectionWorld>().ResetAll();

            Assert.That(brain.Stage, Is.EqualTo(StealthDetectionStage.Patrol),
                        "「复原初始状态」必须连 NPC 反应状态一起复原");
            Assert.That(brain.Active, Is.False);
            Assert.That(Services.Get<IDetectionWorld>().SuspicionOf(player.TargetId),
                        Is.EqualTo(0f).Within(1e-5f));
        }

        static void AssertVec(Vector3 expected, Vector3 actual, float delta, string msg = null)
        {
            Assert.AreEqual(expected.x, actual.x, delta, (msg ?? "") + " x");
            Assert.AreEqual(expected.y, actual.y, delta, (msg ?? "") + " y");
            Assert.AreEqual(expected.z, actual.z, delta, (msg ?? "") + " z");
        }
    }
}
