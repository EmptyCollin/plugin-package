using System;
using UnityEngine;

namespace Collin.CityMap
{
    /// <summary>
    /// 负约束（Modules/08 §4.3）：让"不想要某物"成为可持久的意图层数据。
    /// 删除自动对象 = 挂一条覆盖其 footprint 的约束，而非删除；Ctrl+Z 移除约束即复活。
    /// <para>S0 把约束烘焙进掩码（<see cref="CellFlag.NoRoad"/> 等），
    /// 使"禁止"是每格 O(1) 判定，而不是各阶段各自查询集合（00 §2.4）。</para>
    /// </summary>
    [CreateAssetMenu(menuName = "Collin/CityMap/Constraint Asset", fileName = "CityConstraints")]
    public sealed class ConstraintAsset : ScriptableObject, IIntentTracked
    {
        [Serializable]
        public struct Entry
        {
            public string id;
            public ConstraintKind kind;
            public Vector2 center;
            public float radius;
            [Tooltip("线状约束（禁路走廊）用的折点；为空表示圆域")]
            public Vector2[] polyline;
            public float corridorWidth;
            public bool enabled;
        }

        [SerializeField] int schemaVersion = 1;
        [SerializeField] string stableId;
        [SerializeField] IntentChangeLedger ledger = new IntentChangeLedger();
        [SerializeField] System.Collections.Generic.List<Entry> entries = new System.Collections.Generic.List<Entry>();

        public string StableId => stableId;
        public IntentChangeLedger Ledger => ledger;
        public System.Collections.Generic.List<Entry> Entries => entries;
        public int SchemaVersion => schemaVersion;

        /// <summary>本资产所有约束的影响并集（提交时写入账本通道）。</summary>
        public Bounds AffectedBounds()
        {
            if (entries.Count == 0) return new Bounds(Vector3.zero, Vector3.zero);
            var b = new Bounds(new Vector3(entries[0].center.x, 0f, entries[0].center.y), Vector3.zero);
            for (var i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                b.Encapsulate(new Bounds(new Vector3(e.center.x, 0f, e.center.y),
                                         new Vector3(e.radius * 2f, 0f, e.radius * 2f)));
            }
            return b;
        }

        public void EnsureStableId()
        {
            if (string.IsNullOrEmpty(stableId)) stableId = IntentRegistry.NewStableId("cons");
        }

        /// <summary>唯一 bump 入口（写 Occupancy 通道）。Runtime 不触碰 UnityEditor API（§0.1 原则3）：
        /// 标脏与保存由 Editor 侧调用方负责。</summary>
        private void Bump()
        {
            ledger ??= new IntentChangeLedger();
            ledger.Bump(IntentKind.Occupancy, AffectedBounds());
        }

        /// <summary>提交入口：任何约束增删改都走这里。
        /// 返回 false 表示无变化（不 bump，避免空重算风暴）。</summary>
        public bool Add(ConstraintKind kind, Vector2 center, float radius, out string newId)
        {
            EnsureStableId();
            newId = IntentRegistry.NewStableId("c" + (byte)kind);
            entries.Add(new Entry
            {
                id = newId, kind = kind, center = center,
                radius = Mathf.Max(0.5f, radius), enabled = true,
            });
            Bump();
            return true;
        }

        public bool Remove(string id)
        {
            var idx = entries.FindIndex(e => e.id == id);
            if (idx < 0) return false;
            entries.RemoveAt(idx);
            Bump();
            return true;
        }

        public bool SetEnabled(string id, bool on)
        {
            for (var i = 0; i < entries.Count; i++)
            {
                if (entries[i].id != id) continue;
                var e = entries[i];
                if (e.enabled == on) return false;
                e.enabled = on;
                entries[i] = e;
                Bump();
                return true;
            }
            return false;
        }

        /// <summary>累计提交次数（供面板/journal 追溯；Tracker 按通道序列号判定，不用它）。</summary>
        public long ManualVersion => ledger != null ? ledger.ManualVersion : 0;

        private void OnEnable() { EnsureStableId(); ledger ??= new IntentChangeLedger(); IntentRegistry.Register(this); }
        private void OnDisable() => IntentRegistry.Unregister(this);
    }

    public enum ConstraintKind : byte { NoRoad = 0, NoBuild = 1, NoWater = 2 }

    /// <summary>
    /// S0 装配阶段（Modules/00 §2.4 掩码写入方之一、08 §4.3 负约束烘焙、10 §4 OccupiedHard 烘焙）。
    /// <para>M1 实现可独立验证的三件事：外环带位、负约束位、掩码 O(1) 查询一致性。
    /// `OccupiedHard` 的输入（记录集合）在 M8 才有，本阶段留接口不猜数据。</para>
    /// </summary>
    public sealed class AssembleStage : IPipelineStage
    {
        public StageId Id => StageId.S0_Assemble;
        public DirtyBits DependsOn => StageClaims.Of(StageId.S0_Assemble);

        private readonly ConstraintAsset _constraints;

        public AssembleStage(ConstraintAsset constraints) { _constraints = constraints; }

        public StageResult Run(PipelineContext ctx, RunMode mode)
        {
            if (ctx == null) return StageResult.Fail("ctx 为空");
            var g = ctx.Grid;
            var mask = ctx.Mask;
            var writer = ctx.Writer;
            var touched = 0;

            // ① 外环带与图幅：逐格重算（幂等：先清位再置位，避免上轮残留）
            for (var i = 0; i < mask.Length; i++)
            {
                if (ctx.CancelRequested) return StageResult.CancelledResult();

                writer.TrySet(i, CellFlag.None, CellFlag.InsetBand | CellFlag.NoRoad | CellFlag.NoBuild | CellFlag.NoWater);
                if (g.IsInInsetBand(i))
                {
                    writer.TrySet(i, CellFlag.InsetBand);
                    touched++;
                }
                else
                {
                    writer.TrySet(i, CellFlag.Buildable);
                    touched++;
                }
            }

            // ② 负约束烘焙（S0 唯一职责之一；半径 0 的条目视为点，仍占一格）
            var warnings = 0;
            if (_constraints != null)
            {
                var entries = _constraints.Entries;
                for (var e = 0; e < entries.Count; e++)
                {
                    var en = entries[e];
                    if (!en.enabled) continue;
                    if (ctx.CancelRequested) return StageResult.CancelledResult();

                    var flag = en.kind switch
                    {
                        ConstraintKind.NoRoad  => CellFlag.NoRoad,
                        ConstraintKind.NoBuild => CellFlag.NoBuild,
                        ConstraintKind.NoWater => CellFlag.NoWater,
                        _ => CellFlag.None,
                    };
                    if (flag == CellFlag.None) { warnings++; continue; }

                    g.CellRectForCircle(en.center, en.radius, out var minCx, out var minCz, out var maxCx, out var maxCz);
                    var r2 = en.radius * en.radius;
                    for (var cz = minCz; cz <= maxCz; cz++)
                    {
                        for (var cx = minCx; cx <= maxCx; cx++)
                        {
                            var c = g.CellCenterWorld(cx, cz);
                            var dx = c.x - en.center.x;
                            var dz = c.y - en.center.y;
                            if (dx * dx + dz * dz > r2) continue;    // 平方距离剔除，不开方（05 §3.1 同源优化）
                            writer.TrySet(cz * g.Columns + cx, flag);
                            touched++;
                        }
                    }
                }
            }

            ctx.SetSlot(StageId.S0_Assemble, new AssembleData { CellSize = g.CellSize, Touched = touched });
            return StageResult.Ok(touched, 0, warnings);
        }

        /// <summary>S0 产出（供下游读取档位信息；M1 只放最小必要数据）。</summary>
        public sealed class AssembleData
        {
            public float CellSize;
            public int Touched;
        }
    }
}
