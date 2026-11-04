using System;
using System.Collections.Generic;
using UnityEngine;

namespace Collin.CityMap
{
    /// <summary>
    /// 场景侧地形实例（Modules/01 §2.2，AGENTS §0.1 原则2 的"Instance 半边"）。
    /// <para>判据：<see cref="TerrainProfile"/> 可被多个城市实例复用，因此
    /// <b>实例差异（人工平整区、自动试设开关、高度数据）一律序列化在本组件上，绝不写回 Profile</b>。</para>
    /// <para>M2 范围：只承载 S1a/S1b 所需的意图数据。
    /// 雕刻 delta（<c>SculptDeltaAsset</c>）与河流下切层在 M3/M4 接入，
    /// 接入时按 01 §3.2 的固定次序插在基底与约束之间。</para>
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [AddComponentMenu("Collin/CityMap/City Terrain")]
    public sealed class CityTerrain : MonoBehaviour, IIntentTracked
    {
        [Header("共享配置（Model）")]
        [Tooltip("地形参数；可被多个城市实例复用")]
        [SerializeField] TerrainProfile profile;

        [Header("实例态：S1b 自动试设地块（M2 用于验证平整约束）")]
        [Tooltip("以城心为锚铺 N×N 个试设地块，M5 后由真实 parcel 供数")]
        [SerializeField] bool autoFlattenAtCenter = false;
        [SerializeField, Range(1, 12)] int autoTileCount = 3;

        [Header("实例态：人工平整区（意图层，存区域不存平面）")]
        [SerializeField] List<ManualFlattenZone> manualZones = new List<ManualFlattenZone>();

        [Header("实例态：山水雕刻人工层（01 §2.2/§2.3，实例差异不写 Model）")]
        [Tooltip("SculptDeltaAsset：笔刷增量与绝对压平目标；null = 无人工改动")]
        [SerializeField] SculptDeltaAsset delta;

        [Header("阶段开关：自动平整（12 §2 三步工作流）")]
        [Tooltip("关＝停在「噪声+雕刻」的意图地表，平整不参与（边雕边看、不被抹平）；"
                 + "开＝在已定稿的意图地表之上跑自动平整整理。雕刻期建议关。")]
        [SerializeField] bool flattenEnabled = true;

        [Header("身份")]
        [SerializeField] string stableId;
        [SerializeField] IntentChangeLedger ledger = new IntentChangeLedger();

        /// <summary>
        /// 生成统计（<b>故意不序列化</b>：S1b 在编辑期回写，若为序列化字段会因 ExecuteAlways
        /// 把场景标脏 ⇒ 用户什么都没改却要保存场景。呈现用，重启后归零无影响）。</summary>
        int lastConstraints, lastCells, lastRejectedConstraints;

        public TerrainProfile Profile => profile;
        public SculptDeltaAsset Delta => delta;
        public bool AutoFlattenAtCenter => autoFlattenAtCenter;
        public bool FlattenEnabled => flattenEnabled;
        public int AutoTileCount => Mathf.Clamp(autoTileCount, 1, 12);
        public IReadOnlyList<ManualFlattenZone> ManualZones => manualZones;
        public string StableId => stableId;
        public IntentChangeLedger Ledger => ledger;
        public long ManualVersion => ledger?.ManualVersion ?? 0;
        public int LastConstraints => lastConstraints;
        public int LastCells => lastCells;
        public int LastRejectedConstraints => lastRejectedConstraints;

        public void SetProfile(TerrainProfile p)
        {
            if (ReferenceEquals(p, profile)) return;
            profile = p;
            // 换 Profile 不 bump 本对象的 TerrainModel 通道（那是 Profile 自己的变化），
            // 但实例引用变了 ⇒ 必须重算地形
            Bump(IntentKind.TerrainModel);
        }

        /// <summary>
        /// 自动平整阶段开关（12 §2 三步工作流）。<b>关闭</b>时 S1b 只重放到"噪声基底+雕刻"的
        /// 意图地表就返回（平整不参与，雕刻不被抹平）；<b>开启</b>时在已定稿的意图地表之上跑平整。
        /// <para>走 ParcelModel 通道（S4→S1b→S6）：切换本身必须触发一次 S1b 重算，
        /// 否则从关到开时画面仍是旧的未平整态（所见即所得要求即时响应）。</para>
        /// </summary>
        public void SetFlattenEnabled(bool on)
        {
            if (on == flattenEnabled) return;
            flattenEnabled = on;
            Bump(IntentKind.ParcelModel);
        }

        public void SetAutoFlatten(bool on, int tiles)
        {
            tiles = Mathf.Clamp(tiles, 1, 12);
            if (on == autoFlattenAtCenter && tiles == autoTileCount) return;
            autoFlattenAtCenter = on;
            autoTileCount = tiles;
            // 地块级意图：走 ParcelModel 通道（S4→S1b→S6），不是地形噪声通道
            Bump(IntentKind.ParcelModel);
        }

        /// <summary>新增一个人工平整区（意图提交点）。返回该区的 id。</summary>
        public string AddZone(Vector2 center, Vector2 fullSize, float absoluteHeight = 0f)
        {
            EnsureStableId();
            var zone = new ManualFlattenZone
            {
                stableId = IntentRegistry.NewStableId("zone"),
                center = center,
                fullSize = new Vector2(Mathf.Max(4f, fullSize.x), Mathf.Max(4f, fullSize.y)),
                absoluteHeight = Mathf.Max(0f, absoluteHeight),
            };
            manualZones.Add(zone);
            Bump(IntentKind.ParcelModel);
            return zone.stableId;
        }

        public bool UpdateZone(string zoneId, Vector2 center, Vector2 fullSize, float extend,
                               float weight, float absoluteHeight, bool enabled)
        {
            var idx = manualZones.FindIndex(z => z.stableId == zoneId);
            if (idx < 0) return false;
            var z = manualZones[idx];
            if (z.center == center && z.fullSize == fullSize && Mathf.Approximately(z.extendMeters, extend)
                && Mathf.Approximately(z.weight, weight) && Mathf.Approximately(z.absoluteHeight, absoluteHeight)
                && z.enabled == enabled)
                return false;                                   // 无变化不 bump（防空重算风暴）

            z.center = center;
            z.fullSize = new Vector2(Mathf.Max(4f, fullSize.x), Mathf.Max(4f, fullSize.y));
            z.extendMeters = Mathf.Max(0.5f, extend);
            z.weight = Mathf.Max(0.05f, weight);
            z.absoluteHeight = Mathf.Max(0f, absoluteHeight);
            z.enabled = enabled;
            manualZones[idx] = z;
            Bump(IntentKind.ParcelModel);
            return true;
        }

        public bool RemoveZone(string zoneId)
        {
            var removed = manualZones.RemoveAll(z => z.stableId == zoneId);
            if (removed == 0) return false;
            Bump(IntentKind.ParcelModel);
            return true;
        }

        /// <summary>由 S1b 回填，仅用于呈现（不参与判定，见字段注释）。</summary>
        public void ReportSolveResult(int constraints, int cells, int rejected)
        {
            lastConstraints = constraints; lastCells = cells; lastRejectedConstraints = rejected;
        }

        public void EnsureStableId()
        {
            if (string.IsNullOrEmpty(stableId)) stableId = IntentRegistry.NewStableId("terrainInst");
        }

        void Bump(IntentKind kind)
        {
            EnsureStableId();
            ledger ??= new IntentChangeLedger();
            ledger.Bump(kind, GetAffectedBounds());
        }

        /// <summary>
        /// 山水笔刷落笔提交点（工具在**落笔时调一次**，不是每 tick，01 §5.2）。
        /// <para>脏区域必须用<b>笔刷实际位置</b>而非实例 transform：
        /// 实例通常放在原点，用 `transform.position` 会让脏盒永远落在原点 ⇒
        /// 增量重建找错块，表现为"雕了别处、画面不动"（与 H-42 同族：算错影响范围）。</para>
        /// <para>影响面按用户裁定 = Flatten + Building（01 §5，DESIGN **D-31** 推翻旧「仅脏 S6」）：
        /// S1b 重算受波及地块的目标平面，S6 重排落位；<b>不含 Terrain</b> ⇒ 不重跑噪声基底（A-2）。</para>
        /// </summary>
        public void MarkDeltaChanged(Vector2 centerWorld, float brushRadius)
        {
            EnsureStableId();
            ledger ??= new IntentChangeLedger();
            var r = Mathf.Max(MinAffectedRadius, brushRadius * 2f);   // 笔刷外扩一档，覆盖过渡带波及
            ledger.Bump(IntentKind.DeltaStroke,
                new Bounds(new Vector3(centerWorld.x, 0f, centerWorld.y),
                           new Vector3(r * 2f, 1f, r * 2f)));
        }

        /// <summary>换 delta 资产：实例引用变了，按当前生效笔刷区域重新提交一次。</summary>
        public void SetDelta(SculptDeltaAsset d)
        {
            if (ReferenceEquals(d, delta)) return;
            delta = d;
            MarkDeltaChanged(transform.position, MinAffectedRadius);
        }

        /// <summary>
        /// 实例变化的影响范围（保守上界）。
        /// <para>取所有启用人工区的外接半径，并钳到 ≥16m —— 全为 0 时不返回退化包围盒，
        /// 因为 <c>PipelineContext.AddDirtyRegion</c> 会忽略零尺寸盒而丢掉脏区域。
        /// S1b 施加约束时按<b>各自约束的外接盒</b>做局部重算，不依赖这里的粗粒度值（00 §4.2）。</para>
        /// </summary>
        const float MinAffectedRadius = 16f;

        Bounds GetAffectedBounds()
        {
            var radius = MinAffectedRadius;
            for (var i = 0; i < manualZones.Count; i++)
            {
                var z = manualZones[i];
                if (z == null || !z.enabled) continue;
                var r = Mathf.Max(z.fullSize.x, z.fullSize.y) * 0.5f + z.extendMeters;
                if (r > radius) radius = r;
            }
            return new Bounds(transform.position, new Vector3(radius * 2f, 1f, radius * 2f));
        }

        private void OnEnable()
        {
            EnsureStableId();
            ledger ??= new IntentChangeLedger();
            IntentRegistry.Register(this);
        }

        private void OnDisable() => IntentRegistry.Unregister(this);

        /// <summary>
        /// 自动试设地块网格布局（<b>单一真源</b>：S1b 求解、面板叠加绘制、EditMode 测试都走这里）。
        /// <para>存在理由：地块矩形位置决定"哪些格属于地块"。若测试自己另写一份半径判据来算坡度，
        /// 就会断言一个实现并不使用的区域 —— 断言看着通过、实际地块仍歪（M2 首版正是这样，半径 1.15×
        /// 覆盖到约束带外与两块之间的空隙，那里坡度仍是原始值）。</para>
        /// </summary>
        public static int EnumerateAutoTiles(CityTerrain terrain, TerrainProfile profile,
                                             CityProject project, CityGrid grid,
                                             System.Action<Vector2, Vector2> onTile)
        {
            if (terrain == null || profile == null || grid.Rows == 0 || onTile == null) return 0;
            if (!terrain.AutoFlattenAtCenter) return 0;

            var center = profile.ResolveUrbanCenter(project, grid);
            var radius = Mathf.Max(1f, profile.ResolveUrbanRadius(project, grid));
            var n = terrain.AutoTileCount;
            var tile = Mathf.Max(16f, radius * 2f / n);

            // H-31：相邻地块之间必须留街道间隙。间隙宽度按 tile 比例取，且不小于一个现实街宽
            // （03 §2.1 的 Collector/Local 红线 10~16m），否则过渡带无处展开。
            var street = Mathf.Max(10f, tile * 0.06f);
            if (street >= tile - 4f) street = Mathf.Max(0f, tile * 0.5f - 2f);
            var pitch = tile;                       // 中心间距
            var half = (tile - street) * 0.5f;      // 地块实际半尺寸
            var originX = center.x - pitch * n * 0.5f + street * 0.5f;
            var originZ = center.y - pitch * n * 0.5f + street * 0.5f;

            var emitted = 0;
            for (var tz = 0; tz < n; tz++)
            for (var tx = 0; tx < n; tx++)
            {
                var c = new Vector2(originX + (tx + 0.5f) * pitch, originZ + (tz + 0.5f) * pitch);
                // 完全在图幅外的 tile 无意义（拟合无样本）
                if (c.x < -half || c.y < -half || c.x > grid.WorldSize + half || c.y > grid.WorldSize + half)
                    continue;
                onTile(c, new Vector2(half, half));
                emitted++;
            }
            return emitted;
        }
    }
}
