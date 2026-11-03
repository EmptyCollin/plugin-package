using System.Collections.Generic;
using UnityEngine;

namespace Collin.Stealth
{
    /// <summary>
    /// 察觉目标注册表：避免每帧全场景搜索 <see cref="DetectionState"/>。
    /// 与 <see cref="VisionRegistry"/> 同一保守失效策略（变更置脏、读时重建）。
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
                if (_dirty)
                {
                    _targets.Clear();
                    _targets.AddRange(UnityEngine.Object.FindObjectsByType<DetectionState>(
                        FindObjectsInactive.Exclude, FindObjectsSortMode.None));
                    _dirty = false;
                }
                return _targets;
            }
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
