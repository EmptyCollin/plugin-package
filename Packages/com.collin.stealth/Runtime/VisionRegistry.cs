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
                // 除显式置脏外，加一道便宜的「死引用」自愈：编辑态切场景时组件 OnDisable 不保证触发
                // （U-8 实测：切走再切回，_dirty 仍为 False、缓存里是 3 条已销毁引用 ⇒
                //  上层 StealthWorld.Volumes 用 v==null 过滤后静默返回 0）。
                // ⚠️ 判据必须是 O(缓存长度) 的 null 检查，**不能**「非运行态每次读取都重扫」——
                //    CanSeeAt → FindVolume 是本包烘焙的热路径（实测一次烘焙 1,970,540 次查询），
                //    每次读取重扫 = 每次一个全场景 FindObjectsByType ⇒ 0.66s 的烘焙会退化到不可用。
                //    （本会话先写成「重扫」后实测发现是性能炸弹，故收回。）
                // ⚠️ 该判据只在缓存「混有死引用」时触发；若重建结果为<b>空</b>（切到无视野体的场景），
                //    HasDestroyed([])==false 就不会再重建 ⇒ 空缓存那条路必须靠 Editor 侧的
                //    StealthEditorBootstrap 场景切换钩子置脏来兜（那里 O(1)、完全不碰本热路径）。
                if (_dirty || (!Application.isPlaying && HasDestroyed(_volumes)))
                {
                    _volumes.Clear();
                    _volumes.AddRange(UnityEngine.Object.FindObjectsByType<VisionVolume>(
                        FindObjectsInactive.Exclude, FindObjectsSortMode.None));
                    _dirty = false;
                }
                return _volumes;
            }
        }

        /// <summary>缓存中是否混有已销毁（Unity <c>==null</c> 为真）的引用。空缓存返回 false。</summary>
        static bool HasDestroyed(List<VisionVolume> list)
        {
            for (var i = 0; i < list.Count; i++)
                if (list[i] == null) return true;
            return false;
        }

        /// <summary>测试复位。</summary>
        public static void ResetForTests()
        {
            _volumes.Clear();
            _dirty = true;
        }
    }
}
