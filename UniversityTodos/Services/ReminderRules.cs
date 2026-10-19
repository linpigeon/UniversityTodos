using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UniversityTodos.Models;

namespace UniversityTodos.Services;

/// <summary>提醒类型。</summary>
public enum ReminderKind
{
    /// <summary>临近截止（按设置的提前量）。</summary>
    DueSoon,

    /// <summary>自建事项的显式提醒时间。</summary>
    Explicit,
}

/// <summary>一条命中的提醒。</summary>
/// <param name="Kind">类型。</param>
/// <param name="Item">命中的事项。</param>
/// <param name="Target">提醒针对的时刻（截止 / 显式提醒时间）。</param>
/// <param name="MinutesUntil">距目标还有多少分钟（显式提醒为 0）。</param>
public sealed record ReminderHit(ReminderKind Kind, TodoItem Item, DateTimeOffset Target, int MinutesUntil);

/// <summary>
/// 截止提醒的**纯逻辑**：给定事项、当前时刻与已提醒记录，算出这一轮该提醒谁。
/// <para>
/// 抽成纯函数是为了能离线断言（<c>--jsoncheck</c>）：提醒窗口、改期重提醒、
/// 休眠补发、显式提醒覆盖默认提醒这几条规则都容易写错，且只在真实时间流逝下才暴露。
/// </para>
/// </summary>
public static class ReminderRules
{
    /// <summary>休眠/挂起恢复后的补发窗口：错过不超过这个时长的提醒才补发。</summary>
    public const int CatchUpMinutes = 30;

    /// <summary>是否值得提醒：公告不算待办，已完成的不提醒。</summary>
    public static bool IsActionable(TodoItem item) =>
        item.Kind != TodoKind.Announcement && item.Status != TodoStatus.Completed;

    /// <summary>截止提醒的去重键：id + 截止时刻（改期会变，从而重新提醒）。</summary>
    public static string DueKey(TodoItem item, DateTimeOffset due) =>
        string.Create(CultureInfo.InvariantCulture, $"{item.Id}|{due.ToUnixTimeMilliseconds()}");

    /// <summary>显式提醒的去重键：id + 提醒时刻。</summary>
    public static string ExplicitKey(TodoItem item, DateTimeOffset remindAt) =>
        string.Create(CultureInfo.InvariantCulture, $"{item.Id}|{remindAt.ToUnixTimeMilliseconds()}");

    /// <summary>
    /// 计算本轮应发的提醒。
    /// </summary>
    /// <param name="items">当前全部事项（含已完成的也没关系，内部会过滤）。</param>
    /// <param name="now">当前时刻。</param>
    /// <param name="dueSoonMinutes">临近截止的提前量（分钟）；&lt;= 0 表示关闭该类型。</param>
    /// <param name="remindedDue">已提醒过的截止键集合。</param>
    /// <param name="remindedExplicit">已提醒过的显式键集合。</param>
    /// <param name="lastCheckAt">上一次检查时刻；null 表示首次检查（不做补发）。</param>
    public static List<ReminderHit> Evaluate(
        IEnumerable<TodoItem> items,
        DateTimeOffset now,
        int dueSoonMinutes,
        IReadOnlySet<string> remindedDue,
        IReadOnlySet<string> remindedExplicit,
        DateTimeOffset? lastCheckAt)
    {
        var hits = new List<ReminderHit>();

        foreach (var item in items)
        {
            if (!IsActionable(item)) continue;

            // 显式提醒优先：有 RemindAt 的事项只按它提醒，不再叠加默认截止提醒。
            if (item.RemindAt is { } remindAt)
            {
                var explicitKey = ExplicitKey(item, remindAt);
                if (remindAt <= now && !remindedExplicit.Contains(explicitKey))
                    hits.Add(new ReminderHit(ReminderKind.Explicit, item, remindAt, 0));
                continue;
            }

            if (dueSoonMinutes <= 0) continue;
            if (item.EffectiveDueAt is not { } due) continue;

            var key = DueKey(item, due);
            if (remindedDue.Contains(key)) continue;
            if (!WithinWindow(now, lastCheckAt, due, dueSoonMinutes)) continue;

            var minutes = (int)Math.Ceiling((due - now).TotalMinutes);
            hits.Add(new ReminderHit(ReminderKind.DueSoon, item, due, Math.Max(0, minutes)));
        }

        // 先到期的先提醒，输出稳定。
        return hits
            .OrderBy(h => h.Target)
            .ThenBy(h => h.Item.Id, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// 是否落在提醒窗口内：
    /// <list type="number">
    /// <item>正常窗口：<c>[target - minutesBefore, target]</c>；</item>
    /// <item>补发：上次检查之后跨过了 target，且错过不超过 <see cref="CatchUpMinutes"/>。</item>
    /// </list>
    /// </summary>
    public static bool WithinWindow(
        DateTimeOffset now, DateTimeOffset? lastCheckAt, DateTimeOffset target, int minutesBefore)
    {
        var from = target.AddMinutes(-minutesBefore);
        if (now >= from && now <= target) return true;

        return lastCheckAt is { } last
               && last <= target
               && now > target
               && now - target <= TimeSpan.FromMinutes(CatchUpMinutes);
    }

    /// <summary>已逾期的待办（不含公告、不含已完成）。</summary>
    public static List<TodoItem> Overdue(IEnumerable<TodoItem> items, DateTimeOffset now) =>
        items
            .Where(IsActionable)
            .Where(i => i.EffectiveDueAt is { } due && due < now)
            .OrderBy(i => i.EffectiveDueAt)
            .ThenBy(i => i.Id, StringComparer.Ordinal)
            .ToList();

    /// <summary>今天是否还没发过逾期汇总、且已过配置的汇总时刻。</summary>
    public static bool IsDigestDue(DateTimeOffset now, string? digestTime, string? lastDigestDate)
    {
        var today = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (string.Equals(lastDigestDate, today, StringComparison.Ordinal)) return false;

        var at = ParseClock(digestTime) ?? new TimeSpan(8, 0, 0);
        return now.TimeOfDay >= at;
    }

    /// <summary>解析 <c>HH:mm</c>；非法返回 null。</summary>
    private static TimeSpan? ParseClock(string? value)
    {
        if (!AppSettings.IsValidDigestTime(value)) return null;
        return new TimeSpan((value![0] - '0') * 10 + (value[1] - '0'),
                            (value[3] - '0') * 10 + (value[4] - '0'), 0);
    }

    /// <summary>构造一条提醒通知。</summary>
    public static AppNotification BuildNotification(ReminderHit hit)
    {
        if (hit.Kind == ReminderKind.Explicit)
        {
            return new AppNotification(
                "待办提醒",
                hit.Item.Title,
                hit.Item.EffectiveDueAt is { } due ? $"截止 {due:MM-dd HH:mm}" : "自建待办",
                hit.Item.SourceUrl);
        }

        var title = string.IsNullOrWhiteSpace(hit.Item.CourseName) ? "即将截止" : hit.Item.CourseName;
        var attribution = hit.MinutesUntil <= 0
            ? "已到截止时间"
            : $"还有 {hit.MinutesUntil} 分钟截止";
        return new AppNotification(title, hit.Item.Title, attribution, hit.Item.SourceUrl);
    }
}
