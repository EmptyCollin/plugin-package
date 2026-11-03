using System;

namespace Collin.CityMap
{
    /// <summary>
    /// 每格一枚 ushort 位掩码（Modules/00 §2.4，H-3 升位定稿）。
    /// 单一掩码 = 全部"禁止/属性"判定的唯一真源，避免各模块各存一份禁建集合互相打架。
    /// <para>位预算纪律：共 16 位，已用 13 位；新增位必须先在 00 §2.4 登记，再在此处占用。</para>
    /// 写入方：S0（负约束烘焙）/ S2（水/缓冲）/ S3（道路）/ S4（地块/Interior）/
    /// Occupied 重烘焙（adopt、手刷、orphan）；S6 只读。集中经 <see cref="MaskWriter"/> 写。
    /// </summary>
    [Flags]
    public enum CellFlag : ushort
    {
        None         = 0,
        Buildable    = 1 << 0,
        Water        = 1 << 1,
        WaterBuffer  = 1 << 2,
        Road         = 1 << 3,
        Parcel       = 1 << 4,
        ManualLocked = 1 << 5,
        OutOfBounds  = 1 << 6,

        /// <summary>adopt/手刷对象 footprint 硬占用：S2 河道 / S3 细分 / S4 地块 / S6 落位一律视为墙（09 H-2 → 08 D-08-11）。</summary>
        OccupiedHard = 1 << 7,

        /// <summary>负约束：禁路（08 §4.3）。由 ConstraintAsset 在 S0 烘焙进掩码，使禁成为 O(1) 判定。</summary>
        NoRoad  = 1 << 8,

        /// <summary>负约束：禁建。</summary>
        NoBuild = 1 << 9,

        /// <summary>负约束：禁水。</summary>
        NoWater = 1 << 10,

        /// <summary>过大地块内部不产 slot（04 §3.3 depthCap 的结果位）。</summary>
        Interior = 1 << 11,

        /// <summary>外环不可建带（00 §2.3 buildableInset）；与 OutOfBounds 区分：inset 内仍属"图内"，可参与地形与导出。</summary>
        InsetBand = 1 << 12,

        // 13..15 保留。

        /// <summary>任何"不许盖建筑"的位的并集（S6 判定的单一入口）。</summary>
        NoBuildAny = Water | WaterBuffer | Road | OutOfBounds | OccupiedHard | NoBuild | InsetBand,
    }

    /// <summary>
    /// 掩码写入的唯一入口（00 §9：写入点分散是"规则互相覆盖不可追溯"的根因）。
    /// M1 仅提供设置/查询原语；各阶段在 M2+ 经此写掩码，禁止直接触碰数组。
    /// </summary>
    public readonly struct MaskWriter
    {
        private readonly ushort[] _mask;
        private readonly int _columns;

        public MaskWriter(ushort[] mask, int columns)
        {
            if (mask == null) throw new ArgumentNullException(nameof(mask));
            if (columns <= 0) throw new ArgumentOutOfRangeException(nameof(columns));
            _mask = mask;
            _columns = columns;
        }

        public bool TrySet(int cellIndex, CellFlag on, CellFlag off = CellFlag.None)
        {
            if ((uint)cellIndex >= (uint)_mask.Length) return false;
            _mask[cellIndex] = (ushort)((_mask[cellIndex] | (ushort)on) & ~((ushort)off));
            return true;
        }

        public bool TrySet(int cx, int cz, CellFlag on, CellFlag off = CellFlag.None)
        {
            if ((uint)cx >= (uint)_columns || (uint)cz >= (_mask.Length / _columns)) return false;
            return TrySet(cz * _columns + cx, on, off);
        }

        public bool Has(int cellIndex, CellFlag flag)
        {
            if ((uint)cellIndex >= (uint)_mask.Length) return false;
            return (_mask[cellIndex] & (ushort)flag) != 0;
        }

        /// <summary>该格是否禁止落建筑（S6 的唯一判定入口，07 §3.1 第 4 步）。</summary>
        public bool BlocksBuilding(int cellIndex) => Has(cellIndex, CellFlag.NoBuildAny);
    }
}
