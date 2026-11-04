using System;
using System.Collections.Generic;
using UnityEngine;

namespace Collin.CityMap
{
    /// <summary>
    /// 运行时桥：编辑器面板（驱动方）与场景侧 View（消费方）之间唯一的接合点。
    /// <para>存在理由：View 在 Runtime 程序集，禁止引用 UnityEditor（§0.1 原则3），
    /// 而权威管线上下文由 Editor 面板持有。若让 View 自己找对象猜数据源，
    /// 就会出现"两份世界"（面板一份、View 一份）且必然分叉成"面板算了、画面没变"。</para>
    /// <para>协议：面板每完成一次 Commit 就 <see cref="MarkCommitted"/>（带脏区域并集，null = 全图）。
    /// View 只在 <see cref="CommitSeq"/> 前进时重建 —— 不用事件（域重载后面板与 View 的订阅时机
    /// 不一致会漏事件；序号轮询对两者都无害）。</para>
    /// <para>静态存储随域重载清空（R-07 同款纪律），显式 Reset 只是把语义钉死而非依赖巧合。</para>
    /// </summary>
    public static class CityRuntime
    {
        /// <summary>权威上下文（面板 Bind 时写入；null = 无面板驱动，View 走自建路径）。</summary>
        public static PipelineContext ActiveContext { get; private set; }

        /// <summary>提交序号：View 的唯一"要不要重建"判据。</summary>
        public static long CommitSeq { get; private set; }

        /// <summary>最近一次提交的影响范围（null = 全图）。</summary>
        public static Bounds? LastCommittedRegion { get; private set; }

        /// <summary>面板绑定/换项目：上下文整体替换，View 必须全量重建。</summary>
        public static void Activate(PipelineContext ctx)
        {
            ActiveContext = ctx;
            LastCommittedRegion = null;
            CommitSeq++;
        }

        public static void Deactivate()
        {
            ActiveContext = null;
            LastCommittedRegion = null;
            CommitSeq++;
        }

        public static void MarkCommitted(Bounds? region)
        {
            LastCommittedRegion = region;
            CommitSeq++;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            ActiveContext = null;
            LastCommittedRegion = null;
            CommitSeq = 0;
        }
    }

    /// <summary>
    /// 场景侧地形显示宿主（Modules/01 §3.3 的 View 半边，AGENTS §0.1 原则1：所见即所得的第一屏）。
    /// <para>职责边界：<b>只读</b> —— 读 <see cref="TerrainField"/>、产出 chunk mesh；
    /// 不写任何意图、不参与任何生成判定（01 §6 表：显示 mesh 不可编辑、不落盘、不进撤销）。</para>
    /// <para>两条数据路径、<b>一条构建路径</b>：①面板驱动（编辑期权威）——消费 <see cref="CityRuntime"/>；
    /// ②自建（Play 模式 / 独立场景 / 无面板）——用 <see cref="projectForStandalone"/> 跑 S1a→S1b。
    /// 两条路径都经 <see cref="TerrainView.BuildChunk"/>，所以编辑态与 Play 态画面必然一致。</para>
    /// <para>派生态卫生：chunk 物体与 mesh 一律 <c>HideAndDontSave</c>，状态字段不序列化 ——
    /// 否则 <c>[ExecuteAlways]</c> 会把派生信息写进场景，用户什么都没改却要保存。</para>
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CityTerrain))]
    [AddComponentMenu("Collin/CityMap/City Terrain View")]
    public sealed class CityTerrainView : MonoBehaviour
    {
        [Header("显示（只读派生，不参与生成）")]
        [SerializeField] bool showTerrain = true;
        [Tooltip("chunk 边长（米）；自动对齐到显示步长的整数倍，生效值见 EffectiveChunkSize")]
        [SerializeField, Min(16f)] float chunkSizeMeters = 128f;
        [Tooltip("UV 世界映射密度：uv = 世界坐标 / tileScale（01 §3.3，避免接缝拉伸）")]
        [SerializeField, Min(2f)] float uvTileScale = 16f;
        [Tooltip("顶点色 = 坡度带（调试着色；关掉则纯白，交给材质贴图）")]
        [SerializeField] bool slopeVertexColors = true;

        [Header("数据源")]
        [Tooltip("材质引用；留空则自动回退 URP Lit → Diffuse（本包不依赖任何 shader 包，01 §3.3）")]
        [SerializeField] Material materialOverride;
        [Tooltip("无面板驱动（Play 模式等）时，用这个项目自建上下文跑 S1a/S1b")]
        [SerializeField] CityProject projectForStandalone;

        /// <summary>诊断文本（<b>故意不序列化</b>，理由见类注释）。</summary>
        string _status = "未初始化";
        public string LastStatus => _status;
        public bool ShowTerrain => showTerrain;
        public float ChunkSizeMeters => chunkSizeMeters;
        public float UvTileScale => uvTileScale;
        public bool SlopeVertexColors => slopeVertexColors;
        public Material MaterialOverride => materialOverride;
        public CityProject StandaloneProject => projectForStandalone;
        public int ChunkObjectCount => _chunks.Count;
        public CityGrid? BoundGrid => _hasGrid ? _grid : (CityGrid?)null;

        /// <summary>
        /// 本 View 当前用于生成 mesh 的高度场（只读）。
        /// <para>存在理由：编辑器侧工具（山水笔刷的光圈、平滑取样）需要"贴在你看到的那张皮上"。
        /// 若工具自己去读 <c>CityRuntime.ActiveContext</c>，就只覆盖了面板驱动分支，
        /// 自建路径（Play / 面板未 Bind）下会拿到 null —— 光圈退化成画在 y=0 又被起伏埋掉（H-53）。
        /// 数据源真源在本 View 的 <see cref="ResolveData"/>，故由它对外只读暴露，工具不再自猜。</para>
        /// </summary>
        public TerrainField BoundField => _field;

        public float EffectiveChunkSize => _hasGrid
            ? TerrainView.EffectiveChunkSize(chunkSizeMeters, _grid.DisplayStep) : 0f;

        private sealed class ChunkHost
        {
            public GameObject Go;
            public MeshFilter Filter;
            public MeshRenderer Renderer;
            public Mesh Mesh;
        }

        private readonly Dictionary<Vector2Int, ChunkHost> _chunks = new Dictionary<Vector2Int, ChunkHost>(64);
        private Transform _root;
        private Material _autoMat;
        private CityTerrain _terrain;

        private CityGrid _grid;
        private bool _hasGrid;
        private TerrainField _field;
        private TerrainView.ViewParams _vp;

        // 面板驱动路径
        private long _seenSeq = -1;
        private PipelineContext _seenCtx;

        // 自建路径
        private PipelineContext _ownCtx;
        private PipelineRunner _ownRunner;
        private ulong _ownVersion;

        // 重建请求：本帧是否需要全量重建（同帧合并见 00 §4.2；增量路径由提交序号驱动，不需列表）
        private bool _wantFull;
        private ulong _configKey;

        private void OnEnable()
        {
            _terrain = GetComponent<CityTerrain>();
            _seenSeq = -1;
            _configKey = 0;
            _wantFull = true;
        }

        private void OnDisable()
        {
            DestroyAllChunks();
            _ownCtx = null;
            _ownRunner = null;
            _field = null;
            _hasGrid = false;
            _seenCtx = null;
        }

        // ───────────── 外部接线（面板/测试显式驱动，不依赖引擎调度时机） ─────────────

        /// <summary>立即重建一次。EditMode 测试与面板"刷新显示"按钮共用（同步、可断言）。</summary>
        public void RebuildNow()
        {
            _wantFull = true;
            DoUpdate();
        }

        /// <summary>
        /// 模拟一帧常规更新（<b>不强制</b>）。测试用它覆盖"提交序号未前进 ⇒ 不得重建"的防抖语义；
        /// 与 <see cref="RebuildNow"/> 的区别就是这条：强制入口永远看不出空转。
        /// </summary>
        public void TickOnce() => DoUpdate();

        /// <summary>强制按脏区域增量重建一次（面板在 Commit 后调用；无脏区域 = 全量）。</summary>
        public void RefreshFromRuntime()
        {
            _seenSeq = -1;                          // 让下一轮视为"序号变了"
            DoUpdate();
        }

        public void SetShowTerrain(bool on)
        {
            if (on == showTerrain) return;
            showTerrain = on;
            _wantFull = true;
        }

        public void SetChunkSizeMeters(float m)
        {
            m = Mathf.Max(16f, m);
            if (Mathf.Approximately(m, chunkSizeMeters)) return;
            chunkSizeMeters = m;
            _wantFull = true;
        }

        public void SetStandaloneProject(CityProject p)
        {
            projectForStandalone = p;
            _ownCtx = null;
            _field = null;
            _wantFull = true;
        }

        private void LateUpdate() => DoUpdate();

        private void DoUpdate()
        {
            if (!showTerrain)
            {
                if (_chunks.Count > 0) DestroyAllChunks();
                _status = "地形显示已关闭";
                _wantFull = false;
                return;
            }

            if (!ResolveData())
            {
                _wantFull = false;
                return;
            }

            var key = ComputeConfigKey();
            var cfgChanged = key != _configKey;
            _configKey = key;

            // ① 面板驱动：权威上下文存在且本 View 已绑定它 ⇒ 只在提交序号前进时重建。
            //    这是编辑期唯一路径，也是"改参数即时反映"的实现点（§0.1 原则1）。
            var auth = CityRuntime.ActiveContext;
            if (auth != null && ReferenceEquals(_seenCtx, auth))
            {
                if (!_wantFull && !cfgChanged && CityRuntime.CommitSeq == _seenSeq) return;  // 无事发生
                _seenSeq = CityRuntime.CommitSeq;

                var region = CityRuntime.LastCommittedRegion;
                if (_wantFull || cfgChanged || region == null) RebuildAll();
                else RebuildRegion(region.Value);
                _wantFull = false;
                return;
            }

            // ② 自建路径（Play / 无面板）：没有提交序号，退化为"意图版本 + 配置指纹"驱动。
            //    Inspector 直接改显示字段不 bump 意图版本 ⇒ 只靠配置指纹发现（指纹已含项目/Profile 版本，
            //    所以生成结果一变必然被捕获，无需定时轮询）。
            if (!_wantFull && !cfgChanged) return;

            _seenSeq = CityRuntime.CommitSeq;
            RebuildAll();
            _wantFull = false;
        }

        /// <summary>解析数据源：面板上下文优先（必须已绑定到同一个上下文实例），否则自建。</summary>
        private bool ResolveData()
        {
            var profile = _terrain != null ? _terrain.Profile : null;

            var auth = CityRuntime.ActiveContext;
            if (auth != null && auth.Project != null)
            {
                if (!ReferenceEquals(_seenCtx, auth))
                {
                    _seenCtx = auth;
                    _seenSeq = CityRuntime.CommitSeq;
                    _wantFull = true;
                }
                _field = auth.GetSlot<TerrainField>(StageId.S1a_TerrainBase);
                if (_field == null)
                {
                    _hasGrid = false;
                    _status = "面板尚未跑出高度场（先执行 S1a）";
                    return false;
                }
                _grid = auth.Grid;
                _hasGrid = true;
                _vp = MakeParams();
                return true;
            }

            _seenCtx = null;

            if (projectForStandalone == null || profile == null)
            {
                _status = projectForStandalone == null
                    ? "无权威上下文且未绑定自建项目（Play 里需指定项目）"
                    : "CityTerrain 未绑定 TerrainProfile";
                _hasGrid = false;
                return false;
            }

            // 自建路径：意图版本驱动（意图没变 = 不重跑生成，只按配置指纹重建显示）
            var ver = unchecked((ulong)(projectForStandalone.ManualVersion * 1000003L
                                        + profile.ManualVersion * 31L
                                        + (_terrain != null ? _terrain.ManualVersion : 0L)));
            if (_ownCtx != null && _ownVersion == ver && _field != null)
            {
                _vp = MakeParams();
                return true;
            }

            _ownVersion = ver;
            _ownCtx = new PipelineContext(projectForStandalone);
            _ownRunner ??= new PipelineRunner();
            _ownRunner.Unregister(StageId.S1a_TerrainBase);
            _ownRunner.Unregister(StageId.S1b_Flatten);
            _ownRunner.Register(new TerrainBaseStage(profile, projectForStandalone));
            _ownRunner.Register(new FlattenSolveStage(profile, _terrain));
            _ownRunner.MarkDirty(DirtyBits.All);
            _ownRunner.Tick(_ownCtx, RunMode.Commit);

            _field = _ownCtx.GetSlot<TerrainField>(StageId.S1a_TerrainBase);
            if (_field == null)
            {
                _hasGrid = false;
                _status = "自建路径未产出高度场（检查世界参数是否非法）";
                return false;
            }
            _grid = _ownCtx.Grid;
            _hasGrid = true;
            _vp = MakeParams();
            return true;
        }

        private TerrainView.ViewParams MakeParams()
            => new TerrainView.ViewParams(
                TerrainView.EffectiveChunkSize(chunkSizeMeters, _grid.DisplayStep),
                Mathf.Max(2f, uvTileScale), slopeVertexColors, TerrainView.SlopeRefDeg);

        /// <summary>配置指纹 = 显示参数 + 意图版本 + 图幅。任一项变 ⇒ 全量重建。
        /// 材质用 <c>GetEntityId().GetHashCode()</c>（Unity 6 起 GetInstanceID 废弃为 error，CS0619）。</summary>
        private ulong ComputeConfigKey()
        {
            unchecked
            {
                var h = 14695981039346656037UL;
                h = CityRng.Mix(h, (ulong)(showTerrain ? 1 : 0));
                h = CityRng.Mix(h, (ulong)(int)(chunkSizeMeters * 100f));
                h = CityRng.Mix(h, (ulong)(int)(uvTileScale * 100f));
                h = CityRng.Mix(h, (ulong)(slopeVertexColors ? 1 : 0));
                h = CityRng.Mix(h, (ulong)(materialOverride != null
                    ? materialOverride.GetEntityId().GetHashCode() : 0));
                var p = _terrain != null ? _terrain.Profile : null;
                if (p != null) h = CityRng.Mix(h, (ulong)p.ManualVersion);
                if (_hasGrid)
                {
                    h = CityRng.Mix(h, (ulong)(int)(_grid.WorldSize * 10f));
                    h = CityRng.Mix(h, (ulong)(int)(_grid.DisplayStep * 100f));
                }
                if (projectForStandalone != null) h = CityRng.Mix(h, (ulong)projectForStandalone.ManualVersion);
                return h;
            }
        }

        private void RebuildAll()
        {
            var per = TerrainView.ChunksPerSide(_grid.WorldSize, _vp.ChunkSize);

            // 图幅/尺寸缩小后多余的 chunk 宿主要先回收，否则残留旧地形
            List<Vector2Int> stale = null;
            foreach (var kv in _chunks)
                if (kv.Key.x >= per || kv.Key.y >= per) (stale ??= new List<Vector2Int>()).Add(kv.Key);
            if (stale != null)
                for (var i = 0; i < stale.Count; i++) DestroyChunk(stale[i]);

            var built = 0;
            for (var cz = 0; cz < per; cz++)
            for (var cx = 0; cx < per; cx++)
                if (RebuildChunk(new Vector2Int(cx, cz))) built++;

            _status = $"显示 {built}/{per * per} chunk · 逻辑 {_grid.Columns}²格 @{_grid.CellSize:0}m · " +
                      $"显示 {_grid.DisplayStep:0}m · chunk {_vp.ChunkSize:0}m";
        }

        private void RebuildRegion(Bounds region)
        {
            var per = TerrainView.ChunksPerSide(_grid.WorldSize, _vp.ChunkSize);
            var list = TerrainView.ChunksTouching(_grid, _vp.ChunkSize, region);
            var built = 0;
            for (var i = 0; i < list.Count; i++)
                if (RebuildChunk(list[i])) built++;
            _status = $"增量重建 {built}/{list.Count} chunk（脏盒 {region.size.x:0}×{region.size.z:0}m，" +
                      $"全图 {per * per}）";
        }

        private bool RebuildChunk(Vector2Int c)
        {
            if (!_chunks.TryGetValue(c, out var host))
            {
                host = CreateChunk(c);
                _chunks[c] = host;
            }

            var geom = TerrainView.BuildChunk(_field, _grid, c, _vp);
            if (geom.IsEmpty)
            {
                if (host.Mesh != null && host.Mesh.vertexCount > 0)
                {
                    host.Mesh.Clear(false);
                    host.Renderer.enabled = false;
                }
                return false;
            }

            host.Mesh ??= NewMesh($"terrain_mesh_{c.x}_{c.y}");
            TerrainView.ApplyToMesh(geom, host.Mesh);
            host.Filter.sharedMesh = host.Mesh;
            host.Renderer.sharedMaterial = ResolveMaterial();
            host.Renderer.enabled = true;
            return true;
        }

        private ChunkHost CreateChunk(Vector2Int c)
        {
            EnsureRoot();
            var go = new GameObject($"terrain_chunk_{c.x}_{c.y}")
            {
                hideFlags = HideFlags.HideAndDontSave,          // 派生态不入库（01 §6）
            };
            go.transform.SetParent(_root, false);
            var h = new ChunkHost
            {
                Go = go,
                Filter = go.AddComponent<MeshFilter>(),
                Renderer = go.AddComponent<MeshRenderer>(),
            };
            h.Renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            h.Renderer.receiveShadows = true;
            return h;
        }

        private void EnsureRoot()
        {
            if (_root != null) return;
            _root = new GameObject("terrain_chunks (derived)")
            {
                hideFlags = HideFlags.HideAndDontSave,
            }.transform;
            _root.SetParent(transform, false);
        }

        private Mesh NewMesh(string name)
            => new Mesh { name = name, hideFlags = HideFlags.HideAndDontSave };

        /// <summary>
        /// 材质回退链：引用 → 本包自带顶点色着色器 → URP Lit → Diffuse。
        /// <para>M3 交付缺口（用户实机反馈"地表没有材质，看起伏比较累"）：显示层已把坡度写进顶点色，
        /// 但 URP/Lit 与 Diffuse <b>都不读 COLOR 语义</b> ⇒ 色带等于白写。本包按 01 §3.3 自带
        /// 一个不依赖外部 shader 包的着色器（<c>Sample/Shaders/CityTerrainVertexColor.shader</c>），
        /// 优先级放在 URP Lit 之前。正式项目仍可用 <see cref="materialOverride"/> 换成自己的地形材质。</para>
        /// </summary>
        private Material ResolveMaterial()
        {
            if (materialOverride != null) return materialOverride;
            if (_autoMat != null) return _autoMat;

            var sh = Shader.Find(OwnShaderName);
            if (sh == null) sh = Shader.Find("Universal Render Pipeline/Lit");
            if (sh == null) sh = Shader.Find("Diffuse");
            if (sh == null)
            {
                _status += "（无可用 shader：几何已产出但不可见）";
                return null;
            }
            _autoMat = new Material(sh)
            {
                hideFlags = HideFlags.HideAndDontSave,
                name = "terrain_view_fallback",
            };
            return _autoMat;
        }

        /// <summary>本包自带地形着色器的名字（单一真源，与 shader 文件内声明一致）。</summary>
        public const string OwnShaderName = "Collin/CityMap/Terrain VertexColor";

        private void DestroyChunk(Vector2Int c)
        {
            if (!_chunks.TryGetValue(c, out var host)) return;
            _chunks.Remove(c);
            DestroyObj(host.Mesh);
            DestroyObj(host.Go);
        }

        private void DestroyAllChunks()
        {
            foreach (var kv in _chunks)
            {
                DestroyObj(kv.Value.Mesh);
                DestroyObj(kv.Value.Go);
            }
            _chunks.Clear();
            if (_root != null) DestroyObj(_root.gameObject);
            _root = null;
            DestroyObj(_autoMat);
            _autoMat = null;
        }

        /// <summary>销毁时机纪律：Play 中只能 <c>Destroy</c>（帧末执行），编辑态才可用 <c>DestroyImmediate</c>。</summary>
        private static void DestroyObj(UnityEngine.Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(o);
            else UnityEngine.Object.DestroyImmediate(o);
        }
    }
}
