namespace Collin.CityMap
{
    /// <summary>
    /// 包身份常量。版本真源是 <c>package.json</c>，此处仅供日志前缀与产物戳记使用，
    /// 禁止用于任何逻辑分支或版本比较（AGENTS.md §4: package.json 为依赖唯一真源）。
    /// 存在本文件的另一目的：让 Runtime 程序集非空，使 runtime-purity 与编译门禁
    /// 在 M0 就有真实检查对象，而不是对空程序集空转。
    /// </summary>
    public static class PackageIdentity
    {
        public const string Name = "com.collin.citymap";
        public const string Version = "0.0.1";
        public const string LogPrefix = "[CityMap]";
    }
}
