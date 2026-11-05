using System.Collections.Generic;
using Collin.StealthPlanner;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Collin.StealthPlanner.Tests
{
    /// <summary>
    /// 玩家路点数据集的<b>往返与线上契约</b>守卫（验收组 E：「导出 JSON schema 稳定、且能被读回并复现同一条动线」）。
    /// <para>为什么重点测"往返 + 字段名"而非只测赋值：数据集是要<b>落盘、被 M5 注入器读回</b>的跨时间产物，
    /// 一旦某次重命名了 JSON 字段，旧数据集会<b>解析成默认值而不报错</b>（JsonUtility 对缺字段静默补零），
    /// 于是注入器拿到一条"看着合法其实全 0"的动线——静默错数据，正是本包 D-17/D-19 反复防的那一类。
    /// 所以这里手写一段<b>线上 JSON 字面量</b>喂进去，字段被改名就会读不回，等于把契约钉死。</para>
    /// </summary>
    public class PlayerRouteDatasetTests
    {
        const float Dt = 0.5f;

        readonly List<Object> _spawned = new List<Object>();

        [TearDown]
        public void CleanUp()
        {
            // 只销毁本用例自建的对象（判 IsPersistent 防误删库内资产——本包条目 27 实测踩过）
            for (var i = _spawned.Count - 1; i >= 0; i--)
            {
                var o = _spawned[i];
                if (o != null && !EditorUtility.IsPersistent(o)) Object.DestroyImmediate(o, true);
            }
            _spawned.Clear();
        }

        T New<T>() where T : ScriptableObject
        {
            var s = ScriptableObject.CreateInstance<T>();
            _spawned.Add(s);
            return s;
        }

        static VisibilityTable Corridor(int dimX, int buckets, List<(int cell, int bucket)> danger)
        {
            var t = new VisibilityTable();
            t.ConfigureGrid(Vector3.zero, 0.5f, dimX, 1, Dt);
            var slice = t.AddSlice(1, "g", buckets * Dt, buckets);
            for (var i = 0; i < danger.Count; i++)
                t.SetSliceVisible(slice, danger[i].bucket, danger[i].cell, true);
            t.RebuildPermanentSafeLayer();
            return t;
        }

        static List<PhaseKShortest.Route> Routes(VisibilityTable t, int start, int goal, int horizon, int step, int m)
        {
            var list = new List<PhaseKShortest.Route>();
            PhaseKShortest.FindRoutes(t, start, goal, horizon, step, m, list, out _);
            return list;
        }

        // ── 1. 每层时间线无损：折叠成稀疏路点仍能逐层还原，且与源路线完全一致 ──

        [Test]
        public void RoundTrip_PreservesPerLayerTimeline()
        {
            var table = Corridor(6, 10, new List<(int, int)> { (3, 2), (3, 3) });   // 中段偶发危险 ⇒ 含等待
            var routes = Routes(table, 0, 5, 10, 1, 3);
            Assert.Greater(routes.Count, 0, "前置：应能搜出路线");

            var ds = New<PlayerRouteDataset>();
            Assert.True(ds.BuildFromRoutes(table, routes), "从路线建数据集应成功");

            var json = ds.ToJson(out var fail);
            Assert.True(string.IsNullOrEmpty(fail), "非空数据集导出不应有失败原因：" + fail);
            Assert.NotZero(json.Length, "导出的 JSON 不应为空串");

            var back = New<PlayerRouteDataset>();
            Assert.True(back.FromJson(json, out var f2), "读回应成功：" + f2);

            Assert.AreEqual(ds.Count, back.Count);
            for (var i = 0; i < ds.Count; i++)
            {
                ds.TryGetRoute(i, out var a);
                back.TryGetRoute(i, out var b);
                Assert.AreEqual(a.cellsPerLayer.Length, b.cellsPerLayer.Length, "路线 " + i + " 层数不等");
                for (var lay = 0; lay < a.cellsPerLayer.Length; lay++)
                    Assert.AreEqual(a.cellsPerLayer[lay], b.cellsPerLayer[lay],
                        "路线 " + i + " 第 " + lay + " 层站位不等（往返有损）");
                Assert.AreEqual(a.arrivalSeconds, b.arrivalSeconds, 1e-4f);
                Assert.AreEqual(a.goalCell, b.goalCell);
                Assert.AreEqual(a.startCell, b.startCell);
            }
        }

        [Test]
        public void MovementWaypoints_CollapseMatchesTimeline()
        {
            var table = Corridor(5, 8, new List<(int, int)>());
            var routes = Routes(table, 0, 4, 8, 1, 1);
            Assert.AreEqual(1, routes.Count, "前置：全安全走廊步长1 首条应搜出");

            var ds = New<PlayerRouteDataset>();
            Assert.True(ds.BuildFromRoutes(table, routes));
            Assert.True(ds.MovementWaypoints(0, out var cells, out var layers));

            ds.TryGetRoute(0, out var e);
            // 折叠后：每个 (cells[k], layers[k]) 必等于时间线该层站位；且相邻移动层号严格递增
            for (var k = 0; k < cells.Length; k++)
            {
                Assert.AreEqual(e.cellsPerLayer[layers[k]], cells[k], "折叠路点与时间线不符 @k" + k);
                if (k > 0) Assert.Greater(layers[k], layers[k - 1]);
            }
            // 末个移动路点必须就是撤离点
            Assert.AreEqual(e.goalCell, cells[cells.Length - 1]);
            // 全安全直线：移动数=4、无等待 ⇒ 时间线长度=5、折叠后也=5
            Assert.AreEqual(4, cells.Length - 1, "直线 4 跳");
        }

        // ── 2. 线上 JSON 契约：字段名被改就读不回（钉死 schema）──────────

        [Test]
        public void WireContract_FixedFieldNames_ParseBack()
        {
            // 手写字面量 = 当前 schema 的线上形状。任何人改了 FileSnapshot/RouteDto 的字段名，
            // 这段就会解析成默认值 ⇒ 下面的断言红 ⇒ 逼他同步改测试（即意识到"破坏旧数据集"）。
            const string KnownJson = @"{
  ""schemaVersion"": 1,
  ""dimX"": 4, ""dimZ"": 1, ""cellSize"": 0.5, ""timeStep"": 0.5,
  ""origin"": {""x"":0,""y"":0,""z"":0},
  ""routes"": [
    { ""cellsPerLayer"":[0,1,2,3], ""arrivalBucket"":3, ""arrivalSeconds"":1.5, ""startCell"":0, ""goalCell"":3 }
  ]
}";
            var ds = New<PlayerRouteDataset>();
            Assert.True(ds.FromJson(KnownJson, out var fail), "已知合法 JSON 应可载入：" + fail);
            Assert.AreEqual(1, ds.Count);
            ds.TryGetRoute(0, out var e);
            Assert.AreEqual(4, e.cellsPerLayer.Length);
            Assert.AreEqual(0, e.cellsPerLayer[0]);
            Assert.AreEqual(3, e.cellsPerLayer[3]);
            Assert.AreEqual(1.5f, e.arrivalSeconds, 1e-4f);
            Assert.AreEqual(4, ds.DimX);
            Assert.AreEqual(0.5f, ds.TimeStep, 1e-4f);
        }

        // ── 3. 版本不符 / 非法输入：可读失败、不产出半张 ───────────────

        [Test]
        public void WrongSchemaVersion_Rejected_WithReason()
        {
            var ds = New<PlayerRouteDataset>();
            const string FutureJson = @"{ ""schemaVersion"": 999, ""routes"": [ { ""cellsPerLayer"":[0,1] } ] }";
            Assert.False(ds.FromJson(FutureJson, out var fail));
            Assert.That(fail, Does.Contain("版本"));
            Assert.AreEqual(0, ds.Count, "版本不符不得载入任何路线");
        }

        [Test]
        public void InvalidOrEmpty_Inputs_FailReadably_NoHalfDataset()
        {
            var ds = New<PlayerRouteDataset>();
            Assert.False(ds.FromJson("", out var e1));
            Assert.That(e1, Does.Contain("为空"));
            Assert.False(ds.FromJson("{ not json ", out var e2));      // 语法坏
            Assert.False(string.IsNullOrEmpty(e2));
            Assert.False(ds.FromJson(@"{ ""schemaVersion"":1, ""routes"":[] }", out var e3));
            Assert.That(e3, Does.Contain("无路线"));

            // 空数据集导出 ToJson 给原因、返回空串，不写半成品
            Assert.AreEqual(string.Empty, ds.ToJson(out var ef));
            Assert.That(ef, Does.Contain("为空"));
        }

        [Test]
        public void BuildFromRoutes_GuardsNullAndEmpty()
        {
            var ds = New<PlayerRouteDataset>();
            Assert.False(ds.BuildFromRoutes(null, new List<PhaseKShortest.Route>()), "空表应拒绝");
            var table = Corridor(4, 6, new List<(int, int)>());
            Assert.False(ds.BuildFromRoutes(table, null), "路线列表 null 应拒绝");
            Assert.False(ds.BuildFromRoutes(table, new List<PhaseKShortest.Route>()), "空路线应拒绝");
            Assert.AreEqual(0, ds.Count, "失败路径不得留下部分数据");
        }

        // ── 4. 网格快照一致性：注入端据此拒绝错配数据集 ─────────────────

        [Test]
        public void GridMatches_DetectsMismatchedTable()
        {
            var tableA = Corridor(6, 8, new List<(int, int)>());
            var routes = Routes(tableA, 0, 5, 8, 1, 1);
            var ds = New<PlayerRouteDataset>();
            Assert.True(ds.BuildFromRoutes(tableA, routes));
            Assert.True(ds.GridMatches(tableA), "同一张表必须匹配");

            var tableB = Corridor(7, 8, new List<(int, int)>());     // 维度不同
            Assert.False(ds.GridMatches(tableB), "网格维度不同 ⇒ 不匹配（格索引会串位）");
            Assert.False(ds.GridMatches(null), "空表判不匹配");
        }

        [Test]
        public void SchemaVersionIsOne_SingleSource()
        {
            Assert.AreEqual(1, PlayerRouteDataset.SchemaVersion,
                "首版格式=1；改字段布局时必须抬它（D-22 同源纪律），且旧数据集靠它被拒而非静默误读");
        }
    }
}
