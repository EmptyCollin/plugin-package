using UnityEngine;

namespace Collin.Stealth
{
    /// <summary>
    /// 场地边界（Arena）的纯几何：把玩家钳制在可活动范围内。
    /// <para>存在理由（用户实机反馈「像主流设计一样加上外侧围栏」）：围栏如果<b>只是几块看得见的墙</b>，
    /// 那么穿模、陡坡滑行、或掩体被删掉任意一项都会让玩家溜出地图，跑到虚空里看哨兵的后脑勺。
    /// 主流做法是<b>可见围栏 + 不可见的硬边界</b>两层，本类就是后一层。</para>
    /// <para>做成纯函数（而非依赖碰撞体）有两个硬好处：
    /// ①脱离 Play 态可断言（本包纪律）；②预制件拖进任意空场景、场景里没建墙也依然有效。</para>
    /// </summary>
    public static class StealthArena
    {
        /// <summary>半宽为 0 或负 ⇒ 边界关闭（不限制移动）。</summary>
        public static bool IsEnabled(Vector2 halfSize)
        {
            return halfSize.x > 0.01f && halfSize.y > 0.01f;
        }

        /// <summary>点是否落在矩形场内（水平面，含边界）。</summary>
        public static bool Contains(Vector3 p, Vector3 center, Vector2 halfSize, float margin = 0f)
        {
            if (!IsEnabled(halfSize)) return true;
            var dx = Mathf.Abs(p.x - center.x);
            var dz = Mathf.Abs(p.z - center.z);
            var m = Mathf.Max(0f, margin);
            return dx <= halfSize.x - m && dz <= halfSize.y - m;
        }

        /// <summary>
        /// 把点钳回矩形场内。<b>只改 x/z，纵向（y）原样保留</b>——
        /// 高度归重力与地形管，边界只管「别走出去」。
        /// </summary>
        public static Vector3 Clamp(Vector3 p, Vector3 center, Vector2 halfSize, float margin = 0f)
        {
            if (!IsEnabled(halfSize)) return p;
            var m = Mathf.Max(0f, margin);
            var hx = Mathf.Max(0f, halfSize.x - m);
            var hz = Mathf.Max(0f, halfSize.y - m);
            return new Vector3(
                Mathf.Clamp(p.x, center.x - hx, center.x + hx),
                p.y,
                Mathf.Clamp(p.z, center.z - hz, center.z + hz));
        }

        /// <summary>
        /// 点到矩形边界的<b>最小内距</b>（负值 = 已在场外）。用于日志与自检，不参与判定。
        /// </summary>
        public static float Inset(Vector3 p, Vector3 center, Vector2 halfSize)
        {
            if (!IsEnabled(halfSize)) return float.PositiveInfinity;
            var dx = halfSize.x - Mathf.Abs(p.x - center.x);
            var dz = halfSize.y - Mathf.Abs(p.z - center.z);
            return Mathf.Min(dx, dz);
        }
    }
}
