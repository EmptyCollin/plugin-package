using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Collin.CityMap.EditorTooling
{
    /// <summary>
    /// 意图层 → 派生层的一致性桥（Modules/08 §5.1，本包最重要的实现件）。
    /// <para>问题：Unity Undo 只回滚意图层对象，派生层还停在 undo 之前
    /// （建筑还站在撤销前的路上）。解法：把 undo/redo 当成一次普通意图修改，复用全部脏传播机制。</para>
    ///
    /// <para><b>为什么必须按"通道"比对而不是按"对象版本"比对</b>（M1 自测修正的设计缺陷，登记 09 H-29 附录）：
    /// 一个意图对象常常承载多种互不相干的可变面 —— <c>CityProject</c> 同时握着"世界参数"与"城心锚点"。
    /// 若只比对象级单一版本号，Tracker 无法区分"哪一面变了"，只能退化成"猜一个 Kind"；
    /// 实测后果是「设为城心」被读成 WorldParam ⇒ Expand 成 All（全量重算），
    /// 面板显示的脏位也是错的，H-7 的修复被这条接线悄悄抵消。
    /// 现在比对 <see cref="IntentChangeLedger"/> 里每个通道的序列号，一个对象多面互不污染。</para>
    ///
    /// <para><b>undo 为什么能被感知</b>：通道序列号是 <c>[SerializeField]</c>，Undo 还原序列化字段
    /// ⇒ 序列号回到旧值 ⇒ 与基线不等 ⇒ 标脏。反之若账本放在非序列化字段（如 C# 普通字段），
    /// undo 后序列号不变，Tracker 就瞎了 —— 这正是 09 H-9 描述的漏检形态。</para>
    ///
    /// <para><b>双检</b>（09 H-9）：① 逐通道序列号差（参数改动、undo 回滚）；
    /// ② 对象集合哈希（新增/删除对象，含 undo 恢复被删对象）。</para>
    /// <para><b>首帧基线</b>（09 H-10b / A-33）：会话或域重载后第一帧只建基线、不触发重算，
    /// 否则场景加载会把全部对象判为差异 → 打开即近全量重算。</para>
    /// <para><b>合并窗口</b>（A-17）：连续 Ctrl+Z 在 250ms 静默后只重算一次。</para>
    /// </summary>
    public static class UndoHistoryTracker
    {
        /// <summary>合并窗口毫秒数。窗口内重复上报不触发 Commit，仅累计。</summary>
        public const int MergeWindowMs = 250;

        /// <summary>基线：stableId → (通道 → 上次见过的序列号)。</summary>
        private static readonly Dictionary<string, Dictionary<IntentKind, long>> _baseline
            = new Dictionary<string, Dictionary<IntentKind, long>>(StringComparer.Ordinal);

        private static readonly List<IntentChangeLedger.Channel> _scratch
            = new List<IntentChangeLedger.Channel>(8);

        private static ulong _baselineSetHash;
        private static bool _hasBaseline;
        private static int _lastChangeTick = -100000;
        private static bool _subscribed;

        /// <summary>当前挂起的脏位（由变化通道映射并展开得到）。</summary>
        public static DirtyBits PendingDirty { get; private set; }

        /// <summary>等待历史稳定的提示位（面板显示"等待历史稳定…"，08 §5.1 风险条）。</summary>
        public static bool AwaitingSettle => _lastChangeTick >= 0
            && Environment.TickCount - _lastChangeTick < MergeWindowMs;

        /// <summary>挂起集合已可交给 Runner 执行（静默到期且有脏）。</summary>
        public static bool ReadyToApply => PendingDirty != DirtyBits.None && !AwaitingSettle;

        /// <summary>供面板/测试读取当前基线对象数（不暴露内部结构）。</summary>
        public static int BaselineCount => _baseline.Count;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetAfterDomainReload()
        {
            // 静态存储随域重载清空；显式复位以免残留基线（R-07）
            _baseline.Clear();
            _baselineSetHash = 0;
            _hasBaseline = false;
            PendingDirty = DirtyBits.None;
            _lastChangeTick = -100000;
            _subscribed = false;
        }

        [InitializeOnLoadMethod]
        private static void Install()
        {
            if (_subscribed) return;
            _subscribed = true;
            // 不订阅 Undo 事件：每帧双检已覆盖 undo/redo 的全部后果
            // （通道序列号被还原 → 版本差；对象增删 → 集合哈希），少依赖一个未验证的 API。
            EditorApplication.update += Update;
        }

        /// <summary>外部提交点显式上报（工具 Commit 后调用；与自动扫描互为冗余，不冲突）。</summary>
        public static void MarkChanged() => _lastChangeTick = Environment.TickCount;

        /// <summary>
        /// <b>立即结束合并窗口</b>（面板在"一次输入动作收尾"时调用：滑条松手、输入框失焦/回车、
        /// 点一下建种子这类离散点击）。
        /// <para>存在理由（用户 2026-11-06 反馈②「数值变更时没有及时重建」的根因）：
        /// <see cref="MergeWindowMs"/> 的本意是把<b>连续 Ctrl+Z</b> 合成一次重算（A-17），
        /// 但它对所有意图改动一视同仁 —— 而 <see cref="Scan"/> 每检出一个差异就重新拨表，
        /// 于是拖滑条期间<b>一次都不会结算</b>，只有手停下来满 250ms 之后才动一次。
        /// 数值输入必须"结束动作就看到画面"（§0.1 原则1 所见即所得），所以这条路上需要一个显式结算口。</para>
        /// <para><b>只拨表、不取脏、不自己提交</b>：脏位仍由宿主按既有链路
        /// <c>ConsumeDirty → MarkDirty → CommitAndNotify</c> 消费。
        /// 这里若直接提交就会出现第二个提交点，与面板的自动跟随叠成双跑
        /// （更要命的是绕过 <c>LastDirtyBounds</c> 的搬入，增量重建退化成全图，见 M4-j）。</para>
        /// <para>撤销链路仍走合并窗口：那是 A-17 的契约，不是本入口的目标。</para>
        /// </summary>
        public static void SettleNow() => _lastChangeTick = -100000;

        private static void Update()
        {
            Scan();
            if (PendingDirty == DirtyBits.None) return;
            if (AwaitingSettle) return;   // 合并窗口内不重算（连续 Ctrl+Z 只在停下后算一次）
        }

        /// <summary>
        /// 扫描意图登记表求差异，返回本次新增的脏位。
        /// 首帧只建基线（A-33：打开场景不产生任何重算）。
        /// </summary>
        public static DirtyBits Scan()
        {
            var all = IntentRegistry.All;
            var setHash = ComputeSetHash(all);

            if (!_hasBaseline)
            {
                TakeBaseline(all, setHash);
                return DirtyBits.None;          // 首帧：只建基线，不标脏（H-10b / A-33）
            }

            var changed = DirtyBits.None;

            // ① 逐通道序列号差（含"值被改回去"= undo/redo 的情形）
            foreach (var t in all)
            {
                var id = t.StableId;
                if (string.IsNullOrEmpty(id) || t.Ledger == null) continue;

                if (!_baseline.TryGetValue(id, out var channels))
                {
                    // 新出现（含 undo 恢复被删对象 —— 仅比对象版本会漏的那一类）。
                    // 不在此单独标脏：下面按通道比对时，新对象每个通道都必然与"无记录"不等 ⇒ 自动标脏。
                    channels = new Dictionary<IntentKind, long>();
                    _baseline[id] = channels;
                }

                t.Ledger.CollectSorted(_scratch);
                for (var i = 0; i < _scratch.Count; i++)
                {
                    var ch = _scratch[i];
                    if (channels.TryGetValue(ch.Kind, out var seen) && seen == ch.Sequence) continue;

                    channels[ch.Kind] = ch.Sequence;
                    changed |= IntentChangePlanner.DirtyOf(ch.Kind);
                    MarkDirtyWithBounds(ch.Kind, t.Ledger.BoundsOf(ch.Kind));
                }
            }

            // ② 消失的对象：集合哈希必变 ⇒ 逐个剔除并标脏其全部通道
            if (setHash != _baselineSetHash)
            {
                List<string> dead = null;
                foreach (var kv in _baseline)
                    if (!IntentRegistry.TryGet(kv.Key, out _)) (dead ??= new List<string>()).Add(kv.Key);

                if (dead != null)
                {
                    foreach (var id in dead)
                    {
                        foreach (var ch in _baseline[id].Keys)
                            changed |= IntentChangePlanner.DirtyOf(ch);
                        _baseline.Remove(id);
                    }
                }
                _baselineSetHash = setHash;
            }

            // 必须并入挂起集合：面板/Runner 都以 PendingDirty 为驱动条件，
            // 只 return 不累加会让 ReadyToApply 永假 ⇒ 意图改动永不重算（静默失效，A-17 会当场红）
            if (changed != DirtyBits.None)
            {
                PendingDirty |= changed;
                MarkChanged();
            }
            return changed;
        }

        /// <summary>意图对象提交点上报：把它当前全部通道的脏位并进来（面板用，即时生效不等扫描）。</summary>
        public static void MarkIntent(IIntentTracked tracked)
        {
            if (tracked?.Ledger == null) return;
            tracked.Ledger.CollectSorted(_scratch);
            for (var i = 0; i < _scratch.Count; i++)
            {
                var ch = _scratch[i];
                PendingDirty |= IntentChangePlanner.DirtyOf(ch.Kind);
                MarkDirtyWithBounds(ch.Kind, tracked.Ledger.BoundsOf(ch.Kind));
            }
            MarkChanged();
        }

        private static void MarkDirtyWithBounds(IntentKind kind, Bounds bound)
        {
            if (bound.size.x <= 0f && bound.size.z <= 0f) return;
            // 只记最近一次的脏区域；宿主 CityWindow 在自己的 Tick 里按需取用（00 §4.2 同帧合并）
            LastDirtyBounds = bound;
        }

        /// <summary>最近一次上报的脏区域（可能为 default = 全图）。</summary>
        public static Bounds LastDirtyBounds { get; private set; }

        /// <summary>宿主取走挂起脏位（取后即清空，避免重复执行）。</summary>
        public static DirtyBits ConsumeDirty()
        {
            var d = PendingDirty;
            PendingDirty = DirtyBits.None;
            return d;
        }

        public static void Reset()
        {
            _baseline.Clear();
            _baselineSetHash = 0;
            _hasBaseline = false;
            PendingDirty = DirtyBits.None;
            LastDirtyBounds = default;
            _lastChangeTick = -100000;
        }

        /// <summary>测试/重载后可强制把当前状态当基线（等价于"打开场景"）。</summary>
        public static void RebuildBaseline()
        {
            var all = IntentRegistry.All;
            var h = ComputeSetHash(all);
            TakeBaseline(all, h);
        }

        private static void TakeBaseline(
            System.Collections.Generic.Dictionary<string, IIntentTracked>.ValueCollection all, ulong setHash)
        {
            _baseline.Clear();
            foreach (var t in all)
            {
                if (string.IsNullOrEmpty(t.StableId) || t.Ledger == null) continue;
                var map = new Dictionary<IntentKind, long>();
                t.Ledger.CollectSorted(_scratch);
                for (var i = 0; i < _scratch.Count; i++) map[_scratch[i].Kind] = _scratch[i].Sequence;
                _baseline[t.StableId] = map;
            }
            _baselineSetHash = setHash;
            _hasBaseline = true;
            PendingDirty = DirtyBits.None;
            LastDirtyBounds = default;
        }

        /// <summary>对象集合哈希（顺序无关：按 stableId 有序折叠，00 §3.1 铁律）。</summary>
        private static ulong ComputeSetHash(
            System.Collections.Generic.Dictionary<string, IIntentTracked>.ValueCollection all)
        {
            var ids = new List<string>(all.Count);
            foreach (var t in all) if (!string.IsNullOrEmpty(t.StableId)) ids.Add(t.StableId);
            ids.Sort(StringComparer.Ordinal);

            ulong h = 14695981039346656037UL;
            for (var i = 0; i < ids.Count; i++)
                h = CityRng.Mix(h, (ulong)(uint)IntentHash.StableStringHash(ids[i]));
            return h;
        }
    }
}
