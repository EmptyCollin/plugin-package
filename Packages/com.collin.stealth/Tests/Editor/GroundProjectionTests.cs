using System.Collections.Generic;
using Collin.Stealth;
using NUnit.Framework;
using UnityEngine;

namespace Collin.Stealth.Tests
{
    /// <summary>
    /// 成品质量修正的验收：地面投影边界尺寸、近身察觉、操控数学、撤离区判定。
    /// <para>来自用户实机反馈的三条问题，逐条给出可复算的数值断言：</para>
    /// <list type="number">
    /// <item>转向与镜头共用同一角度 ⇒ <see cref="ThirdPersonRig"/> 把「镜头朝向」与
    /// 「相机相对的移动方向」彻底分离；</item>
    /// <item>NPC 与玩家重叠后丧失目标 ⇒ 近身档 + 身体判定点；</item>
    /// <item>看不出地面边界 ⇒ 投影必须给出<b>与判定同源</b>的地面半径
    /// （旧实现把俯仰角错用成射程上限，14 m 视距只画 ~4.9 m，正是截图里看到的现象）。</item>
    /// </list>
    /// <para>断言写在纯函数层，不依赖物理场景与帧循环。</para>
    /// </summary>
    public class GroundProjectionTests
    {
        readonly List<Object> _assets = new List<Object>();
        readonly List<GameObject> _games = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
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
        }

        /// <summary>
        /// 视野参数：视距 14、眼高 1.7、水平 110°、垂直 60°、遮挡层 0（不依赖物理）。
        /// </summary>
        VisionProfile NewProfile(float maxDist = 14f, float eye = 1.7f,
                                 float prox = 1.8f, float vert = 60f)
        {
            var p = ScriptableObject.CreateInstance<VisionProfile>();
            _assets.Add(p);
            p.SetHorizontalAngle(110f);
            p.SetVerticalAngle(vert);
            p.SetMaxDistance(maxDist);
            p.SetEyeHeight(eye);
            p.SetOccluderMask(0);
            p.SetProximityRadius(prox);
            p.SetLightSensitivity(0f);
            p.SetSuspicionGain(0.5f);
            p.SetDecayPerSecond(0.25f);
            p.SetSuspicionThreshold(0.35f);
            p.SetAlertThreshold(0.8f);
            return p;
        }

        VisionVolume NewVolume(VisionProfile profile, Vector3 foot)
        {
            var go = new GameObject("guard");
            _games.Add(go);
            go.transform.position = foot;
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

        // ── 1. 投影外缘必须等于判定的真实地面半径 ───────────────────

        [Test]
        public void Projection_OuterRadiusMatchesSlantDistanceOnGround()
        {
            // 眼在 (0,1.7,0)，判定点高 0.95 ⇒ Δh = 0.75；斜距 14 ⇒ 水平半径 = sqrt(14² − 0.75²)
            const float maxDist = 14f;
            var eye = new Vector3(0f, 1.7f, 0f);
            var foot = Vector3.zero;
            var verts = new List<Vector3>();
            var idx = new List<int>();

            Assert.True(GroundVisionProjection.Build(eye, 0f, NewProfile(maxDist: maxDist), 1f,
                                                     foot, 0.95f, 24, 0f, 0, verts, idx),
                        "投影应构建成功");

            var expected = Mathf.Sqrt(maxDist * maxDist - 0.75f * 0.75f);
            var outerCount = (verts.Count + 1) / 2;
            var maxGroundR = 0f;
            for (var i = 0; i < outerCount; i++)
            {
                var p = verts[i];
                var r = Mathf.Sqrt(p.x * p.x + p.z * p.z);
                if (r > maxGroundR) maxGroundR = r;
            }

            Assert.That(maxGroundR, Is.EqualTo(expected).Within(0.06f),
                        "地面外缘半径必须等于斜距换算值（旧实现被俯仰角压成 ~4.9m，与实际 14m 不符）");
            Assert.That(maxGroundR, Is.GreaterThan(13.5f), "必须接近满视距，不能再被俯仰角截短");
        }

        [Test]
        public void Projection_ProximityExemptionCollapsesInnerHole()
        {
            // 近身半径 1.8 > Δh/tan(30°) 换算出的锥盲区 ⇒ 内缘归零，投影是连续扇形。
            var eye = new Vector3(0f, 1.7f, 0f);
            var verts = new List<Vector3>();
            var idx = new List<int>();

            Assert.True(GroundVisionProjection.Build(eye, 0f, NewProfile(prox: 1.8f), 1f,
                                                     Vector3.zero, 0.95f, 24, 0f, 0, verts, idx));

            var outer = (verts.Count + 1) / 2;
            for (var i = outer; i < verts.Count; i++)
            {
                var p = verts[i];
                Assert.That(Mathf.Sqrt(p.x * p.x + p.z * p.z), Is.LessThan(0.05f),
                            "近身豁免覆盖盲区时内缘必须收到脚底（连续扇形，无环口）");
            }
        }

        [Test]
        public void Projection_WithoutProximityKeepsBlindRing()
        {
            // 关掉近身豁免：脚下出现锥盲区环口，内缘 = Δh / tan(半垂直角) = 0.75 / tan(30°)
            var eye = new Vector3(0f, 1.7f, 0f);
            var verts = new List<Vector3>();
            var idx = new List<int>();

            Assert.True(GroundVisionProjection.Build(eye, 0f, NewProfile(prox: 0f), 1f,
                                                     Vector3.zero, 0.95f, 24, 0f, 0, verts, idx));

            var outer = (verts.Count + 1) / 2;
            var expected = 0.75f / Mathf.Tan(30f * Mathf.Deg2Rad);
            var inner = verts[outer];
            var r = Mathf.Sqrt(inner.x * inner.x + inner.z * inner.z);
            Assert.That(r, Is.EqualTo(expected).Within(0.05f),
                        "内缘半径必须等于俯仰盲区换算值（这才是『脚边看不见』的真实边界）");
            Assert.That(r, Is.GreaterThan(1.0f));
        }

        [Test]
        public void Projection_AreaIsPositiveAndScalesWithDistance()
        {
            var eye = new Vector3(0f, 1.7f, 0f);
            var a = new List<Vector3>(); var ai = new List<int>();
            var b = new List<Vector3>(); var bi = new List<int>();

            GroundVisionProjection.Build(eye, 0f, NewProfile(maxDist: 10f), 1f, Vector3.zero, 0.95f, 24, 0f, 0, a, ai);
            GroundVisionProjection.Build(eye, 0f, NewProfile(maxDist: 20f), 1f, Vector3.zero, 0.95f, 24, 0f, 0, b, bi);

            var area10 = GroundVisionProjection.PolygonArea(a, ai);
            var area20 = GroundVisionProjection.PolygonArea(b, bi);
            Assert.That(area10, Is.GreaterThan(1f), "投影面积应为正");
            Assert.That(area20, Is.GreaterThan(area10 * 2.5f), "视距翻倍面积应显著增大");
        }

        [Test]
        public void Projection_RejectsDegenerateProfiles()
        {
            var v = new List<Vector3>(); var i = new List<int>();
            Assert.False(GroundVisionProjection.Build(Vector3.up, 0f, null, 1f, Vector3.zero, 0.95f, 24, 0f, 0, v, i),
                         "空资产不得构建");
            Assert.False(GroundVisionProjection.Build(Vector3.up, 0f, NewProfile(maxDist: 0.001f), 1f,
                                                     Vector3.zero, 0.95f, 24, 0f, 0, v, i),
                         "零视距不得构建");
        }

        // ── 2. 近身档：贴脸与正下方必须能察觉（旧行为是失明）──────

        [Test]
        public void Proximity_MakesTargetUnderFeetVisible_WhileConeAloneHidesIt()
        {
            var origin = new Vector3(0f, 1.7f, 0f);
            var fwd = Vector3.forward;
            // 玩家贴身站在我脚下前方 0.3m：方向几乎正下，俯仰差 ~79.7° > 半垂直角 30°
            var under = new Vector3(0f, 0.05f, 0.3f);

            var without = new VisionParams
            {
                maxDistance = 14f,
                horizontalAngleDegrees = 110f,
                verticalAngleDegrees = 60f,
                proximityRadius = 0f
            };
            var with = new VisionParams
            {
                maxDistance = 14f,
                horizontalAngleDegrees = 110f,
                verticalAngleDegrees = 60f,
                proximityRadius = 1.8f
            };

            Assert.False(VisionGeometry.Evaluate(origin, fwd, under, without, QueryOptions.ConeOnly, null),
                         "前提：无近身档时正下方被俯仰闸门判不可见（这正是『重叠后丧失目标』）");
            Assert.True(VisionGeometry.Evaluate(origin, fwd, under, with, QueryOptions.ConeOnly, null),
                        "有近身档时贴脸目标必须可见");
        }

        [Test]
        public void Proximity_StillRespectsOcclusion()
        {
            // 近身档只跳锥，不跳 L3：贴脸但中间有掩体时仍应判不可见。
            var origin = new Vector3(0f, 1.7f, 0f);
            var p = new VisionParams
            {
                maxDistance = 14f,
                horizontalAngleDegrees = 110f,
                verticalAngleDegrees = 60f,
                proximityRadius = 1.8f
            };

            Assert.False(VisionGeometry.Evaluate(origin, Vector3.forward, new Vector3(0f, 0.5f, 1f),
                        p, QueryOptions.Full, (a, b) => false),
                        "近身豁免不得绕过遮挡判定");
        }

        [Test]
        public void VisionVolume_PassesProximityIntoParams()
        {
            var v = NewVolume(NewProfile(prox: 2.5f), Vector3.zero);
            Assert.That(v.Params.proximityRadius, Is.EqualTo(2.5f).Within(1e-4f),
                        "实例参数未把近身半径传给几何层");
            Assert.That(v.EffectiveProximityRadius, Is.EqualTo(2.5f).Within(1e-4f));

            var none = NewVolume(NewProfile(prox: 0f), new Vector3(3f, 0f, 0f));
            Assert.That(none.Params.proximityRadius, Is.EqualTo(0f));
        }

        [Test]
        public void DetectionState_BodyPointUsedByGuardSensing_KeepsTargetWhenOverlapping()
        {
            // 玩家直接站在哨兵身上：脚底在视线正下方，判定点在身体中部 ⇒ 仍应维持目标。
            var profile = NewProfile(maxDist: 14f, prox: 1.8f);
            profile.SetHorizontalAngle(360f);
            profile.SetVerticalAngle(60f);
            var guard = NewVolume(profile, Vector3.zero);
            var guardId = StealthIds.Of(guard.gameObject);

            var playerGo = new GameObject("player");
            _games.Add(playerGo);
            playerGo.transform.position = new Vector3(0f, 0f, 0.4f);   // 贴脸
            var state = playerGo.AddComponent<DetectionState>();

            Assert.That(state.BodyPoint.y, Is.EqualTo(0.95f).Within(1e-4f), "身体判定点高度不对");
            Assert.True(guard.CanSee(state.BodyPoint, QueryOptions.Full, guardId, playerGo.transform),
                        "重叠时必须仍能看见玩家");

            state.EvaluateOnce(1f);
            Assert.That(state.LastSeen, Is.True, "重叠时不应丧失目标");
            Assert.That(state.Suspicion, Is.GreaterThan(0f), "重叠时察觉度应继续上升");
        }

        // ── 3. 操控数学：A/D 是侧移，不是转镜头 ────────────────────

        [Test]
        public void MoveDirection_IsCameraRelative_AndIndependentOfFacing()
        {
            // 镜头朝 +Z（yaw 0）：D 应给世界 +X（镜头右手边）
            AssertVec(new Vector3(1f, 0f, 0f), ThirdPersonRig.MoveDirection(0f, new Vector2(1f, 0f)), 1e-4f,
                      "yaw=0 时 D 应向右");
            // 镜头转 90°（朝 +X）：D 应给世界 -Z，W 应给 +X
            AssertVec(new Vector3(0f, 0f, -1f), ThirdPersonRig.MoveDirection(90f, new Vector2(1f, 0f)), 1e-4f,
                      "yaw=90 时 D 应向右（世界 -Z）");
            AssertVec(new Vector3(1f, 0f, 0f), ThirdPersonRig.MoveDirection(90f, new Vector2(0f, 1f)), 1e-4f,
                      "yaw=90 时 W 应朝镜头前方（世界 +X）");
            // A 与 D 必须严格相反（旧 bug 是 A/D 变成绕自身转，方向不是这样反的）
            AssertVec(-ThirdPersonRig.MoveDirection(37f, new Vector2(1f, 0f)),
                      ThirdPersonRig.MoveDirection(37f, new Vector2(-1f, 0f)), 1e-4f, "A 必须与 D 反向");
        }

        [Test]
        public void MoveDirection_DiagonalNormalizedAndZeroSafe()
        {
            var diag = ThirdPersonRig.MoveDirection(0f, new Vector2(1f, 1f));
            Assert.That(diag.magnitude, Is.EqualTo(1f).Within(1e-4f), "斜向输入不应比正向更快");
            Assert.That(ThirdPersonRig.MoveDirection(123f, Vector2.zero).magnitude, Is.EqualTo(0f).Within(1e-6f));
            // 超过单位长度的输入要夹住，避免斜向 + 外扩键位叠加导致超速
            Assert.That(ThirdPersonRig.MoveDirection(0f, new Vector2(3f, 3f)).magnitude,
                        Is.EqualTo(1f).Within(1e-4f));
        }

        [Test]
        public void Smoothing_IsFrameRateIndependent_AndSnapsOnZeroDt()
        {
            // dt<=0（编辑态预览）必须直接到位，不留缓动残差
            Assert.That(ThirdPersonRig.SmoothAngle(10f, 90f, 12f, 0f), Is.EqualTo(90f).Within(1e-4f));
            Assert.That(ThirdPersonRig.SmoothValue(1f, 5f, 12f, 0f), Is.EqualTo(5f).Within(1e-4f));

            // 同样时长拆成 2 帧与 200 帧，收敛进度应接近（旧 Lerp(a,b,dt*k) 在高帧率下明显更慢）
            float Two(long steps)
            {
                var a = 0f;
                for (var i = 0; i < steps; i++) a = ThirdPersonRig.SmoothAngle(a, 90f, 8f, 1f / steps);
                return a;
            }
            Assert.That(Two(2), Is.EqualTo(Two(200)).Within(6f),
                        "角度平滑必须与帧率无关，否则拖镜头会随时而抖动");
        }

        [Test]
        public void ClampPitch_AcceptsInvertedWindow()
        {
            // 有人把上下限填反时不能每次拖拽来回弹
            Assert.That(ThirdPersonRig.ClampPitch(-40f, 60f, -15f), Is.EqualTo(-15f).Within(1e-4f));
            Assert.That(ThirdPersonRig.ClampPitch(80f, -15f, 60f), Is.EqualTo(60f).Within(1e-4f));
        }

        // ── 4. 撤离区：有终点、可通关、可重开 ─────────────────────

        [Test]
        public void ExitZone_UsesHorizontalRadiusAndHeightWindow()
        {
            var go = new GameObject("exit");
            _games.Add(go);
            go.transform.position = new Vector3(4f, 0f, 16f);
            var zone = go.AddComponent<StealthExitZone>();

            Assert.True(zone.Contains(new Vector3(4f, 0.05f, 16f)), "区内应判命中");
            Assert.True(zone.Contains(new Vector3(6f, 0.05f, 16f)), "半径内（2m）应命中");
            Assert.False(zone.Contains(new Vector3(9f, 0.05f, 16f)), "超出半径不得命中");
            Assert.False(zone.Contains(new Vector3(4f, 9f, 16f)), "高度差过大说明玩家已跳离本层");
        }

        [Test]
        public void ExitZone_RequiresDwellAndIgnoresPreviewSteps()
        {
            var go = new GameObject("exit");
            _games.Add(go);
            var zone = go.AddComponent<StealthExitZone>();

            // 预览（dt<=0）不得推进通关
            Assert.False(zone.Tick(true, 0f), "纯预览不得推进驻留");
            Assert.False(zone.Reached);

            Assert.False(zone.Tick(true, 0.2f));
            Assert.False(zone.Tick(false, 0.2f), "离开区域应打断驻留");
            Assert.False(zone.Tick(true, 0.2f));

            for (var i = 0; i < 6; i++) zone.Tick(true, 0.2f);
            Assert.True(zone.Reached, "连续驻留超过阈值必须通关");
            Assert.That(zone.DwellProgress, Is.EqualTo(1f).Within(1e-4f));

            zone.ResetZone();
            Assert.False(zone.Reached, "复原后应可再玩一局");
        }

        [Test]
        public void ExitZone_InstantEscapeWhenDwellZero()
        {
            var go = new GameObject("exit");
            _games.Add(go);
            var zone = go.AddComponent<StealthExitZone>();
            var so = new UnityEditor.SerializedObject(zone);
            so.FindProperty("_dwellSeconds").floatValue = 0f;
            so.ApplyModifiedPropertiesWithoutUndo();

            Assert.True(zone.Tick(true, 0.016f), "dwell=0 时踏入即通关");
        }

        [Test]
        public void GroundProjectionComponent_ReportsCoverageWithoutPhysics()
        {
            // 渲染器只在 Play 态绘制，但覆盖率与几何同源；EditMode 下手动驱动 Build 校验口径一致。
            var profile = NewProfile(maxDist: 14f);
            var v = NewVolume(profile, Vector3.zero);
            Assert.That(v.OccluderMaskValue, Is.EqualTo(0), "用例前提：不依赖物理遮挡");

            var verts = new List<Vector3>(); var idx = new List<int>();
            var ok = GroundVisionProjection.Build(v.OriginWorld, 0f, profile, v.DistanceMultiplier,
                                                 v.transform.position,
                                                 GroundVisionProjection.DefaultProbeHeight,
                                                 32, 0f, v.OccluderMaskValue, verts, idx);
            Assert.True(ok);
            Assert.That(verts.Count, Is.EqualTo(66), "外弧 33 + 内弧 33");
            Assert.That(idx.Count, Is.EqualTo(32 * 6), "每格两个三角形");
            Assert.That(verts[0].y, Is.EqualTo(0f).Within(1e-4f), "投影必须贴在地面上");
        }
    }
}
