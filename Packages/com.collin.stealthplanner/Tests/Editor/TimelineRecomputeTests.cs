using System.Collections.Generic;
using System.Reflection;
using Collin.StealthPlanner.EditorTooling;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Collin.StealthPlanner.Tests
{
    /// <summary>
    /// 时间轴窗口「改参数即时响应」的守卫用例（AGENTS.md §0.1 第 1 条）。
    /// <para>为什么必须有这一组：窗口层最容易犯的错是<b>把换算结果缓存起来</b>。本包实测踩过一次
    /// ——<c>_startCell</c> 一旦算出就不再按 <c>_startWorld</c> 重算，于是策划拖起点输入框时，
    /// 面板永远报着「出发格在 t=0 已被看见」，<b>怎么改都不动</b>。这类缺陷 Runtime 单测看不见
    /// （传播算法本身是对的），只有把窗口当被测对象才暴露。</para>
    /// <para>表用 <see cref="VisibilityTableAsset.SetBaked"/> <b>现场注入</b>：若去载库里的
    /// <c>Sample/VisibilityTable.asset</c>，断言前提就变成「这台机器恰好烘过一张什么表」
    /// ——表资产按派生产物处理不入库，那样用例只会随现场时红时绿。</para>
    /// </summary>
    public class TimelineRecomputeTests
    {
        const BindingFlags Priv = BindingFlags.Instance | BindingFlags.NonPublic;
        const float Dt = 1f;
        static readonly System.Type WinType = typeof(StealthTimelineWindow);

        static VisibilityTable Corridor(int dimX, int buckets, List<(int cell, int bucket)> danger)
        {
            var t = new VisibilityTable();
            t.ConfigureGrid(Vector3.zero, 1f, dimX, 1, Dt);
            var slice = t.AddSlice(1, "guard", buckets * Dt, buckets);
            for (var i = 0; i < danger.Count; i++)
                t.SetSliceVisible(slice, danger[i].bucket, danger[i].cell, true);
            t.RebuildPermanentSafeLayer();
            return t;
        }

        /// <summary>
        /// 建窗口并注入表。注意 <c>CreateInstance</c> 已经跑过一轮 OnEnable（那里会去 Load 库内资产、
        /// 并挂上 SceneView 回调），故必须显式把 <c>_tableAsset</c> 覆盖成注入资产再 Reload。
        /// </summary>
        static EditorWindow NewWindow(VisibilityTable table)
        {
            var asset = ScriptableObject.CreateInstance<VisibilityTableAsset>();
            asset.SetBaked(table, table.CellSize, table.TimeStep, 0f,
                           table.Slices.Count, table.PermanentlySafeCellCount, 0L);

            var w = (EditorWindow)ScriptableObject.CreateInstance(WinType);
            Created.Add(w);                                    // 供 TearDown 精确回收
            WinType.GetField("_tableAsset", Priv).SetValue(w, asset);
            WinType.GetField("_settings", Priv).SetValue(w, null);
            WinType.GetField("_buckets", Priv).SetValue(w, Mathf.Max(1, table.Slices[0].buckets));
            WinType.GetMethod("Reload", Priv).Invoke(w, null);
            return w;
        }

        static void SetStart(EditorWindow w, Vector3 world)
        {
            WinType.GetField("_startWorld", Priv).SetValue(w, world);
            WinType.GetMethod("Recompute", Priv).Invoke(w, null);
        }

        static void SetTime(EditorWindow w, float t)
        {
            WinType.GetField("_t", Priv).SetValue(w, t);
            WinType.GetMethod("Recompute", Priv).Invoke(w, null);
        }

        static string StatusOf(EditorWindow w)
        {
            return WinType.GetField("_status", Priv).GetValue(w) as string;
        }

        static object FieldOf(EditorWindow w)
        {
            return WinType.GetField("_field", Priv).GetValue(w);
        }

        static object Get(EditorWindow w, string name)
        {
            return WinType.GetField(name, Priv).GetValue(w);
        }

        static int CumCount(EditorWindow w)
        {
            var buf = (byte[])Get(w, "_cumBuf");
            var c = 0;
            for (var i = 0; i < buf.Length; i++) if (buf[i] != 0) c++;
            return c;
        }

        static readonly System.Collections.Generic.List<Object> Created =
            new System.Collections.Generic.List<Object>();

        [TearDown]
        public void CloseCreatedWindows()
        {
            // 只销毁**本用例自己** CreateInstance 出来的窗口。
            // 早期版本用 FindObjectsOfTypeAll 扫全部窗口，会把「用户/其他 AI 会话正开着的时间轴窗口」
            // 一起 DestroyImmediate —— 那是动他人现场（AGENTS.md §2/§9 的纪律），且窗口是
            // ScriptableObject 型对象，销毁库内关联资产还会打出 error 破归零门禁（版本用例实测踩过）。
            for (var i = 0; i < Created.Count; i++)
                if (Created[i] != null) Object.DestroyImmediate(Created[i], true);
            Created.Clear();
        }

        // ── 1. 改起点必须立即生效（实测踩过的缓存 bug）──────────

        [Test]
        public void ChangingStartWorld_TakesEffect_WithoutAnyManualRefresh()
        {
            // 格 0 在 t=0 被看见 ⇒ 以格 0 起步必然失败；格 2 干净 ⇒ 换过去就该能传播。
            var table = Corridor(5, 4, new List<(int, int)> { (0, 0) });
            var win = NewWindow(table);

            SetStart(win, table.CellCenterWorld(0));
            Assert.That(StatusOf(win), Does.Contain("出发格"),
                "前置：起点落在 t=0 已被看见的格 ⇒ 应报起步失败");
            Assert.IsNull(FieldOf(win), "失败时不得留下半张可用的场");

            // 关键一步：只改世界坐标，不点「重算」、不重建窗口
            SetStart(win, table.CellCenterWorld(2));
            Assert.NotNull(FieldOf(win),
                "改了起点却不重算格号 = 起点输入框失灵（违反 §0.1 所见即所得）。现状态：" + StatusOf(win));
            Assert.That(StatusOf(win), Does.StartWith("t="), "成功路径的状态应给出 t 读数：" + StatusOf(win));
        }

        [Test]
        public void StartWorld_OutsideGrid_ReportsAndClearsField()
        {
            var table = Corridor(5, 4, new List<(int, int)> { (0, 0) });
            var win = NewWindow(table);

            SetStart(win, table.CellCenterWorld(2));
            Assert.NotNull(FieldOf(win), "前置：正常起点应算出场");

            SetStart(win, new Vector3(9999f, 0f, 9999f));
            Assert.IsNull(FieldOf(win), "起点移出网格后不得继续显示上一次的传播结果");
            Assert.That(StatusOf(win), Does.Contain("网格外"), "应明确说清是网格外：" + StatusOf(win));

            SetStart(win, table.CellCenterWorld(2));      // 再拖回来必须恢复（不是单向卡死）
            Assert.NotNull(FieldOf(win), "起点回到网格内应重新算出");
        }

        // ── 2. 累计热区必须随 t 推进（不是整窗并集）──────────────

        [Test]
        public void CumulativeMask_GrowsWithBucket_WhileScrubbing()
        {
            var table = Corridor(5, 4, new List<(int, int)>());   // 全程无人可见 ⇒ 纯几何扩散
            var win = NewWindow(table);
            SetStart(win, table.CellCenterWorld(0));

            SetTime(win, 0f);
            var at0 = CumCount(win);
            SetTime(win, 3f * Dt);
            var at3 = CumCount(win);

            Assert.AreEqual(1, at0, "桶 0 的累计只该有起点格自己");
            Assert.Greater(at3, at0,
                "累计可达拖到桶 3 必须扩大；若恒定即是拷成整窗并集（时间轴看着像坏了）");
        }

        // ── 3. 表不可用时不得残留旧热区 ─────────────────────────

        [Test]
        public void LosingTable_ReleasesStaleHeatmap_AndCounters()
        {
            var table = Corridor(5, 4, new List<(int, int)>());
            var win = NewWindow(table);
            SetStart(win, table.CellCenterWorld(0));
            Assert.AreEqual(table.CellCount, ((byte[])Get(win, "_dangerMask")).Length,
                "前置：正常载入后危险掩码应是满长度");

            // 清掉表资产引用并重载：面板不该还挂着上一份表的网格与计数
            WinType.GetField("_tableAsset", Priv).SetValue(win, null);
            WinType.GetMethod("Reload", Priv).Invoke(win, null);

            Assert.IsNull(Get(win, "_meshDanger"), "旧危险热区网格必须释放");
            Assert.IsNull(Get(win, "_meshCum"), "旧累计热区网格必须释放");
            Assert.IsNull(FieldOf(win), "旧传播结果必须清掉");
            Assert.AreEqual(0, ((byte[])Get(win, "_dangerMask")).Length,
                "危险掩码必须清空，否则状态行的格数会继续谎报");
            Assert.That(StatusOf(win), Does.Contain("表"), "状态应说明表不可用：" + StatusOf(win));
        }
    }
}
