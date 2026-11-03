using System.Collections.Generic;
using Collin.Stealth;
using NUnit.Framework;
using UnityEngine;

namespace Collin.Stealth.Tests
{
    /// <summary>
    /// M1 验收：巡逻位姿必须是「时间的纯函数」（DESIGN D-01）。
    /// <para>断言全部写在**数据层**（局部坐标 + 周期表），不写 transform——
    /// B 包的编辑态 T 周期回溯预览依赖的正是这一层的确定性；
    /// 若在 transform 层断言，出错时无法区分「求值错」与「应用错」。</para>
    /// </summary>
    public class PatrolRouteTests
    {
        /// <summary>
        /// 用例创建的资产与 GameObject 必须回收；时钟还带有全局单例，
        /// 因此回收前先 SetActive(false) 触发 OnDisable，避免静态引用泄漏到后续用例。
        /// </summary>
        readonly List<GameObject> _games = new List<GameObject>();
        readonly List<Object> _assets = new List<Object>();

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

        PatrolRoute NewRoute()
        {
            var r = ScriptableObject.CreateInstance<PatrolRoute>();
            _assets.Add(r);
            return r;
        }

        PatrolRouteRoot NewRoot(PatrolRoute route, Vector3 rootPosition, float rootYaw = 0f)
        {
            var go = new GameObject("route-root");
            _games.Add(go);
            go.transform.position = rootPosition;
            go.transform.rotation = Quaternion.Euler(0f, rootYaw, 0f);
            var root = go.AddComponent<PatrolRouteRoot>();
            root.SetRoute(route);
            return root;
        }

        PatrolAgent NewAgent(PatrolRouteRoot root, string name)
        {
            var go = new GameObject(name);
            _games.Add(go);
            var agent = go.AddComponent<PatrolAgent>();
            agent.RouteRoot = root;
            agent.ApplyToTransform = false;
            return agent;
        }

        StealthClock NewClock()
        {
            var go = new GameObject("clock");
            _games.Add(go);
            return go.AddComponent<StealthClock>();
        }

        /// <summary>按分量断言，规避 NUnit 对非数值类型的容差重载不确定。</summary>
        static void AssertVec(Vector3 expected, Vector3 actual, float delta, string msg = null)
        {
            Assert.AreEqual(expected.x, actual.x, delta, (msg ?? string.Empty) + " x");
            Assert.AreEqual(expected.y, actual.y, delta, (msg ?? string.Empty) + " y");
            Assert.AreEqual(expected.z, actual.z, delta, (msg ?? string.Empty) + " z");
        }

        /// <summary>正方形路线：边长 edge、默认速度 speed、每点停留 dwell。</summary>
        static void FillSquare(PatrolRoute route, float edge = 10f, float speed = 2f, float dwell = 1f)
        {
            route.SetDefaultSpeed(speed);
            route.AddWaypoint(new Vector3(0f, 0f, 0f), dwell);
            route.AddWaypoint(new Vector3(edge, 0f, 0f), dwell);
            route.AddWaypoint(new Vector3(edge, 0f, edge), dwell);
            route.AddWaypoint(new Vector3(0f, 0f, edge), dwell);
        }

        // ── 验收 1：确定性 ────────────────────────────────────────────

        [Test]
        public void EvaluatePose_IsDeterministic_AcrossManyCalls()
        {
            var route = NewRoute();
            FillSquare(route);
            var cycle = route.TotalCycle;
            Assert.Greater(cycle, 0f);

            for (var i = 0; i < 1000; i++)
            {
                var t = cycle * (i % 97) / 97f + 0.013f;
                var a = route.EvaluatePose(t);
                var b = route.EvaluatePose(t);

                AssertVec(a.localPosition, b.localPosition, 0f, "t=" + t + " 位置不可复现");
                Assert.AreEqual(a.localYawDegrees, b.localYawDegrees, 0f, "t=" + t + " 朝向不可复现");
                Assert.AreEqual(a.legIndex, b.legIndex, "段索引不可复现");
                Assert.AreEqual(a.isDwelling, b.isDwelling, "停留标志不可复现");
            }
        }

        [Test]
        public void EvaluatePose_RepeatsEveryCycle()
        {
            var route = NewRoute();
            FillSquare(route);
            var cycle = route.TotalCycle;
            var basePose = route.EvaluatePose(1.234f);

            for (var k = 1; k <= 5; k++)
            {
                var shifted = route.EvaluatePose(1.234f + cycle * k);
                AssertVec(basePose.localPosition, shifted.localPosition, 1e-4f, "第 " + k + " 个周期未复原");
            }
        }

        // ── 验收 2：T 周期 = Σ(段长/速度 + 停留) ──────────────────────

        [Test]
        public void TotalCycle_MatchesSumOfTravelAndDwell()
        {
            var route = NewRoute();
            const float edge = 10f, speed = 2f, dwell = 1f;
            FillSquare(route, edge, speed, dwell);

            // 闭环 4 段，每段 = 出发点停留 1s + 行程 10/2=5s ⇒ 4 × 6 = 24
            Assert.AreEqual(4f * (edge / speed + dwell), route.TotalCycle, 1e-4f);
        }

        [Test]
        public void TotalCycle_RespectsPerSegmentSpeedOverride()
        {
            var route = NewRoute();
            route.SetDefaultSpeed(2f);
            route.AddWaypoint(new Vector3(0f, 0f, 0f));
            route.AddWaypoint(new Vector3(10f, 0f, 0f), 0f, 5f);   // 本段单独 5 → 2s
            route.AddWaypoint(new Vector3(20f, 0f, 0f));

            // 段0 10/2=5；段1 10/5=2；闭合段 20/2=10
            Assert.AreEqual(5f + 2f + 10f, route.TotalCycle, 1e-4f);
        }

        [Test]
        public void TotalCycle_PingPong_CoversBothDirections()
        {
            var route = NewRoute();
            route.SetDefaultSpeed(2f);
            route.SetLoopMode(PatrolLoopMode.PingPong);
            route.AddWaypoint(new Vector3(0f, 0f, 0f), 0f);
            route.AddWaypoint(new Vector3(10f, 0f, 0f), 2f);
            route.AddWaypoint(new Vector3(20f, 0f, 0f), 0f);

            // 去程 0→1 (0+5) + 1→2 (2+5)；返程 2→1 (0+5) + 1→0 (2+5) = 24
            Assert.AreEqual(24f, route.TotalCycle, 1e-4f);
        }

        [Test]
        public void ChangingLayout_RebuildsTable()
        {
            var route = NewRoute();
            FillSquare(route);
            var before = route.TotalCycle;

            route.SetWaypointPosition(1, new Vector3(20f, 0f, 0f));
            var after = route.TotalCycle;

            Assert.That(after, Is.Not.EqualTo(before).Within(1e-3f), "改路点后周期未更新（缓存未失效）");
        }

        // ── 验收 3：四相位落点 ────────────────────────────────────────

        [Test]
        public void EvaluatePose_LandsOnWaypointsAtQuarterPhases()
        {
            var route = NewRoute();
            const float edge = 12f;
            route.SetDefaultSpeed(3f);
            route.AddWaypoint(new Vector3(0f, 0f, 0f));
            route.AddWaypoint(new Vector3(edge, 0f, 0f));
            route.AddWaypoint(new Vector3(edge, 0f, edge));
            route.AddWaypoint(new Vector3(0f, 0f, edge));

            var cycle = route.TotalCycle;
            Assert.AreEqual(4f * (edge / 3f), cycle, 1e-4f);

            var expected = new[]
            {
                new Vector3(0f, 0f, 0f),
                new Vector3(edge, 0f, 0f),
                new Vector3(edge, 0f, edge),
                new Vector3(0f, 0f, edge)
            };

            for (var q = 0; q < 4; q++)
            {
                var pose = route.EvaluatePose(cycle * q / 4f);
                AssertVec(expected[q], pose.localPosition, 0.05f, "1/4 相位 " + q + " 落点错误");
            }
        }

        [Test]
        public void EvaluatePose_IsDwellingDuringStopWindow()
        {
            var route = NewRoute();
            route.SetDefaultSpeed(5f);
            route.AddWaypoint(Vector3.zero, 2f);
            route.AddWaypoint(new Vector3(10f, 0f, 0f), 0f);

            // 段0 = 停留 2s + 行程 2s
            var during = route.EvaluatePose(1f);
            Assert.IsTrue(during.isDwelling, "停留窗口内标志应为 true");
            AssertVec(Vector3.zero, during.localPosition, 1e-5f, "停留期间不应位移");

            var mid = route.EvaluatePose(3f);
            Assert.IsFalse(mid.isDwelling);
            AssertVec(new Vector3(5f, 0f, 0f), mid.localPosition, 1e-4f, "行程中点应在 5");

            // 周期 = 段0(停留2 + 行程2) + 段1(行程2) = 6。t=4 恰为段1 起点，即已抵达 wp1。
            var arrived = route.EvaluatePose(4f);
            Assert.AreEqual(1, arrived.legIndex, "t=4 应已进入下一段");
            AssertVec(new Vector3(10f, 0f, 0f), arrived.localPosition, 1e-4f, "段末应抵达下一路点");
        }

        [Test]
        public void EvaluatePose_WrapsNegativeAndOvershootTime()
        {
            var route = NewRoute();
            FillSquare(route);
            var cycle = route.TotalCycle;

            var normal = route.EvaluatePose(3.3f);
            AssertVec(normal.localPosition, route.EvaluatePose(3.3f - cycle).localPosition, 1e-4f, "负时间取模失败");
            AssertVec(normal.localPosition, route.EvaluatePose(3.3f + cycle * 3f).localPosition, 1e-4f, "超周期取模失败");
        }

        [Test]
        public void EvaluatePose_HandlesDegenerateRoutes()
        {
            var empty = NewRoute();
            Assert.AreEqual(0f, empty.TotalCycle);
            Assert.IsFalse(empty.EvaluatePose(1.5f).HasLeg, "空路线不应有有效段");

            var single = NewRoute();
            single.SetDefaultSpeed(2f);
            single.AddWaypoint(new Vector3(4f, 0f, 1f), 0.5f);
            AssertVec(new Vector3(4f, 0f, 1f), single.EvaluatePose(0.1f).localPosition, 1e-5f, "单点路线应驻守该点");

            var duplicate = NewRoute();
            duplicate.SetDefaultSpeed(2f);
            duplicate.AddWaypoint(Vector3.zero);
            duplicate.AddWaypoint(Vector3.zero);
            AssertVec(Vector3.zero, duplicate.EvaluatePose(0.1f).localPosition, 1e-5f, "零长段应停在原地");
        }

        [Test]
        public void EvaluatePose_YawFollowsSegmentDirection()
        {
            var route = NewRoute();
            route.SetDefaultSpeed(4f);
            route.AddWaypoint(Vector3.zero);
            route.AddWaypoint(new Vector3(10f, 0f, 0f));   // +X ⇒ yaw 90
            route.AddWaypoint(new Vector3(10f, 0f, 10f));  // +Z ⇒ yaw 0

            Assert.AreEqual(90f, route.EvaluatePose(0.5f).localYawDegrees, 1e-3f);
            Assert.AreEqual(0f, route.EvaluatePose(3.5f).localYawDegrees, 1e-3f);
        }

        // ── 验收 14（静态部分）：局部坐标 + 根搬移语义 ────────────────

        [Test]
        public void MovingRoot_TranslatesWholeTrajectory_WithoutChangingShape()
        {
            var route = NewRoute();
            FillSquare(route, 10f, 2f, 1f);

            var offset = new Vector3(37.5f, 2f, -14f);
            var rootA = NewRoot(route, Vector3.zero);
            var rootB = NewRoot(route, offset);

            for (var i = 0; i < 40; i++)
            {
                var t = route.TotalCycle * i / 40f;
                var wa = rootA.ToWorld(route.EvaluatePose(t));
                var wb = rootB.ToWorld(route.EvaluatePose(t));
                AssertVec(wa.position + offset, wb.position, 1e-4f, "移动路线根后轨迹未整体平移（t=" + t + "）");
            }
        }

        [Test]
        public void RootYaw_RotatesTrajectoryAroundRoot()
        {
            var route = NewRoute();
            route.SetDefaultSpeed(2f);
            route.AddWaypoint(Vector3.zero);
            route.AddWaypoint(new Vector3(10f, 0f, 0f));

            var rootPos = new Vector3(5f, 0f, 5f);
            var root = NewRoot(route, rootPos, 90f);

            var local = route.EvaluatePose(2.5f);
            AssertVec(new Vector3(5f, 0f, 0f), local.localPosition, 1e-4f, "段中点应在局部 (5,0,0)");

            var world = root.ToWorld(local);
            // Unity 绕 Y 旋转 θ 把 +X 映到 (cosθ,0,-sinθ)：θ=90 ⇒ 局部 (5,0,0) → 世界 (0,0,-5)。
            // 根在 (5,0,5) ⇒ 期望世界点 (5,0,0)。
            AssertVec(new Vector3(5f, 0f, 0f), world.position, 1e-3f, "路线根旋转未作用于轨迹");

            // 世界朝向 = 根朝向 + 段朝向（段0 沿局部 +X ⇒ 90°）
            Assert.AreEqual(180f, Mathf.Repeat(world.rotation.eulerAngles.y, 360f), 1e-2f);
        }

        [Test]
        public void BakeWaypointsFromWorld_ConvertsToRelativeCoordinates()
        {
            var route = NewRoute();
            var root = NewRoot(route, new Vector3(100f, 0f, 200f));

            var world = new List<Vector3>
            {
                new Vector3(100f, 0f, 200f),
                new Vector3(110f, 0f, 200f),
                new Vector3(110f, 0f, 210f)
            };

            Assert.AreEqual(3, root.BakeWaypointsFromWorld(world));
            AssertVec(Vector3.zero, route.GetWaypointPosition(0), 1e-4f, "首点应换算为局部原点");
            AssertVec(new Vector3(10f, 0f, 0f), route.GetWaypointPosition(1), 1e-4f);
            AssertVec(new Vector3(10f, 0f, 10f), route.GetWaypointPosition(2), 1e-4f);
        }

        // ── 轨迹采样（B 包分层绘制须复用同一口径）────────────────────

        [Test]
        public void BuildTrajectorySamples_HonoursSpacingAndClosesLoop()
        {
            var route = NewRoute();
            route.SetDefaultSpeed(2f);
            route.AddWaypoint(Vector3.zero);
            route.AddWaypoint(new Vector3(10f, 0f, 0f));

            var samples = new List<PatrolRoute.TrajectorySample>();
            route.BuildTrajectorySamples(1f, samples);

            Assert.Greater(samples.Count, 10, "1 米间隔应至少 10 个样本");

            for (var i = 1; i < samples.Count; i++)
            {
                var d = (samples[i].localPosition - samples[i - 1].localPosition).magnitude;
                Assert.LessOrEqual(d, 1.01f, "第 " + i + " 个样本间隔超过请求值");
                Assert.GreaterOrEqual(samples[i].time, samples[i - 1].time, "采样时间非单调");
            }

            // 闭环：末样本应回到起点（闭合段 1→0 的终点）。
            AssertVec(Vector3.zero, samples[samples.Count - 1].localPosition, 1e-3f, "闭环末点错误");
        }

        [Test]
        public void BuildTrajectorySamples_MatchesEvaluatePoseAtSampleTimes()
        {
            var route = NewRoute();
            FillSquare(route, 8f, 2f, 0.5f);

            var samples = new List<PatrolRoute.TrajectorySample>();
            route.BuildTrajectorySamples(0.7f, samples);

            foreach (var s in samples)
            {
                var pose = route.EvaluatePose(s.time);
                AssertVec(s.localPosition, pose.localPosition, 1e-3f,
                    "采样点与逐点求值不一致（t=" + s.time + "）");
            }
        }

        [Test]
        public void BuildTrajectorySamples_ClearsOutputAndToleratesEmptyRoute()
        {
            var route = NewRoute();
            var samples = new List<PatrolRoute.TrajectorySample>
            {
                new PatrolRoute.TrajectorySample()
            };
            route.BuildTrajectorySamples(1f, samples);
            Assert.AreEqual(0, samples.Count, "传入容器未清空");
        }

        // ── PatrolAgent：实例级差异数据（§0.1 原则 2）────────────────

        [Test]
        public void AgentPhaseOffset_StaggersInstancesOnSharedRoute()
        {
            var route = NewRoute();
            FillSquare(route, 10f, 2f, 1f);
            var root = NewRoot(route, Vector3.zero);

            var a = NewAgent(root, "npc-a");
            var b = NewAgent(root, "npc-b");
            b.PhaseOffset = route.TotalCycle / 2f;

            var pa = a.EvaluateLocal(1f);
            var pb = b.EvaluateLocal(1f);
            AssertVec(new Vector3(0f, 0f, 0f), pa.localPosition, 1e-4f, "a 在 t=1 应刚结束停留于 wp0");
            AssertVec(new Vector3(10f, 0f, 10f), pb.localPosition, 1e-4f, "b 相位错开半个周期应在对角路点");

            // 相位可预测：b(t) == a(t + T/2)
            AssertVec(pb.localPosition, a.EvaluateLocal(1f + route.TotalCycle / 2f).localPosition,
                      1e-4f, "相位偏移不可复现");
        }

        [Test]
        public void AgentSpeedMultiplier_ScalesCycleAndPositions()
        {
            var route = NewRoute();
            route.SetDefaultSpeed(2f);
            route.AddWaypoint(Vector3.zero);
            route.AddWaypoint(new Vector3(10f, 0f, 0f));
            var root = NewRoot(route, Vector3.zero);
            var agent = NewAgent(root, "npc");

            Assert.AreEqual(route.TotalCycle, agent.EffectiveCycle, 1e-4f, "倍数 1 时有效周期应等于路线周期");

            var slow = agent.EvaluateLocal(1f);
            agent.SpeedMultiplier = 2f;

            Assert.AreEqual(route.TotalCycle / 2f, agent.EffectiveCycle, 1e-4f, "2× 速度应把有效周期减半");
            AssertVec(slow.localPosition, agent.EvaluateLocal(0.5f).localPosition, 1e-4f,
                      "2× 速度下 t=0.5 应与原 t=1 等价");
        }

        [Test]
        public void AgentWithoutRoute_DoesNotThrow()
        {
            var go = new GameObject("npc");
            _games.Add(go);
            var agent = go.AddComponent<PatrolAgent>();

            Assert.IsFalse(agent.EvaluateLocal(3f).HasLeg);
            var pose = agent.EvaluateWorld(3f);
            AssertVec(go.transform.position, pose.position, 1e-5f, "无路线时应保持原地");
        }

        // ── StealthClock：Manual 模式是编辑态回溯的唯一入口 ─────────

        [Test]
        public void ClockManualMode_ExposesExternalTimeWrite()
        {
            var clock = NewClock();
            Assert.AreSame(clock, StealthClock.Instance, "时钟应登记为全局单例");

            clock.Mode = StealthClockMode.Manual;
            clock.ManualTime = 12.5f;
            Assert.AreEqual(12.5f, clock.Current, 1e-5f);

            clock.ManualTime = -3f;
            Assert.AreEqual(0f, clock.Current, "Manual 时间不得为负");
        }

        [Test]
        public void ClockAutoMode_ClampsExternalWrites()
        {
            var clock = NewClock();
            clock.Mode = StealthClockMode.Auto;
            clock.SetAutoTime(-5f);
            Assert.AreEqual(0f, clock.Current, "Auto 时间不得为负");

            clock.SetAutoTime(5f);
            Assert.AreEqual(5f, clock.Current, 1e-5f);

            clock.TimeScale = -1f;
            Assert.AreEqual(0f, clock.TimeScale, "时间缩放不得为负");
        }

        [Test]
        public void ClockDestroyingObject_ClearsSingleton()
        {
            var clock = NewClock();
            Assert.AreSame(clock, StealthClock.Instance, "编辑态下应能兜底找到场景时钟");

            // 真正要防的是「销毁后仍持有陈旧引用」导致后续用例被污染。
            Object.DestroyImmediate(clock.gameObject, true);
            Assert.IsNull(StealthClock.Instance, "时钟被销毁后单例仍指向已销毁对象");
        }

        // ── 编辑态回溯契约（B 包滑动条依赖，验收 11 的前置）─────────

        [Test]
        public void ManualClock_ScrubsFullCycleWithoutPlayMode()
        {
            var route = NewRoute();
            FillSquare(route, 10f, 2f, 1f);
            var root = NewRoot(route, Vector3.zero);
            var agent = NewAgent(root, "npc");
            var clock = NewClock();
            clock.Mode = StealthClockMode.Manual;

            var previous = agent.EvaluateLocal(0f).localPosition;
            var movedSteps = 0;
            var steps = 100;
            for (var i = 1; i <= steps; i++)
            {
                clock.ManualTime = route.TotalCycle * i / steps;
                var now = agent.EvaluateLocal(clock.Current).localPosition;
                if ((now - previous).sqrMagnitude > 1e-8f) movedSteps++;
                previous = now;
            }

            Assert.GreaterOrEqual(movedSteps, 40,
                "整周期扫描中位移样本过少 ⇒ 编辑态回溯预览会显得卡顿/不动");
        }
    }
}
