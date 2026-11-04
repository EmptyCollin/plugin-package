using UnityEditor;

namespace Collin.Stealth.Navigation.EditorTooling
{
    /// <summary>
    /// 可选导航子程序集的<b>编辑态</b>注册点。
    /// <para>为什么放这里而不是塞进示例场景：主包 Editor 程序集若引用 <c>com.collin.stealth.Navigation</c>，
    /// 那个「可选」就名存实亡——删掉 Navigation 目录会让主包编译失败。
    /// 注册点自带在可选程序集内，<b>装了就有绕行、删了就没</b>，主包零耦合。</para>
    /// <para>Play 态由 <see cref="NavMeshPathBridgeInstaller"/> 的
    /// <c>RuntimeInitializeOnLoadMethod</c> 负责；本处只补编辑态（<c>Services</c> 域重载即清空，
    /// <c>[InitializeOnLoad]</c> 每次重载都会跑，正好对应）。
    /// 注册是幂等的（先判存在），两条路径同时命中也不会报冲突。</para>
    /// </summary>
    [InitializeOnLoad]
    static class StealthNavEditorBootstrap
    {
        static StealthNavEditorBootstrap()
        {
            NavMeshPathBridgeInstaller.Register();
        }
    }
}
