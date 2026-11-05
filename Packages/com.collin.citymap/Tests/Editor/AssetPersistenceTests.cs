using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Collin.CityMap.Tests
{
    /// <summary>
    /// 层资产的<b>可序列化性</b>与<b>可撤销性</b>回归防线（用户反馈⑤「雕刻笔刷 undo 不生效」的根因所在）。
    /// <para>本轮取证在磁盘上看到的事实：<c>SculptDelta.asset</c>、<c>FlattenFilter.asset</c>、
    /// <c>WaterNetwork.asset</c> 三个资产存盘后的 YAML 里<b>只有 <c>schemaVersion</c> 一行</b>，
    /// 列表内容整个不见了。根因是字段写成 <c>[SerializeField] readonly List&lt;T&gt;</c> ——
    /// Unity 的序列化管线<b>跳过 readonly 字段</b>，加了特性也一样跳。</para>
    /// <para>它一次造成两个用户级现象，而且是同一个原因：
    /// ① <b>数据从来没落过盘</b>：重开工程，雕刻、水种子、烘焙滤镜全空；
    /// ② <b>撤销必然不生效</b>：<c>UnityEditor.Undo.RecordObject</c> 拍的快照就是序列化数据，
    ///    没被序列化的字段不在快照里 ⇒ 撤销时无东西可还原 ⇒ 画面停在"撤销之前"，
    ///    用户读作"Ctrl+Z 没反应"。</para>
    /// <para>所以"字段被序列化收录"不是卫生问题，而是<b>撤销能力的前置条件</b>。</para>
    /// <para>⚠️ 三条已验证的纪律，都是本轮自己踩出来的：
    /// ① 探针用 <see cref="UnityEditor.SerializedObject"/>（写 YAML 的那条管线）。我先前用
    ///   <c>JsonUtility.ToJson</c> 看到同样的"字段不见了"，却把现象<b>编造解释</b>成
    ///   "JsonUtility 不可靠、实际入库正常"——真信了就把 bug 放过去了；
    /// ② <b>不要拿"存完再 LoadAssetAtPath 读回来比对"当落盘证据</b>：那是同一个内存实例，
    ///   对是否落盘零信息量，一眼像往返、实则恒真（本包已五次栽在恒真断言上，H-43/H-62/H-67 同族）；
    /// ③ <b>前提断言必须现读实现</b>：<c>CreateInstance</c> 只走 OnEnable 登记、<b>不 bump</b> 任何通道，
    ///   而 <see cref="Collin.CityMap.EditorTooling.UndoHistoryTracker.Scan"/> 首帧只建基线不标脏 ——
    ///   我第一版按直觉写了 <c>before &gt; 0</c> 与"扫描后 PendingDirty 必含某位"两条前提，必红。</para>
    /// </summary>
    public class AssetPersistenceTests
    {
        const float Cell = 2f;
        const int N = 32;

        readonly List<Object> _track = new List<Object>(16);
        readonly List<string> _assetPaths = new List<string>(8);

        CityGrid _grid;

        [SetUp]
        public void SetUp()
        {
            _grid = new CityGrid(N * Cell, Cell, Cell * 4f, N * Cell, N * Cell * 2f, 0f, Vector2.zero);
        }

        [TearDown]
        public void TearDown()
        {
            // 先删资产文件（AGENTS §9 卫生：临时产物不得留在包内），再释放对象引用
            foreach (var p in _assetPaths)
                if (!string.IsNullOrEmpty(p) && UnityEditor.AssetDatabase.LoadAssetAtPath<Object>(p) != null)
                    UnityEditor.AssetDatabase.DeleteAsset(p);
            _assetPaths.Clear();

            for (var i = _track.Count - 1; i >= 0; i--)
                if (_track[i] != null) Object.DestroyImmediate(_track[i], true);
            _track.Clear();
            UnityEditor.AssetDatabase.Refresh();
        }

        /// <summary>存成包内<b>固定路径</b>的探针资产（同 D-35：禁 GenerateUniqueAssetPath，那是副本滋生的来源）。</summary>
        T ToAsset<T>(T obj, string file) where T : ScriptableObject
        {
            var path = "Packages/com.collin.citymap/Sample/Sculpt/_probe_" + file + ".asset";
            if (UnityEditor.AssetDatabase.LoadAssetAtPath<T>(path) != null)
                UnityEditor.AssetDatabase.DeleteAsset(path);
            UnityEditor.AssetDatabase.CreateAsset(obj, path);
            UnityEditor.AssetDatabase.SaveAssets();
            _assetPaths.Add(path);
            return obj;
        }

        string ReadYaml(string file)
        {
            var path = "Packages/com.collin.citymap/Sample/Sculpt/_probe_" + file + ".asset";
            var abs = System.IO.Path.GetFullPath(path);
            Assert.IsTrue(System.IO.File.Exists(abs), "资产文件没落盘 = CreateAsset 就没成功");
            return System.IO.File.ReadAllText(abs);
        }

        // ─────────── ① 序列化收录：撤销能力的前置条件 ───────────

        /// <summary>三张层资产的<b>列表字段与账本</b>都必须被序列化管线收录。</summary>
        [Test]
        public void Layer_Assets_List_And_Ledger_Fields_Are_Serialized()
        {
            AssertLayer(ScriptableObject.CreateInstance<SculptDeltaAsset>(), "chunks");
            AssertLayer(ScriptableObject.CreateInstance<SculptFilterAsset>(), "chunks");
            AssertLayer(ScriptableObject.CreateInstance<WaterNetworkAsset>(), "seeds");
        }

        static void AssertLayer(ScriptableObject so, string listField)
        {
            try
            {
                var tracked = so as IIntentTracked;
                Assert.IsNotNull(tracked, $"{so.name} 必须实现 IIntentTracked：账本得跟着数据走");

                var wrapped = new UnityEditor.SerializedObject(so);
                Assert.IsNotNull(wrapped.FindProperty(listField),
                    $"{so.GetType().Name}.{listField} 没被序列化收录（多半是 readonly 修饰）⇒ " +
                    "既不会落盘，Undo 也拍不到它 ⇒ 用户看到的正是「重开就没了 / 撤销没反应」");
                Assert.IsNotNull(wrapped.FindProperty("ledger"),
                    $"{so.GetType().Name} 的账本必须与数据同资产序列化：撤销还原数据时序列号要一起回滚");
                Assert.IsNotNull(wrapped.FindProperty("stableId"),
                    $"{so.GetType().Name} 的 stableId 必须入库，否则重开后意图登记表认不回同一个对象");
            }
            finally { Object.DestroyImmediate(so, true); }
        }

        // ─────────── ② 真实 Undo 往返：撤销必须真的回退 ───────────

        /// <summary>
        /// 一笔雕刻被撤销后，<b>内容与它自己的账本序列号必须一起回到改前</b>。
        /// <para>这是用户反馈⑤的直接验收，两半缺一不可：
        /// 内容回滚但序列号不动 ⇒ <c>UndoHistoryTracker</c> 的通道检扫不出差异 ⇒
        /// 数据回到过去、画面停在未来（上一版就是这么坏的：脏位记在场景组件上，而 Undo 只还原这个资产）；
        /// 序列号回滚但内容不回 ⇒ 撤销本身无效。</para>
        /// </summary>
        [Test]
        public void Undo_Restores_Delta_Content_And_Its_Own_Ledger()
        {
            var delta = ToAsset(ScriptableObject.CreateInstance<SculptDeltaAsset>(), "delta");
            var go = new GameObject("undo_probe");
            var terr = go.AddComponent<CityTerrain>();
            _track.Add(go);
            terr.SetDelta(delta);

            // 基线：此刻<b>不假设</b>它 >0（CreateInstance/SetDelta 都不 bump 资产自己的 DeltaStroke）
            var before = delta.Ledger.SequenceOf(IntentKind.DeltaStroke);

            // 落一笔：与 SculptBrushTool.BeginStroke→EndStroke 同一顺序
            //（先登记 Undo 拍快照，再写格，再提交脏位）
            UnityEditor.Undo.RecordObject(delta, "Sculpt Stroke");
            delta.AddDelta(_grid, 6, 6, 5f);
            terr.MarkDeltaChanged(new Vector2(12f, 12f), 4f);

            Assert.AreEqual(1, delta.ChunkCount, "前提：这一笔确实写进了 delta");
            Assert.Greater(delta.Ledger.SequenceOf(IntentKind.DeltaStroke), before,
                "前提：资产账本必须前进（否则撤销后也没有差异可扫）");

            UnityEditor.Undo.PerformUndo();

            Assert.IsTrue(delta.IsEmpty,
                "撤销后雕刻内容必须回到改前：字段没被序列化的话 Undo 快照里根本没有它 ⇒ 就是「Ctrl+Z 没反应」");
            Assert.AreEqual(before, delta.Ledger.SequenceOf(IntentKind.DeltaStroke),
                "序列号必须与内容一起回滚：Tracker 靠「与基线不等」判定改动，序列号不动等于撤销不被发现");
        }

        /// <summary>撤销水种子：种子列表与它的账本必须一起回退。</summary>
        [Test]
        public void Undo_Restores_Water_Seeds_And_Their_Ledger()
        {
            var water = ToAsset(ScriptableObject.CreateInstance<WaterNetworkAsset>(), "water_undo");
            var before = water.Ledger.SequenceOf(IntentKind.RiverStroke);

            UnityEditor.Undo.RecordObject(water, "添加水");
            water.AddSeed(5, 5, 12f, 80f, out _);
            water.CommitChange(WaterNetworkAsset.SeedBounds(_grid, 5, 5));
            Assert.AreEqual(1, water.Count, "前提：种子已加入");

            UnityEditor.Undo.PerformUndo();

            Assert.AreEqual(0, water.Count, "撤销后种子必须消失（用户点错一下要能一键收回）");
            Assert.AreEqual(before, water.Ledger.SequenceOf(IntentKind.RiverStroke),
                "撤销必须同时回滚序列号，否则水面不会重算回去");
        }

        /// <summary>
        /// 撤销<b>烘焙滤镜内容</b>后，画面重放所依据的那层必须一起回去。
        /// <para>存在理由：烘焙是全图重写，"不可撤销的重计算 = 用户不敢点"。
        /// 面板在 Bake 前登记 <c>UnityEditor.Undo.RecordObject(filter)</c>，本用例钉的就是它真的能退。</para>
        /// </summary>
        [Test]
        public void Undo_Restores_Filter_Content()
        {
            var filter = ToAsset(ScriptableObject.CreateInstance<SculptFilterAsset>(), "filter_undo");
            Assert.IsTrue(filter.IsEmpty, "前提：新滤镜资产为空");

            UnityEditor.Undo.RecordObject(filter, "烘焙平整");
            filter.AddDelta(_grid, 3, 4, 2.5f);
            filter.Compact();
            Assert.IsFalse(filter.IsEmpty, "前提：写入后非空");

            UnityEditor.Undo.PerformUndo();
            Assert.IsTrue(filter.IsEmpty, "撤销烘焙后滤镜层必须回到改前的空层");
        }

        // ─────────── ③ 双写账本：两处脏位必须同源同值 ───────────

        /// <summary>
        /// 落一笔必须<b>同时</b>脏「场景组件」与「delta 资产」两份账本的同一个 <c>DeltaStroke</c> 通道，
        /// 且两份记录<b>同一个脏盒</b>。
        /// <para>为什么两处都要：组件那份是显示层取脏盒做<b>增量重建</b>的依据
        /// （<c>CityTerrainView</c> 自建路径读 <c>_terrain.Ledger.BoundsOf(DeltaStroke)</c>）；
        /// 资产那份是<b>撤销可见</b>的依据（Undo 只还原资产）。两者同为 DeltaStroke 位 ⇒
        /// Tracker 按位并入，<b>不会跑两遍</b>。</para>
        /// </summary>
        [Test]
        public void One_Stroke_Bumps_Both_Ledgers_With_The_Same_Dirty_Box()
        {
            var delta = ToAsset(ScriptableObject.CreateInstance<SculptDeltaAsset>(), "delta_dual");
            var go = new GameObject("dual_ledger");
            var terr = go.AddComponent<CityTerrain>();
            _track.Add(go);
            terr.SetDelta(delta);

            var t0 = terr.Ledger.SequenceOf(IntentKind.DeltaStroke);
            var d0 = delta.Ledger.SequenceOf(IntentKind.DeltaStroke);

            terr.MarkDeltaChanged(new Bounds(new Vector3(500f, 0f, 500f), new Vector3(120f, 1f, 80f)), 40f);

            Assert.Greater(terr.Ledger.SequenceOf(IntentKind.DeltaStroke), t0, "组件账本必须脏（取脏盒用）");
            Assert.Greater(delta.Ledger.SequenceOf(IntentKind.DeltaStroke), d0, "资产账本必须脏（撤销可见用）");

            var tb = terr.Ledger.BoundsOf(IntentKind.DeltaStroke);
            var db = delta.Ledger.BoundsOf(IntentKind.DeltaStroke);
            Assert.AreEqual(tb.center, db.center,
                "两份脏盒中心不一致 ⇒ 增量重建与撤销重放会找不同的 chunk（§0.1 双源）");
            Assert.AreEqual(tb.size, db.size, "两份脏盒尺寸不一致 = 两处各写了一份口径");

            // 逐轴保留（H-63）：长笔划是长方形，取长轴当正方形会虚报一大片 chunk
            Assert.Greater(tb.size.x, tb.size.z, "前提：这条笔划是横长形（120×80 外加两侧各 40）");

            // 语义仍是 D-31：脏 Flatten，绝不脏 Terrain
            var bits = IntentChangePlanner.DirtyOf(IntentKind.DeltaStroke);
            Assert.IsTrue((bits & DirtyBits.Flatten) != 0, "雕刻必须脏 Flatten（D-31）");
            Assert.AreEqual(0, (int)(bits & DirtyBits.Terrain), "雕刻绝不得脏 Terrain（不重跑噪声，D-31/A-2）");
        }

        /// <summary>
        /// 水种子提交点：脏位落在<b>种子资产</b>上，且资产在意图登记表里可被认回。
        /// <para>存在理由（用户反馈②「种子变了没刷新」）：上一版水面脏位只记在
        /// <see cref="CityTerrain"/> 上，撤销还原的是资产 ⇒ 组件账本不动 ⇒ 静默失效。</para>
        /// </summary>
        [Test]
        public void Seed_Change_Dirties_Through_The_Asset_That_Gets_Undone()
        {
            var water = ToAsset(ScriptableObject.CreateInstance<WaterNetworkAsset>(), "water_dirty");
            Assert.IsTrue(IntentRegistry.TryGet(water.StableId, out var found),
                "种子资产必须登记在意图表里（否则 Tracker 的集合哈希检覆盖不到它）");
            Assert.AreSame(water, found, "登记在册的必须是同一个对象，否则扫的是别人的账本");

            var s0 = water.Ledger.SequenceOf(IntentKind.RiverStroke);
            water.AddSeed(9, 9, 20f, 60f, out _);
            water.CommitChange(WaterNetworkAsset.SeedBounds(_grid, 9, 9));
            Assert.Greater(water.Ledger.SequenceOf(IntentKind.RiverStroke), s0,
                "提交点必须把 RiverStroke 记在被撤销的那个对象上");

            // 零尺寸入参兜底：否则被下游当"无脏区"丢掉 ⇒ 增量重建退化成全图（H-63 同族）
            water.CommitChange(default);
            var b = water.Ledger.BoundsOf(IntentKind.RiverStroke);
            Assert.Greater(b.size.x, 0f, "退化输入必须兜底成可用脏盒");
            Assert.Greater(b.size.z, 0f);
        }

        /// <summary>烘焙提交点同样落在滤镜资产上；对照：纯工具态切换绝不 bump。</summary>
        [Test]
        public void Filter_Commits_Through_Its_Own_Asset_And_Tool_State_Stays_Silent()
        {
            var filter = ToAsset(ScriptableObject.CreateInstance<SculptFilterAsset>(), "filter_commit");
            var go = new GameObject("filter_ledger");
            var terr = go.AddComponent<CityTerrain>();
            _track.Add(go);

            var f0 = filter.Ledger.SequenceOf(IntentKind.ParcelModel);
            filter.CommitChange(_grid.WorldSize);
            Assert.Greater(filter.Ledger.SequenceOf(IntentKind.ParcelModel), f0,
                "烘焙/清空后必须脏滤镜资产自己的账本（撤销烘焙才看得见）");

            // 对照：接管开关是纯交互态，误 bump 会让每次切工具白跑一遍 S1b+S6（H-50 的成本红线）
            var t0 = terr.ManualVersion;
            terr.SetSculptToolActive(true);
            terr.SetWaterToolActive(true);
            terr.SetSculptToolActive(false);
            terr.SetWaterToolActive(false);
            Assert.AreEqual(t0, terr.ManualVersion, "切工具不得推进 ManualVersion");
        }

        // ─────────── ④ 落盘文本：字段收录了，内容也要真写出去 ───────────

        /// <summary>
        /// 雕刻增量与烘焙滤镜必须<b>真的写出内容</b>。
        /// <para>水种子那一份落在 <c>WaterFloodTests.Seed_And_Ledger_Actually_Write_Into_Asset_Yaml_On_Disk</c>，
        /// 这里只补另外两张资产（三张是同一处 readonly 缺陷，逐个钉住才不会改一个漏两个）。</para>
        /// <para>⚠️ 断言读的是<b>磁盘文本</b>本身。不要写成"存完再 LoadAssetAtPath 读回来比对"：
        /// 那拿回的是同一个内存实例，值必然相等，对是否落盘零信息量（恒真）。</para>
        /// </summary>
        [Test]
        public void Delta_And_Filter_Content_Actually_Write_Onto_Disk()
        {
            var delta = ScriptableObject.CreateInstance<SculptDeltaAsset>();
            delta.AddDelta(_grid, 4, 5, 7.25f);
            UnityEditor.EditorUtility.SetDirty(delta);
            ToAsset(delta, "delta_persist");

            var dy = ReadYaml("delta_persist");
            StringAssert.Contains("chunks:", dy, "delta 的 chunks 没落盘（readonly 同族缺陷）");
            StringAssert.Contains("mode: 0", dy, "叠加模式没落盘");
            StringAssert.Contains("7.25", dy, "雕刻增量值没落盘：用户雕的山重开工程就没了");

            var filter = ScriptableObject.CreateInstance<SculptFilterAsset>();
            filter.AddDelta(_grid, 2, 3, 3.5f);
            filter.Compact();
            UnityEditor.EditorUtility.SetDirty(filter);
            ToAsset(filter, "filter_persist");

            var fy = ReadYaml("filter_persist");
            StringAssert.Contains("chunks:", fy, "滤镜的 chunks 没落盘 ⇒「烘焙完画面不动/开关看起来一样」的真正原因");
            StringAssert.Contains("3.5", fy, "滤镜修正值没落盘");
        }
    }
}
