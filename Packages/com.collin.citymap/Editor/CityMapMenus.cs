using System.IO;
using UnityEditor;
using UnityEngine;

namespace Collin.CityMap.EditorTooling
{
    /// <summary>
    /// 包菜单入口骨架（AGENTS.md §0.1 原则3：编辑增强一律走 Editor 程序集）。
    /// M0 只提供「Package Info」自检项；后续里程碑往本菜单组追加 City Window / 生成命令入口。
    /// 本文件存在的另一必要理由：Editor 目录必须至少有一个源文件，
    /// 否则 asmdef 不会产出程序集，Unity 会报 error（控制台归零门禁 §1.6b 不允许）。
    /// </summary>
    internal static class CityMapMenus
    {
        /// <summary>菜单一律收进 Tools 下（用户 2026-11-03 要求：工具栏不单开顶层 Collin 项，
        /// 与 stretch 包的 Tools/Collin/… 约定对齐；顶层 Collin 曾由本包 MenuItem 产生）。</summary>
        private const string MenuRoot = "Tools/Collin/CityMap/";

        [MenuItem(MenuRoot + "Package Info")]
        private static void ShowPackageInfo()
        {
            var text = BuildInfoText();
            Debug.Log($"{PackageIdentity.LogPrefix} {text.Replace('\n', ' ')}");
            EditorUtility.DisplayDialog("Collin CityMap", text, "好");
        }

        /// <summary>包身份自检文本：版本真源仍取 package.json（§4），此处只做呈现。</summary>
        private static string BuildInfoText()
        {
            var root = FindPackageRoot();
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"包名：{PackageIdentity.Name}");
            sb.AppendLine($"代码侧版本常量：{PackageIdentity.Version}（真源为 package.json）");

            if (root != null)
            {
                sb.AppendLine($"包根目录：{root}");
                sb.AppendLine($"package.json 存在：{File.Exists(Path.Combine(root, "package.json"))}");
                sb.AppendLine($"Showcase.json 存在：{File.Exists(Path.Combine(root, "Showcase.json"))}");

                var docs = Path.Combine(root, "Docs");
                sb.AppendLine("四件套：");
                foreach (var name in new[] { "DESIGN.md", "ACCEPTANCE.md", "CHANGELOG.md" })
                {
                    sb.AppendLine($"  {name}：{(File.Exists(Path.Combine(docs, name)) ? "有" : "缺")}");
                }
                var modules = Path.Combine(docs, "Modules");
                sb.AppendLine(Directory.Exists(modules)
                    ? $"模块细则：{Directory.GetFileSystemEntries(modules, "*.md").Length} 份"
                    : "模块细则：缺 Docs/Modules/");
            }
            else
            {
                sb.AppendLine("未定位到包根目录（内嵌包未被正确解析？）");
            }

            return sb.ToString();
        }

        /// <summary>从本脚本的资产路径反推包根；失败返回 null（不抛异常，自检不应打断操作）。</summary>
        private static string FindPackageRoot()
        {
            try
            {
                var self = AssetDatabase.FindAssets("CityMapMenus");
                if (self == null || self.Length == 0) return null;
                var path = AssetDatabase.GUIDToAssetPath(self[0]);
                return string.IsNullOrEmpty(path) ? null : Path.GetDirectoryName(Path.GetDirectoryName(path));
            }
            catch
            {
                return null;
            }
        }
    }
}
