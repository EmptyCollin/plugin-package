using System;
using System.Collections.Generic;

namespace Collin.Core
{
    /// <summary>展板条目：由各包 Showcase.json 反序列化而来（AGENTS.md §6）。</summary>
    [Serializable]
    public sealed class ShowcaseEntry
    {
        public string name;        // 显示名
        public string category;    // 功能分类
        public string summary;     // 功能介绍
        public string video;       // 包内相对路径（ShowcaseMedia/...）
        public float order;        // 同拓扑层内的先后
        public string packageId;   // 扫描时注入，非 JSON 字段
        public List<string> dependencies = new List<string>(); // 扫描时注入（com.collin.* 部分）
    }

    /// <summary>
    /// 排序结果：有序条目 + 是否检测到依赖环。
    /// cycleDetected=true 时，环上及因环不可达的条目已按 order+名称兜底追加在尾部。
    /// </summary>
    public sealed class ShowcaseSortResult
    {
        public bool cycleDetected;
        public readonly List<ShowcaseEntry> ordered = new List<ShowcaseEntry>();
    }

    /// <summary>
    /// 展板显示顺序（纯函数，无 IO，供 EditMode 测试直接验证）：
    /// 依赖拓扑序（被依赖在前）→ order 定并行分支先后 → 名称字典序兜底。
    /// 非 com.collin.* 的外部依赖不参与拓扑（只裁剪 com.collin.* 闭包）。
    /// </summary>
    public static class ShowcaseSorter
    {
        public static ShowcaseSortResult Sort(IReadOnlyList<ShowcaseEntry> entries)
        {
            var result = new ShowcaseSortResult();
            if (entries == null || entries.Count == 0) return result;

            var byId = new Dictionary<string, ShowcaseEntry>(StringComparer.Ordinal);
            foreach (var e in entries)
            {
                if (string.IsNullOrEmpty(e.packageId)) continue;
                byId[e.packageId] = e;
            }

            // 入度 = 该包在集合内被裁掉的 com.collin.* 依赖数
            var indegree = new Dictionary<string, int>();
            var dependents = new Dictionary<string, List<string>>(); // dep -> [users]
            foreach (var id in byId.Keys)
            {
                int degree = 0;
                foreach (var dep in byId[id].dependencies ?? new List<string>())
                {
                    if (!byId.ContainsKey(dep) || dep == id) continue;
                    degree++;
                    if (!dependents.TryGetValue(dep, out var list))
                    {
                        list = new List<string>();
                        dependents[dep] = list;
                    }
                    if (!list.Contains(id)) list.Add(id);
                }
                indegree[id] = degree;
            }

            // Kahn：每层取 order 最小、并列取名称字典序最小
            var ready = new List<string>();
            foreach (var kv in indegree)
            {
                if (kv.Value == 0) ready.Add(kv.Key);
            }

            var visited = new HashSet<string>(StringComparer.Ordinal);
            while (ready.Count > 0)
            {
                ready.Sort((a, b) => Compare(byId[a], byId[b]));
                var pick = ready[0];
                ready.RemoveAt(0);
                if (!visited.Add(pick)) continue;
                result.ordered.Add(byId[pick]);

                if (!dependents.TryGetValue(pick, out var users)) continue;
                foreach (var user in users)
                {
                    indegree[user]--;
                    if (indegree[user] == 0) ready.Add(user);
                }
            }

            if (visited.Count < byId.Count)
            {
                result.cycleDetected = true;
                var leftovers = new List<ShowcaseEntry>();
                foreach (var kv in byId)
                {
                    if (!visited.Contains(kv.Key)) leftovers.Add(kv.Value);
                }
                leftovers.Sort(Compare);
                result.ordered.AddRange(leftovers);
            }
            return result;
        }

        /// <summary>依赖闭包：目标包 + 全部传递依赖（com.collin.* 集合内）。供「部分提取」使用。</summary>
        public static HashSet<string> DependencyClosure(
            string targetPackageId, IReadOnlyCollection<ShowcaseEntry> entries)
        {
            var closure = new HashSet<string>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(targetPackageId) || entries == null) return closure;

            var byId = new Dictionary<string, ShowcaseEntry>(StringComparer.Ordinal);
            foreach (var e in entries)
            {
                if (!string.IsNullOrEmpty(e.packageId)) byId[e.packageId] = e;
            }
            if (!byId.ContainsKey(targetPackageId)) return closure;

            var stack = new Stack<string>();
            stack.Push(targetPackageId);
            while (stack.Count > 0)
            {
                var id = stack.Pop();
                if (!closure.Add(id)) continue;
                foreach (var dep in byId[id].dependencies ?? new List<string>())
                {
                    if (byId.ContainsKey(dep) && dep != id) stack.Push(dep);
                }
            }
            return closure;
        }

        private static int Compare(ShowcaseEntry a, ShowcaseEntry b)
        {
            int byOrder = a.order.CompareTo(b.order);
            if (byOrder != 0) return byOrder;
            return string.CompareOrdinal(a.name ?? a.packageId, b.name ?? b.packageId);
        }
    }
}
