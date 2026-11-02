using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Collin.Stretch.EditorTooling
{
    /// <summary>
    /// 实例主面板（AGENTS.md §0.1 原则1）：结构（共享 Profile：轴/边界/段类型）+
    /// 实例状态（每段倍数）在同一 Inspector 内编辑，改动即时重建预览。
    /// 结构写入 Profile 资产、倍数写入组件序列化字段（原则2 的 Model/实例分离）。
    /// </summary>
    [CustomEditor(typeof(StretchInstance))]
    public class StretchInstanceEditor : Editor
    {
        private StretchInstance Inst => (StretchInstance)target;
        private int _newBoundPct = 50;
        private bool _showStruct = true;

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            var profile = Inst.profile;

            if (profile == null)
            {
                EditorGUILayout.HelpBox("未指定 Profile。用菜单 Tools/Collin/Stretch 拉伸/新建拉伸参数 创建，或拖入资产。", MessageType.Info);
                EditorGUILayout.PropertyField(serializedObject.FindProperty("profile"));
                serializedObject.ApplyModifiedProperties();
                return;
            }

            // ── 轴 ──
            EditorGUI.BeginChangeCheck();
            var axis = (StretchAxis)EditorGUILayout.EnumPopup("拉伸轴（局部）", profile.axis);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(profile, "Stretch Axis");
                profile.axis = axis;
                EditorUtility.SetDirty(profile);
                Rebuild();
            }

            // ── 实例状态：倍数（组件序列化，原则2） ──
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("实例状态 · 复制倍数", EditorStyles.boldLabel);
            var repeats = Inst.EffectiveRepeats();
            bool dirtyRepeats = false;
            for (int i = 0; i < profile.segmentTypes.Count; i++)
            {
                bool repeatable = profile.TypeAt(i) == SegmentType.Repeatable;
                float lo = profile.sliceBounds[i], hi = profile.sliceBounds[i + 1];
                EditorGUILayout.BeginHorizontal();
                var prev = GUI.enabled;
                GUI.enabled = repeatable;
                EditorGUI.BeginChangeCheck();
                int r = Mathf.Max(1, EditorGUILayout.IntField(
                    $"段{i} [{lo:F2}→{hi:F2}] {(repeatable ? "×复制" : "固定")}", repeats[i]));
                if (EditorGUI.EndChangeCheck() && repeatable && r != repeats[i])
                {
                    Undo.RecordObject(Inst, "Stretch Repeat");
                    repeats[i] = r;
                    dirtyRepeats = true;
                }
                GUI.enabled = prev;
                EditorGUILayout.EndHorizontal();
            }
            if (dirtyRepeats)
            {
                EditorUtility.SetDirty(Inst);
                Rebuild();
            }

            // ── 结构（共享 Profile，折叠区） ──
            EditorGUILayout.Space(4);
            _showStruct = EditorGUILayout.Foldout(_showStruct, "共享结构（本 Profile 的所有实例受影响）", true);
            if (_showStruct)
            {
                using (new EditorGUILayout.VerticalScope(GUI.skin.box))
                {
                    StretchStructUI.DrawStructure(profile, () =>
                    {
                        Inst.SyncRepeats();
                        EditorUtility.SetDirty(Inst);
                        Rebuild();
                    }, Inst.Filter, out _);

                    EditorGUILayout.LabelField("资产：" + AssetDatabase.GetAssetPath(profile),
                        EditorStyles.miniLabel);
                }
            }

            // ── 状态反馈 ──
            EditorGUILayout.Space(4);
            var src = Inst.OriginalMesh != null ? Inst.OriginalMesh
                : (Inst.Filter != null ? Inst.Filter.sharedMesh : null);
            if (src != null)
            {
                float factor = profile.TotalFactor(
                    StretchMeshBuilder.BoundsMin(src, profile.axis),
                    StretchMeshBuilder.BoundsMax(src, profile.axis), repeats);
                var shown = Inst.Filter != null ? Inst.Filter.sharedMesh : null;
                EditorGUILayout.LabelField($"总长因子 {factor:F2} · 结果 {(shown != null ? shown.vertexCount : 0)} 顶点");
            }
            string reason = Inst.FailReason();
            if (reason != null)
            {
                EditorGUILayout.HelpBox(reason + "（修复前保持原网格）", MessageType.Warning);
            }

            EditorGUILayout.Space(2);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("还原原网格"))
                {
                    Undo.RecordObject(Inst, "Stretch Restore");
                    Inst.Restore();
                    SceneView.RepaintAll();
                }
                if (GUILayout.Button("重建预览"))
                {
                    Rebuild();
                }
            }

            EditorGUILayout.Space(2);
            EditorGUILayout.LabelField("提示：边界应落在楼层/桥面之间的空白带；贴边几何请内缩 ε（见 DESIGN R-1）。",
                EditorStyles.wordWrappedMiniLabel);

            serializedObject.ApplyModifiedProperties();
        }

        private void Rebuild()
        {
            if (!Inst.Rebuild())
            {
                // 失败原因已在面板 HelpBox 显示，不写 error 污染门禁（§1.6b）
            }
            SceneView.RepaintAll();
        }

        private void OnSceneGUI()
        {
            var profile = Inst.profile;
            if (profile == null) return;
            var filter = Inst.Filter;
            var shown = filter != null ? filter.sharedMesh : null;
            // 辅助线画在"结果网格"上：源网格用于取原始几何，结果用于对齐显示位置
            StretchSceneHandles.Draw(Inst, profile, Inst.OriginalMesh, shown);
        }
    }

    /// <summary>结构编辑 UI（实例面板与 Profile 资产面板共用，避免两套真源）。</summary>
    public static class StretchStructUI
    {
        /// <summary>绘制边界/段类型编辑。返回是否改动过结构。</summary>
        public static bool DrawStructure(StretchProfile profile, System.Action onChanged,
            MeshFilter refFilter, out bool structureChanged)
        {
            structureChanged = false;
            float min = profile.sliceBounds.Count > 0 ? profile.sliceBounds[0] : 0f;
            float max = profile.sliceBounds.Count > 0 ? profile.sliceBounds[profile.sliceBounds.Count - 1] : 1f;
            var src = refFilter != null ? refFilter.sharedMesh : null;

            if (profile.sliceBounds.Count < 2)
            {
                if (GUILayout.Button("按当前选中模型初始化边界"))
                {
                    var go = Selection.activeGameObject;
                    var f = go != null ? go.GetComponent<MeshFilter>() : null;
                    if (f != null && f.sharedMesh != null)
                    {
                        Undo.RecordObject(profile, "Stretch Init");
                        profile.InitFromBounds(
                            StretchMeshBuilder.BoundsMin(f.sharedMesh, profile.axis),
                            StretchMeshBuilder.BoundsMax(f.sharedMesh, profile.axis));
                        EditorUtility.SetDirty(profile);
                        onChanged?.Invoke();
                        structureChanged = true;
                    }
                }
                return structureChanged;
            }

            // 段类型
            for (int i = 0; i < profile.segmentTypes.Count; i++)
            {
                var t = profile.segmentTypes[i];
                EditorGUILayout.BeginHorizontal();
                EditorGUI.BeginChangeCheck();
                var nt = (SegmentType)EditorGUILayout.EnumPopup($"段{i} 类型", t);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(profile, "Stretch Segment Type");
                    profile.segmentTypes[i] = nt;
                    EditorUtility.SetDirty(profile);
                    onChanged?.Invoke();
                    structureChanged = true;
                }
                if (i > 0 && GUILayout.Button("删尾边界", GUILayout.Width(78)))
                {
                    Undo.RecordObject(profile, "Stretch Remove Bound");
                    if (profile.RemoveBound(i + 1))
                    {
                        EditorUtility.SetDirty(profile);
                        onChanged?.Invoke();
                        structureChanged = true;
                    }
                }
                EditorGUILayout.EndHorizontal();
            }

            // 插入边界（按源包围盒归一化百分比）
            EditorGUILayout.BeginHorizontal();
            int pct = EditorGUILayout.IntSlider("插入边界 %", 50, 5, 95);
            if (GUILayout.Button("插入", GUILayout.Width(56)))
            {
                float baseMin = profile.sliceBounds[0];
                float baseMax = profile.sliceBounds[profile.sliceBounds.Count - 1];
                float coord = Mathf.Lerp(baseMin, baseMax, pct / 100f);
                Undo.RecordObject(profile, "Stretch Insert Bound");
                int idx = profile.InsertBound(coord);
                if (idx < 0)
                {
                    coord = Mathf.Clamp(coord, baseMin + 1e-3f, baseMax - 1e-3f);
                    idx = profile.InsertBound(coord);
                }
                if (idx >= 0)
                {
                    EditorUtility.SetDirty(profile);
                    onChanged?.Invoke();
                    structureChanged = true;
                }
            }
            EditorGUILayout.EndHorizontal();

            if (src != null && GUILayout.Button("端点对齐选中模型包围盒"))
            {
                Undo.RecordObject(profile, "Stretch Realign Ends");
                profile.sliceBounds[0] = StretchMeshBuilder.BoundsMin(src, profile.axis);
                profile.sliceBounds[profile.sliceBounds.Count - 1] = StretchMeshBuilder.BoundsMax(src, profile.axis);
                EditorUtility.SetDirty(profile);
                onChanged?.Invoke();
                structureChanged = true;
            }
            _ = min; _ = max;
            return structureChanged;
        }
    }
}
