using System.Reflection;
using Collin.StealthPlanner;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Collin.StealthPlanner.Tests
{
    /// <summary>
    /// 烘焙参数载体（<see cref="PlannerBakeSettings"/>）的守卫用例。
    /// <para>覆盖的是 DESIGN 决议 <b>D-16（三项可配，默认 0.5m / 0.5s）</b> 与
    /// <b>D-13（相位窗手配、与 Δt 解耦）</b>，以及 §6.7 的兜底阈值单一真源。</para>
    /// <para>纪律与 A 包同源：本包所有可观测判据都必须<b>脱离 Play 态</b>断言。
    /// <c>OnValidate</c> 在 EditMode 不自动执行（A 包实测坑），故钳制类断言统一走
    /// <see cref="InvokeValidate"/> 反射显式触发，而不是依赖导入时机。</para>
    /// </summary>
    public class PlannerBakeSettingsTests
    {
        PlannerBakeSettings _s;

        [SetUp]
        public void SetUp()
        {
            _s = ScriptableObject.CreateInstance<PlannerBakeSettings>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_s != null) Object.DestroyImmediate(_s, true);
        }

        /// <summary>反射触发 OnValidate（EditMode 不调用它，不显式触发就测不到钳制）。</summary>
        static void InvokeValidate(PlannerBakeSettings s)
        {
            typeof(PlannerBakeSettings)
                .GetMethod("OnValidate", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(s, null);
        }

        static void SetFloat(PlannerBakeSettings s, string field, float v)
        {
            var so = new SerializedObject(s);
            var p = so.FindProperty(field);
            Assert.That(p, Is.Not.Null, "找不到序列化字段 " + field);
            p.floatValue = v;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void SetInt(PlannerBakeSettings s, string field, int v)
        {
            var so = new SerializedObject(s);
            var p = so.FindProperty(field);
            Assert.That(p, Is.Not.Null, "找不到序列化字段 " + field);
            p.intValue = v;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ── 1. 默认值即拍板结论（D-16 / D-13）──────────────────────

        [Test]
        public void Defaults_MatchApprovedValues()
        {
            Assert.AreEqual(0.5f, _s.CellSize, 1e-5f, "格长默认必须 0.5m（用户拍板）");
            Assert.AreEqual(0.5f, _s.TimeStep, 1e-5f, "Δt 默认必须 0.5s（用户拍板）");
            Assert.Greater(_s.PhaseWindow, 0f, "相位窗须为正的默认值");
            Assert.AreEqual(4, _s.MaxPhases);
            Assert.AreEqual(5, _s.MaxSolutions);
            Assert.AreEqual(5, _s.FallbackTopK, "静态3 要求「最近 5 个位置」，默认须等于 5");
        }

        // ── 2. 三项真的可配（不是写死常量）────────────────────────

        [Test]
        public void CellSizeTimeStepPhaseWindow_AreConfigurable()
        {
            SetFloat(_s, "_cellSize", 0.2f);
            SetFloat(_s, "_timeStep", 0.25f);
            SetFloat(_s, "_phaseWindow", 90f);

            Assert.AreEqual(0.2f, _s.CellSize, 1e-5f);
            Assert.AreEqual(0.25f, _s.TimeStep, 1e-5f);
            Assert.AreEqual(90f, _s.PhaseWindow, 1e-5f);
        }

        // ── 3. 派生量口径（§6.7 的桶数，面板与求解共用同一算法）──

        [Test]
        public void BucketsPerPhase_CeilsSoTailBucketNotLost()
        {
            SetFloat(_s, "_phaseWindow", 60f);
            SetFloat(_s, "_timeStep", 0.5f);
            Assert.AreEqual(120, _s.BucketsPerPhase, "60/0.5 应得 120 桶");

            SetFloat(_s, "_phaseWindow", 90f);
            SetFloat(_s, "_timeStep", 0.25f);
            Assert.AreEqual(360, _s.BucketsPerPhase, "90/0.25 应得 360 桶");

            // 除不尽必须向上取整：向下取整会漏掉末尾不足一桶的时间（漏判危险）
            SetFloat(_s, "_phaseWindow", 10f);
            SetFloat(_s, "_timeStep", 3f);
            Assert.AreEqual(4, _s.BucketsPerPhase, "10/3=3.33 须进位到 4，不能截成 3 漏掉尾段");
        }

        [Test]
        public void BucketsPerPhase_NeverZero_EvenWhenP小于Dt()
        {
            // P 比 Δt 还小的病态配置：桶数若为 0，所有时间查询都会越界
            SetFloat(_s, "_phaseWindow", 1f);
            SetFloat(_s, "_timeStep", 0.5f);
            SetFloat(_s, "_cellSize", 5f);
            Assert.GreaterOrEqual(_s.BucketsPerPhase, 1);
        }

        // ── 4. 越界只钳制、禁止隐式联动（§0.1.1 所见即所得）───────

        [Test]
        public void OnValidate_ClampsOutOfRange_WithoutTouchingOthers()
        {
            SetFloat(_s, "_cellSize", 0.001f);     // 低于下限
            SetFloat(_s, "_timeStep", -3f);        // 非法
            SetInt(_s, "_maxPhases", 999);         // 高于上限
            var pBefore = _s.PhaseWindow;

            InvokeValidate(_s);

            Assert.AreEqual(0.05f, _s.CellSize, 1e-5f, "格长应被钳到下限 0.05");
            Assert.That(_s.TimeStep, Is.GreaterThanOrEqualTo(0.05f), "Δt 非法值须被钳回合法下限");
            Assert.That(_s.MaxPhases, Is.LessThanOrEqualTo(64), "N 应被钳到上限 64");
            Assert.AreEqual(pBefore, _s.PhaseWindow, 1e-5f,
                            "关键：钳制 Δt 时**不得**连带改写用户手配的 P（D-13 解耦）");
        }

        [Test]
        public void ChangingTimeStep_DoesNotSilentlyRewritePhaseWindow()
        {
            // 用户拍板「相位窗由策划手填」。若改 Δt 把 P 一起换算走，面板上会看到值自己动 = 失控
            SetFloat(_s, "_phaseWindow", 90f);
            SetFloat(_s, "_timeStep", 0.5f);
            InvokeValidate(_s);
            var p = _s.PhaseWindow;

            SetFloat(_s, "_timeStep", 0.2f);
            InvokeValidate(_s);

            Assert.AreEqual(p, _s.PhaseWindow, 1e-5f, "改 Δt 后 P 必须原样不动");
        }

        // ── 5. 兜底阈值单一真源（草案期两套口径漂移的老毛病）─────

        [Test]
        public void AlignedBucketCap_IsSingleSourceOfTruth()
        {
            Assert.AreEqual(7200, PlannerBakeSettings.MaxAlignedBuckets,
                            "DESIGN §6.7 定的 L_max=7200 桶；面板与求解器必须共用本常量");

            // 单一真源守卫：实现代码里「以字面量赋值形态」出现 7200 的地方只能有定义处一处。
            // 判据刻意收窄，避免两类误报/漏报：
            // ① 不扫 Tests/ —— 本用例自己的期望值与搜索词都是字面量，扫进来必然假阳性（实测踩过：曾报 6 次）；
            // ② 只匹配 "= 7200" 赋值形态 —— 文档注释里的说明性数字（如「7200 桶≈1 小时」）不该算重复定义。
            // 真正的防回归目标是：M1 求解器若图省事再硬编码一次阈值，本用例即失败。
            var dataRoot = Application.dataPath;
            var pkgRoot = System.IO.Path.Combine(
                System.IO.Directory.GetParent(dataRoot).FullName,
                "Packages", "com.collin.stealthplanner");
            Assert.True(System.IO.Directory.Exists(pkgRoot), "包目录应存在：" + pkgRoot);

            // 前置：定义文件必须真的被扫到，否则 hits=0 会让下面的断言「虚假通过」
            var defFound = false;
            foreach (var f in System.IO.Directory.GetFiles(pkgRoot, "PlannerBakeSettings.cs",
                       System.IO.SearchOption.AllDirectories))
            {
                defFound = true;
                Assert.That(System.IO.Path.GetFileName(f), Is.EqualTo("PlannerBakeSettings.cs"));
            }
            Assert.True(defFound, "扫描没找到定义文件，本用例的判据不成立（先修路径再谈回归）");

            int hits = 0;
            var violations = new System.Collections.Generic.List<string>();
            foreach (var dir in new[] { "Runtime", "Editor" })
            {
                var full = System.IO.Path.Combine(pkgRoot, dir);
                if (!System.IO.Directory.Exists(full)) continue;
                foreach (var file in System.IO.Directory.GetFiles(full, "*.cs",
                           System.IO.SearchOption.AllDirectories))
                {
                    // 定义处本身豁免：整个包只允许「一个文件」持有该字面量的赋值形态
                    var isDefinition = System.IO.Path.GetFileName(file) == "PlannerBakeSettings.cs";
                    foreach (var line in System.IO.File.ReadAllLines(file))
                    {
                        var t = line.TrimStart();
                        if (t.StartsWith("//") || t.StartsWith("///")) continue;   // 跳过注释
                        if (!t.Contains("= 7200")) continue;
                        hits++;
                        if (!isDefinition)
                        {
                            violations.Add(System.IO.Path.GetFileName(file) + " -> " + t);
                        }
                    }
                }
            }

            // 定义处恰好 1 次（真的还存在，没被删成裸数）；实现侧不得再多出任何一次
            Assert.AreEqual(1, hits,
                "7200 的赋值形态应只在 PlannerBakeSettings 常量定义处出现一次，实际 " + hits + " 次");
            Assert.That(violations, Is.Empty,
                "发现第二处硬编码阈值（应改用 PlannerBakeSettings.MaxAlignedBuckets）：\n"
                + string.Join("\n", violations));
        }
    }
}
