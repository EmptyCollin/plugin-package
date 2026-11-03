using System.Collections.Generic;
using UnityEngine;

namespace Collin.Stealth
{
    /// <summary>
    /// 视野体注册表：避免每个察觉目标每帧都全场景搜索 <see cref="VisionVolume"/>。
    /// <para>失效策略保守——任何启用/禁用都置脏，下一次读取时重建；
    /// 不做精细增删，因为「读时重建」本身就是 O(n) 且 n 是场景内视野体数量。</para>
    /// </summary>
    public static class VisionRegistry
    {
        static readonly List<VisionVolume> _volumes = new List<VisionVolume>();
        static bool _dirty = true;

        /// <summary>标记需要重建（由 VisionVolume 的启用/禁用钩子调用）。</summary>
        public static void MarkDirty() => _dirty = true;

        /// <summary>当前有效视野体列表。返回列表为内部缓冲，<b>调用后立即使用、不得持有</b>。</summary>
        public static IReadOnlyList<VisionVolume> Volumes
        {
            get
            {
                if (_dirty)
                {
                    _volumes.Clear();
                    _volumes.AddRange(UnityEngine.Object.FindObjectsByType<VisionVolume>(
                        FindObjectsInactive.Exclude, FindObjectsSortMode.None));
                    _dirty = false;
                }
                return _volumes;
            }
        }

        /// <summary>测试复位。</summary>
        public static void ResetForTests()
        {
            _volumes.Clear();
            _dirty = true;
        }
    }
}
