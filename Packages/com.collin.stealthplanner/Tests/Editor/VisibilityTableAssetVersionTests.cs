using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Collin.StealthPlanner.Tests
{
    /// <summary>
    /// 表资产的结构版本门禁（DESIGN **D-19** 的处置落地，原未完成项 U-5）。
    /// <para>要防的失效很具体：v1 的表<b>没有足迹栏</b>（哪些格真被射线探测过）。
    /// 代码若照常接受 v1，"没测过的格"（墙、掩体内部）就会读成<b>安全可走</b>——
    /// 热区把墙涂绿、求解器规划出穿墙路线，而且<b>全程不报错</b>。
    /// 兼容默认值救不了这一类：默认"全都有数据"恰恰是最危险的方向。
    /// 用户 2026-11-05 拍板：<b>抬版本号让 v1 一律作废重烘</b>（重烘实测 0.82s、菜单一键，
    /// 且表是场景派生产物、不入库）。</para>
    /// <para>所以这里钉三件事：① 版本号确实是 2（防有人改回去）；
    /// ② v1 资产<b>拒用</b>且原因可操作（含"重烘"）；③ 未烘与版本不符是<b>两条不同</b>提示。
    /// 只增字段不抬版本这条老毛病，由 ① 的常量断言 + ② 的行为断言一起守着。</para>
    /// </summary>
    public class VisibilityTableAssetVersionTests
    {
        const BindingFlags Priv = BindingFlags.Instance | BindingFlags.NonPublic;

        static VisibilityTableAsset MakeBaked(int dimX, out VisibilityTable table)
        {
            table = new VisibilityTable();
            table.ConfigureGrid(Vector3.zero, 0.5f, dimX, 1, 0.5f);
            var slice = table.AddSlice(1, "guard", 4f, 8);
            table.SetSliceVisible(slice, 0, 0, true);
            table.RebuildPermanentSafeLayer();
            // 模拟真实烘焙器收尾：登记足迹（v2 的表必然带足迹，见两条落盘路径都要求跑完全程）
            table.MarkEvaluated(new System.Collections.Generic.List<int> { 0, 1, 2 });

            var asset = ScriptableObject.CreateInstance<VisibilityTableAsset>();
            asset.SetBaked(table, table.CellSize, table.TimeStep, 8f,
                           table.Slices.Count, table.PermanentlySafeCellCount, table.PackedByteCount);
            return asset;
        }

        [TearDown]
        public void CleanUp()
        {
            // ⚠️ 只销毁本用例 CreateInstance 出来的临时实例。
            // Resources.FindObjectsOfTypeAll<T>() 会把**库里的真实资产**也列进来
            // （本包 Sample/VisibilityTable.asset 就在其中），直接销毁会打出
            // 「Destroying assets is not permitted to avoid data loss」——
            // Unity 拦得对，但这行 error 会破 §1.6b 归零门禁、且用例判失败（TearDown 里失败）。
            // IsPersistent 为 true ⇔ 该对象是磁盘上的资产，一律不许碰。
            foreach (var o in Resources.FindObjectsOfTypeAll<VisibilityTableAsset>())
            {
                if (o != null && !EditorUtility.IsPersistent(o)) Object.DestroyImmediate(o, true);
            }
        }

        [Test]
        public void LayoutVersion_IsTwo_SinceFootprintFieldWasAdded()
        {
            Assert.AreEqual(2, VisibilityTableAsset.LayoutVersion,
                "足迹栏（_evaluated）是 v2 新增：v1 资产缺这一栏 ⇒ 墙会被当可走，必须作废。"
                + "若此断言变红说明有人把版本号改回去了（等于把 U-5 的洞重新打开）");
        }

        [Test]
        public void CurrentBake_WritesCurrentVersion_AndTableUsable()
        {
            var asset = MakeBaked(4, out _);
            Assert.AreEqual(VisibilityTableAsset.LayoutVersion, asset.Version, "刚烘的表应带当前版本号");
            Assert.NotNull(asset.Table, "当前版本应可用");
            Assert.True(asset.IsBaked);
            Assert.True(asset.Table.HasFootprint, "v2 的表必须带足迹（烘焙收尾登记）");
        }

        [Test]
        public void LegacyVersion1_Asset_IsRejected_NotSilentlyUsed()
        {
            var asset = MakeBaked(4, out var table);
            Assert.NotNull(asset.Table, "前置：v2 状态可用");

            // 把资产版本号改回 1 —— 精确复现「库里躺着 M1 时代烘的旧表」这一现场
            typeof(VisibilityTableAsset).GetField("_version", Priv).SetValue(asset, 1);

            Assert.IsNull(asset.Table, "旧版本资产<b>不得</b>被当作可用表返回");
            Assert.False(asset.IsBaked, "旧版本不得自称已烘焙");
            Assert.False(asset.TryQuery(table.CellCenterWorld(1), 0f, out var dangerous, out var degraded),
                "查询必须返回 false（走降级出口）");
            Assert.True(dangerous, "降级时保守判危险，绝不谎报安全");
            Assert.That(degraded, Does.Contain("重烘"), "原因必须给出可操作动作：" + degraded);
            Assert.That(degraded, Does.Contain("1").And.Contain("2"),
                "原因应同时报出「资产的版本」与「当前版本」，否则用户无法判断该干什么：" + degraded);
        }

        [Test]
        public void NotBakedAndVersionMismatch_GiveDifferentReasons()
        {
            var fresh = ScriptableObject.CreateInstance<VisibilityTableAsset>();
            Assert.False(fresh.TryQuery(Vector3.zero, 0f, out _, out var whyFresh));
            Assert.That(whyFresh, Does.Contain("尚未烘焙"), "未烘资产的提示应指向烘焙入口：" + whyFresh);
            Assert.That(whyFresh, Does.Not.Contain("≠"), "未烘时不该报版本冲突（两条用户动作不同）");

            var legacy = MakeBaked(4, out _);
            typeof(VisibilityTableAsset).GetField("_version", Priv).SetValue(legacy, 1);
            Assert.False(legacy.TryQuery(Vector3.zero, 0f, out _, out var whyLegacy));
            Assert.That(whyLegacy, Does.Contain("≠"), "版本冲突应明说两个版本号：" + whyLegacy);
        }

        [Test]
        public void ClearBaked_ResetsToUnbaked_NotToStaleVersion()
        {
            var asset = MakeBaked(4, out _);
            asset.ClearBaked();
            Assert.AreEqual(0, asset.Version, "清空后须回到「未烘焙」而非留下旧版本号");
            Assert.IsNull(asset.Table);
            Assert.False(asset.IsBaked);
            Assert.False(asset.TryQuery(Vector3.zero, 0f, out _, out var why));
            Assert.That(why, Does.Contain("尚未烘焙"));
        }
    }
}
