using NUnit.Framework;
using UnityEngine;

namespace Collin.CityMap.Tests
{
    /// <summary>
    /// 01 §2.3 人工雕刻层（SculptDeltaAsset）的数据契约断言。
    /// <para>本层最危险的三类失败都是"看着能用、数据其实错"：</para>
    /// <list type="number">
    /// <item><b>哨兵撞值</b>：Absolute 模式若用 0 表示"无目标"，用户把一片地压到海拔 0m 会被静默吞掉。
    /// 契约要求用 NaN（01 §2.3 / 与 11 §2 heightValue 双义同坑）。</item>
    /// <item><b>污染基底</b>：人工层若写进 BaseHeights，下一轮约束就从含人工的高度拟合目标平面
    /// ⇒ 把自己的上轮结果当输入 ⇒ 逐轮漂移（01 §2.4 正反馈）。必须只进 Heights。</item>
    /// <item><b>稀疏不剪枝</b>：全零 chunk 条目不删 → 反复雕刻把资产撑爆（01 §9 风险）。</item>
    /// </list>
    /// <para>本文件只测<b>数据结构与重放</b>，不含管线接线与笔刷几何（那是后续增量）。</para>
    /// </summary>
    public class SculptDeltaTests
    {
        /// <summary>200m 图幅 / 逻辑格 2m / chunk 20m ⇒ 每边 10 格（CellsPerChunk=10）。</summary>
        static CityGrid TestGrid() => new CityGrid(200f, 2f, 8f, 20f, 100f, 10f);

        [TearDown]
        public void TearDown()
        {
            // 同 TerrainDataTests 的纪律：所有 Object 交统一销毁，避免残留污染其他类的登记断言
            foreach (var o in _spawned) if (o != null) Object.DestroyImmediate(o, true);
            _spawned.Clear();
        }
        readonly System.Collections.Generic.List<Object> _spawned
            = new System.Collections.Generic.List<Object>();
        T Track<T>(T o) where T : Object { _spawned.Add(o); return o; }

        SculptDeltaAsset NewAsset() => Track(ScriptableObject.CreateInstance<SculptDeltaAsset>());

        // ───────────────── 基础重放语义 ─────────────────

        [Test]
        public void Additive_Applies_To_Working_Heights_Not_Base()
        {
            var g = TestGrid();
            var field = new TerrainField(g);
            for (var i = 0; i < field.Heights.Length; i++) { field.Heights[i] = 5f; field.BaseHeights[i] = 5f; }

            var d = NewAsset();
            d.AddDelta(g, 3, 4, 2.5f);
            d.ApplyTo(field);

            var idx = 4 * g.Columns + 3;
            Assert.AreEqual(7.5f, field.Heights[idx], 1e-5f, "增量必须进工作数组");
            Assert.AreEqual(5f, field.BaseHeights[idx], 1e-5f,
                "基底绝不能被人工层改写——否则约束会把上轮人工结果当输入而逐轮漂移（01 §2.4）");

            // 未雕刻的格必须一个字节都没动
            var untouched = 0;
            for (var i = 0; i < field.Heights.Length; i++)
                if (i != idx && !Mathf.Approximately(field.Heights[i], 5f)) untouched++;
            Assert.AreEqual(0, untouched, "ApplyTo 不得越出被雕刻的格");
        }

        [Test]
        public void Repeated_Rerun_On_Reset_Base_Is_Stable_No_Drift()
        {
            // 幂等的正确形态：每次先 CommitBaseAsWorking 复位再重放 ⇒ 结果恒定。
            // 若实现把 delta 写进 BaseHeights，这里会逐轮上升——正是漂移的可观测签名。
            var g = TestGrid();
            var field = new TerrainField(g);
            for (var i = 0; i < field.Heights.Length; i++) { field.Heights[i] = 3f; field.BaseHeights[i] = 3f; }
            var d = NewAsset();
            d.AddDelta(g, 10, 10, 1.5f);

            var idx = 10 * g.Columns + 10;
            float last = float.NaN;
            for (var round = 0; round < 5; round++)
            {
                field.CommitBaseAsWorking();
                d.ApplyTo(field);
                last = field.Heights[idx];
                Assert.AreEqual(4.5f, last, 1e-5f, $"第 {round} 轮重放结果漂移（实得 {last}）");
            }
            Assert.AreEqual(3f, field.BaseHeights[idx], 1e-5f, "基底五轮后仍须是原始值");
        }

        [Test]
        public void Apply_Is_Accumulative_By_Design_And_Caller_Owns_Reset()
        {
            // 契约里明确写了"不复位连调两次会加两遍"。这条断言钉住该语义：
            // 一旦有人为了"看起来幂等"改成内部去重，重放次序第 2 层（下切）与第 3 层（delta）
            // 的叠加关系就会被破坏，且是静默的。
            var g = TestGrid();
            var field = new TerrainField(g);
            for (var i = 0; i < field.Heights.Length; i++) { field.Heights[i] = 0f; field.BaseHeights[i] = 0f; }
            var d = NewAsset();
            d.AddDelta(g, 5, 5, 1f);

            var idx = 5 * g.Columns + 5;
            d.ApplyTo(field);
            var once = field.Heights[idx];
            d.ApplyTo(field);
            Assert.AreEqual(once * 2f, field.Heights[idx], 1e-5f,
                "未复位时 ApplyTo 必须仍为累加（复位责任在调用方）");
        }

        // ───────────────── Absolute 哨兵（01 §2.3 核心风险） ─────────────────

        [Test]
        public void Absolute_Target_Zero_Meters_Is_Honored_Not_Dropped()
        {
            var g = TestGrid();
            var field = new TerrainField(g);
            for (var i = 0; i < field.Heights.Length; i++) { field.Heights[i] = 8f; field.BaseHeights[i] = 8f; }

            var d = NewAsset();
            d.SetAbsolute(g, 2, 2, 0f);          // 用户就要海拔 0m
            d.ApplyTo(field);

            var idx = 2 * g.Columns + 2;
            Assert.AreEqual(0f, field.Heights[idx], 1e-5f,
                "0m 是合法高程；若用 0 当【无目标】哨兵，这里的压平会被静默吞掉（11 §2 同类坑）");
            Assert.AreEqual(1, d.CountAbsoluteCells(), "绝对 0m 必须计入非空目标数");
        }

        [Test]
        public void Absolute_Overwrites_Instead_Of_Accumulating()
        {
            var g = TestGrid();
            var field = new TerrainField(g);
            for (var i = 0; i < field.Heights.Length; i++) { field.Heights[i] = 8f; field.BaseHeights[i] = 8f; }

            var d = NewAsset();
            d.SetAbsolute(g, 6, 6, 12f);
            d.SetAbsolute(g, 6, 6, 4f);          // 覆盖，不是 +4
            d.ApplyTo(field);

            Assert.AreEqual(4f, field.Heights[6 * g.Columns + 6], 1e-5f,
                "Absolute 重放必须赋值而非累加（否则同一目标设两次会变成两倍高）");
        }

        [Test]
        public void ClearAbsolute_Restores_NoTarget_And_Prunes()
        {
            var g = TestGrid();
            var d = NewAsset();
            d.SetAbsolute(g, 0, 0, 9f);
            Assert.AreEqual(1, d.ChunkCount);
            Assert.AreEqual(1, d.CountAbsoluteCells());

            d.ClearAbsolute(g, 0, 0);
            Assert.AreEqual(0, d.CountAbsoluteCells(), "清除后不得再有非 NaN 目标");
            Assert.AreEqual(0, d.ChunkCount, "整块空了必须剪除条目（契约：稀疏存储）");

            // 重放不得改动任何高度
            var field = new TerrainField(g);
            for (var i = 0; i < field.Heights.Length; i++) { field.Heights[i] = 7f; field.BaseHeights[i] = 7f; }
            d.ApplyTo(field);
            Assert.AreEqual(7f, field.Heights[0], 1e-5f);
        }

        [Test]
        public void Additive_And_Absolute_Withstand_Each_Other_Independently()
        {
            // H-45：重放顺序必须确定（先 Absolute 赋值、后 Additive 叠加），
            // 且与条目的**插入顺序无关**——两种插入序都得同一个结果，用户才摸得到规律。
            var g = TestGrid();
            var field = new TerrainField(g);
            for (var i = 0; i < field.Heights.Length; i++) { field.Heights[i] = 0f; field.BaseHeights[i] = 0f; }

            var a = NewAsset();
            a.AddDelta(g, 1, 1, 3f);
            a.SetAbsolute(g, 1, 1, 7f);
            Assert.AreEqual(2, a.ChunkCount,
                "同一 chunk 坐标的两种模式必须是两条独立条目（混在一条里就无法表达叠加语义）");
            a.ApplyTo(field);
            var idx = 1 * g.Columns + 1;
            var got1 = field.Heights[idx];
            Assert.AreEqual(10f, got1, 1e-5f, "重放次序：先 Absolute 赋值再 Additive 叠加 = 7+3");

            // 反序插入
            for (var i = 0; i < field.Heights.Length; i++) field.Heights[i] = 0f;
            var b = NewAsset();
            b.SetAbsolute(g, 1, 1, 7f);
            b.AddDelta(g, 1, 1, 3f);
            b.ApplyTo(field);
            Assert.AreEqual(got1, field.Heights[idx], 1e-5f,
                "插入顺序不同、结果必须相同（单遍按插入序实现会得 7 或 10 的不定值）");
        }

        // ───────────────── 稀疏剪枝（01 §9 资产膨胀风险） ─────────────────

        [Test]
        public void Additive_Back_To_Zero_Prunes_Chunk()
        {
            var d = NewAsset();
            var g = TestGrid();
            d.AddDelta(g, 3, 3, 2f);
            Assert.AreEqual(1, d.ChunkCount);
            Assert.AreEqual(1, d.CountAdditiveCells());

            d.AddDelta(g, 3, 3, -2f);            // 抬回来 = 净零
            Assert.AreEqual(0, d.CountAdditiveCells(), "净零格不得计为非零");
            Assert.AreEqual(0, d.ChunkCount, "全零 chunk 必须删除条目——否则反复大笔刷会把资产撑爆（01 §9）");
        }

        [Test]
        public void Residual_Noise_Below_Epsilon_Is_Snapped_To_Zero()
        {
            // 固定步长积分会累出 1e-8 级残差；不吸附零就永远剪不掉条目，且 CountAdditiveCells 虚高。
            var d = NewAsset();
            var g = TestGrid();
            d.AddDelta(g, 5, 5, 1f);
            d.AddDelta(g, 5, 5, -0.99999999f);   // 残差 ~1e-8
            Assert.AreEqual(0, d.ChunkCount, "亚容差残差应视为零并剪枝");
        }

        [Test]
        public void Multiple_Cells_One_Chunk_Stay_One_Entry()
        {
            var d = NewAsset();
            var g = TestGrid();
            var cpc = SculptDeltaAsset.CellsPerChunk(g);
            Assert.AreEqual(10, cpc, "chunk 20m / 格 2m ⇒ 每边 10 格");

            for (var i = 0; i < cpc; i++) d.AddDelta(g, i, 0, 0.5f);   // 全在 chunk(0,0)
            Assert.AreEqual(1, d.ChunkCount, "同 chunk 内多格不得各开一条目（否则稀疏毫无意义）");
            Assert.AreEqual(cpc, d.CountAdditiveCells());
        }

        // ───────────────── 坐标与越界安全 ─────────────────

        [Test]
        public void Out_Of_Map_Writes_Are_Rejected_Without_Corrupting()
        {
            var g = TestGrid();
            var d = NewAsset();
            d.AddDelta(g, -5, 3, 1f);
            d.AddDelta(g, 3, -1, 1f);
            d.AddDelta(g, g.Columns, 0, 1f);      // 恰越界
            d.AddDelta(g, 0, g.Rows + 99, 1f);
            Assert.IsTrue(d.IsEmpty, "图外写入必须被拒（既不能崩，也不能留下永远读不到的幽灵格）");

            var field = new TerrainField(g);
            d.ApplyTo(field);                     // 空资产重放：不得抛
            Assert.AreEqual(0, d.ChunkCount);
        }

        [Test]
        public void Edge_Chunk_Clipped_By_Map_Does_Not_Write_Outside()
        {
            // 图幅不是 chunk 整数倍时，末尾 chunk 的 values 仍按整块分配 ⇒ 重放必须按图幅裁剪。
            // 200m 图幅 / chunk 20m 正好整除，故构造一个非整除的格网来打这条路径。
            var g = new CityGrid(100f, 2f, 8f, 30f, 60f, 0f);   // chunk 30m ⇒ 每边 15 格，100/30 不整除
            var cpc = SculptDeltaAsset.CellsPerChunk(g);
            Assert.Greater(cpc * cpc, 0);

            var d = NewAsset();
            var lastIdx = g.Columns - 1;
            d.AddDelta(g, lastIdx, lastIdx, 4f);               // 落在最后一个 chunk
            Assert.AreEqual(1, d.ChunkCount);

            var field = new TerrainField(g);
            for (var i = 0; i < field.Heights.Length; i++) { field.Heights[i] = 1f; field.BaseHeights[i] = 1f; }
            d.ApplyTo(field);
            Assert.AreEqual(5f, field.Heights[lastIdx * g.Columns + lastIdx], 1e-5f,
                "边缘 chunk 内的合法格必须写到位");

            var changed = 0;
            for (var i = 0; i < field.Heights.Length; i++)
                if (!Mathf.Approximately(field.Heights[i], i == lastIdx * g.Columns + lastIdx ? 5f : 1f)) changed++;
            Assert.AreEqual(0, changed, "越出图幅的格不得被写入（values 尺寸大于实际覆盖时靠裁剪兜住）");
        }

        [Test]
        public void Grid_With_Zero_Chunk_Size_Is_Inert()
        {
            var d = NewAsset();
            CityGrid empty = default;                         // 结构体默认实例：ChunkSize=0
            Assert.AreEqual(0, SculptDeltaAsset.CellsPerChunk(in empty));
            d.AddDelta(in empty, 1, 1, 5f);
            Assert.IsTrue(d.IsEmpty, "无效格网必须整体惰性化，不得除零或崩");

            var field = new TerrainField(new CityGrid(100f, 2f, 8f, 20f, 60f, 0f));
            d.ApplyTo(field);
        }

        [Test]
        public void Cells_Per_Chunk_Matches_Grid_Config()
        {
            // 两档真实配置各核对一次。CityGrid 构造已保证 chunkSize 是 cellSize 的整数倍，
            // 所以这里断言的是**换算关系**；实现用 RoundToInt 只是防御浮点误差，
            // 本档无法构造出 9.999 的输入，故**不假装测过了舍入**。
            var small = TestGrid();                                        // chunk 20m / 格 2m
            Assert.AreEqual(10, SculptDeltaAsset.CellsPerChunk(in small));

            var large = new CityGrid(2000f, 4f, 16f, 128f, 1024f, 60f);     // 8km 档：chunk 128m / 格 4m
            Assert.AreEqual(32, SculptDeltaAsset.CellsPerChunk(in large));

            CityGrid zero = default;
            Assert.AreEqual(0, SculptDeltaAsset.CellsPerChunk(in zero), "无效格网须返回 0，不得除零崩");
        }

        [Test]
        public void CountHelpers_Distinguish_Modes_And_Do_Not_Confuse_Sentinels()
        {
            var g = TestGrid();
            var d = NewAsset();
            d.AddDelta(g, 0, 0, 1f);
            d.AddDelta(g, 0, 1, 0f);              // 零增量 = 无内容
            d.SetAbsolute(g, 5, 5, 0f);           // 绝对零 = 有内容
            Assert.AreEqual(1, d.CountAdditiveCells(), "零增量不得计入");
            Assert.AreEqual(1, d.CountAbsoluteCells(), "绝对 0m 必须计入（与 Additive 的 0 语义相反）");
        }

        [Test]
        public void ClearAll_Empties_Everything()
        {
            var g = TestGrid();
            var d = NewAsset();
            d.AddDelta(g, 1, 1, 2f);
            d.SetAbsolute(g, 2, 2, 9f);
            Assert.AreEqual(2, d.ChunkCount);
            d.ClearAll();
            Assert.IsTrue(d.IsEmpty);
            Assert.AreEqual(0, d.CountAdditiveCells());
            Assert.AreEqual(0, d.CountAbsoluteCells());
        }
    }
}
