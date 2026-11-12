using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using UniversityTodos.Models;

namespace UniversityTodos.Services;

public static class ExamService
{
    public const string ExamPagePath = "/kwgl/kscx_cxXsksxxIndex.html";
    private const string ExamPageUrl = ExamPagePath + "?gnmkdm=N358105&layout=default";
    private const string ExamQueryPath = ExamPagePath + "?doType=query&gnmkdm=N358105";
    private const int PageSize = 100;
    private const int MaxPages = 100;

    internal sealed record ExamQueryPage(
        IReadOnlyList<ExamInfo> Items, int TotalCount, int TotalPages, int CurrentPage);

    public static string BuildExamPageUrl(string loginUrl)
    {
        var baseUri = new Uri(loginUrl);
        return $"{baseUri.GetLeftPart(UriPartial.Authority)}{ExamPageUrl}";
    }

    public static bool IsExamPage(Uri? uri) =>
        uri?.AbsolutePath.Equals(ExamPagePath, StringComparison.OrdinalIgnoreCase) == true;

    internal static string SemesterCode(int semester) => semester switch
    {
        1 => "3",
        2 => "12",
        3 => "16",
        _ => throw new ArgumentOutOfRangeException(nameof(semester)),
    };

    public static async Task<ExamFetchResult> FetchExamsAsync(
        NativeWebDialog dialog,
        PageReadyGate pageGate,
        int academicYear,
        int semester,
        TimeSpan? timeout = null,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        if (academicYear is < 2000 or > 2100)
            return new ExamFetchResult(false, "学年应在 2000–2100 之间", []);
        if (semester is < 1 or > 3)
            return new ExamFetchResult(false, "学期应在 1–3 之间", []);

        var deadline = DateTimeOffset.Now + (timeout ?? TimeSpan.FromMinutes(2));
        while (DateTimeOffset.Now < deadline)
        {
            ct.ThrowIfCancellationRequested();
            if (!pageGate.HasCompletedNavigation)
            {
                await Task.Delay(250, ct).ConfigureAwait(true);
                continue;
            }

            var path = await JwCaptureService.TryEvalAsync(dialog, "location.pathname", ct).ConfigureAwait(true);
            if (string.Equals(path, ExamPagePath, StringComparison.OrdinalIgnoreCase)) break;
            await Task.Delay(250, ct).ConfigureAwait(true);
        }

        if (DateTimeOffset.Now >= deadline)
            return new ExamFetchResult(false, "请先登录教务系统并进入「考试信息查询」页面", []);

        JwCaptureService.Log($"exam: 页面就绪，开始读取 {academicYear} 学年第 {semester} 学期");
        var allItems = new List<ExamInfo>();
        var totalPages = 1;
        for (var page = 1; page <= totalPages && page <= MaxPages; page++)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report(totalPages > 1 ? $"正在读取第 {page}/{totalPages} 页考试信息…" : "正在读取考试信息…");
            var json = await FetchPageAsync(dialog, pageGate, academicYear, semester, page, deadline, ct)
                .ConfigureAwait(true);
            if (json is null)
                return new ExamFetchResult(false, "考试信息读取失败，请确认教务登录状态后重试", []);

            ExamQueryPage parsed;
            try
            {
                parsed = ParsePage(json, academicYear, semester);
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException)
            {
                return new ExamFetchResult(false, "考试接口返回格式无法识别，请确认已进入考试信息查询页", []);
            }

            allItems.AddRange(parsed.Items);
            totalPages = parsed.TotalPages;
        }

        var items = allItems
            .GroupBy(item => (item.CourseCode, item.ExamName, item.StartAt, item.Location, item.SeatNumber))
            .Select(group => group.First())
            .OrderBy(item => item.StartAt)
            .ThenBy(item => item.CourseName)
            .ToList();
        var termText = $"{academicYear}-{academicYear + 1} 学年第 {semester} 学期";
        return new ExamFetchResult(true,
            items.Count == 0 ? $"{termText}暂无考试安排" : $"已获取 {items.Count} 场考试（{termText}）",
            items);
    }

    private static async Task<string?> FetchPageAsync(
        NativeWebDialog dialog,
        PageReadyGate pageGate,
        int academicYear,
        int semester,
        int page,
        DateTimeOffset deadline,
        CancellationToken ct)
    {
        if (!pageGate.HasCompletedNavigation) return null;
        var started = await JwCaptureService.TryEvalAsync(
            dialog, BuildFetchScript(academicYear, semester, page), ct).ConfigureAwait(true);
        if (started is null)
        {
            JwCaptureService.Log($"exam: 第 {page} 页请求脚本启动失败");
            return null;
        }

        while (DateTimeOffset.Now < deadline)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Delay(200, ct).ConfigureAwait(true);
            if (!pageGate.HasCompletedNavigation) return null;
            var raw = await JwCaptureService.TryEvalAsync(dialog, ReadFetchResultScript, ct).ConfigureAwait(true);
            if (string.IsNullOrEmpty(raw)) continue;
            try
            {
                using var doc = JsonDocument.Parse(raw);
                var root = doc.RootElement;
                var ok = root.GetProperty("ok").GetBoolean();
                var status = root.GetProperty("status").GetInt32();
                var text = root.GetProperty("text").GetString() ?? string.Empty;
                JwCaptureService.Log($"exam: 第 {page} 页 HTTP {status}，ok={ok}，返回 {text.Length} 字符");
                return ok ? text : null;
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException)
            {
                JwCaptureService.Log($"exam: 第 {page} 页结果外壳无法解析：{ex.GetType().Name}");
                return null;
            }
        }
        JwCaptureService.Log($"exam: 第 {page} 页请求超时");
        return null;
    }

    internal static string BuildFetchScript(int academicYear, int semester, int page) => $$"""
        (()=>{
          var p=new URLSearchParams();
          p.set('xnm','{{academicYear}}');
          p.set('xqm','{{SemesterCode(semester)}}');
          p.set('ksmcdmb_id','');
          p.set('kch','');p.set('kc','');p.set('ksrq','');p.set('kkbm_id','');
          p.set('_search','false');p.set('nd',String(Date.now()));
          p.set('queryModel.showCount','{{PageSize}}');
          p.set('queryModel.currentPage','{{page}}');
          p.set('queryModel.sortName',' ');p.set('queryModel.sortOrder','asc');
          p.set('time','{{page}}');
          window.__utExamPage={done:false,ok:false,status:0,text:''};
          fetch('{{ExamQueryPath}}',{
            method:'POST',credentials:'same-origin',
            headers:{'Content-Type':'application/x-www-form-urlencoded;charset=UTF-8','X-Requested-With':'XMLHttpRequest'},
            body:p.toString()
          }).then(function(r){return r.text().then(function(t){
            window.__utExamPage={done:true,ok:r.ok,status:r.status,text:t};
          });}).catch(function(e){
            window.__utExamPage={done:true,ok:false,status:0,text:String(e)};
          });
          return 'started';
        })()
        """;

    private const string ReadFetchResultScript = """
        (()=>{
          var r=window.__utExamPage;
          if(!r||!r.done)return '';
          window.__utExamPage=null;
          return JSON.stringify(r);
        })()
        """;

    internal static ExamQueryPage ParsePage(string json, int academicYear, int semester)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var items = new List<ExamInfo>();
        if (root.TryGetProperty("items", out var rows) && rows.ValueKind == JsonValueKind.Array)
        {
            foreach (var row in rows.EnumerateArray())
            {
                var courseName = Text(row, "kcmc");
                if (courseName.Length == 0 || !TryParseExamTime(Text(row, "kssj"), out var start, out var end))
                    continue;
                items.Add(new ExamInfo
                {
                    AcademicYear = academicYear,
                    Semester = semester,
                    CourseName = courseName,
                    CourseCode = Text(row, "kch"),
                    ExamName = Text(row, "ksmc"),
                    ExamMethod = Text(row, "ksfs"),
                    Location = Text(row, "cdmc"),
                    Campus = Text(row, "cdxqmc") is { Length: > 0 } campus ? campus : Text(row, "xqmc"),
                    SeatNumber = Text(row, "zwh"),
                    TeachingClass = Text(row, "jxbmc"),
                    College = Text(row, "kkxy"),
                    RetakeFlag = Text(row, "cxbj"),
                    Remark = Text(row, "bz"),
                    StartAt = start,
                    EndAt = end,
                });
            }
        }

        return new ExamQueryPage(items,
            Int(root, "totalCount", items.Count),
            Int(root, "totalPage", 1),
            Int(root, "currentPage", 1));
    }

    internal static bool TryParseExamTime(string raw, out DateTime start, out DateTime end)
    {
        start = default;
        end = default;
        var match = Regex.Match(raw.Trim(),
            @"^(\d{4}-\d{2}-\d{2})\((\d{2}:\d{2})-(\d{2}:\d{2})\)$");
        if (!match.Success) return false;
        if (!DateTime.TryParseExact(match.Groups[1].Value + " " + match.Groups[2].Value,
                "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out start))
            return false;
        if (!DateTime.TryParseExact(match.Groups[1].Value + " " + match.Groups[3].Value,
                "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out end))
            return false;
        return start < end;
    }

    private static string Text(JsonElement row, string name) =>
        row.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()?.Trim() ?? string.Empty
            : string.Empty;

    private static int Int(JsonElement root, string name, int fallback)
    {
        if (!root.TryGetProperty(name, out var value)) return fallback;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)) return number;
        if (value.ValueKind == JsonValueKind.String
            && int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number))
            return number;
        return fallback;
    }
}
