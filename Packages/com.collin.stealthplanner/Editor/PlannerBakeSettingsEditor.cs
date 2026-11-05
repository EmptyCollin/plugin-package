using UnityEditor;
using UnityEngine;

namespace Collin.StealthPlanner.EditorTooling
{
    /// <summary>
    /// <see cref="PlannerBakeSettings"/> 的检视面板增强：把 DESIGN §6.8 要求的
    /// <b>参数敏感性估算</b>直接画在参数旁边。
    /// <para>存在理由：格长与 Δt 对成本的影響是<b>非线性且不直观</b>的
    /// （格数 ∝ 1/格长²，桶数 ∝ 1/Δt），只给两个滑条让策划凭感觉调，
    /// 实际后果就是「盲烘一次、卡编辑器几分钟」。本面板把派生量即时算出来，
    /// 超阈值时明确警告，<b>但不阻断、不改写用户的值</b>（§0.1.1 所见即所得）。</para>
    /// <para>诚实边界：本阶段只展示<b>当前已能确定</b>的派生量（时间桶数、相位轮数、输出规模）。
    /// 活动格数依赖场景 footprint，要到 M1 烘焙器能枚举可活动格时才能算，
    /// 因此这里<b>不预留假数字</b>，只标注「待 M1 补」。</para>
    /// </summary>
    [CustomEditor(typeof(PlannerBakeSettings))]
    internal sealed class PlannerBakeSettingsEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            DrawDefaultInspector();

            var s = (PlannerBakeSettings)target;
            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("参数敏感性（DESIGN §6.8）", EditorStyles.boldLabel);

            var buckets = s.BucketsPerPhase;
            EditorGUILayout.LabelField("相位窗内时间桶数", buckets.ToString("N0") + " 桶");
            EditorGUILayout.LabelField("相位轮数上限 N", s.MaxPhases + " 轮（决定是否漏解的是它，不是 P）");
            EditorGUILayout.LabelField("输出规模", "短解 ≤ " + s.MaxSolutions + " 条 · 无解 Top-K = " + s.FallbackTopK);
            EditorGUILayout.LabelField("活动格数", "待 M1（需枚举可活动 footprint）");

            if (buckets > PlannerBakeSettings.MaxAlignedBuckets)
            {
                EditorGUILayout.HelpBox(
                    "时间桶数已达 " + buckets.ToString("N0") + "，超过对齐上限 " +
                    PlannerBakeSettings.MaxAlignedBuckets +
                    "。建议增大 Δt 或减小相位窗，否则首烘会显著变慢。", MessageType.Warning);
            }
            else if (s.CellSize <= 0.1f)
            {
                EditorGUILayout.HelpBox(
                    "格长 ≤ 0.1m 会让格数按 1/格长² 增长（0.5m → 0.1m 即 25 倍），" +
                    "烘焙耗时与表体积同步放大。", MessageType.Info);
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
}
