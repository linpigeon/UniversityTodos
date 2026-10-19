using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UniversityTodos.Models;

namespace UniversityTodos.Services;

/// <summary>CSV/TSV 导入的一行解析结果。</summary>
/// <param name="RowNumber">原始行号（1 基，含表头）。</param>
/// <param name="Raw">原文（截断）。</param>
/// <param name="Item">解析成功时的条目。</param>
/// <param name="Error">失败原因。</param>
public sealed record ScheduleImportRow(int RowNumber, string Raw, ScheduleItem? Item, string? Error);

/// <summary>
/// 课表模板导入（CSV / TSV / 文本）。
/// <para>
/// 为什么需要：教务导入只支持正方网页。换学校、页面改版、或只想补几门课时，
/// 用户至少还能按模板手写一份表格导入。
/// </para>
/// <para>
/// 列顺序（与导入说明一致）：
/// <c>课程名,教师,地点,星期,起始节,结束节,起始周,结束周,单双周,开始时间,结束时间</c>。
/// 时间列可留空——那时按节次时间表推导。首行含「课程/星期/节」时视为表头跳过。
/// </para>
/// </summary>
public static class ScheduleImport
{
    private static readonly Dictionary<string, int> WeekdayNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["周一"] = 1, ["星期一"] = 1,
        ["周二"] = 2, ["星期二"] = 2,
        ["周三"] = 3, ["星期三"] = 3,
        ["周四"] = 4, ["星期四"] = 4,
        ["周五"] = 5, ["星期五"] = 5,
        ["周六"] = 6, ["星期六"] = 6,
        ["周日"] = 7, ["星期日"] = 7, ["周天"] = 7, ["星期天"] = 7,
        ["一"] = 1, ["二"] = 2, ["三"] = 3, ["四"] = 4,
        ["五"] = 5, ["六"] = 6, ["日"] = 7, ["天"] = 7,
    };

    /// <summary>解析整份文本；空行忽略，首行表头跳过。</summary>
    public static List<ScheduleImportRow> Parse(
        string text, int term, IReadOnlyList<PeriodSlot> periods)
    {
        var rows = new List<ScheduleImportRow>();
        if (string.IsNullOrWhiteSpace(text)) return rows;

        var lines = text.Split('\n')
            .Select(l => l.TrimEnd('\r'))
            .Where(l => l.Trim().Length > 0)
            .ToList();

        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var rowNumber = i + 1;

            if (i == 0 && LooksLikeHeader(line))
                continue;

            var cells = SplitRow(line);
            var raw = line.Length > 120 ? line[..120] + "…" : line;

            if (cells.Count < 8)
            {
                rows.Add(new ScheduleImportRow(rowNumber, raw, null,
                    "列数不足（至少需要 课程名/教师/地点/星期/起始节/结束节/起始周/结束周）"));
                continue;
            }

            var name = cells[0].Trim();
            if (name.Length == 0)
            {
                rows.Add(new ScheduleImportRow(rowNumber, raw, null, "课程名为空"));
                continue;
            }

            var weekday = ParseWeekday(cells[3]);
            if (weekday is null)
            {
                rows.Add(new ScheduleImportRow(rowNumber, raw, null, $"星期无法识别：{cells[3]}"));
                continue;
            }

            if (!int.TryParse(cells[4], out var startPeriod) || !int.TryParse(cells[5], out var endPeriod))
            {
                rows.Add(new ScheduleImportRow(rowNumber, raw, null, "节次无法识别"));
                continue;
            }

            if (!int.TryParse(cells[6], out var startWeek) || !int.TryParse(cells[7], out var endWeek))
            {
                rows.Add(new ScheduleImportRow(rowNumber, raw, null, "周次无法识别"));
                continue;
            }

            var parity = ParseParity(cells.Count > 8 ? cells[8] : string.Empty);

            var startRaw = cells.Count > 9 ? cells[9].Trim() : string.Empty;
            var endRaw = cells.Count > 10 ? cells[10].Trim() : string.Empty;

            string startTime;
            string endTime;
            if (startRaw.Length == 0 && endRaw.Length == 0)
            {
                // 时间留空 → 按节次时间表推导（周视图/提醒的同一份口径）。
                var fromPeriods = ResolveFromPeriods(periods, startPeriod, endPeriod);
                if (fromPeriods is null)
                {
                    rows.Add(new ScheduleImportRow(rowNumber, raw, null,
                        $"第 {startPeriod}-{endPeriod} 节不在节次时间表内，且没有填写时间"));
                    continue;
                }
                startTime = fromPeriods.Value.Start;
                endTime = fromPeriods.Value.End;
            }
            else
            {
                var normalizedStart = NormalizeTime(startRaw);
                var normalizedEnd = NormalizeTime(endRaw);
                if (normalizedStart is null || normalizedEnd is null)
                {
                    rows.Add(new ScheduleImportRow(rowNumber, raw, null,
                        "开始/结束时间格式无法识别（应为 HH:mm，且两列都要填）"));
                    continue;
                }
                startTime = normalizedStart;
                endTime = normalizedEnd;
            }

            var item = new ScheduleItem
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = name,
                Teacher = cells[1].Trim(),
                Location = cells[2].Trim(),
                Weekday = weekday.Value,
                StartPeriod = startPeriod,
                EndPeriod = endPeriod,
                StartWeek = startWeek,
                EndWeek = endWeek,
                WeekParity = parity,
                StartTime = startTime,
                EndTime = endTime,
                Term = term,
            };

            try
            {
                ScheduleStore.ValidateOne(item);
                rows.Add(new ScheduleImportRow(rowNumber, raw, item, null));
            }
            catch (ArgumentException ex)
            {
                rows.Add(new ScheduleImportRow(rowNumber, raw, null, ex.Message));
            }
        }

        return rows;
    }

    private static bool LooksLikeHeader(string line) =>
        line.Contains("课程", StringComparison.Ordinal)
        && (line.Contains("星期", StringComparison.Ordinal) || line.Contains("周", StringComparison.Ordinal))
        && line.Contains("节", StringComparison.Ordinal);

    private static int? ParseWeekday(string raw)
    {
        var text = raw.Trim();
        if (int.TryParse(text, out var n) && n is >= 1 and <= 7) return n;
        return WeekdayNames.TryGetValue(text, out var value) ? value : null;
    }

    private static WeekParity ParseParity(string raw)
    {
        var text = raw.Trim();
        if (text.Contains('单')) return WeekParity.Odd;
        if (text.Contains('双')) return WeekParity.Even;
        return WeekParity.Every;
    }

    private static (string Start, string End)? ResolveFromPeriods(
        IReadOnlyList<PeriodSlot> periods, int startPeriod, int endPeriod)
    {
        var start = Timetable.ResolvePeriodTime(periods, startPeriod);
        var end = Timetable.ResolvePeriodTime(periods, endPeriod);
        if (start is null || end is null || startPeriod > endPeriod) return null;
        return (start.Value.StartTime, end.Value.EndTime);
    }

    /// <summary>拆分一行：支持制表符与逗号，字段可用双引号包裹（内部双引号写成两个）。</summary>
    internal static List<string> SplitRow(string line)
    {
        var delimiter = line.Contains('\t') ? '\t' : ',';
        var cells = new List<string>();
        var current = new System.Text.StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (inQuotes)
            {
                if (ch == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Append('"');
                        i++;
                    }
                    else inQuotes = false;
                }
                else current.Append(ch);
            }
            else if (ch == '"') inQuotes = true;
            else if (ch == delimiter)
            {
                cells.Add(current.ToString().Trim());
                current.Clear();
            }
            else current.Append(ch);
        }
        cells.Add(current.ToString().Trim());
        return cells;
    }

    /// <summary>把时间列规范化成 <c>HH:mm</c>（允许 <c>8:00</c>）；非法返回 null。</summary>
    internal static string? NormalizeTime(string raw)
    {
        var text = raw.Trim().Replace('：', ':');
        if (text.Length == 0) return null;
        var parts = text.Split(':');
        if (parts.Length != 2) return null;
        if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var h)
            || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var m))
            return null;
        if (h is < 0 or > 23 || m is < 0 or > 59) return null;
        return $"{h:00}:{m:00}";
    }
}
