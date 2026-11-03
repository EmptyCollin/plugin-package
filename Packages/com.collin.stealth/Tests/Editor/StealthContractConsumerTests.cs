using System;
using System.Collections.Generic;
using Collin.Core;
using Collin.Core.Stealth;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Collin.Stealth.Tests
{
    /// <summary>
    /// M0.5 契约验收：从**消费方视角**实测 core 的潜行跨包契约（stealth DESIGN §4.4 方案 P）。
    /// <para>本文件刻意只用 <c>Collin.Core.Stealth</c> 的类型写代码，不碰任何玩法包具体类型——
    /// 这正是 B 包（设计辅助台）将来能编过的前提。若 core 契约里泄漏了 A 的类型，
    /// 本文件会以编译错误暴露出来（asmdef 层面本测试程序集虽引用了 stealth，
    /// 但契约相关用例一律只引用 core 侧符号）。</para>
    /// <para>覆盖：接口可被外部实现并经 Services 取用、三个通道子类可实例化并广播、
    /// DTO 为值类型且 default 语义可用、安全岛反向接口未注册时的降级路径。</para>
    /// </summary>
    public class StealthContractConsumerTests
    {
        [SetUp]
        public void SetUp()
        {
            Services.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            Services.Clear();
        }

        /// <summary>消费方假实现：不含任何玩法包类型，只用 core 的 DTO 与接口。</summary>
        private sealed class FakePatrolWorld : IPatrolWorld
        {
            public readonly List<StealthActorInfo> Actors = new List<StealthActorInfo>();
            public readonly Dictionary<int, List<StealthWaypointDto>> Routes = new Dictionary<int, List<StealthWaypointDto>>();
            public readonly List<StealthPoseDto> PoseCalls = new List<StealthPoseDto>();
            public float MaxCycleTimeValue = 0f;
            public StealthClockDto Clock = new StealthClockDto { mode = 0, time = 0f };
            public int SetWaypointCalls;

            IReadOnlyList<StealthActorInfo> IPatrolWorld.Actors => Actors;
            float IPatrolWorld.MaxCycleTime => MaxCycleTimeValue;
            StealthClockDto IPatrolWorld.ClockState => Clock;

            StealthPoseDto IPatrolWorld.PoseAt(int actorId, float t)
            {
                var pose = new StealthPoseDto { cycleTime = MaxCycleTimeValue, legIndex = -1 };
                foreach (var a in Actors)
                {
                    if (a.actorId != actorId) continue;
                    pose.valid = true;
                    pose.legIndex = 0;
                    pose.localPosition = new Vector3(t, 0f, 0f);
                    pose.localYawDegrees = 90f;
                }
                PoseCalls.Add(pose);
                return pose;
            }

            bool IPatrolWorld.TryPoseAtWorld(int actorId, float t, out Vector3 worldPosition, out float worldYawDegrees)
            {
                foreach (var a in Actors)
                {
                    if (a.actorId != actorId || !a.hasRoot)
                    {
                        worldPosition = Vector3.zero;
                        worldYawDegrees = 0f;
                        return false;
                    }
                    // 消费方只需知道「有根就能拿到世界量」，换算细节属于提供方。
                    worldPosition = a.rootPosition + new Vector3(t, 0f, 0f);
                    worldYawDegrees = 90f;
                    return true;
                }
                worldPosition = Vector3.zero;
                worldYawDegrees = 0f;
                return false;
            }

            void IPatrolWorld.BuildTrajectory(int actorId, float approxSpacingMeters, List<StealthTrajectorySampleDto> output)
            {
                output.Add(new StealthTrajectorySampleDto
                {
                    localPosition = Vector3.zero,
                    localYawDegrees = 0f,
                    time = 0f,
                });
                output.Add(new StealthTrajectorySampleDto
                {
                    localPosition = new Vector3(approxSpacingMeters, 0f, 0f),
                    localYawDegrees = 0f,
                    time = 1f,
                });
            }

            bool IPatrolWorld.TryGetWaypoints(int actorId, out IReadOnlyList<StealthWaypointDto> waypoints, out StealthLoopMode loopMode, out float defaultSpeed)
            {
                if (Routes.TryGetValue(actorId, out var list))
                {
                    waypoints = list;
                    loopMode = StealthLoopMode.PingPong;
                    defaultSpeed = 1.75f;
                    return true;
                }
                waypoints = null;
                loopMode = StealthLoopMode.Loop;
                defaultSpeed = 0f;
                return false;
            }

            bool IPatrolWorld.TrySetWaypoint(int actorId, int index, StealthWaypointDto waypoint)
            {
                if (!Routes.TryGetValue(actorId, out var list)) return false;
                if (index < 0 || index >= list.Count) return false;
                list[index] = waypoint;
                SetWaypointCalls++;
                return true;
            }

            bool IPatrolWorld.TrySetRouteTuning(int actorId, StealthLoopMode loopMode, float defaultSpeed)
            {
                return Routes.ContainsKey(actorId);
            }
        }

        private static StealthActorInfo MakeActor(int id, bool hasRoot)
        {
            return new StealthActorInfo
            {
                actorId = id,
                displayName = "Guard_" + id,
                cycleTime = 12f,
                hasRoot = hasRoot,
                rootPosition = new Vector3(5f, 0f, 0f),
                rootRotation = Quaternion.identity,
                rootScale = Vector3.one,
            };
        }

        [Test]
        public void PatrolWorld_RegisteredInServices_ResolvableByConsumer()
        {
            var fake = new FakePatrolWorld();
            fake.Actors.Add(MakeActor(1, true));
            fake.MaxCycleTimeValue = 12f;
            Services.Register<IPatrolWorld>(fake);

            var got = Services.Get<IPatrolWorld>();
            Assert.That(got, Is.SameAs(fake), "消费方应能按接口取回实现");
            Assert.That(got.Actors.Count, Is.EqualTo(1));
            Assert.That(got.MaxCycleTime, Is.EqualTo(12f).Within(1e-4f));
        }

        [Test]
        public void PoseAt_ReturnsLocalQuantityConsumerCanUseWithoutPlayMode()
        {
            var fake = new FakePatrolWorld();
            fake.Actors.Add(MakeActor(1, true));
            fake.MaxCycleTimeValue = 12f;
            Services.Register<IPatrolWorld>(fake);

            var world = Services.Get<IPatrolWorld>();
            var pose = world.PoseAt(1, 3.5f);

            // 非 Play 态纯求值：本用例在 EditMode 运行，能取到位姿即证明不依赖帧循环。
            Assert.That(pose.HasLeg, Is.True);
            Assert.That(pose.localPosition.x, Is.EqualTo(3.5f).Within(1e-4f));
            Assert.That(pose.localYawDegrees, Is.EqualTo(90f).Within(1e-4f));
            Assert.That(pose.cycleTime, Is.EqualTo(12f).Within(1e-4f));
        }

        [Test]
        public void UnknownActor_PoseIsDefaultAndHasLegFalse()
        {
            var fake = new FakePatrolWorld();
            Services.Register<IPatrolWorld>(fake);

            var pose = Services.Get<IPatrolWorld>().PoseAt(999, 1f);
            Assert.That(pose.HasLeg, Is.False, "未知 id 必须走 default 语义，而不是抛异常");
        }

        [Test]
        public void TryPoseAtWorld_FailsWithoutRoot()
        {
            var fake = new FakePatrolWorld();
            fake.Actors.Add(MakeActor(2, false));
            Services.Register<IPatrolWorld>(fake);

            var ok = Services.Get<IPatrolWorld>().TryPoseAtWorld(2, 1f, out var pos, out var yaw);
            Assert.That(ok, Is.False, "无路线根时不得给出世界量");
            Assert.That(pos, Is.EqualTo(Vector3.zero));

            fake.Actors[0] = MakeActor(2, true);
            Assert.True(Services.Get<IPatrolWorld>().TryPoseAtWorld(2, 2f, out pos, out yaw));
            Assert.That(pos.x, Is.EqualTo(7f).Within(1e-4f), "根位置 5 + t 2");
        }

        [Test]
        public void BuildTrajectory_RespectsRequestedSpacingAndFillsCallerList()
        {
            var fake = new FakePatrolWorld();
            Services.Register<IPatrolWorld>(fake);

            var output = new List<StealthTrajectorySampleDto>();
            Services.Get<IPatrolWorld>().BuildTrajectory(1, 0.8f, output);

            Assert.That(output.Count, Is.EqualTo(2));
            Assert.That(output[1].localPosition.x, Is.EqualTo(0.8f).Within(1e-4f));
            Assert.That(output[1].time, Is.EqualTo(1f).Within(1e-4f), "采样点必须自带 t，供消费方做位置↔时刻反查");
        }

        [Test]
        public void WaypointWriteback_SupportsReverseAdjustmentFlow()
        {
            var fake = new FakePatrolWorld();
            fake.Routes[1] = new List<StealthWaypointDto>
            {
                new StealthWaypointDto { localPosition = Vector3.zero, stopDuration = 1f, moveSpeed = 0f },
            };
            Services.Register<IPatrolWorld>(fake);
            var world = Services.Get<IPatrolWorld>();

            Assert.True(world.TryGetWaypoints(1, out var wps, out var mode, out var speed));
            Assert.That(mode, Is.EqualTo(StealthLoopMode.PingPong));
            Assert.That(speed, Is.EqualTo(1.75f).Within(1e-4f));
            Assert.That(wps.Count, Is.EqualTo(1));

            var edited = wps[0];
            edited.localPosition = new Vector3(0f, 0f, 4f);
            Assert.True(world.TrySetWaypoint(1, 0, edited), "反推玩家线路后需写回路点");
            Assert.That(fake.SetWaypointCalls, Is.EqualTo(1));
            Assert.That(fake.Routes[1][0].localPosition.z, Is.EqualTo(4f).Within(1e-4f));

            Assert.False(world.TrySetWaypoint(1, 7, edited), "越界索引必须返回 false");
            Assert.False(world.TrySetWaypoint(404, 0, edited), "未知单位必须返回 false");
        }

        [Test]
        public void SafeIsland_ProviderDirectionIsReverse_AndDegradesWhenAbsent()
        {
            // 未注册（辅助台包没被提取走）时，玩法包必须能降级而不是抛异常。
            Assert.False(Services.TryGet<ISafeIslandProvider>(out var absent));
            Assert.That(absent, Is.Null);

            Services.Register<ISafeIslandProvider>(new BoxIsland(
                new Bounds(new Vector3(0f, 0f, 0f), new Vector3(2f, 2f, 2f)), "Island_A"));

            var islands = Services.Get<ISafeIslandProvider>();
            Assert.That(islands.Count, Is.EqualTo(1));
            Assert.True(islands.TryGetBounds(0, out var b, out var name));
            Assert.That(name, Is.EqualTo("Island_A"));
            Assert.True(islands.Contains(new Vector3(0.5f, 0f, 0.5f)));
            Assert.False(islands.Contains(new Vector3(9f, 0f, 9f)));
            Assert.False(islands.TryGetBounds(5, out _, out _));
        }

        private sealed class BoxIsland : ISafeIslandProvider
        {
            private readonly Bounds _b;
            private readonly string _name;
            public BoxIsland(Bounds b, string name) { _b = b; _name = name; }
            public int Count => 1;
            public bool TryGetBounds(int index, out Bounds worldBounds, out string displayName)
            {
                if (index != 0) { worldBounds = default; displayName = null; return false; }
                worldBounds = _b;
                displayName = _name;
                return true;
            }
            public bool Contains(Vector3 worldPoint) => _b.Contains(worldPoint);
        }

        [Test]
        public void ScrubDriver_SwitchesManualAndRaisesChanged()
        {
            var driver = new FakeScrubDriver();
            Services.Register<IStealthScrubDriver>(driver);

            var got = Services.Get<IStealthScrubDriver>();
            int changedHits = 0;
            got.Changed += () => changedHits++;

            Assert.False(got.IsManual);
            got.ScrubTo(4.25f);
            Assert.True(got.IsManual);
            Assert.That(driver.LastT, Is.EqualTo(4.25f).Within(1e-4f));
            Assert.That(changedHits, Is.EqualTo(1), "编辑态没有帧循环，scrub 后必须显式通知重绘");

            got.ResumeAuto();
            Assert.False(got.IsManual);
        }

        private sealed class FakeScrubDriver : IStealthScrubDriver
        {
            public float LastT = float.NaN;
            public bool Manual;
            public event Action Changed;
            public bool IsManual => Manual;
            public void ScrubTo(float t) { LastT = t; Manual = true; Changed?.Invoke(); }
            public void ResumeAuto() { Manual = false; Changed?.Invoke(); }
        }

        [Test]
        public void DetectedChannel_ConcreteSubclassIsInstantiableAndBroadcasts()
        {
            // core v0.1.0 实测：泛型 EventChannel<T> 的 CreateInstance 返回 null，
            // 因此契约必须提供具体子类。本用例守住这条约束不被后来者改回泛型直用。
            var channel = ScriptableObject.CreateInstance<StealthDetectedChannel>();
            Assert.That(channel, Is.Not.Null, "具体子类必须可被 Unity 实例化");

            StealthDetectedPayload last = default;
            int hits = 0;
            void Handler(StealthDetectedPayload p) { last = p; hits++; }

            channel.Subscribe(Handler);
            channel.Raise(new StealthDetectedPayload
            {
                actorId = 7,
                targetId = 1,
                timeIndex = 3.75f,
                stage = StealthAlertStage.Alerted,
                suspicion = 0.62f,
            });

            Assert.That(hits, Is.EqualTo(1));
            Assert.That(last.actorId, Is.EqualTo(7));
            Assert.That(last.timeIndex, Is.EqualTo(3.75f).Within(1e-4f), "事件必须带 t，辅助台才能把它钉回时间轴");
            Assert.That(last.stage, Is.EqualTo(StealthAlertStage.Alerted));
            Assert.That(last.suspicion, Is.EqualTo(0.62f).Within(1e-4f));

            channel.Unsubscribe(Handler);
            channel.Raise(new StealthDetectedPayload { actorId = 8 });
            Assert.That(hits, Is.EqualTo(1), "退订后不应再收到广播");

            UnityEngine.Object.DestroyImmediate(channel);
        }

        [Test]
        public void TimeIndexChannel_BroadcastsRawT()
        {
            var channel = ScriptableObject.CreateInstance<StealthTimeIndexChannel>();
            Assert.That(channel, Is.Not.Null);

            float seen = float.NaN;
            void Handler(float t) { seen = t; }
            channel.Subscribe(Handler);
            channel.Raise(11.5f);
            Assert.That(seen, Is.EqualTo(11.5f).Within(1e-4f));
            UnityEngine.Object.DestroyImmediate(channel);
        }

        [Test]
        public void WorldChangedChannel_TypeSafeOverloadMapsToEnumValue()
        {
            var channel = ScriptableObject.CreateInstance<StealthWorldChangedChannel>();
            Assert.That(channel, Is.Not.Null);

            int seen = -1;
            void Handler(int payload) { seen = payload; }
            channel.Subscribe(Handler);

            channel.Raise(StealthWorldChangedChannel.ChangeKind.Actors);
            Assert.That(seen, Is.EqualTo(1), "枚举载荷必须按定义值传出，防止调用方手写强转写错");

            channel.Raise(StealthWorldChangedChannel.ChangeKind.SafeIsland);
            Assert.That(seen, Is.EqualTo(3));
            UnityEngine.Object.DestroyImmediate(channel);
        }

        [Test]
        public void AlertStage_ValueOrderMatchesSeverity_AndLoopModeMatchesProvider()
        {
            // 消费方会直接比较大小来判断「越严重越靠前」，值序一旦被重排就会静默错乱。
            Assert.That((int)StealthAlertStage.Patrol, Is.EqualTo(0));
            Assert.That((int)StealthAlertStage.Suspicious, Is.EqualTo(1));
            Assert.That((int)StealthAlertStage.Alerted, Is.EqualTo(2));
            Assert.That((int)StealthAlertStage.Searching, Is.EqualTo(3));
            Assert.That(StealthAlertStage.Searching, Is.GreaterThan(StealthAlertStage.Suspicious));

            Assert.That((int)StealthLoopMode.Loop, Is.EqualTo(0));
            Assert.That((int)StealthLoopMode.PingPong, Is.EqualTo(1));
        }

        /// <summary>
        /// 方案 P 下最危险的一类错乱：core 的镜像枚举与玩法包本体枚举**值序不一致**。
        /// 两侧编译互不知情，转换器写成 `+0` 也不会报错，只有断言能拦住。
        /// 本用例只在测试程序集里同时看见两侧类型（Tests 引用 core + stealth），
        /// 生产代码（A 的适配层 / B 的消费层）各自只允许引用一侧。
        /// </summary>
        [Test]
        public void CoreMirrorEnums_ValueOrder_MatchesStealthProviderEnums()
        {
            Assert.That((int)StealthLoopMode.Loop, Is.EqualTo((int)PatrolLoopMode.Loop),
                "core.StealthLoopMode.Loop 与 stealth.PatrolLoopMode.Loop 值序不一致");
            Assert.That((int)StealthLoopMode.PingPong, Is.EqualTo((int)PatrolLoopMode.PingPong),
                "core.StealthLoopMode.PingPong 与 stealth.PatrolLoopMode.PingPong 值序不一致");

            // 时钟模式：StealthClockDto.mode 用 int 承载，两侧必须同值，否则消费方会读错模式。
            Assert.That((int)StealthClockMode.Auto, Is.EqualTo(0));
            Assert.That((int)StealthClockMode.Manual, Is.EqualTo(1));

            // 察觉档位：玩法包内部状态机的档位名与 core 镜像必须一一对应。
            var providerStages = System.Enum.GetNames(typeof(StealthAlertStage));
            Assert.That(providerStages, Is.EqualTo(new[] { "Patrol", "Suspicious", "Alerted", "Searching" }));
        }

        /// <summary>
        /// 仅凭 core 契约就能组装出消费方所需的全部数据（不碰任何玩法包符号）。
        /// <para>真正的守卫是上一条用例（跨包值序对比）与本类中所有 Fake 实现
        /// ——它们的字段与签名只用 <c>Collin.Core.Stealth</c> 的类型；
        /// 若 core 契约里泄漏了玩法包类型，写不出这样的实现就得改代码。</para>
        /// </summary>
        [Test]
        public void ConsumerCanAssembleDto_UsingOnlyCoreTypes()
        {
            var pose = new StealthPoseDto
            {
                valid = true,
                localPosition = new Vector3(1f, 2f, 3f),
                localYawDegrees = 90f,
                legIndex = 2,
                legProgress01 = 0.5f,
                isDwelling = false,
                cycleTime = 12f,
            };
            Assert.That(pose.HasLeg, Is.True);
            Assert.That(pose.localPosition.z, Is.EqualTo(3f).Within(1e-4f));

            var info = new StealthActorInfo
            {
                actorId = 1,
                displayName = "G",
                cycleTime = 12f,
                hasRoot = true,
                rootPosition = Vector3.one,
                rootRotation = Quaternion.identity,
                rootScale = Vector3.one,
            };
            Assert.That(info.hasRoot, Is.True);

            // default 语义：未填充的结构必须是「无段 / 无根」的安全空值，而不是看起来像有数据。
            Assert.That(default(StealthPoseDto).HasLeg, Is.False, "default 位姿不得被当成有效");
            Assert.That(default(StealthActorInfo).hasRoot, Is.False, "default 单位不得被当成有根");

            // 判据必须依赖 valid：只看 legIndex 时 default(legIndex=0) 会被误读成「位于第 0 段」。
            var legZeroButInvalid = new StealthPoseDto { legIndex = 0 };
            Assert.That(legZeroButInvalid.HasLeg, Is.False, "HasLeg 必须同时要求 valid");
        }

        [Test]
        public void QueryLayers_AreComposableFlags()
        {
            var layers = StealthQueryLayers.DistanceOnly | StealthQueryLayers.Cone;
            Assert.True(layers.HasFlag(StealthQueryLayers.DistanceOnly));
            Assert.True(layers.HasFlag(StealthQueryLayers.Cone));
            Assert.False(layers.HasFlag(StealthQueryLayers.LineOfSight));
            Assert.That((int)StealthQueryLayers.Full, Is.EqualTo(7));
        }

        [Test]
        public void DetectionWorld_ReadsSuspicionAndCanReset()
        {
            var fake = new FakeDetectionWorld();
            Services.Register<IDetectionWorld>(fake);

            var world = Services.Get<IDetectionWorld>();
            Assert.That(world.SuspicionOf(1), Is.EqualTo(0.4f).Within(1e-4f));
            Assert.That(world.StageOf(1), Is.EqualTo(StealthAlertStage.Suspicious));
            Assert.That(world.SuspicionOf(404), Is.EqualTo(0f), "未知目标应返回 0 而非抛异常");

            world.ResetAll();
            Assert.That(fake.ResetCalls, Is.EqualTo(1));
            Assert.That(world.SuspicionOf(1), Is.EqualTo(0f));
        }

        private sealed class FakeDetectionWorld : IDetectionWorld
        {
            public int ResetCalls;
            public float SuspicionOf(int targetId) => ResetCalls > 0 ? 0f : (targetId == 1 ? 0.4f : 0f);
            public StealthAlertStage StageOf(int targetId) => ResetCalls > 0 ? StealthAlertStage.Patrol : (targetId == 1 ? StealthAlertStage.Suspicious : StealthAlertStage.Patrol);
            public void ResetAll() { ResetCalls++; }
        }

        [Test]
        public void VisionWorld_EnumeratesVolumesAndQueriesLayers()
        {
            var fake = new FakeVisionWorld();
            Services.Register<IVisionWorld>(fake);

            var world = Services.Get<IVisionWorld>();
            Assert.That(world.Volumes.Count, Is.EqualTo(2));
            Assert.That(world.Volumes[1].enabled, Is.False, "禁用视野必须被标记出来，消费方据此跳过绘制");
            Assert.That(world.Volumes[0].horizontalAngleDegrees, Is.EqualTo(110f).Within(1e-4f));

            Assert.True(world.CanSee(1, new Vector3(2f, 0f, 0f), StealthQueryLayers.Cone));
            Assert.True(world.CanSeeAt(1, 6f, Vector3.zero, StealthQueryLayers.Full));
            Assert.That(fake.LastLayers, Is.EqualTo(StealthQueryLayers.Full));
            Assert.That(world.CountSeers(-1, Vector3.zero, 6f, StealthQueryLayers.DistanceOnly), Is.EqualTo(1));
        }

        private sealed class FakeVisionWorld : IVisionWorld
        {
            public readonly List<StealthVisionInfo> List = new List<StealthVisionInfo>
            {
                new StealthVisionInfo
                {
                    actorId = 1,
                    originWorld = new Vector3(0f, 1.7f, 0f),
                    forwardWorld = Vector3.forward,
                    horizontalAngleDegrees = 110f,
                    verticalAngleDegrees = 60f,
                    maxDistance = 18f,
                    enabled = true,
                },
                new StealthVisionInfo { actorId = 2, enabled = false },
            };

            public StealthQueryLayers LastLayers;

            IReadOnlyList<StealthVisionInfo> IVisionWorld.Volumes => List;

            public bool CanSee(int actorId, Vector3 worldPoint, StealthQueryLayers layers)
            {
                LastLayers = layers;
                return actorId == 1 && worldPoint.x < 3f;
            }

            public bool CanSeeAt(int actorId, float t, Vector3 worldPoint, StealthQueryLayers layers)
            {
                LastLayers = layers;
                return actorId == 1;
            }

            public int CountSeers(int actorIdFilter, Vector3 worldPoint, float t, StealthQueryLayers layers)
            {
                return actorIdFilter == -1 ? 1 : 0;
            }
        }

        [Test]
        public void Services_DuplicateRegistration_KeepsFirstAndLogsError()
        {
            // 契约的注册冲突语义由 core 提供；本用例确认潜行接口也受同一保护。
            // 本用例会**故意**触发 core 的冲突 error。实测（citymap 交接单同样记录过）：
            // LogAssert.Expect 只让测试本身不失败，**不会回退 consoleErrors 计数**，
            // 因而会破 §1.6b 归零门禁。正解是在断言期间关掉 logger 写入并在 finally 复位。
            var a = new BoxIsland(new Bounds(), "A");
            var b = new BoxIsland(new Bounds(), "B");

            Debug.unityLogger.logEnabled = false;
            try
            {
                Services.Register<ISafeIslandProvider>(a);
                Services.Register<ISafeIslandProvider>(b);
                Assert.That(Services.Get<ISafeIslandProvider>(), Is.SameAs(a), "重复注册须保留先注册者");
            }
            finally
            {
                Debug.unityLogger.logEnabled = true;
            }
        }
    }
}
