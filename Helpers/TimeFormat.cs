using System;

namespace QuickLook.Plugin.GitViewer.Helpers
{
    /// <summary>提交列表和分支列表里显示的相对时间。</summary>
    internal static class TimeFormat
    {
        public static string Relative(DateTimeOffset when)
        {
            if (when == default(DateTimeOffset))
                return string.Empty;

            var delta = DateTimeOffset.Now - when;

            // 时钟偏差，或者提交日期本来就是未来时间。
            if (delta.TotalSeconds < 0)
                delta = TimeSpan.Zero;

            if (delta.TotalSeconds < 60)
                return Translate.Get("TimeJustNow", "just now");
            if (delta.TotalMinutes < 60)
                return Plural((int)delta.TotalMinutes, "TimeMinute", "{0} minute ago", "TimeMinutes", "{0} minutes ago");
            if (delta.TotalHours < 24)
                return Plural((int)delta.TotalHours, "TimeHour", "{0} hour ago", "TimeHours", "{0} hours ago");
            if (delta.TotalDays < 30)
                return Plural((int)delta.TotalDays, "TimeDay", "{0} day ago", "TimeDays", "{0} days ago");
            if (delta.TotalDays < 365)
                return Plural((int)(delta.TotalDays / 30), "TimeMonth", "{0} month ago", "TimeMonths", "{0} months ago");

            return Plural((int)(delta.TotalDays / 365), "TimeYear", "{0} year ago", "TimeYears", "{0} years ago");
        }

        /// <summary>按数量挑选单数或复数措辞。中文两者写法相同，英文才有区别。</summary>
        private static string Plural(int value, string oneId, string oneFailsafe, string manyId, string manyFailsafe)
        {
            var format = value == 1 ? Translate.Get(oneId, oneFailsafe) : Translate.Get(manyId, manyFailsafe);
            return string.Format(format, value);
        }
    }
}
