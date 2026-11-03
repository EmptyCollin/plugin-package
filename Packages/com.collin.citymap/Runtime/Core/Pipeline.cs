using System;
using System.Collections.Generic;
using UnityEngine;

namespace Collin.CityMap
{
    /// <summary>
    /// 阶段间唯一数据面（Modules/00 §4.1）。
    /// <para>纪律：阶段之间只经本上下文交换数据，禁止阶段互相持有引用——
    /// 否则脏传播会漏，出现"改了没刷新"的假死。</para>
    /// <para>也是导出侧 `CitySnapshot` 的上游（10 §5.4 H-22：三形态共享单一快照，禁止各写一套）。</para>
    /// </summary>
    public sealed class PipelineContext
    {
        public CityProject Project { get; }
        public CityGrid Grid => Project.GetGrid();
        public ulong RootSeed => Project.Seed;

        /// <summary>每格 2 字节掩码（00 §2.4）。写只允许经 <see cref="Writer"/>。</summary>
        public ushort[] Mask { get; }
        public MaskWriter Writer { get; }

        private readonly object[] _slots = new object[(int)StageId.StageCount];

        /// <summary>本轮待处理的脏区域（世界包围盒）。空 = 全图。</summary>
        public readonly List<Bounds> DirtyBounds = new List<Bounds>(32);

        public GenerationReport Report { get; } = new GenerationReport();

        public bool CancelRequested => _cancel.IsCancellationRequested;
        // 复位时要重新赋值，故不可 readonly（CS0191）
        private System.Threading.CancellationTokenSource _cancel
            = new System.Threading.CancellationTokenSource();

        public PipelineContext(CityProject project)
        {
            if (project == null) throw new ArgumentNullException(nameof(project));
            Project = project;
            var g = Grid;
            Mask = new ushort[g.CellCount];
            Writer = new MaskWriter(Mask, g.Columns);
        }

        public void SetSlot<T>(StageId stage, T data) => _slots[(int)stage] = data;
        public T GetSlot<T>(StageId stage) where T : class => _slots[(int)stage] as T;
        public bool HasSlot(StageId stage) => _slots[(int)stage] != null;

        /// <summary>把脏区域裁剪进图幅（负坐标/越界安全；圆心与半径都用世界 XZ）。</summary>
        public void AddDirtyRegion(Vector2 center, float radius)
        {
            var g = Grid;
            g.CellRectForCircle(center, radius, out var minCx, out var minCz, out var maxCx, out var maxCz);
            var a = g.CellCenterWorld(minCx, minCz) - new Vector2(0.5f * g.CellSize, 0.5f * g.CellSize);
            var b = g.CellCenterWorld(maxCx, maxCz) + new Vector2(0.5f * g.CellSize, 0.5f * g.CellSize);
            var size = b - a;
            AddDirtyBound(new Bounds(new Vector3((a.x + b.x) * 0.5f, 0f, (a.y + b.y) * 0.5f),
                                     new Vector3(Mathf.Max(size.x, g.CellSize), 1f, Mathf.Max(size.y, g.CellSize))));
        }

        public void AddDirtyBound(Bounds b)
        {
            if (b.size.x <= 0f && b.size.z <= 0f) return;
            DirtyBounds.Add(b);
        }

        public void ClearDirtyBounds() => DirtyBounds.Clear();

        /// <summary>脏区域并集；无脏区域 = null（调用方按"全图"处理）。</summary>
        public Bounds? UnionOfDirtyBounds()
        {
            if (DirtyBounds.Count == 0) return null;
            var c = DirtyBounds[0];
            for (var i = 1; i < DirtyBounds.Count; i++) c.Encapsulate(DirtyBounds[i]);
            return c;
        }

        public void RequestCancel() => _cancel.Cancel();

        /// <summary>取消后必须复位才能再跑（00 §4.4）。</summary>
        public void ResetCancel()
        {
            _cancel.Dispose();
            _cancel = new System.Threading.CancellationTokenSource();
        }

        public bool HasFlag(int cellIndex, CellFlag flag) => Writer.Has(cellIndex, flag);
    }

    /// <summary>管线阶段接口（00 §4.1）。纯数据进 / 纯数据出，禁止引用 UnityEditor。</summary>
    public interface IPipelineStage
    {
        StageId Id { get; }

        /// <summary>哪些脏位会触发我（00 §4.1）。</summary>
        DirtyBits DependsOn { get; }

        StageResult Run(PipelineContext ctx, RunMode mode);
    }

    public readonly struct StageResult
    {
        public readonly bool Success;
        public readonly bool Cancelled;
        public readonly int CellsTouched;
        public readonly int Outputs;
        public readonly int Warnings;
        public readonly string Message;

        public StageResult(bool success, int cells = 0, int outputs = 0, int warnings = 0,
                           string message = null, bool cancelled = false)
        {
            Success = success; Cancelled = cancelled;
            CellsTouched = cells; Outputs = outputs; Warnings = warnings; Message = message;
        }

        public static StageResult Ok(int cells = 0, int outputs = 0, int warnings = 0)
            => new StageResult(true, cells, outputs, warnings);
        public static StageResult Fail(string msg) => new StageResult(false, message: msg);
        public static StageResult CancelledResult() => new StageResult(false, cancelled: true, message: "已取消");
    }

    /// <summary>
    /// 阶段调度器（Modules/00 §4）。
    /// 同帧脏位合并 → 不动点展开 → 固定次序执行 → 逐阶段耗时记录（§4.5）。
    /// <para>M1 不含真实阶段（M2+ 逐个注册）；执行次序里 S1a 严格早于 S1b，是打破 S1↔S4 环的实现约束。</para>
    /// <para>帧预算属于阶段内部（M2 起：Job 分帧 / 分块事务），Runner 不自造脏位做截断——
    /// 截断逻辑写成"补一个 Building 脏位"会造成下一帧重复计算与不可复现的执行序列。</para>
    /// </summary>
    public sealed class PipelineRunner
    {
        /// <summary>固定执行顺序（总纲 §3.2）。不可调换：S1a→…→S4→S1b→S5→S6。</summary>
        public static readonly StageId[] ExecutionOrder =
        {
            StageId.S0_Assemble,
            StageId.S1a_TerrainBase,
            StageId.S2_River,
            StageId.S3_Road,
            StageId.S4_Parcel,
            StageId.S1b_Flatten,
            StageId.S5_LandUse,
            StageId.S6_BuildingFill,
        };

        private readonly List<IPipelineStage> _stages = new List<IPipelineStage>(8);
        private DirtyBits _pending;

        public DirtyBits Pending => _pending;
        public bool HasPending => _pending != DirtyBits.None;
        public IReadOnlyList<IPipelineStage> Stages => _stages;

        public void Register(IPipelineStage stage)
        {
            if (stage == null) throw new ArgumentNullException(nameof(stage));
            for (var i = 0; i < _stages.Count; i++)
                if (_stages[i].Id == stage.Id)
                    throw new InvalidOperationException($"{PackageIdentity.LogPrefix} 阶段 {stage.Id} 重复注册");
            _stages.Add(stage);
        }

        public void Unregister(StageId id) => _stages.RemoveAll(s => s.Id == id);

        /// <summary>唯一脏上报入口（00 §4.2）：同帧多次 Mark 合并为一次执行。</summary>
        public void MarkDirty(DirtyBits bits, Bounds bound = default)
        {
            _pending |= bits;
            if ((bound.size.x > 0f || bound.size.z > 0f) && _ctx != null) _ctx.AddDirtyBound(bound);
        }

        /// <summary>
        /// 意图对象提交点上报（A-17 的执行入口之一；与 Editor 侧 Tracker 扫描互为冗余，不冲突）。
        /// 按<b>通道</b>累积：一个对象可能同时改了多种互不相干的可变面（如 CityProject 的世界参数与城心锚点），
        /// 只取"对象的唯一 Kind"会把其中一种误判成另一种（M1 自测修的那个缺陷）。
        /// </summary>
        public void MarkIntent(IIntentTracked tracked)
        {
            if (tracked?.Ledger == null) return;
            tracked.Ledger.CollectSorted(_intentScratch);
            for (var i = 0; i < _intentScratch.Count; i++)
            {
                var ch = _intentScratch[i];
                if (ch.Sequence <= 0) continue;                 // 未提交过的通道不算变化
                _pending |= IntentChangePlanner.DirtyOf(ch.Kind);
                _ctx?.AddDirtyBound(tracked.Ledger.BoundsOf(ch.Kind));
            }
        }

        private static readonly System.Collections.Generic.List<IntentChangeLedger.Channel> _intentScratch
            = new System.Collections.Generic.List<IntentChangeLedger.Channel>(8);

        private PipelineContext _ctx;

        /// <summary>跑完挂起的脏集合。返回执行的阶段数；0 = 无脏或无匹配阶段。</summary>
        public int Tick(PipelineContext ctx, RunMode mode)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));
            _ctx = ctx;
            if (_pending == DirtyBits.None) return 0;

            var work = IntentChangePlanner.Expand(_pending);
            _pending = DirtyBits.None;

            var executed = 0;
            foreach (var id in ExecutionOrder)
            {
                var stage = Find(id);
                if (stage == null) continue;
                if ((work & stage.DependsOn) == 0) continue;

                if (ctx.CancelRequested)
                {
                    ctx.Report.Add(id, mode, StageResult.CancelledResult(), 0);
                    return executed;
                }

                var sw = System.Diagnostics.Stopwatch.StartNew();
                StageResult res;
                try
                {
                    res = stage.Run(ctx, mode);
                }
                catch (Exception ex)
                {
                    // 事务边界（00 §4.4）：单阶段失败不整城中断，但记失败供门禁判定
                    res = StageResult.Fail(ex.Message);
                    Debug.LogException(ex);
                }
                sw.Stop();
                ctx.Report.Add(id, mode, res, (int)sw.ElapsedMilliseconds);
                executed++;

                if (!res.Success) return executed;   // 失败/取消都中止，不拿半成品算下游
            }

            ctx.ClearDirtyBounds();
            return executed;
        }

        private IPipelineStage Find(StageId id)
        {
            for (var i = 0; i < _stages.Count; i++)
                if (_stages[i].Id == id) return _stages[i];
            return null;
        }
    }

    /// <summary>逐阶段耗时与产物记录（00 §4.5：没有逐阶段耗时，性能预算就只能靠猜）。</summary>
    public sealed class GenerationReport
    {
        public struct Entry
        {
            public StageId Stage;
            public RunMode Mode;
            public int Ms;
            public int Cells;
            public int Outputs;
            public int Warnings;
            public bool Success;
            public bool Cancelled;
            public string Message;
        }

        public readonly List<Entry> Entries = new List<Entry>(16);

        public void Add(StageId stage, RunMode mode, StageResult r, int ms)
        {
            Entries.Add(new Entry
            {
                Stage = stage, Mode = mode, Ms = ms,
                Cells = r.CellsTouched, Outputs = r.Outputs, Warnings = r.Warnings,
                Success = r.Success, Cancelled = r.Cancelled, Message = r.Message,
            });
        }

        public int TotalMs
        {
            get
            {
                var t = 0;
                for (var i = 0; i < Entries.Count; i++) t += Entries[i].Ms;
                return t;
            }
        }

        public int TotalWarnings
        {
            get
            {
                var t = 0;
                for (var i = 0; i < Entries.Count; i++) t += Entries[i].Warnings;
                return t;
            }
        }

        public bool AnyFailure
        {
            get
            {
                for (var i = 0; i < Entries.Count; i++)
                    if (!Entries[i].Success && !Entries[i].Cancelled) return true;
                return false;
            }
        }

        public void Clear() => Entries.Clear();
    }
}
