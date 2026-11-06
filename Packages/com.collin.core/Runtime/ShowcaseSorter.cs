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
        public string tier;            // 谱系层（可选）：middleware / capability / product / tool；缺省按 category 推断
        public string extractedFrom;   // 抽离血缘（可选）：本包诞生自哪个包（com.collin.*），与依赖方向可以相反
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
            return Closure(targetPackageId, entries, null);
        }

        /// <summary>
        /// 导出闭包（谱系语义）：package.json 依赖闭包 ＋ 方向化抽离边——
        /// 向下抽离·能力：母产品导出携带能力（product → capability 的导出边）；
        /// 向上抽离·工具：工具导出携带运行时（工具→运行时的传递依赖已覆盖）。
        /// 未显式给出边字段的场合，由 tier + extractedFrom 推导，不改写数据。
        /// </summary>
        public static HashSet<string> ExportClosure(
            string targetPackageId, IReadOnlyCollection<ShowcaseEntry> entries)
        {
            return Closure(targetPackageId, entries, ExportEdge);

            bool ExportEdge(ShowcaseEntry from, ShowcaseEntry to)
            {
                // 向下抽离·能力：产品导出携带其能力包（能力的 extractedFrom == 产品）
                if (TierOf(from) == TierProduct && TierOf(to) == TierCapability &&
                    string.Equals((to.extractedFrom ?? "").Trim(), from.packageId,
                        System.StringComparison.Ordinal))
                {
                    return true;
                }
                // 向上抽离·工具：工具导出携带其运行时母体（工具的 extractedFrom == 母体）
                if (TierOf(from) == TierTool &&
                    string.Equals((from.extractedFrom ?? "").Trim(), to.packageId,
                        System.StringComparison.Ordinal))
                {
                    return true;
                }
                return false;
            }
        }

        private static HashSet<string> Closure(
            string targetPackageId, IReadOnlyCollection<ShowcaseEntry> entries,
            System.Func<ShowcaseEntry, ShowcaseEntry, bool> extraEdge)
        {
            var closure = new HashSet<string>(System.StringComparer.Ordinal);
            if (string.IsNullOrEmpty(targetPackageId) || entries == null) return closure;

            var byId = new Dictionary<string, ShowcaseEntry>(System.StringComparer.Ordinal);
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
                var cur = byId[id];
                foreach (var dep in cur.dependencies ?? new List<string>())
                {
                    if (byId.ContainsKey(dep) && dep != id) stack.Push(dep);
                }
                if (extraEdge != null)
                {
                    foreach (var kv in byId)
                    {
                        if (kv.Key != id && closure.Count < byId.Count && extraEdge(cur, kv.Value))
                        {
                            stack.Push(kv.Key);
                        }
                    }
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

        // ── 谱系（AGENTS.md §6：tier / extractedFrom） ──────────────

        public const string TierMiddleware = "middleware";
        public const string TierProduct = "product";
        public const string TierCapability = "capability";
        public const string TierTool = "tool";
        public const string TierUnknown = "component";

        /// <summary>谱系层：显式 tier 优先；缺省 category=="core" 视为 middleware，其余 component。</summary>
        public static string TierOf(ShowcaseEntry e)
        {
            if (e == null) return TierUnknown;
            if (!string.IsNullOrEmpty(e.tier))
            {
                return e.tier.Trim().ToLowerInvariant();
            }
            if (string.Equals(e.category, "core", System.StringComparison.OrdinalIgnoreCase))
            {
                return TierMiddleware;
            }
            return TierUnknown;
        }

        /// <summary>谱系分组：中间件层一组，其余按抽离血缘聚成产品线（根 + 抽离品）。</summary>
        public sealed class LineageGroup
        {
            public string title;
            public string rootId;
            public readonly List<ShowcaseEntry> entries = new List<ShowcaseEntry>();
        }

        /// <summary>
        /// 把已排序条目聚成谱系组（纯函数，输入应为 Sort 的 ordered 结果）：
        /// ① tier==middleware 全部归入「中间件层」；
        /// ② 其余包按 extractedFrom 聚簇：根（无血缘或血缘父不存在）带队，
        ///    抽离品跟随其后（无论抽离方向向上/向下，同组展示）；
        /// ③ 组顺序 = 根在 ordered 中的首次出现顺序；环与悬空血缘按根处理，绝不丢条目。
        /// </summary>
        public static List<LineageGroup> LineageGroups(IReadOnlyList<ShowcaseEntry> ordered)
        {
            var groups = new List<LineageGroup>();
            if (ordered == null || ordered.Count == 0) return groups;

            var byId = new Dictionary<string, ShowcaseEntry>(System.StringComparer.Ordinal);
            foreach (var e in ordered)
            {
                if (!string.IsNullOrEmpty(e.packageId)) byId[e.packageId] = e;
            }

            var children = new Dictionary<string, List<ShowcaseEntry>>(System.StringComparer.Ordinal);
            foreach (var e in ordered)
            {
                var parent = (e.extractedFrom ?? "").Trim();
                if (string.IsNullOrEmpty(parent) || parent == e.packageId) continue;
                if (!byId.ContainsKey(parent)) continue; // 悬空血缘：按根处理
                if (!children.TryGetValue(parent, out var list))
                {
                    list = new List<ShowcaseEntry>();
                    children[parent] = list;
                }
                list.Add(e);
            }

            var assigned = new HashSet<string>(System.StringComparer.Ordinal);

            var middleware = new LineageGroup { title = "中间件层", rootId = "" };
            foreach (var e in ordered)
            {
                if (TierOf(e) != TierMiddleware || !assigned.Add(e.packageId)) continue;
                middleware.entries.Add(e);
            }
            if (middleware.entries.Count > 0) groups.Add(middleware);

            // 真正的根 = 无血缘、或血缘父不存在/构成环。根按 ordered 顺序带队聚簇，
            // 抽离品即使排在母体之前（拓扑序如此）也会被收进母体组，不被拆散。
            System.Func<ShowcaseEntry, bool> isRoot = e =>
            {
                var parent = (e.extractedFrom ?? "").Trim();
                if (string.IsNullOrEmpty(parent) || parent == e.packageId) return true;
                return !byId.ContainsKey(parent); // 悬空血缘按根处理
            };

            foreach (var root in ordered)
            {
                if (assigned.Contains(root.packageId) || !isRoot(root)) continue;
                var group = new LineageGroup
                {
                    title = string.IsNullOrEmpty(root.name) ? root.packageId : root.name,
                    rootId = root.packageId,
                };
                var stack = new List<ShowcaseEntry> { root };
                while (stack.Count > 0)
                {
                    var cur = stack[0];
                    stack.RemoveAt(0);
                    if (!assigned.Add(cur.packageId)) continue;
                    group.entries.Add(cur);
                    if (children.TryGetValue(cur.packageId, out var kids))
                    {
                        foreach (var kid in kids)
                        {
                            if (!assigned.Contains(kid.packageId)) stack.Add(kid);
                        }
                    }
                }
                groups.Add(group);
            }

            // 血缘环兜底：剩余未分组条目各自成组，绝不丢条目
            foreach (var e in ordered)
            {
                if (assigned.Contains(e.packageId)) continue;
                var orphan = new LineageGroup
                {
                    title = string.IsNullOrEmpty(e.name) ? e.packageId : e.name,
                    rootId = e.packageId,
                };
                assigned.Add(e.packageId);
                orphan.entries.Add(e);
                groups.Add(orphan);
            }
            return groups;
        }

        /// <summary>血缘说明：本包诞生自哪个条目（找不到 id 时回退显示原始字符串）。</summary>
        public static ShowcaseEntry ExtractedFromEntry(
            ShowcaseEntry e, IReadOnlyList<ShowcaseEntry> pool)
        {
            if (e == null || string.IsNullOrEmpty(e.extractedFrom) || pool == null) return null;
            foreach (var p in pool)
            {
                if (string.Equals(p.packageId, e.extractedFrom.Trim(), System.StringComparison.Ordinal))
                {
                    return p;
                }
            }
            return null;
        }

        /// <summary>反向血缘：哪些包声明诞生自本包（按输入顺序）。</summary>
        public static List<ShowcaseEntry> ExtracteesOf(
            ShowcaseEntry e, IReadOnlyList<ShowcaseEntry> pool)
        {
            var list = new List<ShowcaseEntry>();
            if (e == null || pool == null) return list;
            foreach (var p in pool)
            {
                if (p != null && p != e &&
                    string.Equals((p.extractedFrom ?? "").Trim(), e.packageId,
                        System.StringComparison.Ordinal))
                {
                    list.Add(p);
                }
            }
            return list;
        }
    }
}
