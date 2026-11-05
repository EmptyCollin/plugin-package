using System.Runtime.CompilerServices;

// 契约适配与烘焙会话都是 internal（DESIGN §0.1「编辑器增强全走 Editor 程序集」，
// 也不希望外部包来引用本包的编辑器类型）。但 U-1「玩法包缺席必须优雅降级」的断言
// 必须打到这两条真实入口上——若为测试把它们改成 public，等于把内部实现固化成对外 API。
// 故只对测试程序集开口，范围与测试 asmdef 名称严格一致。
[assembly: InternalsVisibleTo("com.collin.stealthplanner.Tests")]
