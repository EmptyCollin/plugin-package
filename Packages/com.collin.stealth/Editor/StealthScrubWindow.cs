using Collin.Core;
using Collin.Core.Stealth;
using UnityEditor;
using UnityEngine;

namespace Collin.Stealth.EditorTooling
{
    /// <summary>
    /// 编辑态回溯窗口（M4）：一条 T 周期滑动条实时看 NPC 位置，无需进入 Play。
    /// <para>这是「设计辅助台」能力在本包内的最小可用形态（DESIGN 特性 2 的第 2 项）：
    /// 编辑器非 Play 态没有帧循环，所以拖动滑块必须经 <see cref="IStealthScrubDriver"/>
    /// 显式把全部位姿推入场景，并<b>主动重绘 Scene 视图</b>——
    /// 这正是 M1 把位姿做成「时间的纯函数」换来的能力。</para>
    /// <para>只经 core 接口取服务（不引用任何 feature 包类型），保持 §5 的依赖纪律。</para>
    /// </summary>
    internal sealed class StealthScrubWindow : EditorWindow
    {
        float _t;
        bool _playing;
        double _lastTick;
        string _status = "";

        [MenuItem("Tools/Collin/Stealth/巡逻回溯窗口")]
        static void Open()
        {
            var w = GetWindow<StealthScrubWindow>("巡逻回溯");
            w.minSize = new Vector2(320f, 190f);
        }

        void OnEnable() => EditorApplication.update += OnEditorUpdate;
        void OnDisable() => EditorApplication.update -= OnEditorUpdate;

        /// <summary>
        /// 自动播放推进。<b>编辑态与 Play 态都要能推进</b>：本窗口的价值就是「不 Play 也能看走位」，
        /// 所以不能因编辑器状态提前返回。计时基准只在开始播放时归零一次，
        /// 绝不能在 OnGUI 里刷新（OnGUI 每次重绘都跑，dt 会永远≈0 而一动不动）。
        /// </summary>
        void OnEditorUpdate()
        {
            if (!_playing) return;

            var driver = Driver();
            var world = World();
            var maxT = world != null ? world.MaxCycleTime : 0f;
            if (driver == null || maxT <= 0f)
            {
                _playing = false;
                return;
            }

            var now = EditorApplication.timeSinceStartup;
            var dt = now - _lastTick;
            _lastTick = now;
            if (dt <= 0.0 || dt > 0.5) return;   // 首次/卡顿帧跳过，避免一步跳满周期

            Push(driver, Mathf.Repeat(_t + (float)dt, maxT));
            Repaint();
        }

        static IStealthScrubDriver Driver()
        {
            return Services.TryGet<IStealthScrubDriver>(out var d) ? d : null;
        }

        static IPatrolWorld World()
        {
            return Services.TryGet<IPatrolWorld>(out var w) ? w : null;
        }

        /// <summary>
        /// 推送一次 t。<b>必须显式重绘 Scene 视图</b>：编辑态没有帧循环，
        /// 只改 transform 不会自动刷新画面，用户会以为拖动滑块没反应。
        /// </summary>
        void Push(IStealthScrubDriver driver, float t)
        {
            _t = t;
            driver.ScrubTo(_t);
            SceneView.RepaintAll();

            var world = World();
            var actors = world != null ? world.Actors.Count : 0;
            _status = "已推送 t=" + _t.ToString("0.00") + "（" + actors + " 个单位位姿已更新并重绘）";
        }

        void OnGUI()
        {
            var driver = Driver();
            var world = World();

            if (driver == null || world == null)
            {
                EditorGUILayout.HelpBox(
                    "未取到巡逻服务。\n"
                    + "· 场景里要有 StealthClock（示例场景已自带）。\n"
                    + "· 服务随域重载清空：重开本窗口，或改动一次场景物体即可恢复。",
                    MessageType.Info);
                return;
            }

            var maxT = world.MaxCycleTime;
            if (maxT <= 0f)
            {
                EditorGUILayout.HelpBox("场景内没有巡逻单位（PatrolAgent）。", MessageType.Warning);
                return;
            }

            var clock = world.ClockState;
            EditorGUILayout.LabelField("T 周期（最大）", maxT.ToString("0.00") + " 秒");
            EditorGUILayout.LabelField("时钟模式", clock.IsManual ? "Manual（回溯中）" : "Auto（Play 驱动）");
            EditorGUILayout.LabelField("单位数", world.Actors.Count.ToString());

            EditorGUILayout.Space();
            var newT = EditorGUILayout.Slider(
                new GUIContent("t = " + _t.ToString("0.00")), _t, 0f, maxT);
            if (!Mathf.Approximately(newT, _t)) Push(driver, newT);

            EditorGUILayout.Space();
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(_playing ? "停止" : "自动播放"))
            {
                _playing = !_playing;
                _lastTick = EditorApplication.timeSinceStartup;   // 只在开始播放时设基准
            }
            using (new EditorGUI.DisabledScope(!clock.IsManual))
            {
                if (GUILayout.Button("回到 t=0")) Push(driver, 0f);
            }
            if (GUILayout.Button("恢复自动"))
            {
                _playing = false;
                driver.ResumeAuto();
                SceneView.RepaintAll();
                _status = "已恢复 Auto：编辑态不再驱动位姿。";
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();
            DrawActorTable(world, _t);

            if (!string.IsNullOrEmpty(_status)) EditorGUILayout.HelpBox(_status, MessageType.None);
        }

        void DrawActorTable(IPatrolWorld world, float t)
        {
            EditorGUILayout.LabelField("单位位姿（t=" + t.ToString("0.00") + "）", EditorStyles.miniBoldLabel);
            var actors = world.Actors;
            for (var i = 0; i < actors.Count; i++)
            {
                var a = actors[i];
                if (!world.TryPoseAtWorld(a.actorId, t, out var pos, out var yaw))
                {
                    EditorGUILayout.LabelField(a.displayName, "无路线根");
                    continue;
                }
                EditorGUILayout.LabelField(a.displayName,
                    "(" + pos.x.ToString("0.0") + ", " + pos.z.ToString("0.0") + ")  朝 " + yaw.ToString("0") + "°");
            }
        }
    }
}
