using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Collin.Stretch.EditorTooling
{
    /// <summary>
    /// 实例主面板（AGENTS.md §0.1 原则1）：结构（共享 Profile：轴/边界/段类型）+
    /// 实例状态（每段倍数）在同一 Inspector 内编辑，改动即时重建预览。
    /// 边界每条独立一行、可直接输入（越界自动重排，段类型/倍数跟随），Scene 视图可拖把手。
    /// 布局纪律：每行 using HorizontalScope（异常/跳出也闭合）；数据变更一律延迟到绘制完成后统一应用，
    /// 杜绝"break 未关布局组 → 后续控件挤进同一行"（0.3.2 面板缺陷的根因）。
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

            var src = Inst.OriginalMesh != null ? Inst.OriginalMesh
                : (Inst.Filter != null ? Inst.Filter.sharedMesh : null);

            _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.MaxHeight(460));

            // ── 收集本帧意图（先画完再改数据） ──
            bool doAxis = false;
            var newAxis = profile.axis;
            var newBounds = new List<float>(profile.sliceBounds);
            bool doBounds = false;
            int deleteBoundAt = -1;
            bool doInsert = false, doRealign = false;
            var typeEdits = new List<(int idx, SegmentType t)>();
            var repeatEdits = new List<(int idx, int v)>();

            // ── 轴 + 渲染路径 ──
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginChangeCheck();
                newAxis = (StretchAxis)EditorGUILayout.EnumPopup("拉伸轴（局部）", profile.axis);
                doAxis = EditorGUI.EndChangeCheck();
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                var oldMode = Inst.renderMode;
                var nm = (StretchInstance.RenderModeKind)EditorGUILayout.EnumPopup("渲染路径", oldMode);
                if (nm != oldMode)
                {
                    Undo.RecordObject(Inst, "Stretch Render Mode");
                    Inst.renderMode = nm;
                    EditorUtility.SetDirty(Inst);
                    if (Inst.GetComponent<MeshRenderer>() == null && nm == StretchInstance.RenderModeKind.Chunked)
                        Debug.LogWarning("[Stretch] Chunked 需要本物体有 MeshRenderer+材质；已自动添加");
                    Inst.RebuildNow();
                    SceneView.RepaintAll();
                }
            }
            if (Inst.renderMode == StretchInstance.RenderModeKind.Chunked)
            {
                EditorGUILayout.LabelField("分块实例化：复制份=矩阵，Draw 组数≈块×材质（与实例/份数解耦）。"
                    + "同提交器实例须同材质数组；暂不投影（§8.2 P3）。", EditorStyles.wordWrappedMiniLabel);
            }

            // ── 边界：每条独立一行（滑条+数值） ──
            EditorGUILayout.LabelField("边界（局部坐标，拖动滑条或输入数值）", EditorStyles.boldLabel);
            float bMin = src != null ? StretchMeshBuilder.BoundsMin(src, profile.axis) : 0f;
            float bMax = src != null ? StretchMeshBuilder.BoundsMax(src, profile.axis) : 1f;
            float bSpan = bMax - bMin;
            int n = profile.sliceBounds.Count;
            for (int i = 0; i < n; i++)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    bool middle = i > 0 && i < n - 1;
                    EditorGUILayout.LabelField(
                        i == 0 ? "起点" : (i == n - 1 ? "终点" : ("边界 " + i)),
                        GUILayout.Width(52));
                    var prev = GUI.enabled;
                    GUI.enabled = middle;   // 首尾锁定，改动用"端点对齐"
                    EditorGUI.BeginChangeCheck();
                    float sv = newBounds[i];
                    if (src != null && bSpan > 1e-6f)
                    {
                        // 滑条按源包围盒范围拖动（局部坐标）
                        sv = EditorGUILayout.Slider(sv, bMin, bMax, GUILayout.MinWidth(80));
                    }
                    else
                    {
                        sv = EditorGUILayout.FloatField(sv);
                    }
                    bool sEdited = EditorGUI.EndChangeCheck();
                    if (sEdited && !float.IsNaN(sv) && !float.IsInfinity(sv)
                        && !Mathf.Approximately(sv, newBounds[i]))
                    {
                        newBounds[i] = sv; doBounds = true;
                    }
                    EditorGUI.BeginChangeCheck();
                    float nv = EditorGUILayout.FloatField(newBounds[i], GUILayout.Width(70));
                    bool edited = EditorGUI.EndChangeCheck()
                        && !float.IsNaN(nv) && !float.IsInfinity(nv);
                    GUI.enabled = prev;
                    if (edited) { newBounds[i] = nv; doBounds = true; }
                    GUILayout.FlexibleSpace();
                    if (middle && GUILayout.Button("删除", GUILayout.Width(40)))
                    {
                        deleteBoundAt = i;
                    }
                }
            }

            // ── 插入 / 端点对齐 ──
            using (new EditorGUILayout.HorizontalScope())
            {
                _insertPct = EditorGUILayout.IntSlider("按 % 插入", _insertPct, 5, 95);
                doInsert = GUILayout.Button("插入", GUILayout.Width(48));
                doRealign = src != null && GUILayout.Button("端点对齐", GUILayout.Width(64));
            }

            // ── 段：每段一行（类型 + 倍数） ──
            EditorGUILayout.Space(2);
            EditorGUILayout.LabelField("段（类型对全部实例生效；倍数是本实例状态）", EditorStyles.boldLabel);
            var repeats = Inst.EffectiveRepeats();
            int segN = Mathf.Min(profile.segmentTypes.Count, n - 1);
            for (int i = 0; i < segN; i++)
            {
                float lo = profile.sliceBounds[i], hi = profile.sliceBounds[i + 1];
                bool repeatable = profile.TypeAt(i) == SegmentType.Repeatable;
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(
                        string.Format("[{0:F2} → {1:F2}] 长{2:F2}", lo, hi, hi - lo),
                        GUILayout.Width(150));
                    EditorGUI.BeginChangeCheck();
                    var nt = (SegmentType)EditorGUILayout.EnumPopup(profile.TypeAt(i), GUILayout.Width(84));
                    if (EditorGUI.EndChangeCheck()) typeEdits.Add((i, nt));
                    var prevG = GUI.enabled;
                    GUI.enabled = repeatable;
                    EditorGUI.BeginChangeCheck();
                    int rv = Mathf.Max(1, EditorGUILayout.IntField(repeats[i], GUILayout.Width(34)));
                    rv = Mathf.Max(1, EditorGUILayout.IntSlider(rv, 1, 32, GUILayout.MinWidth(80)));
                    if (EditorGUI.EndChangeCheck() && rv != repeats[i]) repeatEdits.Add((i, rv));
                    GUI.enabled = prevG;
                    GUILayout.Label(repeatable ? "×复制" : "固定", EditorStyles.miniLabel, GUILayout.Width(38));
                }
            }

            // ── 状态反馈 ──
            if (src != null)
            {
                float factor = profile.TotalFactor(
                    StretchMeshBuilder.BoundsMin(src, profile.axis),
                    StretchMeshBuilder.BoundsMax(src, profile.axis), repeats);
                var shown = Inst.Filter != null ? Inst.Filter.sharedMesh : null;
                EditorGUILayout.LabelField(
                    string.Format("总长因子 {0:F2} · 结果 {1} 顶点", factor, shown != null ? shown.vertexCount : 0),
                    EditorStyles.miniLabel);
            }
            string reason = Inst.FailReason();
            if (reason != null)
            {
                EditorGUILayout.HelpBox(reason + "（保持原网格；点「端点对齐」或重选拉伸轴可自动修复边界）", MessageType.Warning);
            }
            if (GUILayout.Button("烘焙为静态网格（大规模/份数固定推荐）", GUILayout.Width(240)))
            {
                StretchBakeCommand.BakeOneAndReport(Inst.gameObject);
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
            EditorGUILayout.LabelField("提示：Scene 视图可拖青色把手调边界；越界自动重排。",
                EditorStyles.miniLabel);

            ApplyEdits(profile, doAxis, newAxis, doBounds, newBounds, deleteBoundAt,
                doInsert, doRealign, src, typeEdits, repeatEdits);

            serializedObject.ApplyModifiedProperties();
        }

        /// <summary>统一应用本帧意图：任意一项命中记录一次 Undo，最后一次性重建。</summary>
        private void ApplyEdits(StretchProfile profile, bool doAxis, StretchAxis newAxis,
            bool doBounds, List<float> newBounds, int deleteBoundAt,
            bool doInsert, bool doRealign, Mesh src,
            List<(int idx, SegmentType t)> typeEdits, List<(int idx, int v)> repeatEdits)
        {
            if (!doAxis && !doBounds && deleteBoundAt < 0 && !doInsert && !doRealign
                && typeEdits.Count == 0 && repeatEdits.Count == 0)
            {
                return;
            }

            Undo.RecordObjects(new Object[] { profile, Inst }, "Stretch Edit");

            if (doAxis)
            {
                var oldAxis = profile.axis;
                profile.axis = newAxis;
                // 边界是轴坐标：切轴后首尾重设新轴包围盒端点，中间 clamp 进新范围（下方 NormalizeBounds 统一重排）
                if (src != null && oldAxis != newAxis && profile.sliceBounds.Count >= 2)
                {
                    float nMin = StretchMeshBuilder.BoundsMin(src, newAxis);
                    float nMax = StretchMeshBuilder.BoundsMax(src, newAxis);
                    profile.sliceBounds[0] = nMin;
                    profile.sliceBounds[profile.sliceBounds.Count - 1] = nMax;
                    for (int i = 1; i < profile.sliceBounds.Count - 1; i++)
                    {
                        profile.sliceBounds[i] = Mathf.Clamp(profile.sliceBounds[i], nMin + 1e-4f, nMax - 1e-4f);
                    }
                }
            }

            if (doBounds)
            {
                for (int i = 0; i < newBounds.Count && i < profile.sliceBounds.Count; i++)
                {
                    profile.sliceBounds[i] = newBounds[i];
                }
            }

            if (typeEdits.Count > 0)
            {
                foreach (var e in typeEdits)
                {
                    if (e.idx >= 0 && e.idx < profile.segmentTypes.Count) profile.segmentTypes[e.idx] = e.t;
                }
            }

            if (doInsert)
            {
                float baseMin = profile.sliceBounds[0];
                float baseMax = profile.sliceBounds[profile.sliceBounds.Count - 1];
                float coord = Mathf.Lerp(baseMin, baseMax, Mathf.Clamp(_insertPct, 5, 95) / 100f);
                profile.InsertBound(coord);
            }

            if (deleteBoundAt > 0) profile.RemoveBound(deleteBoundAt);

            if (doRealign && src != null)
            {
                profile.sliceBounds[0] = StretchMeshBuilder.BoundsMin(src, profile.axis);
                profile.sliceBounds[profile.sliceBounds.Count - 1] = StretchMeshBuilder.BoundsMax(src, profile.axis);
            }

            Inst.NormalizeBounds();   // 越界/乱序统一重排（0.3.2 语义，0.3.3 重写时丢失，现补回）
            Inst.SyncRepeats();
            var reps = Inst.repeats;
            foreach (var e in repeatEdits)
            {
                if (e.idx >= 0 && e.idx < reps.Length) reps[e.idx] = e.v;
            }
            Inst.repeats = reps;

            EditorUtility.SetDirty(profile);
            EditorUtility.SetDirty(Inst);
            Rebuild();
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
        /// 中间边界的拖拽把手。位移换算 dSource = dWorld / (lossyScale×repBelow) 精确；
        /// 越界由 NormalizeBounds 重排。
        /// </summary>
        private void DrawBoundaryHandles(StretchProfile profile, Mesh src)
        {
            var tr = Inst.transform;
            int ax = (int)profile.axis;
            Vector3 axisLocal = ax == 0 ? Vector3.right : (ax == 1 ? Vector3.up : Vector3.forward);
            var axisWorld = tr.TransformDirection(axisLocal).normalized;
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
                var newPos = Handles.FreeMoveHandle(world, size,
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
