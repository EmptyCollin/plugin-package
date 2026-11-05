using System.Collections.Generic;
using Collin.Core;
using Collin.Core.Stealth;
using Collin.Stealth;
using NUnit.Framework;
using UnityEngine;

namespace Collin.Stealth.Tests
{
    /// <summary>
    /// M2 验收：四层察觉管线（L1 距离 → L2 视角锥 → L3 视线 → L4 遮蔽修正）。
    /// <para>断言全部写在<b>数据层</b>：<see cref="VisionGeometry"/> 是纯函数，
    /// <see cref="VisionVolume.CanSeeFrom"/> 接受任意观察原点，
    /// 因此 B 包「按 t 时刻的 NPC 位姿预演可见性」所需的语义在这里就能验完，
    /// 不必依赖物理场景或 Play 态。</para>
    /// <para>L3 用 <see cref="VisionVolume.LineOfSightOverride"/> 注入结论，
    /// 使遮挡翻转与帧预算两条分支可确定性断言（真实射线命中依赖物理场景，留待 Play 实测）。</para>
    /// </summary>
    public class VisionDetectionTests
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

        VisionProfile NewProfile(float h = 110f, float dist = 18f, float v = 60f, float eye = 1.7f, int occluderMask = 0)
        {
            var p = ScriptableObject.CreateInstance<VisionProfile>();
            _assets.Add(p);
            p.SetHorizontalAngle(h);
            p.SetVerticalAngle(v);
            p.SetMaxDistance(dist);
            p.SetEyeHeight(eye);
            p.SetOccluderMask(occluderMask);
            return p;
        }

        GameObject NewGo(string name, Vector3 position, float yaw = 0f)
        {
            var go = new GameObject(name);
            _games.Add(go);
            go.transform.position = position;
            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            return go;
        }

        VisionVolume NewVolume(VisionProfile profile, Vector3 position, float yaw = 0f)
        {
            var go = NewGo("guard", position, yaw);
            var v = go.AddComponent<VisionVolume>();
            v.Profile = profile;
            return v;
        }

        static void AssertVec(Vector3 expected, Vector3 actual, float delta, string msg = null)
        {
            Assert.AreEqual(expected.x, actual.x, delta, (msg ?? "") + " x");
            Assert.AreEqual(expected.y, actual.y, delta, (msg ?? "") + " y");
            Assert.AreEqual(expected.z, actual.z, delta, (msg ?? "") + " z");
        }

        // ── L1 距离 ─────────────────────────────────────────────────

        [Test]
        public void DistanceOnly_UsesSphereCheckNotRaycast()
        {
            // 眼高取 0：球心就在物体原点，平面距离即斜距，边界断言才精确成立。
            // （眼高 1.7 时「平面 10 米」的斜距是 sqrt(10²+1.7²)=10.14 > 10，会正常出界。）
            var v = NewVolume(NewProfile(dist: 10f, eye: 0f), Vector3.zero);
            var id = StealthIds.Of(v.gameObject);

            Assert.True(v.CanSee(new Vector3(0f, 0f, 9f), QueryOptions.DistanceOnly, id));
            Assert.False(v.CanSee(new Vector3(0f, 0f, 11f), QueryOptions.DistanceOnly, id));

            // 恰在球面上：边界含入（等号成立，避免辅助台热力图边缘闪烁）。
            Assert.True(v.CanSee(new Vector3(0f, 0f, 10f), QueryOptions.DistanceOnly, id));

            // 眼高确实把原点抬起来了：同一点在 eye=1.7 时出界。
            var raised = NewVolume(NewProfile(dist: 10f, eye: 1.7f), Vector3.zero);
            Assert.False(raised.CanSee(new Vector3(0f, 0f, 10f), QueryOptions.DistanceOnly,
                StealthIds.Of(raised.gameObject)), "球检应按含眼高的真实斜距计算");

            StealthPerformance.BeginFrame(7);
            Assert.That(StealthPerformance.UsedThisFrame, Is.EqualTo(0), "L1 不该消耗射线预算");
        }

        [Test]
        public void DistanceCheck_FoldsInstanceDistanceMultiplier()
        {
            var v = NewVolume(NewProfile(dist: 10f), Vector3.zero);
            var id = StealthIds.Of(v.gameObject);
            var target = new Vector3(0f, 0f, 15f);

            Assert.False(v.CanSee(target, QueryOptions.DistanceOnly, id));
            v.DistanceMultiplier = 2f;
            Assert.True(v.CanSee(target, QueryOptions.DistanceOnly, id), "视距倍率未生效");
            Assert.That(v.Params.maxDistance, Is.EqualTo(20f).Within(1e-4f));
        }

        // ── L2 视角锥（验收 7：越界即翻转）─────────────────────────

        [Test]
        public void Cone_BoundaryFlipsWhenTargetRotatesOut()
        {
            // 100° 全角 ⇒ 半角 50°。正前方 0° 在内，55° 在外。
            var v = NewVolume(NewProfile(h: 100f, dist: 20f), Vector3.zero);
            var id = StealthIds.Of(v.gameObject);

            var inside = new Vector3(Mathf.Sin(40f * Mathf.Deg2Rad), 0f, Mathf.Cos(40f * Mathf.Deg2Rad)) * 10f;
            var outside = new Vector3(Mathf.Sin(55f * Mathf.Deg2Rad), 0f, Mathf.Cos(55f * Mathf.Deg2Rad)) * 10f;

            Assert.True(v.CanSee(inside, QueryOptions.ConeOnly, id), "40° 应在 100° 锥内");
            Assert.False(v.CanSee(outside, QueryOptions.ConeOnly, id), "55° 应在 100° 锥外");

            // 只判距离时同一个外侧点应可见 ⇒ 证明是视角锥在起作用，不是距离。
            Assert.True(v.CanSee(outside, QueryOptions.DistanceOnly, id));
        }

        [Test]
        public void Cone_RotatingObserverSweepsSameTarget()
        {
            var v = NewVolume(NewProfile(h: 90f, dist: 20f), Vector3.zero);
            var id = StealthIds.Of(v.gameObject);
            var target = new Vector3(0f, 0f, 10f);

            v.transform.rotation = Quaternion.Euler(0f, 0f, 0f);
            Assert.True(v.CanSee(target, QueryOptions.ConeOnly, id), "朝向正对目标时应可见");

            v.transform.rotation = Quaternion.Euler(0f, 90f, 0f);   // 转向 +X，目标落到侧后方
            Assert.False(v.CanSee(target, QueryOptions.ConeOnly, id), "转开 90° 后半角 45° 应判不可见");
        }

        [Test]
        public void Cone_ThreeSixtyIsOmnidirectional()
        {
            var v = NewVolume(NewProfile(h: 360f, dist: 20f), Vector3.zero);
            var id = StealthIds.Of(v.gameObject);

            Assert.True(v.CanSee(new Vector3(0f, 0f, -10f), QueryOptions.ConeOnly, id), "全向视野背后也应可见");
            Assert.True(v.CanSee(new Vector3(-10f, 0f, 0f), QueryOptions.ConeOnly, id));
        }

        [Test]
        public void VerticalCone_ChecksPitchAgainstEyeLine()
        {
            var v = NewVolume(NewProfile(h: 360f, v: 60f, dist: 30f), Vector3.zero);
            var id = StealthIds.Of(v.gameObject);

            var high = new Vector3(0f, 12f, 10f);   // 仰角约 50° ⇒ 超出半角 30°
            var low = new Vector3(0f, -2f, 10f);    // 俯角约 8° ⇒ 在锥内

            Assert.False(v.CanSee(high, QueryOptions.ConeOnly, id), "高处目标应被垂直角挡掉");
            Assert.True(v.CanSee(low, QueryOptions.ConeOnly, id));

            Assert.False(VisionGeometry.InVerticalCone(high, Vector3.forward, 60f));
            Assert.True(VisionGeometry.InVerticalCone(low, Vector3.forward, 60f));
            Assert.True(VisionGeometry.InVerticalCone(high, Vector3.forward, 180f), "180° 视为全向俯仰");
        }

        // ── L3 视线（注入结论，真实射线留待 Play）─────────────────

        [Test]
        public void LineOfSight_OcclusionFlipsVerdictAndCacheResets()
        {
            var occluded = false;
            VisionVolume.LineOfSightOverride = (vol, from, to) => !occluded;

            var v = NewVolume(NewProfile(dist: 20f, occluderMask: ~0), Vector3.zero);
            var id = StealthIds.Of(v.gameObject);
            var target = new Vector3(0f, 0f, 8f);

            StealthPerformance.BeginFrame(0);
            Assert.True(v.CanSee(target, QueryOptions.Full, id), "无遮挡时全开应可见");

            occluded = true;
            StealthPerformance.BeginFrame(1);
            Assert.False(v.CanSee(target, QueryOptions.Full, id), "插入遮挡后必须转不可见");

            occluded = false;
            StealthPerformance.BeginFrame(2);
            Assert.True(v.CanSee(target, QueryOptions.Full, id), "撤掉遮挡后必须恢复可见");
        }

        [Test]
        public void LineOfSight_WithoutOccluderLayerDoesNotConsumeBudget()
        {
            var v = NewVolume(NewProfile(dist: 20f, occluderMask: 0), Vector3.zero);
            var id = StealthIds.Of(v.gameObject);

            StealthPerformance.BeginFrame(0);
            Assert.True(v.CanSee(new Vector3(0f, 0f, 5f), QueryOptions.Full, id));
            Assert.That(StealthPerformance.UsedThisFrame, Is.EqualTo(0), "无遮挡层时不该发射线");
        }

        [Test]
        public void LineOfSight_BudgetExhaustedReusesLastVerdictInsteadOfFlipping()
        {
            var returnOccluded = true;
            VisionVolume.LineOfSightOverride = (vol, from, to) => !returnOccluded;
            StealthPerformance.raycastBudgetPerFrame = 1;

            var a = NewVolume(NewProfile(dist: 20f, occluderMask: ~0), Vector3.zero);
            var b = NewVolume(NewProfile(dist: 20f, occluderMask: ~0), new Vector3(1f, 0f, 0f));
            var idA = StealthIds.Of(a.gameObject);
            var idB = StealthIds.Of(b.gameObject);

            // 帧 0：a 先问 ⇒ 抢到唯一预算，判「被遮挡」；b 后问，预算耗尽且无缓存 ⇒ 按无遮挡放行。
            StealthPerformance.BeginFrame(0);
            Assert.False(a.CanSee(new Vector3(0f, 0f, 8f), QueryOptions.Full, idA));
            Assert.True(b.CanSee(new Vector3(1f, 0f, 8f), QueryOptions.Full, idB));

            // 帧 1：仍只够一条预算。<b>必须先问 a</b> 把预算吃掉，b 才落到「预算不足」分支；
            // 此时 b 必须沿用<b>上帧的 true</b>——绝不因没排到队就把「可见」翻成「看不见」，
            // 否则辅助台热力图会随帧率抖动，把性能问题伪装成玩法问题。
            StealthPerformance.BeginFrame(1);
            Assert.False(a.CanSee(new Vector3(0f, 0f, 8f), QueryOptions.Full, idA), "a 本帧仍应实际评估");
            Assert.True(b.CanSee(new Vector3(1f, 0f, 8f), QueryOptions.Full, idB), "预算不足时 b 不得翻转结论");
        }

        [Test]
        public void Staggering_SpreadsChecksAcrossFrames()
        {
            // 间隔 3 帧时，不同 id 相位的单位不应在同帧全部检查。
            var hits = 0;
            for (var f = 0; f < 3; f++)
            {
                StealthPerformance.BeginFrame(f);
                for (var id = 0; id < 3; id++)
                {
                    if (StealthPerformance.ShouldCheckThisFrame(id, 3)) hits++;
                }
            }

            Assert.That(hits, Is.EqualTo(3), "错峰应让每帧恰好只有一个单位做视线检测");
        }

        [Test]
        public void CheckInterval_CachesVerdictOnSkippedFrames()
        {
            var calls = 0;
            VisionVolume.LineOfSightOverride = (vol, from, to) => { calls++; return true; };

            var profile = NewProfile(dist: 20f, occluderMask: ~0);
            profile.SetLineOfSightCheckInterval(2);
            var v = NewVolume(profile, Vector3.zero);
            var target = new Vector3(0f, 0f, 6f);

            // actorId 由调用方传入（接口如此设计正是为了可测）：取 id=0 ⇒
            // 帧 0 命中检查相位，帧 1 不命中 ⇒ 必须复用缓存。真实实体 id 不可控，
            // 因此不能用 StealthIds.Of 来断言相位。
            const int probeId = 0;

            StealthPerformance.BeginFrame(0);
            Assert.True(v.CanSeeFrom(v.OriginWorld, v.ForwardWorld, target, QueryOptions.Full, probeId),
                        "首个检查帧应实际评估");
            Assert.That(calls, Is.EqualTo(1));

            StealthPerformance.BeginFrame(1);
            Assert.True(v.CanSeeFrom(v.OriginWorld, v.ForwardWorld, target, QueryOptions.Full, probeId));
            Assert.That(calls, Is.EqualTo(1), "跳过的帧必须复用视线结论，不得重复评估");

            StealthPerformance.BeginFrame(2);
            v.CanSeeFrom(v.OriginWorld, v.ForwardWorld, target, QueryOptions.Full, probeId);
            Assert.That(calls, Is.EqualTo(2), "回到检查相位时必须重新评估");
        }

        // ── L4 遮蔽修正（只改速率，不改可见性）──────────────────────

        [Test]
        public void ExposureScale_CrouchLightAndSafeIsland()
        {
            // lightSensitivity=1 ⇒ 倍率完全跟随曝光度
            Assert.That(AwarenessModel.ExposureScale(1f, 1f, false, StealthMask.None), Is.EqualTo(1f).Within(1e-4f));
            Assert.That(AwarenessModel.ExposureScale(1f, 0.2f, false, StealthMask.None), Is.EqualTo(0.2f).Within(1e-4f));
            // lightSensitivity=0 ⇒ 不看光照
            Assert.That(AwarenessModel.ExposureScale(0f, 0f, false, StealthMask.None), Is.EqualTo(1f).Within(1e-4f));

            var crouched = AwarenessModel.ExposureScale(0f, 1f, true, StealthMask.CoverOrCrouch);
            Assert.That(crouched, Is.EqualTo(AwarenessModel.CrouchFactor).Within(1e-4f));

            // 安全岛：无论其他条件如何，累积速率归零
            Assert.That(AwarenessModel.ExposureScale(1f, 1f, false, StealthMask.SafeIsland), Is.EqualTo(0f));
        }

        [Test]
        public void LineOfSight_CoverDoesNotChangeGeometricVerdict()
        {
            // DESIGN §3.3：L4 只改累积速率，不改可见性判定。
            var v = NewVolume(NewProfile(h: 360f, dist: 20f), Vector3.zero);
            var id = StealthIds.Of(v.gameObject);

            Assert.True(v.CanSee(new Vector3(0f, 0f, 5f), QueryOptions.Full, id));
            Assert.True(AwarenessModel.ExposureScale(1f, 1f, true, StealthMask.CoverOrCrouch) > 0f,
                        "蹲伏只应降低速率，不应被当成看不见");
        }

        // ── 察觉累积与衰减（验收 9）─────────────────────────────────

        [Test]
        public void Suspicion_AccumulatesThenDecaysToZero()
        {
            var profile = NewProfile(h: 360f, dist: 20f);
            profile.SetSuspicionGain(0.5f);
            profile.SetDecayPerSecond(0.25f);
            profile.SetSuspicionThreshold(0.35f);
            profile.SetAlertThreshold(0.75f);
            profile.SetLightSensitivity(0f);

            var guard = NewVolume(profile, Vector3.zero);
            var player = NewGo("player", new Vector3(0f, 0f, 5f));
            var state = player.AddComponent<DetectionState>();

            Assert.That(state.TargetId, Is.Not.EqualTo(StealthIds.Of(guard.gameObject)),
                        "玩家与哨兵必须是不同 id，否则辅助台会把目标读成侦察者");

            // 持续可见 1 秒 ⇒ +0.5 ⇒ 越过疑惑阈值
            state.EvaluateOnce(1f);
            Assert.True(state.LastSeen, "玩家在 360° 视野内应被判可见");
            Assert.That(state.Suspicion, Is.EqualTo(0.5f).Within(1e-3f));
            Assert.That(state.Stage, Is.EqualTo(StealthDetectionStage.Suspicious));

            // 再 1 秒 ⇒ 1.0（封顶）⇒ 警觉
            state.EvaluateOnce(1f);
            Assert.That(state.Suspicion, Is.EqualTo(1f).Within(1e-3f));
            Assert.That(state.Stage, Is.EqualTo(StealthDetectionStage.Alerted));

            // 移出视野后必须<b>回落</b>——这条正是本轮抓到的 bug 守卫：
            // 未被看见时若用 default 阈值组（回落速率 0），察觉度会永久钉住、玩家无法脱身。
            player.transform.position = new Vector3(0f, 0f, 60f);
            state.EvaluateOnce(1f);
            Assert.False(state.LastSeen);
            Assert.That(state.Suspicion, Is.EqualTo(0.75f).Within(1e-3f), "应按 Profile 的回落速率下降");

            state.EvaluateOnce(4f);
            Assert.That(state.Suspicion, Is.EqualTo(0f).Within(1e-3f), "足够久后必须归零");
            Assert.That(state.Stage, Is.EqualTo(StealthDetectionStage.Patrol));
        }

        [Test]
        public void PurePreview_NeverMutatesSuspicion()
        {
            var profile = NewProfile(h: 360f, dist: 20f);
            profile.SetSuspicionGain(0.5f);
            profile.SetDecayPerSecond(0.25f);
            profile.SetSuspicionThreshold(0.35f);
            profile.SetAlertThreshold(0.75f);
            profile.SetLightSensitivity(0f);

            NewVolume(profile, Vector3.zero);
            var player = NewGo("player", new Vector3(0f, 0f, 5f));
            var state = player.AddComponent<DetectionState>();

            // dt<=0 = 编辑态 scrub 的纯预览语义：更新可见性，但绝不推进真实累积。
            for (var i = 0; i < 10; i++) state.EvaluateOnce(0f);

            Assert.True(state.LastSeen, "预览应更新可见性");
            Assert.That(state.Suspicion, Is.EqualTo(0f), "纯预览不得改动察觉度");

            // 真实推进一次作对照，证明上一条不是「反正都不动」的假绿。
            state.EvaluateOnce(1f);
            Assert.That(state.Suspicion, Is.EqualTo(0.5f).Within(1e-3f));
        }

        [Test]
        public void SafeIsland_SuppressesAccumulationViaReverseProvider()
        {
            var profile = NewProfile(h: 360f, dist: 20f);
            profile.SetSuspicionGain(0.5f);
            profile.SetSuspicionThreshold(0.35f);
            profile.SetAlertThreshold(0.75f);
            profile.SetLightSensitivity(0f);

            NewVolume(profile, Vector3.zero);
            var player = NewGo("player", new Vector3(0f, 0f, 5f));
            var state = player.AddComponent<DetectionState>();

            // 安全岛数据由<b>辅助台</b>注册（反向注入，AGENTS §5），本包只提供消费面。
            Services.Register<ISafeIslandProvider>(new CoveringIsland());

            state.EvaluateOnce(2f);
            Assert.True(state.LastSeen, "在安全岛内依然几何可见");
            Assert.That(state.Suspicion, Is.EqualTo(0f), "安全岛内不得累积察觉度");

            Services.Clear();
            state.EvaluateOnce(2f);
            Assert.That(state.Suspicion, Is.EqualTo(1f).Within(1e-3f), "撤销安全岛后应正常累积（降级不报错）");
        }

        private sealed class CoveringIsland : ISafeIslandProvider
        {
            public int Count => 1;
            public bool TryGetBounds(int index, out Bounds worldBounds, out string displayName)
            {
                worldBounds = new Bounds(Vector3.zero, new Vector3(1000f, 1000f, 1000f));
                displayName = "All";
                return index == 0;
            }
            public bool Contains(Vector3 worldPoint) => true;
        }

        [Test]
        public void Crouch_SlowsAccumulationComparedToStanding()
        {
            var profile = NewProfile(h: 360f, dist: 20f);
            profile.SetSuspicionGain(1f);
            profile.SetDecayPerSecond(0f);
            profile.SetSuspicionThreshold(0.9f);
            profile.SetAlertThreshold(1f);
            profile.SetLightSensitivity(0f);

            NewVolume(profile, Vector3.zero);

            var stand = NewGo("p-stand", new Vector3(0f, 0f, 5f)).AddComponent<DetectionState>();
            stand.EvaluateOnce(0.5f);

            var crouchGo = NewGo("p-crouch", new Vector3(0f, 0f, 5f));
            var crouch = crouchGo.AddComponent<DetectionState>();
            crouch.Crouching = true;
            crouch.EvaluateOnce(0.5f);

            Assert.That(crouch.Suspicion, Is.LessThan(stand.Suspicion), "蹲伏应让察觉累积更慢");
            Assert.That(stand.Suspicion, Is.EqualTo(0.5f).Within(1e-3f));
            Assert.That(crouch.Suspicion, Is.EqualTo(0.5f * AwarenessModel.CrouchFactor).Within(1e-3f));
        }

        // ── 注册表与跨包接口（M2 起 IVisionWorld/IDetectionWorld 已注册）──

        [Test]
        public void Registries_DoNotLeakDestroyedObjects()
        {
            var profile = NewProfile();
            var v = NewVolume(profile, Vector3.zero);
            var player = NewGo("player", new Vector3(0f, 0f, 5f));
            player.AddComponent<DetectionState>();

            VisionRegistry.MarkDirty();
            DetectionRegistry.MarkDirty();
            Assert.That(VisionRegistry.Volumes.Count, Is.EqualTo(1));
            Assert.That(DetectionRegistry.Targets.Count, Is.EqualTo(1));

            // EditMode 不执行 OnEnable/OnDisable（本包实测过的坑，见 ACCEPTANCE F-5），
            // 所以这里必须手动置脏，否则读到的是上一次缓存。
            v.gameObject.SetActive(false);
            VisionRegistry.MarkDirty();
            Assert.That(VisionRegistry.Volumes.Count, Is.EqualTo(0), "禁用后注册表须剔除");
        }

        [Test]
        public void VisionRegistry_StaleCacheAfterDestroy_SelfHeals()
        {
            // U-8 回归。症状：编辑态切场景后辅助台读到 IVisionWorld.Volumes 恒 0（巡逻单位正常）。
            // 根因＝缓存滞留已销毁引用且 _dirty 不再置真 ⇒ getter 永不重建。
            // ⚠️ 本用例**刻意不调 MarkDirty / ResetForTests**：手动置脏等于把 bug 掩掉，
            //    上一行那条用例正是靠手动置脏才通过的，它测不到这个缺陷。
            var vA = NewVolume(NewProfile(), Vector3.zero);
            Assert.True(ContainsVolume(VisionRegistry.Volumes, vA), "前置：活体应进缓存");

            var goA = vA.gameObject;
            _games.Remove(goA);                       // 交给下面显式销毁，避免 TearDown 二次销毁
            Object.DestroyImmediate(goA, true);       // = 场景卸载时旧物体被销毁，钩子未跑

            var vB = NewVolume(NewProfile(), new Vector3(3f, 0f, 0f));   // = 新场景里的活体

            var after = VisionRegistry.Volumes;
            Assert.True(ContainsVolume(after, vB),
                "缓存里有已销毁引用时必须读时自愈并纳入新活体（修复前缓存停在旧引用 ⇒ 本断言红）");
            Assert.False(HasDestroyedEntry(after), "自愈后不得残留已销毁引用");

            StealthWorld.EnsureRegistered();
            Assert.True(Services.TryGet<IVisionWorld>(out var world), "前置：IVisionWorld 须已注册");
            var idB = StealthIds.Of(vB.gameObject);
            var found = false;
            var list = world.Volumes;                 // 上层用 v==null 过滤死引用 ⇒ 修复前这里恒空
            for (var i = 0; i < list.Count; i++)
                if (list[i].actorId == idB) { found = true; break; }
            Assert.True(found, "上层 IVisionWorld.Volumes 必须枚举到新视野体（修复前此处静默为 0）");
        }

        [Test]
        public void DetectionRegistry_StaleCacheAfterDestroy_SelfHeals()
        {
            // 与上条同构缺陷、同构修法（两个注册表逐字同构 ⇒ 必须对称修，否则 M5 实跑读察觉会再踩一次）。
            var a = NewGo("targetA", Vector3.zero).AddComponent<DetectionState>();
            Assert.True(ContainsTarget(DetectionRegistry.Targets, a), "前置：活目标应进缓存");

            var goA = a.gameObject;
            _games.Remove(goA);
            Object.DestroyImmediate(goA, true);

            var b = NewGo("targetB", new Vector3(3f, 0f, 0f)).AddComponent<DetectionState>();

            var after = DetectionRegistry.Targets;
            Assert.True(ContainsTarget(after, b), "察觉目标缓存滞留死引用时同样必须自愈");
            Assert.False(HasDestroyedTarget(after), "自愈后不得残留已销毁引用");
        }

        // ── 自愈用例专用辅助：按引用查找（不能用 Count，场景基线数量不属于本用例前提）──

        static bool ContainsVolume(IReadOnlyList<VisionVolume> list, VisionVolume v)
        {
            for (var i = 0; i < list.Count; i++) if (list[i] == v) return true;
            return false;
        }

        static bool HasDestroyedEntry(IReadOnlyList<VisionVolume> list)
        {
            for (var i = 0; i < list.Count; i++) if (list[i] == null) return true;   // Unity == 视已销毁为 null
            return false;
        }

        static bool ContainsTarget(IReadOnlyList<DetectionState> list, DetectionState t)
        {
            for (var i = 0; i < list.Count; i++) if (list[i] == t) return true;
            return false;
        }

        static bool HasDestroyedTarget(IReadOnlyList<DetectionState> list)
        {
            for (var i = 0; i < list.Count; i++) if (list[i] == null) return true;
            return false;
        }

        [Test]
        public void VisionWorld_IsRegisteredAndEnumeratesVolumes()
        {
            var profile = NewProfile(h: 100f, v: 50f, dist: 12f, eye: 2f);
            var v = NewVolume(profile, new Vector3(1f, 0f, 3f), 30f);
            var id = StealthIds.Of(v.gameObject);

            StealthWorld.EnsureRegistered();
            Assert.True(Services.TryGet<IVisionWorld>(out var world), "M2 起 IVisionWorld 必须已注册");
            Assert.True(Services.TryGet<IDetectionWorld>(out _), "M2 起 IDetectionWorld 必须已注册");

            var volumes = world.Volumes;
            Assert.That(volumes.Count, Is.EqualTo(1));
            Assert.That(volumes[0].actorId, Is.EqualTo(id), "视野与哨兵同物体时 id 必须同源，否则辅助台对不上号");
            Assert.That(volumes[0].horizontalAngleDegrees, Is.EqualTo(100f).Within(1e-4f));
            Assert.That(volumes[0].maxDistance, Is.EqualTo(12f).Within(1e-4f));
            Assert.That(volumes[0].enabled, Is.True);
            Assert.That(volumes[0].originWorld.y, Is.EqualTo(2f).Within(1e-4f), "原点须含视线高度");
        }

        [Test]
        public void VisionWorld_DisabledVolumeIsFlaggedNotSilentlyDropped()
        {
            var v = NewVolume(NewProfile(), Vector3.zero);
            v.Enabled = false;
            VisionRegistry.MarkDirty();

            StealthWorld.EnsureRegistered();
            var volumes = Services.Get<IVisionWorld>().Volumes;
            Assert.That(volumes.Count, Is.EqualTo(1), "禁用视野仍要枚举出来（由 enabled 标记），辅助台才能区分「关掉」与「不存在」");
            Assert.False(volumes[0].enabled);
            Assert.False(v.CanSee(new Vector3(0f, 0f, 2f), QueryOptions.Full, StealthIds.Of(v.gameObject)),
                        "禁用视野不得判定可见");
        }

        [Test]
        public void VisionWorld_CoreLayerMaskMapsToInternalOptions()
        {
            var v = NewVolume(NewProfile(h: 60f, dist: 20f), Vector3.zero);
            var id = StealthIds.Of(v.gameObject);
            var side = new Vector3(10f, 0f, 0f);   // 侧向 90° ⇒ 在 60° 锥外、10 米距离内

            StealthWorld.EnsureRegistered();
            var world = Services.Get<IVisionWorld>();

            Assert.True(world.CanSee(id, side, StealthQueryLayers.DistanceOnly), "只判距离时应可见");
            Assert.False(world.CanSee(id, side, StealthQueryLayers.Cone), "加上视角锥后应不可见");
            Assert.False(world.CanSee(id, side, StealthQueryLayers.Full));
        }

        [Test]
        public void VisionWorld_CanSeeAt_UsesPoseAtTimeNotCurrentTransform()
        {
            // 哨兵沿 +X 巡逻：t 时刻位姿在路线上，视野也应随该时刻的朝向移动。
            var route = ScriptableObject.CreateInstance<PatrolRoute>();
            _assets.Add(route);
            route.SetDefaultSpeed(4f);
            route.AddWaypoint(Vector3.zero);
            route.AddWaypoint(new Vector3(20f, 0f, 0f));

            var guard = NewGo("guard", Vector3.zero);
            var rootGo = NewGo("route-root", Vector3.zero);
            var root = rootGo.AddComponent<PatrolRouteRoot>();
            root.SetRoute(route);

            var agent = guard.AddComponent<PatrolAgent>();
            agent.RouteRoot = root;
            agent.ApplyToTransform = false;

            var profile = NewProfile(h: 90f, dist: 10f, eye: 0f);
            var v = guard.AddComponent<VisionVolume>();
            v.Profile = profile;

            var id = StealthIds.Of(guard);
            StealthWorld.EnsureRegistered();
            var world = Services.Get<IVisionWorld>();

            // 路线：(0,0,0)→(20,0,0)→回环，速度 4 ⇒ 每段 5s，T 周期 10s。
            Assert.That(route.TotalCycle, Is.EqualTo(10f).Within(1e-3f), "用例前提：T=10s");

            // t=2.5s ⇒ 哨兵在局部 (10,0,0)、朝 +X。判定点取正前方 5 米的 (15,0,0) ⇒ 应可见。
            Assert.True(world.CanSeeAt(id, 2.5f, new Vector3(15f, 0f, 0f), StealthQueryLayers.Full),
                        "应按 t 时刻的巡逻位姿判定，而非当前 transform");

            // 同一时刻，(0,0,0) 在哨兵<b>背后</b>（90° 锥外）⇒ 不可见。
            Assert.False(world.CanSeeAt(id, 2.5f, Vector3.zero, StealthQueryLayers.Full));

            // 关键对照：哨兵的 transform 仍在原点（ApplyToTransform=false）。
            // 若 CanSeeAt 错用当前 transform，(0,0,0) 会零距离命中、(15,0,0) 会落到 90° 侧向而判不可见
            // —— 两条断言合起来才能证明「用的是 t 时刻位姿」。
            Assert.True(world.CanSee(id, Vector3.zero, StealthQueryLayers.DistanceOnly),
                        "当前位姿就在原点，零距离应命中（证明两条路径确有区别）");
            Assert.False(world.CanSee(id, new Vector3(15f, 0f, 0f), StealthQueryLayers.Cone),
                         "按当前位姿（朝 +Z）看 (15,0,0) 在侧向，应判不可见");
        }

        [Test]
        public void CountSeers_AgreesWithPerVolumeVerdicts()
        {
            var profile = NewProfile(h: 120f, dist: 10f);
            var a = NewVolume(profile, Vector3.zero);
            var b = NewVolume(profile, new Vector3(0f, 0f, 40f));   // 太远，看不到目标

            StealthWorld.EnsureRegistered();
            var world = Services.Get<IVisionWorld>();
            var target = new Vector3(0f, 0f, 5f);

            Assert.That(world.CountSeers(-1, target, -1f, StealthQueryLayers.Full), Is.EqualTo(1),
                        "只有一个视野覆盖到目标");
            Assert.That(world.CountSeers(StealthIds.Of(a.gameObject), target, -1f, StealthQueryLayers.Full), Is.EqualTo(1));
            Assert.That(world.CountSeers(StealthIds.Of(b.gameObject), target, -1f, StealthQueryLayers.Full), Is.EqualTo(0));
            Assert.That(world.CountSeers(-1, target, -1f, StealthQueryLayers.DistanceOnly), Is.EqualTo(1));
        }

        [Test]
        public void DetectionWorld_ReadsStateAndResetAllClearsEverything()
        {
            var profile = NewProfile(h: 360f, dist: 20f);
            profile.SetSuspicionGain(1f);
            profile.SetSuspicionThreshold(0.2f);
            profile.SetAlertThreshold(0.6f);
            profile.SetDecayPerSecond(0f);
            profile.SetLightSensitivity(0f);

            NewVolume(profile, Vector3.zero);
            var player = NewGo("player", new Vector3(0f, 0f, 5f));
            var state = player.AddComponent<DetectionState>();

            StealthWorld.EnsureRegistered();
            var world = Services.Get<IDetectionWorld>();

            Assert.That(state.TargetId, Is.GreaterThan(0), "未显式指定 id 时须用实体 id");
            Assert.That(world.SuspicionOf(state.TargetId), Is.EqualTo(0f));

            state.EvaluateOnce(1f);
            Assert.That(world.SuspicionOf(state.TargetId), Is.EqualTo(1f).Within(1e-3f));
            Assert.That(world.StageOf(state.TargetId), Is.EqualTo(StealthAlertStage.Alerted));

            Assert.That(world.SuspicionOf(-999), Is.EqualTo(0f), "未知目标不得抛异常");
            Assert.That(world.StageOf(-999), Is.EqualTo(StealthAlertStage.Patrol));

            world.ResetAll();
            Assert.That(world.SuspicionOf(state.TargetId), Is.EqualTo(0f), "ResetAll 须清空全部累积");
            Assert.That(world.StageOf(state.TargetId), Is.EqualTo(StealthAlertStage.Patrol));
        }

        [Test]
        public void AlertStage_MirrorValuesMatchCoreContract()
        {
            // 适配层直接数值转换，两侧值序一旦分叉会静默错档（stealth ACCEPTANCE 记录过同类坑）。
            Assert.That((int)StealthDetectionStage.Patrol, Is.EqualTo((int)StealthAlertStage.Patrol));
            Assert.That((int)StealthDetectionStage.Suspicious, Is.EqualTo((int)StealthAlertStage.Suspicious));
            Assert.That((int)StealthDetectionStage.Alerted, Is.EqualTo((int)StealthAlertStage.Alerted));
            Assert.That((int)StealthDetectionStage.Searching, Is.EqualTo((int)StealthAlertStage.Searching));

            // 层级掩码值序对齐（core 侧无 None 成员，包内 None=0 表示「不启用任何检查」）。
            Assert.That((int)QueryOptions.DistanceOnly, Is.EqualTo((int)StealthQueryLayers.DistanceOnly));
            Assert.That((int)QueryOptions.ConeOnly, Is.EqualTo((int)StealthQueryLayers.Cone));
            Assert.That((int)QueryOptions.LineOfSight, Is.EqualTo((int)StealthQueryLayers.LineOfSight));
            Assert.That((int)QueryOptions.Full, Is.EqualTo((int)StealthQueryLayers.Full));
        }

        [Test]
        public void Thresholds_CannotInvertThroughWriter()
        {
            var p = NewProfile();
            p.SetSuspicionThreshold(0.9f);
            p.SetAlertThreshold(0.3f);   // 倒挂写入

            Assert.That(p.SuspicionThreshold, Is.LessThanOrEqualTo(p.AlertThreshold),
                        "疑惑阈值必须不超过警觉阈值，否则状态机永远停在疑惑");
        }

        [Test]
        public void StageOf_ZeroThresholdsMeansStageDisabled()
        {
            // default(阈值组) 的三个阈值都是 0：必须解释为「该档未启用」而不是「察觉度 0 即已疑惑」。
            var tuning = default(AwarenessTuning);
            Assert.That(AwarenessModel.StageOf(0f, tuning), Is.EqualTo(StealthDetectionStage.Patrol),
                        "阈值 0 时不得把零察觉读成疑惑档");
            Assert.That(AwarenessModel.StageOf(1f, tuning), Is.EqualTo(StealthDetectionStage.Patrol));
        }

        [Test]
        public void Integrate_IgnoresNonPositiveStepAndClampsRange()
        {
            var tuning = new AwarenessTuning
            {
                decayPerSecond = 0.5f,
                suspicionThreshold = 0.3f,
                alertThreshold = 0.7f
            };

            Assert.That(AwarenessModel.Integrate(0.4f, 0f, 1f, 1f, true, tuning), Is.EqualTo(0.4f).Within(1e-5f));
            Assert.That(AwarenessModel.Integrate(0.4f, -2f, 1f, 1f, true, tuning), Is.EqualTo(0.4f).Within(1e-5f));
            Assert.That(AwarenessModel.Integrate(0.9f, 5f, 1f, 1f, true, tuning), Is.EqualTo(1f), "必须封顶");
            Assert.That(AwarenessModel.Integrate(0.1f, 5f, 0f, 0f, false, tuning), Is.EqualTo(0f), "必须归零");
        }

        [Test]
        public void Geometry_OmnidirectionalVerticalAndDegenerateInputsAreSafe()
        {
            var p = new VisionParams
            {
                maxDistance = 10f,
                horizontalAngleDegrees = 110f,
                verticalAngleDegrees = 60f
            };

            // 目标与视线起点重合：无方向可言，判可见而不是除零。
            Assert.True(VisionGeometry.Evaluate(Vector3.zero, Vector3.forward, Vector3.zero, p, QueryOptions.Full, null));

            // 视线朝向纯向上：水平面不受约束，俯仰仍生效。
            Assert.True(VisionGeometry.InHorizontalCone(new Vector3(0f, 0f, 5f), Vector3.up, 60f));

            // 要求 L3 但没有检查器：保守判不可见（宁可漏看也不误报看见）。
            Assert.False(VisionGeometry.Evaluate(Vector3.zero, Vector3.forward, new Vector3(0f, 0f, 3f), p,
                        QueryOptions.LineOfSight, null));
        }

        [Test]
        public void StealthIds_AreStableAndDistinguishObjects()
        {
            var a = NewGo("a", Vector3.zero);
            var b = NewGo("b", Vector3.one);

            Assert.That(StealthIds.Of(a), Is.EqualTo(StealthIds.Of(a)), "同一物体两次取值须一致");
            Assert.That(StealthIds.Of(a.GetComponent<Transform>()), Is.EqualTo(StealthIds.Of(a)),
                        "组件与所在物体必须同一个 id，否则辅助台按组件反查会失配");
            Assert.That(StealthIds.Of(a), Is.Not.EqualTo(StealthIds.Of(b)));
            Assert.That(StealthIds.Of((GameObject)null), Is.EqualTo(0));
        }
    }
}
