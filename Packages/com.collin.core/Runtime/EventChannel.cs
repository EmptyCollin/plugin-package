using System;
using System.Collections.Generic;
using UnityEngine;

namespace Collin.Core
{
    /// <summary>
    /// 跨包广播事件通道（ScriptableObject 信号）。
    /// feature 包之间唯一的合法通信面之一（AGENTS.md §5）：
    /// 持有同一资产引用的包通过 Raise/Subscribe 解耦通信。
    /// 注意：订阅是运行时的，域重载/退出 Play 后订阅清空，
    /// 消费方应在自己的初始化入口重新 Subscribe。
    /// </summary>
    [CreateAssetMenu(menuName = "Collin/Core/Event Channel", fileName = "NewEventChannel")]
    public class EventChannel : ScriptableObject
    {
        private readonly List<Action> _handlers = new List<Action>();

        public void Subscribe(Action handler)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            _handlers.Add(handler);
        }

        public bool Unsubscribe(Action handler)
        {
            return _handlers.Remove(handler);
        }

        public void Raise()
        {
            // 复制后再遍历：允许处理器在回调中安全退订。
            foreach (var handler in _handlers.ToArray())
            {
                try
                {
                    handler();
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[EventChannel:{name}] 处理器抛出异常: {ex}");
                }
            }
        }

        private void OnDisable()
        {
            _handlers.Clear();
        }
    }

    /// <summary>类型化事件通道基类。泛型 ScriptableObject 无法被 Unity 直接实例化
    /// （实测 CreateInstance&lt;EventChannel&lt;int&gt;&gt; 返回 null），消费方须声明具体子类：</summary>
    /// <code>public class DamageChannel : EventChannel&lt;float&gt; { }</code>
    public abstract class EventChannel<TPayload> : ScriptableObject
    {
        private readonly List<Action<TPayload>> _handlers = new List<Action<TPayload>>();

        public void Subscribe(Action<TPayload> handler)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            _handlers.Add(handler);
        }

        public bool Unsubscribe(Action<TPayload> handler)
        {
            return _handlers.Remove(handler);
        }

        public void Raise(TPayload payload)
        {
            foreach (var handler in _handlers.ToArray())
            {
                try
                {
                    handler(payload);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[EventChannel:{name}] 处理器抛出异常: {ex}");
                }
            }
        }

        private void OnDisable()
        {
            _handlers.Clear();
        }
    }

    /// <summary>int 载荷通道：core 自带的具体子类，既是可直接使用的信号类型，
    /// 也是 feature 包自定义具体通道类的参照样板。</summary>
    [CreateAssetMenu(menuName = "Collin/Core/Int Event Channel", fileName = "NewIntEventChannel")]
    public class IntEventChannel : EventChannel<int>
    {
    }
}
