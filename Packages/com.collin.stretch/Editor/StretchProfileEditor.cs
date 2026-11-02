using UnityEditor;
using UnityEngine;

namespace Collin.Stretch.EditorTooling
{
    /// <summary>
    /// StretchProfile 资产面板：轴切换、边界列表（增删改 + 自动分段）、段类型/倍数、预览按钮。
    /// 边界值用**归一化位置 [0,1]**编辑（相对源包围盒），避免用户记绝对坐标；
    /// 应用时换算回局部坐标写入 sliceBounds。
    /// </summary>
    [CustomEditor(typeof(StretchProfile))]
    public class StretchProfileEditor : Editor
    {
        private StretchProfile Profile => (StretchProfile)target;
        private int _newBoundPct = 50;

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("axis"));
            if (EditorGUI.EndChangeCheck())
            {
                serializedObject.ApplyModifiedProperties();
                // 切轴只重算预览；边界若与新轴包围盒不符，Validate 会在预览时告警，
                // 用户可点下方"端点对齐"按钮手动修正——不清段，保护既有工作。
                RefreshPreviewIfAny();
            }

            EditorGUILayout.Space(4);
            if (Profile.sliceBounds.Count < 2)
            {
                EditorGUILayout.HelpBox("尚未初始化：选中一个模型后点击下方按钮按包围盒生成首尾边界。", MessageType.Info);
                if (GUILayout.Button("按当前选中模型初始化边界")) ReinitFromSelection();
                serializedObject.ApplyModifiedProperties();
                return;
            }

            // 源包围盒参考（选中对象提供时才有；仅展示）
            var (min, max, ok) = CurrentSourceSpan();
            if (ok)
            {
                EditorGUILayout.LabelField($"源包围盒({Profile.axis})：{min:F2} … {max:F2}");
            }

            EditorGUILayout.LabelField("段列表", EditorStyles.boldLabel);
            for (int i = 0; i < Profile.segments.Count; i++)
            {
                var seg = Profile.segments[i];
                float lo = Profile.sliceBounds[i], hi = Profile.sliceBounds[i + 1];
                EditorGUILayout.BeginVertical(GUI.skin.box);
                EditorGUILayout.LabelField($"段 {i}  [{lo:F2} → {hi:F2}]  长 {hi - lo:F2}", GUILayout.Width(320));

                EditorGUI.BeginChangeCheck();
                seg.type = (SegmentType)EditorGUILayout.EnumPopup("类型", seg.type);
                if (seg.type == SegmentType.Repeatable)
                {
                    seg.repeat = Mathf.Max(1, EditorGUILayout.IntField("复制倍数", seg.repeat));
                }
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(Profile, "Stretch Segment");
                    EditorUtility.SetDirty(Profile);
                    RefreshPreviewIfAny();
                }

                EditorGUILayout.BeginHorizontal();
                if (i + 1 < Profile.sliceBounds.Count - 1 && GUILayout.Button("删除尾边界", GUILayout.Width(90)))
                {
                    Undo.RecordObject(Profile, "Stretch Remove Bound");
                    Profile.sliceBounds.RemoveAt(i + 1);
                    Profile.segments.RemoveAt(i);
                    EditorUtility.SetDirty(Profile);
                    RefreshPreviewIfAny();
                }
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
            }

            // 插入新边界：按归一化百分比
            EditorGUILayout.Space(4);
            EditorGUILayout.BeginHorizontal(GUI.skin.box);
            _newBoundPct = EditorGUILayout.IntSlider("插入边界位置 %", _newBoundPct, 5, 95);
            if (GUILayout.Button("插入", GUILayout.Width(60)))
            {
                float baseMin = Profile.sliceBounds[0];
                float baseMax = Profile.sliceBounds[Profile.sliceBounds.Count - 1];
                if (!ok)
                {
                    // 未选中模型时用资产自身端点为参考
                    baseMin = Profile.sliceBounds[0];
                    baseMax = Profile.sliceBounds[Profile.sliceBounds.Count - 1];
                }
                else
                {
                    baseMin = min;
                    baseMax = max;
                    Profile.sliceBounds[0] = min;
                    Profile.sliceBounds[Profile.sliceBounds.Count - 1] = max;
                }
                float coord = Mathf.Lerp(baseMin, baseMax, _newBoundPct / 100f);
                Undo.RecordObject(Profile, "Stretch Insert Bound");
                int idx = Profile.InsertBound(coord);
                if (idx < 0)
                {
                    // 端点漂移导致 InsertBound 拒绝：夹一次再试
                    coord = Mathf.Clamp(coord, baseMin + 1e-3f, baseMax - 1e-3f);
                    idx = Profile.InsertBound(coord);
                }
                if (idx >= 0)
                {
                    EditorUtility.SetDirty(Profile);
                    RefreshPreviewIfAny();
                }
            }
            EditorGUILayout.EndHorizontal();

            if (ok && GUILayout.Button("边界端点对齐当前模型包围盒"))
            {
                Undo.RecordObject(Profile, "Stretch Realign Ends");
                Profile.sliceBounds[0] = min;
                Profile.sliceBounds[Profile.sliceBounds.Count - 1] = max;
                EditorUtility.SetDirty(Profile);
                RefreshPreviewIfAny();
            }

            EditorGUILayout.Space(6);
            if (GUILayout.Button("在场景物体上预览（若有 StretchPreview）", GUILayout.Height(26)))
            {
                RefreshPreviewIfAny(true);
            }

            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField("提示：边界应落在楼层/桥面之间的空白带，跨段三角形不会被复制（见 DESIGN R-1）。",
                EditorStyles.wordWrappedMiniLabel);

            serializedObject.ApplyModifiedProperties();
        }

        private (float min, float max, bool ok) CurrentSourceSpan()
        {
            var go = Selection.activeGameObject;
            if (go == null) return (0, 0, false);
            var filter = go.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null) return (0, 0, false);
            var preview = go.GetComponent<StretchPreview>();
            var src = preview != null && preview.OriginalMesh != null ? preview.OriginalMesh : filter.sharedMesh;
            return (StretchMeshBuilder.BoundsMin(src, Profile.axis),
                    StretchMeshBuilder.BoundsMax(src, Profile.axis), true);
        }

        private void ReinitFromSelection()
        {
            var (min, max, ok) = CurrentSourceSpan();
            if (!ok) return;
            Undo.RecordObject(Profile, "Stretch Init");
            Profile.InitFromBounds(min, max);
            EditorUtility.SetDirty(Profile);
        }

        private void RefreshPreviewIfAny(bool forceEnable = false)
        {
            var go = Selection.activeGameObject;
            if (go == null) return;
            var preview = go.GetComponent<StretchPreview>();
            if (preview == null) return;
            if (!preview.Active && forceEnable)
            {
                Undo.RecordObject(preview, "Stretch Preview On");
                preview.enabled = true;
            }
            else
            {
                preview.Rebuild();
            }
            SceneView.RepaintAll();
        }

        /// <summary>Scene 视图挂点：选中带 Profile 的对象（或本资产）时画辅助线。</summary>
        [InitializeOnLoadMethod]
        private static void HookSceneGui()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
            SceneView.duringSceneGui += OnSceneGUI;
        }

        private static void OnSceneGUI(SceneView view)
        {
            var (preview, profile, source) = ResolveTarget();
            if (preview == null) return;
            StretchSceneHandles.Draw(preview, profile, source);
        }

        /// <summary>优先取场景里挂着 Profile 的预览对象；其次资产被直接选中时找引用它的物体。</summary>
        internal static (StretchPreview, StretchProfile, Mesh) ResolveTarget()
        {
            var obj = Selection.activeObject;
            if (obj is GameObject go)
            {
                var p = go.GetComponent<StretchPreview>();
                if (p != null && p.profile != null)
                {
                    var filter = p.GetComponent<MeshFilter>();
                    var src = p.OriginalMesh != null ? p.OriginalMesh
                        : (filter != null ? filter.sharedMesh : null);
                    if (src != null) return (p, p.profile, src);
                }
            }
            else if (obj is StretchProfile prof)
            {
                // 资产被选中：在打开的场景里找引用者
                foreach (var comp in Object.FindObjectsByType<StretchPreview>(
                             FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    if (comp.profile == prof)
                    {
                        var filter = comp.GetComponent<MeshFilter>();
                        var src = comp.OriginalMesh != null ? comp.OriginalMesh
                            : (filter != null ? filter.sharedMesh : null);
                        if (src != null) return (comp, prof, src);
                    }
                }
            }
            return (null, null, null);
        }
    }
}
