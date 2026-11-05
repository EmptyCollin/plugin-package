using System.Reflection;
using Collin.Core;
using Collin.Core.Stealth;
using Collin.StealthPlanner.EditorTooling;
using NUnit.Framework;
using UnityEngine;

namespace Collin.StealthPlanner.Tests
{
    /// <summary>
    /// 「玩法包缺席时优雅降级」（ACCEPTANCE 组 A 第 3 条 / 未完成项 U-1）。
    /// <para>为什么值得单独立一条：设计辅助台与玩法包是**两个包**，用户完全可能在
    /// 玩法包尚未注册的状态下打开本包工具（只导出本包、展板工程、新克隆、CI）。
    /// 这时若烘焙入口抛异常，编辑器会直接弹堆栈窗；更坏的是若静默给出一张**空表**，
    /// 下游会把空表读成「全场安全」——正是 D-17 那类「不报错的错数据」。
    /// 故本组同时钉三点：<b>不抛</b>、<b>不给可用对象</b>、<b>原因文本可区分</b>。</para>
    /// <para>原因文本必须「分得清缺设置还是缺玩法包」：两条对应完全不同的用户动作，
    /// 都写成「烘焙失败」等于把提示丢回给策划自己猜。</para>
    /// </summary>
    public class GracefulDegradationTests
    {
        const BindingFlags Priv = BindingFlags.Instance | BindingFlags.NonPublic;

        [SetUp]
        public void SetUp()
        {
            Services.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            Services.Clear();
        }

        [Test]
        public void ContractSource_Null_WhenGameplayServicesAbsent()
        {
            Assert.False(Services.TryGet<IPatrolWorld>(out _), "前置：服务应已清空");
            Assert.DoesNotThrow(() =>
            {
                Assert.IsNull(ContractVisibilitySource.TryCreate(),
                    "缺玩法包时适配器必须返回 null，而不是造一个空的可见性源");
            }, "适配器不得用异常表达「服务缺席」这种可预期状态");
        }

        [Test]
        public void BakeSession_TryBegin_FailsWithReadableReason_NotThrowing()
        {
            var settings = ScriptableObject.CreateInstance<PlannerBakeSettings>();
            try
            {
                string fail = "未赋值";
                VisibilityBakeSession session = null;
                Assert.DoesNotThrow(() =>
                {
                    session = VisibilityBakeSession.TryBegin(
                        new Vector3(-5f, 0f, -5f), new Vector3(5f, 0f, 5f),
                        settings, 3f, 1.5f, out fail);
                });
                Assert.IsNull(session, "服务缺席时不得给出一个看起来可用的会话");
                Assert.That(fail, Does.Contain("玩法包"), "原因应指出缺的是玩法包：" + fail);

                // 脚本模式走同一入口（RunOnce 内部即 TryBegin），也必须静默失败而非抛
                Assert.DoesNotThrow(() =>
                    Assert.IsNull(VisibilityBakeService.RunOnce(
                        new Vector3(-5f, 0f, -5f), new Vector3(5f, 0f, 5f), settings)));
            }
            finally { Object.DestroyImmediate(settings); }
        }

        [Test]
        public void BakeSession_MissingSettings_ReportsThatSpecifically()
        {
            string fail;
            var session = VisibilityBakeSession.TryBegin(
                new Vector3(-1f, 0f, -1f), new Vector3(1f, 0f, 1f),
                null, 3f, 1.5f, out fail);
            Assert.IsNull(session);
            Assert.That(fail, Does.Contain("设置"), "应先报缺设置资产：" + fail);
            Assert.That(fail, Does.Not.Contain("玩法包"), "缺设置时不得误报成缺玩法包（会误导用户去装错东西）");
        }

        [Test]
        public void TimelineWindow_WithoutTable_WritesStatus_WithoutThrowingOrComputing()
        {
            var winType = typeof(StealthTimelineWindow);
            // EditorWindow.CreateInstance(Type) 是 protected，走 ScriptableObject.CreateInstance(Type)
            var win = (UnityEditor.EditorWindow)ScriptableObject.CreateInstance(winType);
            Assert.NotNull(win, "前置：窗口实例应创建成功");
            try
            {
                // 显断掉资产引用再走 Reload：库里的表资产可能存在（刚烘焙过），
                // 若靠「资产恰好不在」当前提，本用例就随现场状态时红时绿。
                winType.GetField("_tableAsset", Priv).SetValue(win, null);
                winType.GetField("_settings", Priv).SetValue(win, null);
                winType.GetMethod("Reload", Priv).Invoke(win, null);

                var status = winType.GetField("_status", Priv).GetValue(win) as string;
                Assert.False(string.IsNullOrEmpty(status), "降级时状态必须有内容，不能留空白界面");
                Assert.That(status, Does.Contain("表"), "状态应说明是表不可用：" + status);

                // 无表时的重算只该写状态并返回：不画热区、不在 UI 层现算判定（DESIGN §4 红线）
                Assert.DoesNotThrow(() => winType.GetMethod("Recompute", Priv).Invoke(win, null));
                Assert.IsNull(winType.GetField("_meshDanger", Priv).GetValue(win),
                    "无表时不得产出热区网格");
                Assert.IsNull(winType.GetField("_field", Priv).GetValue(win),
                    "无表时不得跑出可达传播");
            }
            finally
            {
                // 必须销毁：OnEnable 挂了 SceneView.duringSceneGui，留着会污染后续用例的重绘
                Object.DestroyImmediate(win);
            }
        }
    }
}
