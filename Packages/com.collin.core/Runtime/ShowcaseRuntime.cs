using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Collin.Core
{
    /// <summary>
    /// 展板条目运行时枚举器：从 <项目根>/Packages/com.collin.*/ 扫描 Showcase.json，
    /// 注入 packageId 与依赖（package.json 中的 com.collin.* 项），复用 ShowcaseSorter 排序。
    /// 展板场景（Play 模式）是唯一消费方；独立构建不含 Packages 目录时静默返回空
    /// （展板仅项目内运行，DESIGN §5 D-5）。解析失败的包记 warning 跳过，不中断整体。
    /// </summary>
    public static class ShowcaseRuntime
    {
        private static readonly Regex DepPattern =
            new Regex("\"(com\\.collin\\.[A-Za-z0-9._-]+)\"", RegexOptions.Compiled);

        /// <summary>自 dataPath 向上找含 Packages/ 的目录；找不到返回 null。</summary>
        public static string FindProjectRoot()
        {
            try
            {
                var dir = new DirectoryInfo(Application.dataPath); // <root>/Assets
                for (int i = 0; i < 5 && dir != null; i++, dir = dir.Parent)
                {
                    if (Directory.Exists(Path.Combine(dir.FullName, "Packages")))
                    {
                        return dir.FullName;
                    }
                }
            }
            catch (Exception)
            {
                // 路径异常视为未找到，由调用方降级处理
            }
            return null;
        }

        public static ShowcaseSortResult LoadAndSort()
        {
            return ShowcaseSorter.Sort(Load());
        }

        public static List<ShowcaseEntry> Load()
        {
            var list = new List<ShowcaseEntry>();
            var root = FindProjectRoot();
            if (root == null)
            {
                Debug.Log("[ShowcaseRuntime] 未找到 Packages 目录（独立构建？），展板列表为空。");
                return list;
            }

            string packagesDir = Path.Combine(root, "Packages");
            foreach (var pkgDir in Directory.GetDirectories(packagesDir, "com.collin.*"))
            {
                var folderName = Path.GetFileName(pkgDir);
                var showcasePath = Path.Combine(pkgDir, "Showcase.json");
                if (!File.Exists(showcasePath))
                {
                    Debug.LogWarning($"[ShowcaseRuntime] {folderName} 缺 Showcase.json，展板跳过该包（§4 必备）");
                    continue;
                }
                try
                {
                    var entry = JsonUtility.FromJson<ShowcaseEntry>(File.ReadAllText(showcasePath));
                    if (entry == null)
                    {
                        Debug.LogWarning($"[ShowcaseRuntime] {folderName} Showcase.json 解析为空");
                        continue;
                    }
                    entry.packageId = folderName;
                    entry.dependencies = new List<string>();
                    var packageJsonPath = Path.Combine(pkgDir, "package.json");
                    if (File.Exists(packageJsonPath))
                    {
                        foreach (Match m in DepPattern.Matches(File.ReadAllText(packageJsonPath)))
                        {
                            var dep = m.Groups[1].Value;
                            if (dep != folderName && !entry.dependencies.Contains(dep))
                            {
                                entry.dependencies.Add(dep);
                            }
                        }
                    }
                    list.Add(entry);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[ShowcaseRuntime] {folderName} 解析失败: {ex.Message}");
                }
            }
            return list;
        }

        /// <summary>视频包内相对路径 → 绝对路径；空/缺失/文件不存在返回 null。</summary>
        public static string ResolveVideoPath(ShowcaseEntry entry)
        {
            if (entry == null || string.IsNullOrEmpty(entry.video)) return null;
            var root = FindProjectRoot();
            if (root == null || string.IsNullOrEmpty(entry.packageId)) return null;
            var path = Path.Combine(root, "Packages", entry.packageId,
                entry.video.Replace('/', Path.DirectorySeparatorChar));
            return File.Exists(path) ? path : null;
        }
    }
}
