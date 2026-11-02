using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEditor;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace Collin.Core.EditorTooling
{
    /// <summary>
    /// 展板条目发现器：枚举全部 com.collin.* 内嵌包，读取各自 Showcase.json，
    /// 注入 packageId 与依赖（package.json dependencies 中的 com.collin.* 项），
    /// 产出经 ShowcaseSorter 排序的结果。展板场景（Assets/）是唯一消费方。
    /// 解析失败的包记 warning 跳过，不中断整体发现（DESIGN §6 风险缓解）。
    /// </summary>
    public static class ShowcaseScanner
    {
        private const string Prefix = "com.collin.";
        private static readonly Regex DepPattern =
            new Regex("\"(com\\.collin\\.[A-Za-z0-9._-]+)\"", RegexOptions.Compiled);

        /// <summary>发现 + 排序。cycleDetected 时展板应在 UI 上给出可见警示。</summary>
        public static ShowcaseSortResult CollectSorted()
        {
            return ShowcaseSorter.Sort(Collect());
        }

        public static List<ShowcaseEntry> Collect()
        {
            var entries = new List<ShowcaseEntry>();
            foreach (var info in PackageInfo.GetAllRegisteredPackages())
            {
                if (info == null || !info.name.StartsWith(Prefix, System.StringComparison.Ordinal))
                {
                    continue;
                }
                try
                {
                    var entry = LoadEntry(info);
                    if (entry != null) entries.Add(entry);
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning($"[ShowcaseScanner] 跳过 {info.name}: {ex.Message}");
                }
            }
            return entries;
        }

        /// <summary>部分提取用：目标包 + 传递依赖闭包（com.collin.* 内）。</summary>
        public static HashSet<string> ExtractClosure(string targetPackageId)
        {
            return ShowcaseSorter.DependencyClosure(targetPackageId, Collect());
        }

        private static ShowcaseEntry LoadEntry(PackageInfo info)
        {
            // 内嵌包 assetPath 形如 Packages/com.collin.x；解析失败/路径不存在的跳过
            string fullRoot = ToFullPath(info.assetPath);
            if (string.IsNullOrEmpty(fullRoot) || !Directory.Exists(fullRoot))
            {
                return null;
            }

            string showcasePath = Path.Combine(fullRoot, "Showcase.json");
            string packageJsonPath = Path.Combine(fullRoot, "package.json");
            if (!File.Exists(showcasePath))
            {
                Debug.LogWarning($"[ShowcaseScanner] {info.name} 缺 Showcase.json，展板不显示该包（§4 必备）");
                return null;
            }

            var entry = JsonUtility.FromJson<ShowcaseEntry>(File.ReadAllText(showcasePath));
            if (entry == null)
            {
                Debug.LogWarning($"[ShowcaseScanner] {info.name} Showcase.json 解析结果为空");
                return null;
            }

            entry.packageId = info.name;
            entry.dependencies = new List<string>();
            if (File.Exists(packageJsonPath))
            {
                string text = File.ReadAllText(packageJsonPath);
                foreach (Match m in DepPattern.Matches(text))
                {
                    string dep = m.Groups[1].Value;
                    if (dep != info.name && !entry.dependencies.Contains(dep))
                    {
                        entry.dependencies.Add(dep);
                    }
                }
            }

            if (string.IsNullOrEmpty(entry.name)) entry.name = info.displayName;
            return entry;
        }

        private static string ToFullPath(string assetPath)
        {
            try
            {
                return AssetDatabase.IsValidFolder(assetPath)
                    ? Path.GetFullPath(assetPath)
                    : null;
            }
            catch
            {
                return null;
            }
        }
    }
}
