using System;
using UnityEngine;

namespace Collin.CityMap
{
    /// <summary>
    /// 世界空间底座（Modules/00 §2）。四层分辨率解耦：逻辑格 / 显示网格 / chunk / region。
    /// <para>铁律：所有模块的格换算必须经本类型（00 §2.1）——各自写 <c>worldPos / cellSize</c>
    /// 会让同一格在不同模块落到不同 index，是最难查的一类 bug。</para>
    /// <para>尺度档位（00 §2.2，v6 D-26）：2km 档 <c>cellSize=2m</c>（Sample/回归基线）；
    /// 8km 档 <c>cellSize=4m</c>（16M 格 float 口径 336MB 不可接受，实算见 11 §3.1）。</para>
    /// </summary>
    public readonly struct CityGrid
    {
        public readonly float WorldSize;
        public readonly float CellSize;
        public readonly float DisplayStep;
        public readonly float ChunkSize;
        public readonly float RegionSize;
        public readonly int Columns;
        public readonly int Rows;
        public readonly int ChunkCols;
        public readonly int ChunkRows;
        public readonly int RegionCols;
        public readonly int RegionRows;

        /// <summary>世界 XZ 原点（角点，非中心）。xQ/zQ 定点量化与解量化都以此为基准（11 §2/§3）。</summary>
        public readonly Vector2 Origin;

        /// <summary>外环不可建带宽度（00 §2.3）。</summary>
        public readonly float BuildableInset;

        public int CellCount => Columns * Rows;
        public int DisplayColumns => Mathf.FloorToInt(WorldSize / DisplayStep) + 1;

        public CityGrid(float worldSize, float cellSize, float displayStep,
                        float chunkSize, float regionSize, float buildableInset,
                        Vector2 origin = default)
        {
            if (worldSize <= 0f) throw new ArgumentException("worldSize 必须 > 0");
            if (cellSize <= 0f) throw new ArgumentException("cellSize 必须 > 0");

            // A-0-7：显示分辨率必须是逻辑格的整数倍（非法值在构造期即拒，不静默取整）
            if (!IsIntegerMultiple(displayStep, cellSize, out _))
                throw new ArgumentException(
                    $"displayStep({displayStep}) 必须是 cellSize({cellSize}) 的整数倍（00 §2.2 构造断言）");

            if (chunkSize <= 0f || !IsIntegerMultiple(chunkSize, cellSize, out _))
                throw new ArgumentException($"chunkSize({chunkSize}) 必须是 cellSize 的整数倍");

            // region 必须是 chunk 的整数倍（工作集按 region 取整，10 §3.1）
            if (regionSize <= 0f || !IsIntegerMultiple(regionSize, chunkSize, out _))
                throw new ArgumentException($"regionSize({regionSize}) 必须是 chunkSize({chunkSize}) 的整数倍");

            WorldSize = worldSize;
            CellSize = cellSize;
            DisplayStep = displayStep;
            ChunkSize = chunkSize;
            RegionSize = regionSize;
            BuildableInset = Mathf.Max(0f, buildableInset);
            Origin = origin;

            Columns = Mathf.CeilToInt(worldSize / cellSize);
            Rows = Columns;
            ChunkCols = Mathf.CeilToInt(worldSize / chunkSize);
            ChunkRows = ChunkCols;
            RegionCols = Mathf.CeilToInt(worldSize / regionSize);
            RegionRows = RegionCols;
        }

        public static bool IsIntegerMultiple(float larger, float smaller, out int ratio)
        {
            ratio = 0;
            if (smaller <= 0f || larger < smaller) return false;
            var raw = larger / smaller;
            var r = Mathf.RoundToInt(raw);
            // 浮点容差：允许 1024/4=256 这类精确比值，也允许 0.1 级建模误差
            if (r < 1 || Mathf.Abs(raw - r) > 1e-3f * r) return false;
            ratio = r;
            return true;
        }

        /// <summary>floor 语义（负坐标正确），含边界外返回 -1 的策略由调用方选 Try 版。</summary>
        public int CellIndex(float x, float z)
        {
            var cx = Mathf.FloorToInt((x - Origin.x) / CellSize);
            var cz = Mathf.FloorToInt((z - Origin.y) / CellSize);
            return cz * Columns + cx;
        }

        public bool TryCellIndex(float x, float z, out int index)
        {
            var cx = Mathf.FloorToInt((x - Origin.x) / CellSize);
            var cz = Mathf.FloorToInt((z - Origin.y) / CellSize);
            if (cx < 0 || cz < 0 || cx >= Columns || cz >= Rows)
            {
                index = -1;
                return false;
            }
            index = cz * Columns + cx;
            return true;
        }

        public void CellCoord(int index, out int cx, out int cz)
        {
            cx = index % Columns;
            cz = index / Columns;
        }

        public Vector2 CellCenterWorld(int cx, int cz)
            => new Vector2(Origin.x + (cx + 0.5f) * CellSize, Origin.y + (cz + 0.5f) * CellSize);

        public Vector2 CellCenterWorld(int index)
        {
            CellCoord(index, out var cx, out var cz);
            return CellCenterWorld(cx, cz);
        }

        public int ChunkIndex(float x, float z)
        {
            var cxs = Mathf.Clamp(Mathf.FloorToInt((x - Origin.x) / ChunkSize), 0, ChunkCols - 1);
            var czs = Mathf.Clamp(Mathf.FloorToInt((z - Origin.y) / ChunkSize), 0, ChunkRows - 1);
            return czs * ChunkCols + cxs;
        }

        public int RegionIndex(float x, float z)
        {
            var cxs = Mathf.Clamp(Mathf.FloorToInt((x - Origin.x) / RegionSize), 0, RegionCols - 1);
            var czs = Mathf.Clamp(Mathf.FloorToInt((z - Origin.y) / RegionSize), 0, RegionRows - 1);
            return czs * RegionCols + cxs;
        }

        public int RegionIndex(int cellIndex)
        {
            CellCoord(cellIndex, out var cx, out var cz);
            var wx = Origin.x + cx * CellSize;
            var wz = Origin.y + cz * CellSize;
            return RegionIndex(wx, wz);
        }

        /// <summary>格覆盖的矩形是否落在外环带（InestBand 判定入口，S0 写掩码用）。</summary>
        public bool IsInInsetBand(int cellIndex)
        {
            if (BuildableInset <= 0f) return false;
            var c = CellCenterWorld(cellIndex);
            return c.x < Origin.x + BuildableInset || c.x > Origin.x + WorldSize - BuildableInset
                || c.y < Origin.y + BuildableInset || c.y > Origin.y + WorldSize - BuildableInset;
        }

        /// <summary>圆外接盒 → 格索引区间（场域脏环、负约束烘焙的通用工具，05 §3.1）。</summary>
        public void CellRectForCircle(Vector2 center, float radius,
                                      out int minCx, out int minCz, out int maxCx, out int maxCz)
        {
            minCx = Mathf.Clamp(Mathf.FloorToInt((center.x - radius - Origin.x) / CellSize), 0, Columns - 1);
            maxCx = Mathf.Clamp(Mathf.CeilToInt((center.x + radius - Origin.x) / CellSize) - 1, 0, Columns - 1);
            minCz = Mathf.Clamp(Mathf.FloorToInt((center.y - radius - Origin.y) / CellSize), 0, Rows - 1);
            maxCz = Mathf.Clamp(Mathf.CeilToInt((center.y + radius - Origin.y) / CellSize) - 1, 0, Rows - 1);
            if (maxCx < minCx) maxCx = minCx;
            if (maxCz < minCz) maxCz = minCz;
        }

        /// <summary>定点量化编码（11 §2：xQ = round((worldX - originX)/0.01)）。</summary>
        public uint EncodeXQ(float worldX) => (uint)Mathf.RoundToInt((worldX - Origin.x) / RecordQuant.Length);
        public uint EncodeZQ(float worldZ) => (uint)Mathf.RoundToInt((worldZ - Origin.y) / RecordQuant.Length);
        public float DecodeXQ(uint q) => Origin.x + q * RecordQuant.Length;
        public float DecodeZQ(uint q) => Origin.y + q * RecordQuant.Length;
    }

    /// <summary>BuildingRecord 定点标度常量（Modules/11 §2 已冻结，改此处即改协议，须走登记流程）。</summary>
    public static class RecordQuant
    {
        /// <summary>位置量化长度：0.01m。A-1 口径因此是"量化后一致 ≤1 量化步"。</summary>
        public const float Length = 0.01f;

        /// <summary>朝向量化：1/4 度，0..1439。</summary>
        public const int YawPerDegree = 4;
        public const int YawMax = 360 * YawPerDegree - 1;

        public static ushort EncodeYaw(float yawDeg)
        {
            var q = Mathf.RoundToInt(yawDeg * YawPerDegree) % (YawMax + 1);
            if (q < 0) q += YawMax + 1;
            return (ushort)q;
        }

        public static float DecodeYaw(ushort q) => q / (float)YawPerDegree;
    }
}
