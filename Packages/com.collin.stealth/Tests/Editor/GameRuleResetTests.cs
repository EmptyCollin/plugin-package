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
    /// M5 成品质量修正的守卫用例（用户实机反馈四条）。
    /// <para>纪律与 M1~M4 同源：断言写在<b>数据层与几何层</b>，不依赖帧循环——
    /// EditMode 不执行 Awake/Update（本包实测坑），所以需要生命周期兜底的地方
    /// 一律提供<b>惰性解析</b>或<b>显式记录入口</b>，测试直接调它们。</para>
    /// <para>覆盖：①视觉胶囊不得埋地；②重开必须全量复位（玩家位姿/察觉/哨兵/时钟/驻留）；
    /// ③场地硬边界的钳制语义；④结算数据的组装契约。</para>
    /// </summary>
    public class GameRuleResetTests
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

        // ── 搭建辅助 ────────────────────────────────────────────────

        GameObject NewPlayer(Vector3 pos, out PlayerStealthController controller,
                             out DetectionState detection)
        {
            var go = new GameObject("player");
            _games.Add(go);
            go.transform.position = pos;
            go.AddComponent<CharacterController>();
            detection = go.AddComponent<DetectionState>();
            controller = go.AddComponent<PlayerStealthController>();
            return go;
        }

        /// <summary>建作者摆好的视觉胶囊子物体（名字必须与控制器 Find 的一致）。</summary>
        Transform NewCapsuleVisual(PlayerStealthController controller)
        {
            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "CapsuleVisual";
            body.transform.SetParent(controller.transform, false);
            body.transform.localPosition = Vector3.zero;   // 故意摆错：控制器必须自己纠正
            body.transform.localScale = new Vector3(1f, 1f, 1f);
            return body.transform;
        }

        static void SetFloat(Component c, string field, float v)
        {
            var so = new SerializedObject(c);
            var p = so.FindProperty(field);
            Assert.That(p, Is.Not.Null, "找不到序列化字段 " + field);
            p.floatValue = v;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void SetObject(Component c, string field, Object v)
        {
            var so = new SerializedObject(c);
            var p = so.FindProperty(field);
            Assert.That(p, Is.Not.Null, "找不到序列化字段 " + field);
            p.objectReferenceValue = v;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        VisionProfile NewProfile()
        {
            var p = ScriptableObject.CreateInstance<VisionProfile>();
            _assets.Add(p);
            p.SetHorizontalAngle(360f);
            p.SetVerticalAngle(180f);
            p.SetMaxDistance(20f);
            p.SetEyeHeight(0f);
            p.SetOccluderMask(0);
            p.SetSuspicionGain(0.9f);
            p.SetDecayPerSecond(0.2f);
            p.SetSuspicionThreshold(0.35f);
            p.SetAlertThreshold(0.8f);
            p.SetLightSensitivity(0f);
            return p;
        }

        GuardBrain NewGuardAtOrigin()
        {
            var go = new GameObject("guard");
            _games.Add(go);
            go.transform.position = Vector3.zero;
            var volume = go.AddComponent<VisionVolume>();
            volume.Profile = NewProfile();
            return go.AddComponent<GuardBrain>();
        }

        StealthGameRule NewRule(DetectionState player)
        {
            var go = new GameObject("rule");
            _games.Add(go);
            var rule = go.AddComponent<StealthGameRule>();
            SetObject(rule, "_player", player);
            return rule;
        }

        // ── 1. 视觉胶囊不得埋地（用户：「玩家一出场，胶囊体半个在地里」）──

        [Test]
        public void CapsulePose_Standing_SitsOnFeetNotHalfBuried()
        {
            // 站立 h=1.8：基元 scale.y=1 ⇔ 总高 2m ⇒ 中心必须在 h/2 才不埋地。
            var pose = ThirdPersonRig.CapsuleVisualPose(1.8f);
            Assert.AreEqual(0.9f, pose.x, 1e-5f, "缩放应使总高=身高");
            Assert.AreEqual(0.9f, pose.y, 1e-5f, "中心应等于半高，否则底边不在脚底");

            // 底边 = 中心 − 半高 = 0 ⇒ 正好落在脚底（y=0 是角色根，即地面）
            Assert.AreEqual(0f, pose.y - pose.x, 1e-5f, "胶囊底边必须贴脚底");
        }

        [Test]
        public void CapsulePose_LegacyFormulaWasTheBug()
        {
            // 旧公式 (h − 站高)/2：站立时算出 0 ⇒ 中心落到脚底、下半截埋地。
            // 这里把「错误答案」钉成断言，防止有人把公式改回去还自觉合理。
            float h = 1.8f;
            float standHeight = 1.8f;
            var legacy = (h - standHeight) * 0.5f;
            Assert.AreEqual(0f, legacy, 1e-5f, "旧公式在站立时必须正好是 0（即 bug 复现条件）");
            Assert.Greater(ThirdPersonRig.CapsuleVisualPose(h).y, legacy,
                           "新公式必须高于旧公式，否则埋地未修复");
        }

        [Test]
        public void CapsulePose_CrouchKeepsFeetPlanted()
        {
            var crouch = ThirdPersonRig.CapsuleVisualPose(1.0f);
            Assert.AreEqual(0.5f, crouch.x, 1e-5f);
            Assert.AreEqual(0.5f, crouch.y, 1e-5f, "蹲伏后底边仍须贴脚底，不能悬空或陷地");
        }

        [Test]
        public void Controller_AppliesCapsulePose_AndPreservesAuthorXZ()
        {
            NewPlayer(new Vector3(0f, 0f, -22f), out var controller, out _);
            var visual = NewCapsuleVisual(controller);
            visual.localScale = new Vector3(0.7f, 1f, 0.7f);   // 作者摆的粗细

            controller.SetCrouching(false);

            Assert.AreEqual(0.9f, visual.localPosition.y, 1e-5f, "站立时胶囊中心应抬到半高");
            Assert.AreEqual(0.7f, visual.localScale.x, 1e-5f, "x 粗细属于作者，不得被覆盖");
            Assert.AreEqual(0.7f, visual.localScale.z, 1e-5f, "z 粗细属于作者，不得被覆盖");
            Assert.AreEqual(0.9f, visual.localScale.y, 1e-5f);

            controller.SetCrouching(true);
            Assert.AreEqual(0.5f, visual.localPosition.y, 1e-5f, "蹲伏应同步下沉但仍贴脚底");
            Assert.AreEqual(0.5f, visual.localScale.y, 1e-5f);
            Assert.AreEqual(0.7f, visual.localScale.x, 1e-5f, "蹲伏只压纵向，不动粗细");
        }

        // ── 2. 场地硬边界（用户：「像主流设计一样加上外侧围栏」）────────

        [Test]
        public void Arena_DisabledWhenHalfSizeNonPositive()
        {
            // 默认关闭是硬要求：预制件拖进任意小场景不该凭空出现隐形墙。
            Assert.False(StealthArena.IsEnabled(Vector2.zero));
            var far = new Vector3(500f, 0f, -500f);
            Assert.AreEqual(far, StealthArena.Clamp(far, Vector3.zero, Vector2.zero),
                            "边界关闭时必须原样放行");
        }

        [Test]
        public void Arena_ClampsOutsidePointOntoInnerEdge()
        {
            var half = new Vector2(27f, 27f);
            var p = StealthArena.Clamp(new Vector3(40f, 1.3f, -99f), Vector3.zero, half);
            Assert.AreEqual(27f, p.x, 1e-4f);
            Assert.AreEqual(-27f, p.z, 1e-4f);
            Assert.AreEqual(1.3f, p.y, 1e-5f, "边界只管水平，纵向必须交重力处理");
        }

        [Test]
        public void Arena_MarginPullsEdgeInward()
        {
            var half = new Vector2(27f, 27f);
            var p = StealthArena.Clamp(new Vector3(30f, 0f, 0f), Vector3.zero, half, 0.55f);
            Assert.AreEqual(26.45f, p.x, 1e-4f, "应停在墙内侧裕度处，避免贴墙摩擦抖动");
        }

        [Test]
        public void Arena_ContainsRespectsMargin()
        {
            var half = new Vector2(27f, 27f);
            Assert.True(StealthArena.Contains(new Vector3(0f, 0f, -22f), Vector3.zero, half),
                        "示例出生点必须在场内");
            Assert.False(StealthArena.Contains(new Vector3(26.9f, 0f, 0f), Vector3.zero, half, 0.55f));
            Assert.True(StealthArena.Contains(new Vector3(26.4f, 0f, 0f), Vector3.zero, half, 0.55f));
            Assert.True(StealthArena.Contains(new Vector3(999f, 0f, 0f), Vector3.zero, Vector2.zero),
                        "边界关闭时 Contains 恒真");
        }

        [Test]
        public void Arena_CenteredFieldClampsAroundCenter()
        {
            var center = new Vector3(10f, 0f, 5f);
            var half = new Vector2(4f, 4f);
            var p = StealthArena.Clamp(new Vector3(100f, 0f, -100f), center, half);
            Assert.AreEqual(14f, p.x, 1e-4f);
            Assert.AreEqual(1f, p.z, 1e-4f, "必须以场地中心为参照，不能假设场地在原点");
        }

        // ── 3. 重开必须全量复位（用户：「按 R 重新开始并没有效果」）──────

        [Test]
        public void ResetRound_ReturnsPlayerToSpawnPoint()
        {
            var spawn = new Vector3(0f, 0.05f, -22f);
            var spawnYaw = 137f;
            NewPlayer(spawn, out var controller, out _);

            // 出生朝向必须在记录<b>之前</b>摆好：记录的就是"当时"的位姿。
            // （先前写成"记录后再改朝向、却断言回到新朝向"，测的是错误前提。）
            controller.transform.rotation = Quaternion.Euler(0f, spawnYaw, 0f);
            controller.RecordSpawnPoint();

            // 玩家跑到撤离点附近被抓
            controller.transform.position = new Vector3(0f, 0.05f, 24f);
            controller.transform.rotation = Quaternion.Euler(0f, 0f, 0f);
            controller.Frozen = true;

            var rule = NewRule(controller.GetComponent<DetectionState>());
            rule.ResetRound();

            Assert.AreEqual(spawn, controller.transform.position, "重开必须把玩家送回出生点");
            Assert.AreEqual(spawnYaw, controller.transform.eulerAngles.y, 1e-3f, "朝向应复原到记录时的值");
            Assert.False(controller.Frozen, "结算冻结必须解除，否则重开后动不了");
            Assert.That(rule.Outcome, Is.EqualTo(StealthGameOutcome.Playing));
        }

        [Test]
        public void ResetRound_WithoutAwakeResolvesReferences()
        {
            // EditMode 不跑 Awake：若引用只在 Awake 解析，重开会静默漏掉玩家，
            // 表现正是用户反馈的「按 R 没效果」。惰性解析必须覆盖这条路径。
            var spawn = new Vector3(-3f, 0f, -8f);
            NewPlayer(spawn, out var controller, out var detection);
            controller.RecordSpawnPoint();
            controller.transform.position = new Vector3(9f, 0f, 9f);

            var exitGo = new GameObject("exit");
            _games.Add(exitGo);
            exitGo.transform.position = new Vector3(9f, 0f, 9f);
            var zone = exitGo.AddComponent<StealthExitZone>();
            zone.ResetZone();

            var rule = NewRule(detection);
            rule.ResetRound();

            Assert.AreEqual(spawn, controller.transform.position);
            Assert.AreEqual(0f, detection.Suspicion, 1e-5f, "玩家察觉度应归零");
        }

        [Test]
        public void ResetRound_ClearsGuardStageAndSuspicion()
        {
            var spawn = new Vector3(0f, 0f, -22f);
            NewPlayer(spawn, out var controller, out var detection);
            controller.RecordSpawnPoint();

            var brain = NewGuardAtOrigin();
            // 把玩家放进哨兵视野并推高察觉度 → 警觉档
            detection.transform.position = new Vector3(0f, 0.95f, 6f);
            detection.EvaluateOnce(1f);
            brain.Tick(0.016f, Vector3.zero, 1f);
            Assert.That(brain.Stage, Is.EqualTo(StealthDetectionStage.Alerted), "前置条件：哨兵已警觉");
            Assert.That(brain.Suspicion, Is.GreaterThan(0.5f));

            var rule = NewRule(detection);
            rule.ResetRound();

            Assert.That(brain.Stage, Is.EqualTo(StealthDetectionStage.Patrol), "重开后哨兵必须回到巡逻档");
            Assert.AreEqual(0f, brain.Suspicion, 1e-5f);
            Assert.False(brain.Active, "反应叠加层应已清空");
            Assert.AreEqual(0f, detection.Suspicion, 1e-5f);
        }

        [Test]
        public void ResetRound_RewindsPatrolClock()
        {
            var clockGo = new GameObject("clock");
            _games.Add(clockGo);
            var clock = clockGo.AddComponent<StealthClock>();

            NewPlayer(Vector3.zero, out var controller, out var detection);
            controller.RecordSpawnPoint();

            clock.SetAutoTime(37.5f);
            Assert.AreEqual(37.5f, StealthClock.Instance.Current, 1e-4f);

            var rule = NewRule(detection);
            rule.ResetRound();

            // 位姿是时间的纯函数：时钟归零 ⇒ 所有 NPC 基线回到 t=0，无需逐个搬 transform
            Assert.AreEqual(0f, StealthClock.Instance.Current, 1e-4f);
            Assert.AreEqual(0f, rule.ElapsedSeconds, 1e-4f, "本局用时应从零起算");
        }

        [Test]
        public void ResetRound_ClearsExitDwellState()
        {
            NewPlayer(Vector3.zero, out var controller, out var detection);
            controller.RecordSpawnPoint();

            var exitGo = new GameObject("exit");
            _games.Add(exitGo);
            exitGo.transform.position = new Vector3(0f, 0f, 16f);
            var zone = exitGo.AddComponent<StealthExitZone>();
            SetFloat(zone, "_dwellSeconds", 0.6f);

            // 驻留到达成
            for (var i = 0; i < 4; i++) zone.Tick(true, 0.5f);
            Assert.True(zone.Reached, "前置条件：撤离区已达成");

            var rule = NewRule(detection);
            rule.ResetRound();

            Assert.False(zone.Reached, "重开必须清掉撤离区达成状态，否则踏入一次就永久通关");
            Assert.AreEqual(0f, zone.DwellProgress, 1e-5f);
        }

        [Test]
        public void ResetRound_UnlocksEndingGateSoPlayCanResume()
        {
            // 结算界面点「再来一局」是在绘制回调里复位：Outcome 必须最先改回 Playing，
            // 否则同帧之后的判定会因「已出结果」直接早退，残留状态再也清不掉。
            NewPlayer(Vector3.zero, out var controller, out var detection);
            controller.RecordSpawnPoint();
            var rule = NewRule(detection);
            SetFloat(rule, "_catchSeconds", 0.2f);

            rule.ResetRound();
            Assert.That(rule.Outcome, Is.EqualTo(StealthGameOutcome.Playing));
            Assert.AreEqual(0f, rule.CatchProgress, 1e-5f);
        }

        [Test]
        public void SpawnPoint_DefaultsToAuthorPlacedPosition()
        {
            // 所见即所得：不写覆盖值时，出生点 = 场景里摆好的位置。
            var placed = new Vector3(4.5f, 0.05f, -19f);
            NewPlayer(placed, out var controller, out _);
            controller.RecordSpawnPoint();

            Assert.True(controller.HasSpawn);
            Assert.AreEqual(placed, controller.SpawnPosition, "预制件拖进新场景不该锁死在示例坐标");
        }

        [Test]
        public void SpawnPoint_ReRecordsOnLaterCall()
        {
            // 覆盖式记录：关卡作者搬完位置再点一次，出生点应跟着走，不会锁死在第一次的值。
            var first = new Vector3(1f, 0f, 3f);
            var second = new Vector3(-7f, 0f, 12f);
            NewPlayer(first, out var controller, out _);
            controller.RecordSpawnPoint();
            controller.transform.position = second;
            controller.RecordSpawnPoint();

            Assert.AreEqual(second, controller.SpawnPosition, "再次记录应覆盖旧出生点");

            controller.transform.position = Vector3.zero;
            controller.ResetToSpawn();
            Assert.AreEqual(second, controller.transform.position);
        }

        // ── 4. 结算数据契约（用户：「给胜利/失败做个简单UI」）───────────

        [Test]
        public void Result_NothingToShowWhilePlaying()
        {
            Assert.Null(StealthResultModel.Build(StealthGameOutcome.Playing, 12f, 0.4f, 1, false),
                        "进行中不得产出卡片数据");
            Assert.AreEqual(string.Empty, StealthResultModel.TitleOf(StealthGameOutcome.Playing));
        }

        [Test]
        public void Result_EscapeShowsTimeAndExitAttribution()
        {
            var data = StealthResultModel.Build(StealthGameOutcome.Escaped, 65.5f, 0.72f, 2, true);
            Assert.NotNull(data);
            Assert.AreEqual("撤离成功", data.Title);
            Assert.AreEqual(4, data.Rows.Count, "胜利应多一行撤离点归因");
            Assert.AreEqual("用时", data.Rows[0].Label);
            Assert.AreEqual("1:05", data.Rows[0].Value);
            Assert.AreEqual("72%", data.Rows[1].Value);
            Assert.AreEqual("2 名", data.Rows[2].Value);
            Assert.AreEqual("已抵达", data.Rows[3].Value);
        }

        [Test]
        public void Result_CaughtAttributesToPeakSuspicion()
        {
            var data = StealthResultModel.Build(StealthGameOutcome.Caught, 8.2f, 1f, 1, false);
            Assert.AreEqual("行动失败", data.Title);
            Assert.AreEqual(3, data.Rows.Count, "失败不该显示撤离点归因");
            Assert.AreEqual("0:08", data.Rows[0].Value);
            Assert.AreEqual("100%", data.Rows[1].Value);
            Assert.AreEqual(StealthResultModel.AccentOf(StealthGameOutcome.Caught), data.Accent);
        }

        [Test]
        public void Result_ClockFormatIsStable()
        {
            Assert.AreEqual("0:00", StealthResultModel.ClockOf(0f));
            Assert.AreEqual("0:09", StealthResultModel.ClockOf(9.9f));
            Assert.AreEqual("1:00", StealthResultModel.ClockOf(60f));
            Assert.AreEqual("2:00", StealthResultModel.ClockOf(120f), "过整分应进位而非显示 1:60");
            Assert.AreEqual("0:00", StealthResultModel.ClockOf(-5f), "负数须钳到零，不允许出现 -0:05");
        }

        [Test]
        public void Result_CardHasActionableHints()
        {
            var data = StealthResultModel.Build(StealthGameOutcome.Caught, 3f, 1f, 0, false);
            Assert.That(data.PrimaryHint, Does.Contain("R"), "主操作须写明重开按键");
            Assert.That(data.SecondaryHint, Does.Contain("Esc"), "须给出关闭入口，否则遮罩挡住画面无法退出");
            Assert.That(data.Subtitle, Is.Not.Empty);
        }
    }
}
