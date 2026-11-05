using System;
using UnityEngine;

namespace Collin.StealthPlanner
{
    /// <summary>
    /// 时空可见性表的<b>持久化载体</b>（<see cref="ScriptableObject"/>，烘焙一次成资产）。
    /// <para>为什么单开一个 SO 而不直接把 <see cref="VisibilityTable"/> 存盘：
    /// ①SO 是 Unity 唯一免配置的资源引用方式，能拖进预制件/场景，满足 AGENTS §0.1「引擎侧无需重复配置」；
    /// ②SO 让表在打包后作为只读资源随包走（引擎运行态可直接查），而 <c>VisibilityTable</c> 只是可序列化结构。</para>
    /// <para><b>确定性</b>：表内容只由 (场景世界布局, <see cref="PlannerBakeSettings"/>) 决定，
    /// 烘焙器写入 <see cref="_bakedFrom"/> 快照便于校验「同一份参数 + 同一场景 = 同一张表」。</para>
    /// <para>本类是 <b>Model</b>（DESIGN §3.1）：任何读表逻辑（求解、可视化、安全岛）都经
    /// <see cref="TryQuery"/> 走，禁止各消费方另建一份判定。</para>
    /// </summary>
    [CreateAssetMenu(menuName = "Collin/Stealth Planner/可见性表资产", fileName = "NewVisibilityTableAsset")]
    public sealed class VisibilityTableAsset : ScriptableObject
    {
        /// <summary>结构版本。烘焙器与查询都先校验它——字段布局变更后读旧资产会给出错误数据（静默污染），
        /// 故版本号不匹配一律拒绝使用、要求重烘。
        /// <para><b>1 → 2（用户 2026-11-05 拍板）</b>：v2 起表带<b>足迹</b>（<c>_evaluated</c>，D-19）。
        /// v1 的资产没有这一栏，读进来时"未被射线探测过的格"（墙、掩体内部）会被当成<b>安全可走</b>——
        /// 于是热区把墙涂成绿色、求解器能规划出穿墙路线，且<b>全程不报错</b>。
        /// 这类"看起来能用其实数据不对"的失效不能靠兼容默认值掩盖（默认"全都有数据"正是最危险的方向），
        /// 所以直接抬版本号让 v1 资产<strong>一律作废重烘</strong>；代价可控：实测重烘仅 0.82s、菜单一键，
        /// 且表是场景的派生产物、本就不入库。</para></para>
        /// <para>抬版本号同时修掉一个此前的洞：只增字段不抬版本 ⇒ 旧资产仍被判"版本相符"，
        /// 新加的保护区静默失效而面板照常出图（ACCEPTANCE 原 U-5）。</para></summary>
        public const int LayoutVersion = 2;

        [SerializeField] int _version;
        [SerializeField] VisibilityTable _table = new VisibilityTable();
        [Tooltip("烘焙用的时间步长与格长快照（不持引用，避免后续改设置静默改变历史资产口径）。")]
        [SerializeField] float _bakedCellSize;
        [SerializeField] float _bakedTimeStep;
        [SerializeField] float _bakedPhaseWindow;
        [SerializeField] int _guardCount;

        /// <summary>派生量快照：永久安全格数 / 位图字节，供面板读数与体积预算核验。</summary>
        [SerializeField] int _safeCellCount;
        [SerializeField] long _packedBytes;

        /// <summary>表数据（只读暴露）。版本不符时为 null。</summary>
        public VisibilityTable Table => _version == LayoutVersion ? _table : null;

        public bool IsBaked => _version == LayoutVersion && _table != null && _guardCount > 0;
        public int Version => _version;
        public int GuardCount => _guardCount;
        public int SafeCellCount => _safeCellCount;
        public long PackedBytes => _packedBytes;
        public float BakedCellSize => _bakedCellSize;
        public float BakedTimeStep => _bakedTimeStep;

        /// <summary>烘焙完成后由 Editor 侧调用，写入数据与快照。</summary>
        public void SetBaked(VisibilityTable table, float cellSize, float timeStep,
                            float phaseWindow, int guardCount,
                            int safeCellCount, long packedBytes)
        {
            _table = table;
            _bakedCellSize = cellSize;
            _bakedTimeStep = timeStep;
            _bakedPhaseWindow = phaseWindow;
            _guardCount = guardCount;
            _safeCellCount = safeCellCount;
            _packedBytes = packedBytes;
            _version = LayoutVersion;
        }

        /// <summary>清空成未烘状态（供重烘前复位）。</summary>
        public void ClearBaked()
        {
            _version = 0;
            _table = new VisibilityTable();
            _guardCount = 0;
            _safeCellCount = 0;
            _packedBytes = 0;
        }

        /// <summary>
        /// 查询入口：某格中心在世界点 <paramref name="world"/>、时刻 <paramref name="t"/> 是否危险。
        /// <para>未烘 / 版本不符 / 表无数据时返回 <c>degraded=true</c>，
        /// <paramref name="dangerous"/> 走<b>保守 true</b>——查不到就假定危险，宁可让策划
        /// 觉得「这里危险不敢用」，也绝不谎报「安全」（谎报安全会让人做出错误的关卡决策）。</para>
        /// </summary>
        public bool TryQuery(Vector3 world, float t, out bool dangerous, out string degraded)
        {
            dangerous = true;
            degraded = null;

            var table = Table;
            if (table == null)
            {
                degraded = _version == 0
                    ? "该资产尚未烘焙（先执行烘焙）"
                    : "表结构版本 " + _version + " ≠ 当前 " + LayoutVersion + "，请重烘";
                return false;
            }
            if (!table.TryCellIndex(world, out var cell))
            {
                degraded = "查询点落在表网格范围之外";   // 保守判危险（dangerous 已为 true）
                return false;
            }
            dangerous = table.IsDangerousAt(cell, t);
            return true;
        }

        /// <summary>永久安全格查询（求解器剪枝①用）。未烘返回 false。</summary>
        public bool TryIsPermanentlySafe(Vector3 world, out bool safe)
        {
            safe = false;
            var table = Table;
            if (table == null || !table.TryCellIndex(world, out var cell)) return false;
            safe = table.IsPermanentlySafe(cell);
            return true;
        }
    }
}
