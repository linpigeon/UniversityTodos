using System;

namespace UniversityTodos.Services;

/// <summary>
/// 待办编辑框的时间选择逻辑（与控件解耦，便于离线自检）。
/// </summary>
public static class DateTimeSelection
{
    /// <summary>
    /// 把「是否启用 + 日期 + 时分」合成为本地 <see cref="DateTimeOffset"/>。
    /// 未启用时返回 null（合法）；启用但日期/时间被清空时通过 <paramref name="error"/> 报错。
    /// </summary>
    public static DateTimeOffset? Compose(
        string fieldName,
        bool enabled,
        DateTime? date,
        TimeSpan? time,
        out string? error)
    {
        error = null;
        if (!enabled) return null;

        if (date is not { } selectedDate)
        {
            error = $"{fieldName}：请选择日期。";
            return null;
        }

        if (time is not { } selectedTime)
        {
            error = $"{fieldName}：请选择时间。";
            return null;
        }

        // 只保留到分钟；秒/毫秒归零，与提醒规则按分钟比较的口径一致。
        var local = DateTime.SpecifyKind(
            selectedDate.Date.AddHours(selectedTime.Hours).AddMinutes(selectedTime.Minutes),
            DateTimeKind.Local);
        return new DateTimeOffset(local);
    }

    /// <summary>
    /// 当前时间之后的下一个 30 分钟刻度（跨天时日期一并加一天，例如 23:45 → 次日 00:00）。
    /// </summary>
    public static DateTime NextHalfHour(DateTime now)
    {
        return new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0)
            .AddMinutes(30 - now.Minute % 30);
    }
}
