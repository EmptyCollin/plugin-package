using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Collin.Stealth.EditorTooling
{
    /// <summary>
    /// 路线根的场景手柄（AGENTS.md §0.1 原则 1「所见即所得」）：
    /// 在 Scene 视图直接拖路点即改路线，改动即时反映到 NPC 走位与折线。
    /// <para>坐标口径（D-09）：手柄按<b>世界位置</b>显示与拖动（直观），
    /// 写回时经 <see cref="PatrolRouteRoot.WorldToLocal"/> 换算成<b>局部坐标</b>存进资产
    /// ——SO 里永远不含世界坐标，可跨场景复用，拖一个根即整条路线搬移。</para>
    /// </summary>
    [CustomEditor(typeof(PatrolRouteRoot))]
    public sealed class PatrolRouteRootEditor : Editor
    {
        static readonly Color LineColor = new Color(0.3f, 0.8f, 1f, 0.95f);
        static readonly Color LoopColor = new Color(0.3f, 0.8f, 1f, 0.45f);
        static readonly Color PointColor = new Color(0.2f, 0.9f, 0.5f);
        static readonly Color HeadColor = new Color(1f, 0.75f, 0.2f);
        static readonly Color AimColor = new Color(1f, 0.8f, 0.3f, 0.85f);

        int _selected = -1;
        bool _layoutChanged;

        public override void OnInspectorGUI()
        {
            var root = (PatrolRouteRoot)target;
            DrawDefaultInspector();

            var route = root.Route;

            EditorGUILayout.Space();
            if (route == null)
            {
                EditorGUILayout.HelpBox("尚未指定 PatrolRoute 资产。可点上方 Patrol Route 字段的选择钮，"
                    + "或右键 Create ▸ Collin ▸ Stealth ▸ Patrol Route 新建。", MessageType.Info);
                return;
            }

            EditorGUILayout.LabelField("T 周期（秒）", route.TotalCycle.ToString("0.000"));
            EditorGUILayout.LabelField("路点数 / 循环方式",
                route.WaypointCount + " · " + route.LoopMode);

            EditorGUILayout.Space();
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("追加路点")) AppendWaypoint(route);
            using (new EditorGUI.DisabledScope(_selected < 0 || _selected >= route.WaypointCount))
            {
                if (GUILayout.Button("删除选中")) DeleteSelected(route);
            }
            EditorGUILayout.EndHorizontal();

            if (GUILayout.Button("从子物体烘焙路点（按名称排序，替换全部）")) BakeFromChildren(root, route);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("路点（局部坐标 · 停留 · 段速）", EditorStyles.miniBoldLabel);

            for (var i = 0; i < route.WaypointCount; i++)
            {
                var isSel = _selected == i;

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField((isSel ? "▶ " : "   ") + i, GUILayout.Width(26f));
                var local = route.GetWaypointPosition(i);
                var newLocal = EditorGUILayout.Vector3Field(string.Empty, local);
                if (GUILayout.Button("选", GUILayout.Width(30f))) _selected = i;
                EditorGUILayout.EndHorizontal();

                if (newLocal != local)
                {
                    Undo.RecordObject(route, "改路点");
                    route.SetWaypointPosition(i, newLocal);
                    EditorUtility.SetDirty(route);
                    _layoutChanged = true;
                }

                var dwell = route.GetStopDuration(i);
                var newDwell = EditorGUILayout.Slider("停留（秒）", dwell, 0f, 10f);
                if (!Mathf.Approximately(newDwell, dwell))
                {
                    Undo.RecordObject(route, "改停留");
                    route.SetStopDuration(i, newDwell);
                    EditorUtility.SetDirty(route);
                    _layoutChanged = true;
                }

                var seg = route.GetSegmentSpeed(i);
                var newSeg = EditorGUILayout.Slider("段速（0=用默认）", seg, 0f, 8f);
                if (!Mathf.Approximately(newSeg, seg))
                {
                    Undo.RecordObject(route, "改段速");
                    route.SetSegmentSpeed(i, newSeg);
                    EditorUtility.SetDirty(route);
                    _layoutChanged = true;
                }
            }

            if (_layoutChanged)
            {
                _layoutChanged = false;
                SceneView.RepaintAll();
            }
        }

        static void AppendWaypoint(PatrolRoute route)
        {
            Undo.RecordObject(route, "追加路点");
            var last = route.WaypointCount > 0
                ? route.GetWaypointPosition(route.WaypointCount - 1)
                : Vector3.zero;
            route.AddWaypoint(last + new Vector3(3f, 0f, 0f));
            EditorUtility.SetDirty(route);
        }

        void DeleteSelected(PatrolRoute route)
        {
            Undo.RecordObject(route, "删除路点");
            if (route.RemoveWaypointAt(_selected))
            {
                _selected = -1;
                EditorUtility.SetDirty(route);
                SceneView.RepaintAll();
            }
        }

        /// <summary>
        /// 从子物体烘焙路点：把根下的<b>纯标记点</b>子物体（无渲染器、无碰撞体）
        /// 按名称排序后的世界坐标换算成局部坐标写入路线。
        /// 用途——策划在场景里摆好一串标记点，一键转成巡逻路线（D-09 的直观性补偿）。
        /// </summary>
        static void BakeFromChildren(PatrolRouteRoot root, PatrolRoute route)
        {
            var marks = new List<Transform>();
            for (var i = 0; i < root.transform.childCount; i++)
            {
                var c = root.transform.GetChild(i);
                if (c.GetComponent<Renderer>() != null || c.GetComponent<Collider>() != null) continue;
                marks.Add(c);
            }

            if (marks.Count == 0)
            {
                Debug.LogWarning("[PatrolRouteRoot] 该根下没有可烘焙的标记点子物体：未做任何改动。"
                    + "请为每个路点建一个空子物体（不要挂 Renderer / Collider）。");
                return;
            }

            marks.Sort((a, b) => string.CompareOrdinal(a.name, b.name));

            Undo.RecordObject(route, "烘焙路点");
            for (var i = route.WaypointCount - 1; i >= 0; i--) route.RemoveWaypointAt(i);
            for (var i = 0; i < marks.Count; i++) route.AddWaypoint(root.WorldToLocal(marks[i].position));
            EditorUtility.SetDirty(route);
            SceneView.RepaintAll();
        }

        void OnSceneGUI()
        {
            var root = (PatrolRouteRoot)target;
            var route = root.Route;
            if (route == null || route.WaypointCount == 0) return;

            var first = root.LocalToWorld(route.GetWaypointPosition(0));
            var prev = first;

            Handles.color = LineColor;
            for (var i = 1; i < route.WaypointCount; i++)
            {
                var p = root.LocalToWorld(route.GetWaypointPosition(i));
                Handles.DrawAAPolyLine(3f, prev, p);
                prev = p;
            }
            if (route.LoopMode == PatrolLoopMode.Loop && route.WaypointCount > 2)
            {
                Handles.color = LoopColor;
                Handles.DrawAAPolyLine(1.5f, prev, first);   // 闭合段用细线区分
            }

            DrawHeadingHandle(root, route);

            for (var i = 0; i < route.WaypointCount; i++)
            {
                var world = root.LocalToWorld(route.GetWaypointPosition(i));
                var size = HandleUtility.GetHandleSize(world) * 0.06f;

                Handles.color = i == _selected ? HeadColor : PointColor;

                EditorGUI.BeginChangeCheck();
                var moved = Handles.FreeMoveHandle(world, size, Vector3.zero, Handles.SphereHandleCap);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(route, "拖路点");
                    route.SetWaypointPosition(i, root.WorldToLocal(moved));
                    EditorUtility.SetDirty(route);
                    _selected = i;
                    Repaint();
                }

                if (i == _selected)
                {
                    Handles.Label(world + Vector3.up * 0.45f,
                        "#" + i + "  停留 " + route.GetStopDuration(i).ToString("0.0") + "s");
                }
            }

            Handles.color = Color.white;
        }

        /// <summary>第一段行进方向的朝向指示线（位置精确折线 + 朝向单独缓动，D-11）。</summary>
        static void DrawHeadingHandle(PatrolRouteRoot root, PatrolRoute route)
        {
            var cycle = route.TotalCycle;
            if (cycle <= 1e-4f) return;

            var step = cycle / Mathf.Max(2, route.WaypointCount * 4);
            for (var t = 0f; t < cycle; t += step)
            {
                var pose = route.EvaluatePose(t);
                if (!pose.HasLeg || pose.isDwelling) continue;

                var origin = root.LocalToWorld(pose.localPosition);
                var rad = pose.localYawDegrees * Mathf.Deg2Rad;
                var dir = new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad));

                Handles.color = AimColor;
                Handles.DrawAAPolyLine(2f, origin, origin + dir * 1.2f);
                return;
            }
        }

        void OnDisable()
        {
            _selected = -1;
        }
    }
}
