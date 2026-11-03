using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Collin.CityMap
{
    /// <summary>
    /// 建筑落位记录 —— **接口已冻结**（Modules/11，用户 2026-11-03 确认）。
    /// 三方消费者：07 的输出、08/10 的认回依据、10 的导出源。
    /// <para>改动本结构（字段名 / 偏移 / 标度 / 位定义）必须先在 11 号文档追加登记行并经用户批准；
    /// 新增位优先占用 <see cref="Reserved0"/>/<see cref="Reserved1"/>。</para>
    /// <para>32 字节无填充：位置为 0.01m 定点（11 §2），A-1 的确定性口径因此是"量化后一致 ≤1 量化步"。</para>
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct BuildingRecord
    {
        /// <summary>冻结布局大小（读侧据此拒绝未知布局，11 §3）。</summary>
        public const int LayoutSize = 32;

        /// <summary>当前 schemaVersion（11 §11）。</summary>
        public const int SchemaVersion = 1;

        /// <summary>唯一稳定键。bit31 = 命名空间（0=派生，1=手刷）；永不复用（11 §4.2）。</summary>
        public int SlotKey;

        /// <summary>→ BuildingProfile 行；查不到 → 本条转 orphan（11 §10 warning）。</summary>
        public int RowId;

        /// <summary>世界 X 定点：round((x - originX)/0.01)，解量化基准记在资产头（11 §3）。</summary>
        public uint XQ;
        public uint ZQ;

        /// <summary>朝向：1/4 度，0..1439。</summary>
        public ushort YawQ;

        /// <summary>
        /// 高度意图，**双义字段**（11 §4.3，用户裁定保留）：
        /// stretchable=1&amp;approximated=0 → 段复制份数 repeat；
        /// stretchable=1&amp;approximated=1 → 缩放百分数（100=×1.0）；
        /// stretchable=0 → 恒为 1（哨兵）。
        /// 读取一律经 <see cref="RecordGeometry"/>，禁止手写字段解释（11 §13 第 1 条）。
        /// </summary>
        public short HeightValue;

        /// <summary>footprint 宽覆盖，0.01m 定点；0 = 使用表值（11 §7 H-26）。</summary>
        public short FootWQ;
        public short FootDQ;

        /// <summary>落位时的四维强度（0~100 整数标度，D-02）。dominant/门槛为纯派生，不另存。</summary>
        public byte LandF, LandI, LandR, LandA;

        /// <summary>位域，见 <see cref="BuildingFlags"/>。</summary>
        public byte Flags;

        /// <summary>保留（必须写 0）。读侧遇非 0 → warning：文件由更新版本写出。</summary>
        public byte Reserved0;
        public ushort Reserved1;

        /// <summary>编译期自证：结构必须恰为 32 字节（A-45 的静态部分）。</summary>
        public static int SizeOf => Marshal.SizeOf<BuildingRecord>();

        public bool Has(BuildingFlags f) => (Flags & (byte)f) != 0;
        public void Set(BuildingFlags f, bool on = true)
            => Flags = on ? (byte)(Flags | (byte)f) : (byte)(Flags & ~(byte)f);

        /// <summary>主导维（argmax）。纯派生，不占字段。</summary>
        public LandUseAxis Dominant
        {
            get
            {
                byte best = LandF; var axis = LandUseAxis.Finance;
                if (LandI > best) { best = LandI; axis = LandUseAxis.Industrial; }
                if (LandR > best) { best = LandR; axis = LandUseAxis.Residential; }
                if (LandA > best) { best = LandA; axis = LandUseAxis.Agricultural; }
                return axis;
            }
        }

        /// <summary>落位时该点的门槛 = 主导维通道值（11 §2：thresholdUsed 字段因此被删，07 §1 定义 Th=F_D）。</summary>
        public byte ThresholdUsed
        {
            get
            {
                switch (Dominant)
                {
                    case LandUseAxis.Industrial:   return LandI;
                    case LandUseAxis.Residential:  return LandR;
                    case LandUseAxis.Agricultural: return LandA;
                    default:                       return LandF;
                }
            }
        }

        /// <summary>位域必须能装下 6 个已定义位（11 §4.1）。</summary>
        public static bool ValidateLayout()
        {
            if (SizeOf != LayoutSize) return false;
            if (Marshal.OffsetOf<BuildingRecord>(nameof(SlotKey)).ToInt32() != 0) return false;
            if (Marshal.OffsetOf<BuildingRecord>(nameof(RowId)).ToInt32() != 4) return false;
            if (Marshal.OffsetOf<BuildingRecord>(nameof(XQ)).ToInt32() != 8) return false;
            if (Marshal.OffsetOf<BuildingRecord>(nameof(ZQ)).ToInt32() != 12) return false;
            if (Marshal.OffsetOf<BuildingRecord>(nameof(YawQ)).ToInt32() != 16) return false;
            if (Marshal.OffsetOf<BuildingRecord>(nameof(HeightValue)).ToInt32() != 18) return false;
            if (Marshal.OffsetOf<BuildingRecord>(nameof(FootWQ)).ToInt32() != 20) return false;
            if (Marshal.OffsetOf<BuildingRecord>(nameof(FootDQ)).ToInt32() != 22) return false;
            if (Marshal.OffsetOf<BuildingRecord>(nameof(LandF)).ToInt32() != 24) return false;
            if (Marshal.OffsetOf<BuildingRecord>(nameof(LandI)).ToInt32() != 25) return false;
            if (Marshal.OffsetOf<BuildingRecord>(nameof(LandR)).ToInt32() != 26) return false;
            if (Marshal.OffsetOf<BuildingRecord>(nameof(LandA)).ToInt32() != 27) return false;
            if (Marshal.OffsetOf<BuildingRecord>(nameof(Flags)).ToInt32() != 28) return false;
            if (Marshal.OffsetOf<BuildingRecord>(nameof(Reserved0)).ToInt32() != 29) return false;
            if (Marshal.OffsetOf<BuildingRecord>(nameof(Reserved1)).ToInt32() != 30) return false;
            return true;
        }
    }

    /// <summary>记录位域（Modules/11 §4.1 定稿）。orphan 与 adopted 可同真；approximated=1 要求行 stretchable。</summary>
    [Flags]
    public enum BuildingFlags : byte
    {
        None         = 0,
        Adopted      = 1 << 0,
        UserModified = 1 << 1,
        Orphan       = 1 << 2,
        Approximated = 1 << 3,
        InLockRegion = 1 << 4,
        ManualSource = 1 << 5,
        // 6..7 保留
    }

    public enum LandUseAxis : byte { Finance = 0, Industrial = 1, Residential = 2, Agricultural = 3 }

    /// <summary>
    /// <see cref="BuildingRecord.HeightValue"/> 的唯一解释入口（11 §13 第 1 条强制）。
    /// 业务代码禁止手写 <c>heightValue/100</c> 之类的推断；代码审查发现直接读字段做算术即打回。
    /// </summary>
    public static class RecordGeometry
    {
        public enum HeightKind { RepeatSegments, ScalePercent, NotStretchable }

        /// <summary>
        /// 依据「行 stretchable × 记录 approximated」二维表解析高度意图。
        /// 表内容即 11 §4.3，逐行对应，勿在此文件外复制一份。
        /// </summary>
        public static bool Resolve(in BuildingRecord r, bool rowStretchable,
                                  out HeightKind kind, out int value)
        {
            kind = HeightKind.NotStretchable;
            value = 0;

            if (!rowStretchable)
            {
                // 情形三/四：false+0 为哨兵；false+1 是非法组合（校验 error，11 §10）
                if (r.Has(BuildingFlags.Approximated)) return false;
                kind = HeightKind.NotStretchable;
                value = 1;
                return r.HeightValue == 0 || r.HeightValue == 1;
            }

            if (r.Has(BuildingFlags.Approximated))
            {
                kind = HeightKind.ScalePercent;
                value = r.HeightValue;
                return value >= ScaleMin && value <= ScaleMax;
            }

            kind = HeightKind.RepeatSegments;
            value = r.HeightValue;
            return value >= 1;   // 0 视为非法（11 §4.3：按 1 处理并记 warning，由调用方决定）
        }

        /// <summary>缩放百分数的有效区（11 §4.3）。</summary>
        public const int ScaleMin = 50;
        public const int ScaleMax = 800;

        /// <summary>把缩放百分数换成 Transform 系数。</summary>
        public static float ToScaleFactor(int percent) => percent / 100f;

        /// <summary>记录 footprint 覆盖值 → 米；0 表示"用表值"，由调用方回落（11 §5/§7 H-26）。</summary>
        public static bool TryGetFootprintOverride(in BuildingRecord r, out float widthM, out float depthM)
        {
            widthM = r.FootWQ * RecordQuant.Length;
            depthM = r.FootDQ * RecordQuant.Length;
            return r.FootWQ != 0 && r.FootDQ != 0;
        }

        public static float EncodeFootprintOverride(float meters) => Mathf.RoundToInt(meters / RecordQuant.Length);
    }

    /// <summary>slotKey 命名空间（11 §4.2）。</summary>
    public static class SlotKey
    {
        public const int NamespaceBit = 1 << 31;

        public static bool IsManual(int slotKey) => (slotKey & NamespaceBit) != 0;
        public static bool IsDerived(int slotKey) => !IsManual(slotKey);
        public static int MakeManual(uint counter) => unchecked((int)(counter | (uint)NamespaceBit));

        /// <summary>派生键的低位有效域上限（bit31 让给命名空间）。</summary>
        public const int DerivedMax = int.MaxValue;
    }
}
