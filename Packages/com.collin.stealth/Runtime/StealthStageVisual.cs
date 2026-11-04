using UnityEngine;

namespace Collin.Stealth
{
    /// <summary>
    /// 察觉档位的统一视觉口径（颜色 + 中文名）。
    /// <para>HUD、地面投影、预制件自检共用这一份：颜色若在两处各写一遍，
    /// 就会出现「HUD 说红、地上画黄」这种自相矛盾的可视化，比没有颜色更糟。</para>
    /// </summary>
    public static class StealthStageVisual
    {
        /// <summary>档位配色（巡逻绿 / 疑惑黄 / 警觉红 / 搜索橙）。</summary>
        public static Color ColorOf(StealthDetectionStage stage)
        {
            switch (stage)
            {
                case StealthDetectionStage.Suspicious: return new Color(0.96f, 0.85f, 0.25f);
                case StealthDetectionStage.Alerted: return new Color(0.95f, 0.25f, 0.2f);
                case StealthDetectionStage.Searching: return new Color(0.95f, 0.55f, 0.15f);
                default: return new Color(0.3f, 0.85f, 0.4f);
            }
        }

        /// <summary>档位中文名（HUD 与标签共用）。</summary>
        public static string NameOf(StealthDetectionStage stage)
        {
            switch (stage)
            {
                case StealthDetectionStage.Suspicious: return "疑惑";
                case StealthDetectionStage.Alerted: return "警觉";
                case StealthDetectionStage.Searching: return "搜索";
                default: return "巡逻";
            }
        }
    }
}
