using System.Collections.Generic;
using UnityEngine;

namespace Collin.Stealth
{
    /// <summary>
    /// 察觉目标注册表：避免每帧全场景搜索 <see cref="DetectionState"/>。
    /// <para>失效策略与 <see cref="VisionRegistry"/> <b>完全对称</b>：变更置脏 + 读时重建走缓存；
    /// 额外一道「缓存混入已销毁引用即自愈」的便宜检查（U-8：编辑态切场景时组件 OnDisable 不保证触发）。
    /// ⚠️ 两侧必须同步修改，别只改一个（历史缺陷 U-8 的根因正在这套共享写法）；
    /// 也不许写成「非运行态每次重扫」——那会拖垮烘焙热路径（详见 <see cref="VisionRegistry"/> 注释）。</para>
    /// </summary>
    public static class DetectionRegistry
    {
        static readonly List<DetectionState> _targets = new List<DetectionState>();
        static bool _dirty = true;

        /// <summary>标记需要重建。</summary>
        public static void MarkDirty() => _dirty = true;

        /// <summary>当前有效察觉目标。返回列表为内部缓冲，<b>取用后立即使用、不得持有</b>。</summary>
        public static IReadOnlyList<DetectionState> Targets
        {
            get
            {
                // 与 VisionRegistry 完全对称：便宜的死引用自愈 + 「空缓存」交给 Editor 侧场景钩子置脏。
                // 之所以不写「非运行态每次重扫」：与另一侧共用同一套推理（热路径不能全量扫描），
                // 保持两个注册表一致也能防止后续维护者把某一侧单独改回去。
                if (_dirty || (!Application.isPlaying && HasDestroyed(_targets)))
                {
                    _targets.Clear();
                    _targets.AddRange(UnityEngine.Object.FindObjectsByType<DetectionState>(
                        FindObjectsInactive.Exclude, FindObjectsSortMode.None));
                    _dirty = false;
                }
                return _targets;
            }
        }

        /// <summary>缓存中是否混有已销毁（Unity <c>==null</c> 为真）的引用。空缓存返回 false。</summary>
        static bool HasDestroyed(List<DetectionState> list)
        {
            for (var i = 0; i < list.Count; i++)
                if (list[i] == null) return true;
            return false;
        }

        /// <summary>按 id 找目标；找不到返回 null。</summary>
        public static DetectionState Find(int targetId)
        {
            var list = Targets;
            for (var i = 0; i < list.Count; i++)
            {
                if (list[i] != null && list[i].TargetId == targetId) return list[i];
            }
            return null;
        }

        /// <summary>测试复位。</summary>
        public static void ResetForTests()
        {
            _targets.Clear();
            _dirty = true;
        }
    }
}
