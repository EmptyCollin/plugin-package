using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Collin.Stretch.EditorTooling
{
    /// <summary>
    /// 实例主面板（AGENTS.md §0.1 原则1）：结构（共享 Profile：轴/边界/段类型）+
    /// 实例状态（每段倍数）在同一 Inspector 内编辑，改动即时重建预览。
    /// 边界可在面板直接输入（越界自动重排，段类型/倍数跟随），也可在 Scene 视图拖拽菱形把手。
    /// 结构写入 Profile 资产、倍数写入组件序列化字段（原则2 的 Model/实例分离）。
    /// </summary>
    [CustomEditor(typeof(StretchInstance))]
    public class StretchInstanceEditor : Editor
    {
        private StretchInstance Inst => (StretchInstance)target;
        private Vector2 _scroll;
        private int _insertPct = 50;

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

            _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.MaxHeight(400));

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

            // ── 边界（中间可改，越界自动重排；首尾用端点对齐） ──
            var src = Inst.OriginalMesh != null ? Inst.OriginalMesh
                : (Inst.Filter != null ? Inst.Filter.sharedMesh : null);
            if (profile.sliceBounds.Count >= 2)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("边界", GUILayout.Width(38));
                bool dirtyBound = false;
                for (int i = 0; i < profile.sliceBounds.Count; i++)
                {
                    var prev = GUI.enabled;
                    GUI.enabled = i > 0 && i < profile.sliceBounds.Count - 1;
                    float cur = profile.sliceBounds[i];
                    float nv = EditorGUILayout.FloatField(cur, GUILayout.Width(64));
                    if (!Mathf.Approximately(nv, cur) && !float.IsNaN(nv) && !float.IsInfinity(nv))
                    {
                        profile.sliceBounds[i] = nv;   // 不夹相邻——交给归一重排
                        dirtyBound = true;
                    }
                    GUI.enabled = prev;
                }
                EditorGUILayout.EndHorizontal();
                if (dirtyBound)
                {
                    Undo.RecordObjects(new Object[] { profile, Inst }, "Stretch Edit Bounds");
                    Inst.NormalizeBounds();
                    EditorUtility.SetDirty(profile);
                    EditorUtility.SetDirty(Inst);
                    Rebuild();
                }
            }

            // ── 段行：范围 + 类型 + 倍数 + 删边界（一行一段） ──
            var repeats = Inst.EffectiveRepeats();
            for (int i = 0; i < profile.segmentTypes.Count; i++)
            {
                float lo = profile.sliceBounds[i], hi = profile.sliceBounds[i + 1];
                bool repeatable = profile.TypeAt(i) == SegmentType.Repeatable;
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField($"段{i} [{lo:F2}→{hi:F2}]", GUILayout.Width(128));
                EditorGUI.BeginChangeCheck();
                var nt = (SegmentType)EditorGUILayout.EnumPopup(profile.TypeAt(i), GUILayout.Width(84));
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(profile, "Stretch Segment Type");
                    profile.segmentTypes[i] = nt;
                    EditorUtility.SetDirty(profile);
                    Inst.SyncRepeats();
                    EditorUtility.SetDirty(Inst);
                    Rebuild();
                    break; // 本帧数据已变，下帧再画
                }
                var prevG = GUI.enabled;
                GUI.enabled = repeatable;
                EditorGUI.BeginChangeCheck();
                int r = Mathf.Max(1, EditorGUILayout.IntField(repeats[i], GUILayout.Width(34)));
                if (EditorGUI.EndChangeCheck() && repeatable && r != repeats[i])
                {
                    Undo.RecordObject(Inst, "Stretch Repeat");
                    repeats[i] = r;
                    Inst.repeats = repeats;
                    EditorUtility.SetDirty(Inst);
                    Rebuild();
                }
                GUI.enabled = prevG;
                if (i > 0 && GUILayout.Button("删", GUILayout.Width(30)))
                {
                    Undo.RecordObjects(new Object[] { profile, Inst }, "Stretch Remove Bound");
                    if (profile.RemoveBound(i + 1))
                    {
                        Inst.SyncRepeats();
                        EditorUtility.SetDirty(profile);
                        EditorUtility.SetDirty(Inst);
                        Rebuild();
                    }
                    EditorGUILayout.EndHorizontal();
                    break;
                }
                EditorGUILayout.EndHorizontal();
            }

            // ── 插入边界 ──
            EditorGUILayout.BeginHorizontal();
            _insertPct = EditorGUILayout.IntSlider("插入 %", _insertPct, 5, 95, GUILayout.Width(180));
            if (GUILayout.Button("插入", GUILayout.Width(50)))
            {
                float baseMin = profile.sliceBounds[0];
                float baseMax = profile.sliceBounds[profile.sliceBounds.Count - 1];
                float coord = Mathf.Lerp(baseMin, baseMax, _insertPct / 100f);
                Undo.RecordObjects(new Object[] { profile, Inst }, "Stretch Insert Bound");
                if (profile.InsertBound(coord) >= 0)
                {
                    Inst.SyncRepeats();
                    EditorUtility.SetDirty(profile);
                    EditorUtility.SetDirty(Inst);
                    Rebuild();
                }
            }
            if (src != null && GUILayout.Button("端点对齐", GUILayout.Width(70)))
            {
                Undo.RecordObject(profile, "Stretch Realign Ends");
                profile.sliceBounds[0] = StretchMeshBuilder.BoundsMin(src, profile.axis);
                profile.sliceBounds[profile.sliceBounds.Count - 1] = StretchMeshBuilder.BoundsMax(src, profile.axis);
                EditorUtility.SetDirty(profile);
                Rebuild();
            }
            EditorGUILayout.EndHorizontal();

            // ── 状态反馈 ──
            if (src != null)
            {
                float factor = profile.TotalFactor(
                    StretchMeshBuilder.BoundsMin(src, profile.axis),
                    StretchMeshBuilder.BoundsMax(src, profile.axis), Inst.EffectiveRepeats());
                var shown = Inst.Filter != null ? Inst.Filter.sharedMesh : null;
                EditorGUILayout.LabelField($"总长因子 {factor:F2} · 结果 {(shown != null ? shown.vertexCount : 0)} 顶点",
                    EditorStyles.miniLabel);
            }
            string reason = Inst.FailReason();
            if (reason != null)
            {
                EditorGUILayout.HelpBox(reason + "（修复前保持原网格）", MessageType.Warning);
            }

            EditorGUILayout.EndScrollView();

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("还原原网格"))
                {
                    Undo.RecordObject(Inst, "Stretch Restore");
                    Inst.Restore();
                    SceneView.RepaintAll();
                }
                if (GUILayout.Button("重建预览")) Rebuild();
            }
            EditorGUILayout.LabelField("提示：Scene 视图可拖青色菱形把手调边界；越界自动重排。",
                EditorStyles.miniLabel);

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
            var src = Inst.OriginalMesh;
            var shown = Inst.Filter != null ? Inst.Filter.sharedMesh : null;
            StretchSceneHandles.Draw(Inst, profile, src, shown);
            if (src != null) DrawBoundaryHandles(profile, src);
        }

        /// <summary>
        /// 中间边界的拖拽把手。结果坐标 R(b)=b+ExtensionBelow(b) 对源坐标斜率恒为该边界下方段倍数
        /// （R'(b_i)=rep_{i-1}，逐段线性），故 dSource = dWorld / rep 精确。越界由 NormalizeBounds 重排。
        /// </summary>
        private void DrawBoundaryHandles(StretchProfile profile, Mesh src)
        {
            var tr = Inst.transform;
            int ax = (int)profile.axis;
            Vector3 axisLocal = ax == 0 ? Vector3.right : (ax == 1 ? Vector3.up : Vector3.forward);
            var axisWorld = tr.TransformDirection(axisLocal).normalized;
            // 把手位移是世界距离，边界是局部坐标：除以该轴损失缩放（如栈桥 scale=100）
            float axisScale = Mathf.Max(1e-6f, tr.lossyScale[ax]);
            var repeats = Inst.EffectiveRepeats();
            var center = src.bounds.center;
            bool changed = false;

            for (int i = 1; i < profile.sliceBounds.Count - 1; i++)
            {
                float b = profile.sliceBounds[i];
                float r = b + profile.ExtensionBelow(b, repeats);
                var p = center;
                p[ax] = r;
                var world = tr.TransformPoint(p);
                float size = HandleUtility.GetHandleSize(world) * 0.055f;

                Handles.color = new Color(0.2f, 0.9f, 1f, 0.95f);
                EditorGUI.BeginChangeCheck();
                var fmh_232_60_639292468737010591 = Quaternion.identity; var newPos = Handles.FreeMoveHandle(world, size,
                    Vector3.zero, Handles.DotHandleCap);
                if (EditorGUI.EndChangeCheck())
                {
                    float dWorld = Vector3.Dot(newPos - world, axisWorld);
                    if (Mathf.Abs(dWorld) > 1e-6f)
                    {
                        if (!changed)
                        {
                            Undo.RecordObjects(new Object[] { profile, Inst }, "Stretch Drag Bound");
                            changed = true;
                        }
                        int repBelow = Mathf.Max(1, repeats[Mathf.Min(i - 1, repeats.Length - 1)]);
                        profile.sliceBounds[i] = b + dWorld / (axisScale * repBelow);
                    }
                }
            }

            if (changed)
            {
                Inst.NormalizeBounds();
                EditorUtility.SetDirty(profile);
                EditorUtility.SetDirty(Inst);
                Inst.Rebuild();
            }
        }
    }
}
