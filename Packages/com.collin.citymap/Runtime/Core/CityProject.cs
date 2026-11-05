using System;
using UnityEngine;

namespace Collin.CityMap
{
    /// <summary>
    /// 意图层对象的共同契约（Modules/08 §5.1 的 Tracker 依据，Modules/00 §3.1 铁律载体）。
    /// <para>规则：<c>stableId</c> 创建时生成、复制粘贴必须重发、永不复用。</para>
    /// <para>变化不记在"对象"上，而记在<b>通道（Aspect/Kind）</b>上：一个对象常常承载多种互不相干的可变面
    /// （<see cref="CityProject"/> 同时握着"世界参数"与"城心锚点"）。若对象只有一个 Kind，
    /// 「设为城心」会被读成"世界参数变了"⇒ 走全量重算而不是 Center 链，面板显示的脏位也是错的。
    /// M1 自测抓到这一点，故改为 <see cref="IntentChangeLedger"/>。</para>
    /// </summary>
    public interface IIntentTracked
    {
        string StableId { get; }
        IntentChangeLedger Ledger { get; }
    }

    /// <summary>
    /// 按通道记账：每个 <see cref="IntentKind"/> 一个独立序列号 + 该次变化的影响包围盒。
    /// <para>为什么序列号必须是<b>序列化字段</b>：Unity Undo 只还原序列化字段。
    /// 还原后通道序列号回到旧值 ⇒ Tracker 比对基线得到"该通道变了"，
    /// 于是 undo 被当成一次普通意图修改复用整条脏传播链（08 §5.1 R-B），不需要任何撤销专用恢复代码。
    /// 若记账放在非序列化字段里，undo 就看不见，正是 H-9 描述的漏检。</para>
    /// </summary>
    [System.Serializable]
    public sealed class IntentChangeLedger
    {
        [System.Serializable]
        public struct Channel
        {
            public IntentKind Kind;
            public long Sequence;
            public Vector3 Center;
            public Vector3 Size;
        }

        [SerializeField] System.Collections.Generic.List<Channel> channels = new System.Collections.Generic.List<Channel>();
        [SerializeField] long manualVersion;

        /// <summary>该对象的累计提交次数（供面板/日志/journal 追溯；Tracker 不用它做判定）。</summary>
        public long ManualVersion => manualVersion;
        public System.Collections.Generic.List<Channel> Channels => channels;

        /// <summary>唯一 bump 入口：只在意图提交点调用，Tick 级预览改动不得调用。</summary>
        public void Bump(IntentKind kind, Bounds affected)
        {
            manualVersion++;
            for (var i = 0; i < channels.Count; i++)
            {
                if (channels[i].Kind != kind) continue;
                var c = channels[i];
                c.Sequence = manualVersion;
                c.Center = affected.center; c.Size = affected.size;
                channels[i] = c;
                return;
            }
            channels.Add(new Channel
            {
                Kind = kind, Sequence = manualVersion,
                Center = affected.center, Size = affected.size,
            });
        }

        public long SequenceOf(IntentKind kind)
        {
            for (var i = 0; i < channels.Count; i++)
                if (channels[i].Kind == kind) return channels[i].Sequence;
            return 0;
        }

        /// <summary>该通道的影响包围盒（未提交过则返回 default）。</summary>
        public Bounds BoundsOf(IntentKind kind)
        {
            for (var i = 0; i < channels.Count; i++)
                if (channels[i].Kind == kind)
                    return new Bounds(channels[i].Center, channels[i].Size);
            return default;
        }

        /// <summary>基线比对用的稳定键序：按 kind 的整数值升序（不依赖 List 插入序，00 §3.1 铁律）。</summary>
        public void CollectSorted(System.Collections.Generic.List<Channel> output)
        {
            output.Clear();
            for (var i = 0; i < channels.Count; i++) output.Add(channels[i]);
            output.Sort((a, b) => ((int)a.Kind).CompareTo((int)b.Kind));
        }
    }

    /// <summary>
    /// stableId 分配与登记的单一真源。
    /// <para>Tracker 依赖登记表而非 <c>FindObjectsByType</c>：10 万级对象遍历不可行（09 H-10 同源教训），
    /// 而意图对象是千级，登记表是 O(1) 增删。</para>
    /// </summary>
    public static class IntentRegistry
    {
        private static readonly System.Collections.Generic.Dictionary<string, IIntentTracked> _byId
            = new System.Collections.Generic.Dictionary<string, IIntentTracked>(StringComparer.Ordinal);

        /// <summary>域重载会清空静态存储 → 注册方必须在 OnEnable 重新登记（对齐 core Services 语义，R-07）。</summary>
        public static bool Register(IIntentTracked tracked)
        {
            if (tracked == null) return false;
            var id = tracked.StableId;
            if (string.IsNullOrEmpty(id)) return false;

            if (_byId.TryGetValue(id, out var existing) && !ReferenceEquals(existing, tracked))
            {
                // stableId 重复会让 diff 与认回错乱（05 §8 风险），必须显式报错而非静默覆盖
                Debug.LogError($"{PackageIdentity.LogPrefix} stableId 重复：{id} " +
                               $"（已存在 {existing.GetType().Name}，拒绝登记 {tracked.GetType().Name}）");
                return false;
            }
            _byId[id] = tracked;
            return true;
        }

        public static void Unregister(string stableId)
        {
            if (!string.IsNullOrEmpty(stableId)) _byId.Remove(stableId);
        }

        public static void Unregister(IIntentTracked tracked)
        {
            if (tracked == null) return;
            if (_byId.TryGetValue(tracked.StableId, out var cur) && ReferenceEquals(cur, tracked))
                _byId.Remove(tracked.StableId);
        }

        public static int Count => _byId.Count;
        public static bool TryGet(string stableId, out IIntentTracked tracked) => _byId.TryGetValue(stableId, out tracked);
        public static System.Collections.Generic.Dictionary<string, IIntentTracked>.ValueCollection All => _byId.Values;

        /// <summary>生成新 stableId：会话内自增 + 全局唯一前缀。仅创建时使用（05 §8：Ctrl+D 必须重发）。</summary>
        public static string NewStableId(string prefix)
        {
            unchecked
            {
                _counter++;
                var id = $"{prefix}-{_counter:x8}-{(ulong)Environment.TickCount & 0xFFFFFF:x6}";
                // 极端情况下仍撞了（多会话并行编辑同场景）→ 追加盐重发，保证唯一
                while (_byId.ContainsKey(id)) id = $"{prefix}-{_counter:x8}-{(ulong)Environment.TickCount & 0xFFFFFF:x6}~{_retry++}";
                return id;
            }
        }

        private static int _counter;
        private static int _retry;
    }

    /// <summary>
    /// 项目根 Model（Modules/00 §2、总纲 §3.1）。
    /// 尺度档位（v6 D-26）：<see cref="ScaleProfile"/> 决定 <c>cellSize</c>；
    /// 世界尺寸、种子、格档位属于「意图层」——改动即 <see cref="DirtyBits.World"/>/<see cref="DirtyBits.Seed"/>。
    /// </summary>
    [CreateAssetMenu(menuName = "Collin/CityMap/City Project", fileName = "CityProject")]
    public sealed class CityProject : ScriptableObject, IIntentTracked
    {
        [Header("身份与版本")]
        [SerializeField] int schemaVersion = 1;
        [SerializeField] string stableId;
        [SerializeField] IntentChangeLedger ledger = new IntentChangeLedger();

        [Header("世界（改动 ⇒ World 脏 ⇒ 全量重算）")]
        [Tooltip("世界边长（米）。2000=Sample 档，8000=正式档")]
        [SerializeField] float worldSize = 8000f;
        [SerializeField] WorldScale scaleProfile = WorldScale.Large8km;
        [Tooltip("外环不可建带（米），让城边界渐隐成郊野并规避边界半格问题")]
        [SerializeField] float buildableInset = 60f;

        [Header("确定性")]
        [Tooltip("全城唯一随机源。改动 ⇒ Seed 脏 ⇒ 全量重算（必过影响预览 + 自动快照）")]
        [SerializeField] ulong seed = 1700;

        /// <summary>
        /// 水陆缓冲带宽度（米）：水域外圈不可建筑带（04 地块边界 / 07 临水偏好消费）。
        /// <para>2026-11-05 用户裁定后<b>全局海平面已删除</b>："什么算水"完全由每颗种子自己的
        /// 水面高度 + 扩散半径决定（见 <see cref="WaterNetworkAsset"/>）。缓冲带是"水画好之后往岸上
        /// 扩多宽禁建带"，与水位无关，所以仍留在项目级做全局默认。</para>
        /// <para>脏通道走 <c>RiverStroke</c>：缓冲带变 ⇒ WaterBuffer 掩码变 ⇒ 下游地块/建筑复检，
        /// 但水面本身与地形都不变。走 WorldParam 会 Expand 成 All 重跑噪声（实测 4860ms），
        /// 与"改的只是一圈禁建带"完全不成比例。</para>
        /// </summary>
        [Header("水体（02 二次改版：水 = 种子高程+半径的连通域；缓冲带仍是全局参数）")]
        [Tooltip("水域外扩的不可建筑带宽度(m)。0 = 不设缓冲带。")]
        [SerializeField] float waterBuffer = 10f;

        public float WaterBufferMeters => waterBuffer;

        /// <summary>缓冲带换算成<b>格数</b>（洪泛外扩按格走，米数除不尽时向上取整，宁多不一格漏）。</summary>
        public int WaterBufferCells(CityGrid grid)
            => waterBuffer <= 0f || grid.CellSize <= 1e-5f ? 0 : Mathf.CeilToInt(waterBuffer / grid.CellSize);

        /// <summary>改缓冲带。返回 false = 无变化（不 bump，防空重算风暴，同各 Set 入口纪律）。</summary>
        public bool SetWaterBuffer(float meters)
        {
            if (float.IsNaN(meters) || float.IsInfinity(meters)) return false;
            var v = Mathf.Max(0f, meters);
            if (Mathf.Approximately(v, waterBuffer)) return false;
            EnsureRegistered();
            waterBuffer = v;
            ledger.Bump(IntentKind.RiverStroke, FullMapBounds());
            return true;
        }

        [Header("城心锚点（09 H-7 / D-19：独立意图对象，不实时读场域）")]
        [SerializeField] CityCenterAnchor centerAnchor;

        [Header("分块分辨率（00 §2.2）")]
        [SerializeField] float chunkSize = 128f;
        [SerializeField] float regionSize = 1024f;
        [Tooltip("导出切块尺寸（Q-12 可自定义；与 region 正交）")]
        [SerializeField] float sceneChunkSize = 1024f;

        public string StableId => stableId;
        public IntentChangeLedger Ledger => ledger;
        /// <summary>累计提交次数（供面板/journal 追溯；Tracker 按通道序列号判定，不用它）。</summary>
        public long ManualVersion => ledger != null ? ledger.ManualVersion : 0;
        public int SchemaVersion => schemaVersion;
        public ulong Seed => seed;
        public float WorldSizeValue => worldSize;
        /// <summary>尺度档位（只读）。面板第①步要显示/切换它，写仍只走 <see cref="SetWorldParams"/>。</summary>
        public WorldScale ScaleProfile => scaleProfile;
        public CityCenterAnchor Center => centerAnchor;

        /// <summary>档位默认逻辑格：Small=2m，Large=4m（00 §2.2，H-18 实算结论）。</summary>
        public float CellSize => scaleProfile == WorldScale.Large8km ? 4f : 2f;
        public float DisplayStep => CellSize * 4f;
        public float SceneChunkSize => sceneChunkSize;

        private CityGrid _grid;
        private bool _gridValid;

        /// <summary>全图包围盒：世界参数与城心提交都用它（两者影响面都是全城）。</summary>
        private Bounds FullMapBounds()
            => new Bounds(new Vector3(worldSize * 0.5f, 0f, worldSize * 0.5f),
                          new Vector3(worldSize, 1f, worldSize));

        /// <summary>派生格网（缓存；参数变则失效重建）。所有模块必须用它换算格。</summary>
        public CityGrid GetGrid()
        {
            if (_gridValid) return _grid;
            EnsureStableId();
            _grid = new CityGrid(worldSize, CellSize, DisplayStep, chunkSize, regionSize,
                                buildableInset, Vector2.zero);
            _gridValid = true;
            return _grid;
        }

        public void EnsureStableId()
        {
            if (string.IsNullOrEmpty(stableId))
                stableId = IntentRegistry.NewStableId("city");
        }

        /// <summary>
        /// 显式登记（幂等）。实测（本会话 EditMode）<c>ScriptableObject.CreateInstance</c>
        /// **会触发 OnEnable**，因此自动登记通常已完成；本方法作为**显式保险**保留，
        /// 供"绕过生命周期"的路径（如反射清空 stableId 后重建）使用。
        /// 目的同一个：Tracker 必须看得见每个意图对象，否则 = "改了但没重算"的静默失效（A-17 前提）。
        /// </summary>
        public void EnsureRegistered()
        {
            EnsureStableId();
            ledger ??= new IntentChangeLedger();   // 旧资产反序列化可能为 null（新增序列化字段）
            IntentRegistry.Register(this);
        }

        /// <summary>世界参数提交入口：写 WorldParam 通道并失效派生缓存。</summary>
        public void TouchWorldParams()
        {
            EnsureRegistered();
            ledger.Bump(IntentKind.WorldParam, FullMapBounds());
            _gridValid = false;
        }

        /// <summary>改种子（高危：WorldParam 脏 ⇒ 全量 ⇒ 调用方必须先过影响预览，08 §6.2）。</summary>
        public void SetSeed(ulong newSeed)
        {
            if (newSeed == seed) return;
            seed = newSeed;
            TouchWorldParams();
        }

        /// <summary>
        /// 世界参数唯一写入口（Controller 只写 Model，§0.1 原则2）。
        /// 返回 false = 全等无变化（不 bump 版本，避免空重算风暴）。
        /// 参数非法时抛（面板须捕获后提示，对应 A-0-7）。
        /// </summary>
        public bool SetWorldParams(float newSize, WorldScale scale, float inset,
                                   float chunk, float region, float sceneChunk)
        {
            var changed = newSize != worldSize || scale != scaleProfile || inset != buildableInset
                       || chunk != chunkSize || region != regionSize || sceneChunk != sceneChunkSize;
            if (!changed) return false;

            // 先校验（非法即抛，不落到已 bump 版本的状态、也不污染字段）
            var cell = scale == WorldScale.Large8km ? 4f : 2f;
            new CityGrid(newSize, cell, cell * 4f, chunk, region, Mathf.Max(0f, inset));
            ValidateSceneChunkSize(sceneChunk, cell);

            worldSize = newSize; scaleProfile = scale; buildableInset = inset;
            chunkSize = chunk; regionSize = region; sceneChunkSize = sceneChunk;
            TouchWorldParams();
            return true;
        }

        /// <summary>
        /// 导出切块尺寸单独校验（10 §5.1 / Q-12：必须是逻辑格整数倍，否则边界格被劈开）。
        /// 与 <see cref="CityGrid"/> 构造校验分离：sceneChunk 属交付期参数，不进工作集网格。
        /// </summary>
        public static void ValidateSceneChunkSize(float size, float cellSize)
        {
            if (size <= 0f || !CityGrid.IsIntegerMultiple(size, cellSize, out _))
                throw new ArgumentException(
                    $"sceneChunkSize({size}) 必须是 cellSize({cellSize}) 的整数倍（10 §5.1，Q-12）");
        }

        /// <summary>
        /// 城心锚点显式提交（01 §4.4 定稿：场域经「设为城心」写入，S1a 与 07 衰减只读锚点）。
        /// 写 CityCenter <b>通道</b>而非世界参数通道——这正是 M1 自测修的缺陷：
        /// 旧的单一 Kind 会让它被读成 WorldParam ⇒ Expand 成 All（全量重算），面板脏位也显示错。
        /// 返回 false = 无变化（不 bump，避免空重算）。
        /// </summary>
        public bool TrySetCenterAnchor(Vector2 pos, float radius, string sourceFieldStableId)
        {
            if (centerAnchor.Position == pos && Mathf.Abs(centerAnchor.Radius - radius) < 1e-3f
                && centerAnchor.SourceFieldStableId == sourceFieldStableId && centerAnchor.IsSet)
                return false;

            EnsureRegistered();
            centerAnchor = new CityCenterAnchor
            {
                IsSet = true,
                Position = pos,
                Radius = Mathf.Max(1f, radius),
                SourceFieldStableId = sourceFieldStableId,
                SetByManualVersion = ledger.ManualVersion + 1,
            };
            // Center 通道 ⇒ 近全链（08 矩阵末行）；不影响 WorldParam 通道
            ledger.Bump(IntentKind.CityCenter, FullMapBounds());
            return true;
        }

        private void OnEnable() => EnsureRegistered();
        private void OnDisable() => IntentRegistry.Unregister(this);

        private void OnValidate()
        {
            // OnValidate 在编辑器加载/改字段时触发：只做参数合法性钳制，不 bump 版本（会引发重算风暴）
            worldSize = Mathf.Max(100f, worldSize);
            buildableInset = Mathf.Clamp(buildableInset, 0f, worldSize * 0.25f);
            chunkSize = Mathf.Max(CellSize, chunkSize);
            regionSize = Mathf.Max(chunkSize, regionSize);
            sceneChunkSize = Mathf.Max(CellSize, sceneChunkSize);
            _gridValid = false;
        }
    }

    public enum WorldScale
    {
        /// <summary>Sample / 回归基线档：逻辑格 2m。</summary>
        Small2km = 0,
        /// <summary>正式档：逻辑格 4m（8km 档实算结论，11 §3.1）。</summary>
        Large8km = 1,
    }

    /// <summary>
    /// 城心锚点（Modules/01 §4.4，09 H-7 / 总纲 D-19）。
    /// <para>存在理由：v2 曾让 S1a 实时取金融场域位置当城心，推演证明"拖一下场域"会升级为全城重算，
    /// 同时摧毁手感（05 A-5-5）与局部稳定性（00 D-10）。故城心降格为独立意图对象，场域经显式提交写入。</para>
    /// </summary>
    [Serializable]
    public struct CityCenterAnchor
    {
        [Tooltip("false = 未显式设置，地形侧回落到 TerrainProfile 的城心默认值")]
        public bool IsSet;
        public Vector2 Position;
        public float Radius;
        [Tooltip("来源可追溯：由哪个场域「设为城心」写入")]
        public string SourceFieldStableId;
        [Tooltip("写入时的 manualVersion，面板需显示（01 §4.4 来源必须可见）")]
        public long SetByManualVersion;
    }
}
