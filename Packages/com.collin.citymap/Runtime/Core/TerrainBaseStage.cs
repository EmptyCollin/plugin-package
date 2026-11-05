using System;
using System.Collections.Generic;
using UnityEngine;

namespace Collin.CityMap
{
    /// <summary>
    /// 地形 Model（Modules/01 §2.1，共享配置）。
    /// <para>判据（§0.1 原则2）：这里只放"可被多个城市实例复用的配置"；
    /// 实例差异（雕刻 delta、高度数据）在 <see cref="CityTerrain"/> 组件上。</para>
    /// </summary>
    [CreateAssetMenu(menuName = "Collin/CityMap/Terrain Profile", fileName = "TerrainProfile")]
    public sealed class TerrainProfile : ScriptableObject, IIntentTracked
    {
        [Header("身份")]
        [SerializeField] int schemaVersion = 1;
        [SerializeField] string stableId;
        [SerializeField] IntentChangeLedger ledger = new IntentChangeLedger();

        [Header("低频基底（城市整体起伏的来源）")]
        [Tooltip("低频起伏幅度（米）")]
        [SerializeField, Min(0f)] float baseAmplitude = 18f;
        [Tooltip("低频波长（米）；≥ 城尺度 1/4，避免城内碎起伏")]
        [SerializeField, Min(20f)] float baseWavelength = 520f;
        [SerializeField, Range(1, 8)] int octaves = 4;
        [SerializeField, Range(0.05f, 0.95f)] float gain = 0.5f;
        [SerializeField, Range(1.05f, 4f)] float lacunarity = 2f;
        [Tooltip("域扭曲强度（相对波长）；打破噪声的规则格感")]
        [SerializeField, Range(0f, 1f)] float domainWarpAmp = 0.35f;

        [Header("细节（只塑形，不参与规划）")]
        [SerializeField, Min(0f)] float detailAmplitude = 1.2f;
        [SerializeField, Min(2f)] float detailWavelength = 24f;

        [Header("城市平整（用户的『相对平整』主旋钮）")]
        [Tooltip("城市区幅度衰减：0=全平 1=全起伏")]
        [SerializeField, Range(0f, 1f)] float urbanFlatness = 0.25f;
        [Tooltip("城心回退值。<b>0 = 自动取图幅中心</b>（推荐）；非 0 视为显式世界坐标。" +
                 "城心已设锚点时本字段不参与（锚点优先，01 §4.4）")]
        [SerializeField] Vector2 urbanCenterFallback = Vector2.zero;
        [Tooltip("城半径；≤0 表示取锚点半径或 0.45×worldSize")]
        [SerializeField] float urbanRadius = 0f;
        [Tooltip("城内到城外过渡起始比例（相对城半径）")]
        [SerializeField, Range(0.1f, 1.5f)] float flatnessInnerEdge = 0.6f;
        [SerializeField, Range(0.2f, 2f)] float flatnessOuterEdge = 1.4f;

        [Header("平整约束求解（S1b）")]
        [Tooltip("单地块允许最大坡度；超过则退化为水平面")]
        [SerializeField, Range(0.5f, 20f)] float maxParcelSlope = 5f;
        [Tooltip("『保留自然地形』的自由权重；越大越不贴约束平面")]
        [SerializeField, Min(0f)] float freeWeight = 0.6f;
        [Tooltip("核心内切量（米）：全权重区止于地块边界内侧这么多距离，过渡带横跨真实边界。" +
                 "必须 >0：否则相邻地块在共享边界同时全权重，边界格被两个不同高程平均成陡坎" +
                 "（M2 实测：地块内最大坡度反升到 65°）")]
        [SerializeField, Min(0f)] float constraintInner = 12f;
        [Tooltip("约束外延过渡带宽（米，越出真实边界的部分）")]
        [SerializeField, Min(0.5f)] float constraintExtend = 12f;
        [Tooltip("单个地块允许的最大目标坡度（度）：防止目标平面过陡时把地块角部翘起。0=不检查")]
        [SerializeField, Range(0f, 30f)] float targetPlaneMaxSlope = 12f;

        /// <summary>
        /// 陡坡保护阈值（度）：原始地面坡度超过本值的格<b>不接受平整约束</b>。
        /// <para>这是 Modules/12 §3／DESIGN <b>D-32</b> 规定的"三档退让"里的第一档（hardRetreatSlope），
        /// 设计文档写了、代码里一处落点都没有 —— 后果就是用户反馈③「平整滤镜简直是灾难」：
        /// 8km 默认档下试设地块铺满全图、拟合坡度超限就退化成<b>水平中位数面</b>，
        /// 雕出来的山被整片削顶填谷成 64 块水平台阶。</para>
        /// <para>默认 18°：陡于 18° 视为"这是山，不该拿来盖房子"。0 = 关闭保护。</para>
        /// </summary>
        [Tooltip("陡坡保护(°)：原始地面坡度超过本值的格不参与压平。0=关闭。默认保住山体不被削成台阶。")]
        [SerializeField, Range(0f, 60f)] float steepProtectSlope = 18f;

        [Header("调试")]
        [Tooltip("冻结细节噪声：只看规划形状（R-04 定位用）")]
        [SerializeField] bool roughnessLock = false;

        public string StableId => stableId;
        public IntentChangeLedger Ledger => ledger;
        public long ManualVersion => ledger?.ManualVersion ?? 0;
        public int SchemaVersion => schemaVersion;

        public float BaseAmplitude => baseAmplitude;
        public float BaseWavelength => baseWavelength;
        public int Octaves => octaves;
        public float Gain => gain;
        public float Lacunarity => lacunarity;
        public float DomainWarpAmp => domainWarpAmp;
        public float DetailAmplitude => detailAmplitude;
        public float DetailWavelength => detailWavelength;
        public float UrbanFlatness => urbanFlatness;
        public Vector2 UrbanCenterFallback => urbanCenterFallback;
        public float UrbanRadiusValue => urbanRadius;
        public float FlatnessInnerEdge => flatnessInnerEdge;
        public float FlatnessOuterEdge => flatnessOuterEdge;
        public float MaxParcelSlope => maxParcelSlope;
        public float FreeWeight => freeWeight;
        public float ConstraintInner => constraintInner;
        public float ConstraintExtend => constraintExtend;
        public float TargetPlaneMaxSlope => targetPlaneMaxSlope;
        /// <summary>陡坡保护阈值（度），见字段注释（D-32 退让档落地）。</summary>
        public float SteepProtectSlope => steepProtectSlope;
        public bool RoughnessLock => roughnessLock;

        /// <summary>城心（世界 XZ）：优先用 CityProject 锚点（01 §4.4，H-7 定稿：不实时读场域）。</summary>
        /// <summary>
        /// 城心（世界 XZ）：锚点优先（01 §4.4，H-7 定稿：不实时读场域）；
        /// 无锚点时取 <see cref="urbanCenterFallback"/>，其为 0 则回退<b>图幅中心</b>。
        /// <para>回退必须是图幅中心而非常数：小图幅（Sample 600m）下写死的坐标会落到图外，
        /// 使 u = d/radius ≥ 1 ⇒ ampFactor 恒为 1 ⇒ <see cref="UrbanFlatness"/> 完全失效
        /// （M2 实测：flatness=0 时城内标准差仍有 0.81m）。</para>
        /// </summary>
        public Vector2 ResolveUrbanCenter(CityProject project, CityGrid grid)
        {
            if (project != null && project.Center.IsSet) return project.Center.Position;
            if (urbanCenterFallback.x > 0f || urbanCenterFallback.y > 0f) return urbanCenterFallback;
            return new Vector2(grid.WorldSize * 0.5f, grid.WorldSize * 0.5f);
        }

        /// <summary>城半径：显式值 → 锚点半径 → 0.45×worldSize。</summary>
        public float ResolveUrbanRadius(CityProject project, CityGrid grid)
        {
            if (urbanRadius > 0f) return urbanRadius;
            if (project != null && project.Center.IsSet && project.Center.Radius > 1f)
                return project.Center.Radius;
            return grid.WorldSize * 0.45f;
        }

        public void EnsureStableId()
        {
            if (string.IsNullOrEmpty(stableId)) stableId = IntentRegistry.NewStableId("terrain");
        }

        /// <summary>唯一写入口（Controller 只写 Model；无变化不 bump 版本，避免空重算风暴）。</summary>
        public void Touch()
        {
            EnsureStableId();
            ledger ??= new IntentChangeLedger();
            ledger.Bump(IntentKind.TerrainModel, new Bounds(Vector3.zero, Vector3.zero));
        }

        public bool SetBaseShape(float amplitude, float wavelength, int octs, float gn, float lac, float warp)
        {
            if (Mathf.Approximately(amplitude, baseAmplitude) && Mathf.Approximately(wavelength, baseWavelength)
                && octs == octaves && Mathf.Approximately(gn, gain) && Mathf.Approximately(lac, lacunarity)
                && Mathf.Approximately(warp, domainWarpAmp)) return false;
            baseAmplitude = Mathf.Max(0f, amplitude);
            baseWavelength = Mathf.Max(20f, wavelength);
            octaves = Mathf.Clamp(octs, 1, 8);
            gain = Mathf.Clamp(gn, 0.05f, 0.95f);
            lacunarity = Mathf.Max(1.05f, lac);
            domainWarpAmp = Mathf.Clamp01(warp);
            Touch();
            return true;
        }

        /// <summary>
        /// 自然化滤镜参数（M4-o / D-41）：烘焙时先<b>软化手绘痕迹</b>，再叠地块/道路压平。
        /// <para>口径关键：低通只作用在<b>人工层（Heights − BaseHeights）</b>上，绝不碰 S1a 自然噪声
        /// ——否则"消除手绘痕迹"会顺手把山体的自然碎纹一起抹平，用户看到的是"整张图糊了"。</para>
        /// </summary>
        [Header("平整滤镜：自然化（烘焙时先软化手绘痕迹）")]
        [Tooltip("软化核半径(m)：把多尺度的手绘折痕抹到多大范围。越大越「化」，但会把设计意图也带走。")]
        [SerializeField, Range(4f, 400f)] float naturalizeRadius = 40f;

        [Tooltip("软化强度 0~1：0=原样保留手绘；1=人工层完全变成其邻域均值（折痕消失）")]
        [SerializeField, Range(0f, 1f)] float naturalizeAmount = 0.7f;

        public float NaturalizeRadius => naturalizeRadius;
        public float NaturalizeAmount => naturalizeAmount;

        public bool SetNaturalize(float radiusMeters, float amount)
        {
            var r = Mathf.Clamp(radiusMeters, 4f, 400f);
            var a = Mathf.Clamp01(amount);
            if (Mathf.Approximately(r, naturalizeRadius) && Mathf.Approximately(a, naturalizeAmount)) return false;
            naturalizeRadius = r;
            naturalizeAmount = a;
            Touch();
            return true;
        }

        public bool SetUrbanFlatness(float flatness, float centerX = 0f, float centerZ = 0f, float radius = 0f)
        {
            var same = Mathf.Approximately(flatness, urbanFlatness)
                       && (centerX <= 0f || (Mathf.Approximately(centerX, urbanCenterFallback.x)
                                          && Mathf.Approximately(centerZ, urbanCenterFallback.y)))
                       && Mathf.Approximately(radius, urbanRadius);
            if (same) return false;
            urbanFlatness = Mathf.Clamp01(flatness);
            if (centerX > 0f) urbanCenterFallback = new Vector2(centerX, centerZ);
            if (radius > 0f) urbanRadius = radius;
            Touch();
            return true;
        }

        /// <summary>
        /// 城区几何（面板显式写入口，M4-o）。与 <see cref="SetUrbanFlatness"/> 的区别：
        /// 本入口<b>允许写 0 表示"回退自动"</b> —— 那个入口用 <c>&gt;0f</c> 当"参数未提供"的哨兵，
        /// 所以半径只能变大不能归零，面板上就永远退不回"0.45×图幅"的默认口径。
        /// <para>中心写 0,0 = 清除回退值（改由城心锚点或图幅中心决定）。哨兵语义必须与"未提供"区分开，
        /// 故这里用显式 bool 参数而非靠 0 值推断（D-01-1 同族教训：0 是合法值时不能拿 0 当哨兵）。</para>
        /// </summary>
        public bool SetUrbanGeometry(Vector2 fallbackCenter, float radius)
        {
            var c = new Vector2(Mathf.Max(0f, fallbackCenter.x), Mathf.Max(0f, fallbackCenter.y));
            var r = Mathf.Max(0f, radius);
            if (urbanCenterFallback == c && Mathf.Approximately(r, urbanRadius)) return false;
            urbanCenterFallback = c;
            urbanRadius = r;
            Touch();
            return true;
        }

        /// <summary>
        /// 城区边界比例（面板可调）：内缘=幅度已完全压到 <see cref="UrbanFlatness"/> 的位置（u 值），
        /// 外缘=完全放开到满幅的位置；两者之间 smoothstep 过渡 ⇒ 不会出现"城内平地/城外大山"的硬边。
        /// <para>强制 <c>outer &gt; inner</c>：反序会让 smoothstep 的边界对调、过渡带消失，
        /// 城界直接变成一条硬折线。</para>
        /// </summary>
        public bool SetUrbanEdgeRatio(float innerEdge, float outerEdge)
        {
            var inner = Mathf.Clamp(innerEdge, 0.1f, 1.45f);
            var outer = Mathf.Max(inner + 0.05f, Mathf.Clamp(outerEdge, 0.2f, 2f));
            if (Mathf.Approximately(inner, flatnessInnerEdge) && Mathf.Approximately(outer, flatnessOuterEdge))
                return false;
            flatnessInnerEdge = inner;
            flatnessOuterEdge = outer;
            Touch();
            return true;
        }

        public bool SetFlattenSolve(float parcelSlope, float freeW, float inner, float extend,
                                    float targetSlopeLock)
        {
            if (Mathf.Approximately(parcelSlope, maxParcelSlope) && Mathf.Approximately(freeW, freeWeight)
                && Mathf.Approximately(inner, constraintInner)
                && Mathf.Approximately(extend, constraintExtend)
                && Mathf.Approximately(targetSlopeLock, targetPlaneMaxSlope)) return false;
            maxParcelSlope = Mathf.Max(0.5f, parcelSlope);
            freeWeight = Mathf.Max(0f, freeW);
            constraintInner = Mathf.Max(0f, inner);
            constraintExtend = Mathf.Max(0.5f, extend);
            targetPlaneMaxSlope = targetSlopeLock;
            Touch();
            return true;
        }

        public bool SetRoughnessLock(bool lockDetail)
        {
            if (lockDetail == roughnessLock) return false;
            roughnessLock = lockDetail;
            Touch();
            return true;
        }

        /// <summary>
        /// 陡坡保护阈值（唯一写入口）。<c>0</c> 是合法值（= 关闭保护），故<b>不</b>做">0 才算提供"的哨兵判断。
        /// </summary>
        public bool SetSteepProtect(float degrees)
        {
            var v = float.IsNaN(degrees) ? 18f : Mathf.Clamp(degrees, 0f, 60f);
            if (Mathf.Approximately(v, steepProtectSlope)) return false;
            steepProtectSlope = v;
            Touch();
            return true;
        }

        /// <summary>
        /// 目标平面坡度上限（面板开放它，理由：<c>TargetPlaneAllowed</c> 会因它<b>整条丢弃</b>约束，
        /// 丢弃又制造补丁状不齐 —— 一个会静默改变结果的参数不该只能改资产）。
        /// <c>0</c> = 不检查，是合法值。
        /// </summary>
        public bool SetTargetPlaneMaxSlope(float degrees)
        {
            var v = float.IsNaN(degrees) ? 12f : Mathf.Clamp(degrees, 0f, 60f);
            if (Mathf.Approximately(v, targetPlaneMaxSlope)) return false;
            targetPlaneMaxSlope = v;
            Touch();
            return true;
        }

        /// <summary>
        /// 参数值指纹（顺序无关、与序列化字段一一对应）。
        /// <para>存在理由：面板控件走 <c>SetXxx()</c> 会 <see cref="Touch"/> bump 序列号，Tracker 看得见；
        /// 但用户**直接在 Inspector 改 SO 字段**（或 Ctrl+Z 回滚字段）不会调用任何 Set 方法 ⇒
        /// 序列号不变 ⇒ "改了参数但地形不动"的静默失效。面板用本指纹做第二检，
        /// 与 Tracker 的序列号检互为冗余（同 08 §5.1 的双检思路）。</para>
        /// <para>纯函数、无副作用；浮点按 1e-4 精度取整后混合，避免末位噪声造成假变化。</para>
        /// </summary>
        public ulong ComputeValueKey()
        {
            unchecked
            {
                var h = 14695981039346656037UL;
                h = CityRng.Mix(h, (ulong)Q(baseAmplitude));
                h = CityRng.Mix(h, (ulong)Q(baseWavelength));
                h = CityRng.Mix(h, (ulong)octaves);
                h = CityRng.Mix(h, (ulong)Q(gain));
                h = CityRng.Mix(h, (ulong)Q(lacunarity));
                h = CityRng.Mix(h, (ulong)Q(domainWarpAmp));
                h = CityRng.Mix(h, (ulong)Q(detailAmplitude));
                h = CityRng.Mix(h, (ulong)Q(detailWavelength));
                h = CityRng.Mix(h, (ulong)Q(urbanFlatness));
                h = CityRng.Mix(h, (ulong)Q(urbanCenterFallback.x));
                h = CityRng.Mix(h, (ulong)Q(urbanCenterFallback.y));
                h = CityRng.Mix(h, (ulong)Q(urbanRadius));
                h = CityRng.Mix(h, (ulong)Q(flatnessInnerEdge));
                h = CityRng.Mix(h, (ulong)Q(flatnessOuterEdge));
                h = CityRng.Mix(h, (ulong)Q(maxParcelSlope));
                h = CityRng.Mix(h, (ulong)Q(freeWeight));
                h = CityRng.Mix(h, (ulong)Q(constraintInner));
                h = CityRng.Mix(h, (ulong)Q(constraintExtend));
                h = CityRng.Mix(h, (ulong)Q(targetPlaneMaxSlope));
                h = CityRng.Mix(h, (ulong)Q(steepProtectSlope));   // 新可调字段必须并入（Value_Key_Covers_Every_Tunable_Field 会逐字段验）
                h = CityRng.Mix(h, (ulong)Q(naturalizeRadius));
                h = CityRng.Mix(h, (ulong)Q(naturalizeAmount));
                h = CityRng.Mix(h, (ulong)(roughnessLock ? 1 : 0));
                return h;
            }
        }

        /// <summary>浮点 → 1e-4 精度整数量（负数映射到高位，保持单射）。</summary>
        static int Q(float v) => Mathf.RoundToInt(v * 10000f);

        private void OnEnable() { EnsureStableId(); ledger ??= new IntentChangeLedger(); IntentRegistry.Register(this); }
        private void OnDisable() => IntentRegistry.Unregister(this);
    }

    /// <summary>
    /// S1a：基底噪声高度场（Modules/01 §3.1）。纯函数、按格位置派生种子 ⇒ 局部稳定（00 D-10）。
    /// <para>输出写 <see cref="TerrainField.BaseHeights"/>，并复制到工作数组供下游使用。
    /// 本阶段<b>不做</b>平整约束（那是 S1b，需地块结果，次序固定见 01 §3.2）。</para>
    /// </summary>
    public sealed class TerrainBaseStage : IPipelineStage
    {
        public StageId Id => StageId.S1a_TerrainBase;
        public DirtyBits DependsOn => StageClaims.Of(StageId.S1a_TerrainBase);

        readonly TerrainProfile _profile;
        readonly CityProject _project;

        public TerrainBaseStage(TerrainProfile profile, CityProject project)
        {
            _profile = profile;
            _project = project;
        }

        public StageResult Run(PipelineContext ctx, RunMode mode)
        {
            if (ctx == null) return StageResult.Fail("ctx 空");
            if (_profile == null) return StageResult.Fail("缺 TerrainProfile");

            var field = ctx.GetSlot<TerrainField>(StageId.S1a_TerrainBase)
                        ?? EnsureField(ctx);
            var grid = ctx.Grid;
            var seed = CityRng.StageSeed(ctx.RootSeed, StageId.S1a_TerrainBase);

            // 城心与半径：锚点优先（H-7：S1a 只读锚点，不实时读场域位置）
            var center = _profile.ResolveUrbanCenter(_project, grid);
            var radius = Mathf.Max(1f, _profile.ResolveUrbanRadius(_project, grid));
            var invRadius = 1f / radius;

            var amp = _profile.BaseAmplitude;
            var lambda = _profile.BaseWavelength;
            var warpAmp = _profile.DomainWarpAmp * lambda;
            var detailA = _profile.RoughnessLock ? 0f : _profile.DetailAmplitude;
            var baseSeed = CityRng.Mix(seed, 1);
            var detailSeed = CityRng.Mix(seed, 2);

            var touched = 0;
            var cols = grid.Columns;
            var heights = field.BaseHeights;

            for (var z = 0; z < grid.Rows; z++)
            {
                if ((z & 127) == 0 && ctx.CancelRequested) return StageResult.CancelledResult();

                var wz = grid.Origin.y + (z + 0.5f) * grid.CellSize;
                for (var x = 0; x < cols; x++)
                {
                    var wx = grid.Origin.x + (x + 0.5f) * grid.CellSize;

                    // ① 城内压低幅度、城外放开（smoothstep 过渡，01 §3.1 避免硬边界）
                    var dx = wx - center.x;
                    var dz = wz - center.y;
                    var u = Mathf.Sqrt(dx * dx + dz * dz) * invRadius;
                    var edge = CityNoise.SmoothStep(_profile.FlatnessInnerEdge, _profile.FlatnessOuterEdge, u);
                    var ampFactor = Mathf.Lerp(_profile.UrbanFlatness, 1f, edge);

                    // ② 域扭曲 + 低频 fbm
                    var sx = wx; var sz = wz;
                    CityNoise.DomainWarp(baseSeed, ref sx, ref sz, warpAmp, lambda);
                    var baseN = CityNoise.SignedFbm(baseSeed, sx, sz, lambda,
                                                    _profile.Octaves, _profile.Gain, _profile.Lacunarity);

                    // ③ 细节：小波长、低幅度，不参与规划（GetBaseHeight 也含它，但规划只用平面拟合的稳健统计）
                    var detail = CityNoise.SignedFbm(detailSeed, wx, wz, _profile.DetailWavelength, 2, 0.5f, 2f);

                    // H-37：ampFactor 必须压在**整条噪声和**上，不能只压低频。
                    // 原写法 `ampFactor*amp*baseN + detailA*detail` 让细节噪声完全绕过 urbanFlatness，
                    // 实测 flatness=0 时城内标准差 0.236m，**违反 A-1-1 的 ≤0.2m**
                    // （细节层 λ=24m/A=1.2m 单独就贡献 ~0.24m）。旋钮语义是"城内有多平"，
                    // 若只削低频、微观起伏照旧，用户把 flatness 拖到 0 仍看到糙地 = 不是所见即所得。
                    heights[z * cols + x] = ampFactor * (amp * baseN + detailA * detail);
                    touched++;
                }
            }

            field.CommitBaseAsWorking();
            return StageResult.Ok(touched, 1);
        }

        /// <summary>确保场数据存在于上下文且与当前格网尺寸匹配（世界尺寸/档位变了则重建，不复用旧数组）。</summary>
        public static TerrainField EnsureField(PipelineContext ctx)
        {
            var field = ctx.GetSlot<TerrainField>(StageId.S1a_TerrainBase);
            var stale = field == null
                        || field.Grid.CellSize != ctx.Grid.CellSize
                        || field.Grid.WorldSize != ctx.Grid.WorldSize
                        || field.Heights.Length != ctx.Grid.CellCount;
            if (stale)
            {
                field = new TerrainField(ctx.Grid);
                ctx.SetSlot(StageId.S1a_TerrainBase, field);
            }
            return field;
        }
    }
}
