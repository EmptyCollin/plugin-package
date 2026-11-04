using System.Collections.Generic;
using UnityEngine;

namespace Collin.Stealth
{
    /// <summary>结算卡片里的一行「标签 — 数值」。</summary>
    public struct StealthResultRow
    {
        public string Label;
        public string Value;

        public StealthResultRow(string label, string value)
        {
            Label = label;
            Value = value;
        }
    }

    /// <summary>
    /// 结算界面的<b>数据层</b>（纯函数，不碰 GUI）。
    /// <para>存在理由（用户实机反馈「给胜利/失败做个简单UI」）：原先胜负只有一行大字横幅，
    /// 玩家不知道自己<b>为什么</b>输、也看不到用时与被察觉程度，重开等于无反馈重来。
    /// 把「显示什么」与「怎么画」分开，是为了让文案与数据能<b>脱离 Play 态断言</b>——
    /// 本包实测过 Play 态探针不可靠，只靠肉眼进 Play 验收等于没验收。</para>
    /// </summary>
    public static class StealthResultModel
    {
        /// <summary>标题：胜利 / 失败二值，绝不出现第三种含糊说法。</summary>
        public static string TitleOf(StealthGameOutcome outcome)
        {
            switch (outcome)
            {
                case StealthGameOutcome.Escaped: return "撤离成功";
                case StealthGameOutcome.Caught: return "行动失败";
                default: return string.Empty;
            }
        }

        /// <summary>强调色：与档位视觉同色系（绿=达成，红=被抓）。</summary>
        public static Color AccentOf(StealthGameOutcome outcome)
        {
            return outcome == StealthGameOutcome.Escaped
                ? new Color(0.35f, 0.9f, 0.45f)
                : new Color(1f, 0.34f, 0.28f);
        }

        /// <summary>秒数 → <c>m:ss</c>（不用 <c>TimeSpan</c> 是为了避免 60 分钟以上出现 <c>1:00:00</c> 的宽度抖动）。</summary>
        public static string ClockOf(float seconds)
        {
            var s = Mathf.Max(0f, seconds);
            var m = Mathf.FloorToInt(s / 60f);
            var rem = Mathf.FloorToInt(s - m * 60f);
            return m + ":" + rem.ToString("00");
        }

        /// <summary>
        /// 组装结算卡片。
        /// </summary>
        /// <param name="outcome">对局结果；<see cref="StealthGameOutcome.Playing"/> 时返回 <b>null</b>（不显示）。</param>
        /// <param name="elapsedSeconds">本局用时。</param>
        /// <param name="suspicionPeak">本局察觉度峰值 [0,1]。</param>
        /// <param name="guardsAlerted">本局进入过警觉档的哨兵数。</param>
        /// <param name="exitReached">撤离区是否已达成（胜利归因用）。</param>
        public static StealthResultData Build(StealthGameOutcome outcome, float elapsedSeconds,
                                              float suspicionPeak, int guardsAlerted, bool exitReached)
        {
            if (outcome == StealthGameOutcome.Playing) return null;

            var won = outcome == StealthGameOutcome.Escaped;
            var rows = new List<StealthResultRow>(4)
            {
                new StealthResultRow("用时", ClockOf(elapsedSeconds)),
                new StealthResultRow("察觉峰值", Mathf.RoundToInt(Mathf.Clamp01(suspicionPeak) * 100f) + "%"),
                new StealthResultRow("惊动哨兵", guardsAlerted + " 名"),
            };
            if (won) rows.Add(new StealthResultRow("撤离点", exitReached ? "已抵达" : "未抵达"));

            return new StealthResultData
            {
                Title = TitleOf(outcome),
                Subtitle = won
                    ? "绕开所有视线，成功脱离"
                    : "被哨兵锁定到顶格 —— 离开视线还有机会挣脱",
                Accent = AccentOf(outcome),
                Rows = rows,
                PrimaryHint = "再来一局（R）",
                SecondaryHint = "关闭（Esc）",
            };
        }
    }

    /// <summary>结算卡片的一帧完整数据（由 <see cref="StealthResultModel.Build"/> 产出）。</summary>
    public sealed class StealthResultData
    {
        public string Title;
        public string Subtitle;
        public Color Accent;
        public List<StealthResultRow> Rows;
        public string PrimaryHint;
        public string SecondaryHint;
    }
}
