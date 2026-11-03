using UnityEditor;
using UnityEngine;

namespace Collin.Stretch.EditorTooling
{
    /// <summary>
    /// StretchProfile 资产面板：只读结构摘要 + 定位到使用该资产的场景实例。
    /// 编辑主入口在场景实例的 Inspector（§0.1 原则1），本面板不设第二套编辑 UI（避免两套真源）。
    /// </summary>
    [CustomEditor(typeof(StretchProfile))]
    public class StretchProfileEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            var profile = (StretchProfile)target;
            EditorGUILayout.LabelField("共享结构资产（在场景实例的 Inspector 编辑）", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("轴", profile.axis.ToString());
            EditorGUILayout.LabelField("边界", profile.sliceBounds.Count + " 个 · 段 " + profile.segmentTypes.Count + " 个");
            if (profile.sliceBounds.Count >= 2)
            {
                var sb = new System.Text.StringBuilder();
                for (int i = 0; i < profile.segmentTypes.Count; i++)
                {
                    sb.Append(profile.TypeAt(i) == SegmentType.Repeatable ? "R" : "F");
                    if (i < profile.segmentTypes.Count - 1) sb.Append('·');
                }
                EditorGUILayout.LabelField("段类型", sb.ToString());
            }

            EditorGUILayout.Space(6);
            var users = FindObjectsByType<StretchInstance>();
            int found = 0;
            foreach (var inst in users)
            {
                if (inst.profile != profile) continue;
                found++;
                if (GUILayout.Button("选中场景实例：" + inst.name +
                    "（倍数 " + string.Join(",", inst.EffectiveRepeats()) + "）"))
                {
                    Selection.activeGameObject = inst.gameObject;
                    EditorGUIUtility.PingObject(inst.gameObject);
                }
            }
            if (found == 0)
            {
                EditorGUILayout.HelpBox("当前打开场景中没有引用本资产的实例。", MessageType.Info);
            }
        }

        // 辅助线绘制唯一入口 = StretchInstanceEditor.OnSceneGUI（CustomEditor 对选中实例调用）。
        // 本资产面板不再注册 duringSceneGui，避免对同一选中实例重复绘制（§0.1 原则1：编辑真源只在实例）。
    }
}
