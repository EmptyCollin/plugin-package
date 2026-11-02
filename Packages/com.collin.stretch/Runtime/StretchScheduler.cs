using System;
using System.Collections.Generic;
using UnityEngine;

namespace Collin.Stretch
{
    /// <summary>
    /// 拉伸重建调度器（DESIGN §8.2 P4）：Rebuild 是 O(V) 主线程工作，万级实例下
    /// 统一入队 + 每帧预算，避免同帧风暴。Play 态预算默认 16/帧（超出滚入下帧）；
    /// 编辑态由 Editor 泵驱动（OnInspectorGUI/参数变更走 RebuildNow 保持所见即所得，
    /// 批量场景加载走队列）。
    /// </summary>
    public static class StretchScheduler
    {
        private static readonly LinkedList<StretchInstance> _queue = new LinkedList<StretchInstance>();
        private static readonly HashSet<StretchInstance> _queued = new HashSet<StretchInstance>();
        public static int rebuildBudgetPerFrame = 16;
        public static int Pending => _queue.Count;
        public static int ProcessedTotal;

        public static void Enqueue(StretchInstance inst)
        {
            if (inst == null || _queued.Contains(inst)) return;
            _queued.Add(inst);
            _queue.AddLast(inst);
        }

        public static void Dequeue(StretchInstance inst)
        {
            if (_queued.Remove(inst))
            {
                var node = _queue.First;
                while (node != null)
                {
                    if (node.Value == inst) { _queue.Remove(node); break; }
                    node = node.Next;
                }
            }
        }

        /// <summary>每帧泵一次（Play 态由 StretchChunkManager 驱动；编辑态由 Editor 泵驱动）。</summary>
        public static void Pump()
        {
            int n = 0;
            while (_queue.Count > 0 && n < rebuildBudgetPerFrame)
            {
                var inst = _queue.First.Value;
                _queue.RemoveFirst();
                _queued.Remove(inst);
                n++;
                if (inst == null) continue;           // 已销毁
                if (!inst.isActiveAndEnabled) continue; // 自己 OnEnable 会再入队
                inst.RebuildNow();
                ProcessedTotal++;
            }
        }

        public static void Clear()
        {
            _queue.Clear();
            _queued.Clear();
        }
    }
}
