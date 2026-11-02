using System;
using System.Collections.Generic;
using UnityEngine;

namespace Collin.Core
{
    /// <summary>
    /// 跨包服务注册表：feature 包把接口实现注册进来，其他包按接口取用。
    /// 是 AGENTS.md §5 允许的第二种跨包通信面。
    /// 语义约定：静态存储，域重载后清空——注册方必须在自身初始化入口
    /// （RuntimeInitializeOnLoadMethod 或显式装配点）重新 Register。
    /// </summary>
    public static class Services
    {
        private static readonly Dictionary<Type, object> _map = new Dictionary<Type, object>();

        /// <summary>注册实现。同接口重复注册视为装配冲突，报错并保留先注册者。</summary>
        public static void Register<TInterface>(TInterface implementation)
            where TInterface : class
        {
            if (implementation == null) throw new ArgumentNullException(nameof(implementation));
            var type = typeof(TInterface);
            if (_map.ContainsKey(type))
            {
                Debug.LogError($"[Services] 接口 {type.Name} 已有实现 {_map[type].GetType().Name}，拒绝重复注册 {implementation.GetType().Name}");
                return;
            }
            _map[type] = implementation;
        }

        public static bool TryGet<TInterface>(out TInterface service)
            where TInterface : class
        {
            if (_map.TryGetValue(typeof(TInterface), out var value))
            {
                service = (TInterface)value;
                return true;
            }
            service = null;
            return false;
        }

        public static TInterface Get<TInterface>() where TInterface : class
        {
            if (TryGet(out TInterface service)) return service;
            throw new InvalidOperationException(
                $"[Services] 未注册 {typeof(TInterface).Name}。检查提供方包是否已加载并在初始化时完成注册。");
        }

        /// <summary>仅测试/装配清理使用，业务代码禁止调用。</summary>
        public static void Clear()
        {
            _map.Clear();
        }
    }
}
