using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using UniversityTodos.Models;

namespace UniversityTodos.Services;

public static class GradeService
{
    public const string GradePagePath = "/cjcx/cjcx_cxDgXscj.html";
    public const string PlanPagePath = "/jxzxjhgl/jxzxjhck_cxJxzxjhckIndex.html";
    private const string GradeQueryPath = "/cjcx/cjcx_cxXsgrcj.html?doType=query&gnmkdm=N305005";
    private const string PlanQueryPath = "/jxzxjhgl/jxzxjhck_cxJxzxjhckIndex.html?doType=query";
    private const int PageSize = 100;
    private const int MaxPages = 100;

    public static string BuildGradePageUrl(string loginUrl) =>
        $"{new Uri(loginUrl).GetLeftPart(UriPartial.Authority)}{GradePagePath}?gnmkdm=N305005&layout=default";

    public static string BuildPlanPageUrl(string loginUrl) =>
        $"{new Uri(loginUrl).GetLeftPart(UriPartial.Authority)}{PlanPagePath}?gnmkdm=N153540&layout=default";

    public static bool IsGradePage(Uri? uri) => uri?.AbsolutePath.Equals(GradePagePath, StringComparison.OrdinalIgnoreCase) == true;
    public static bool IsPlanPage(Uri? uri) => uri?.AbsolutePath.Equals(PlanPagePath, StringComparison.OrdinalIgnoreCase) == true;

    public static async Task<GradeFetchResult> FetchAsync(
        NativeWebDialog dialog,
        PageReadyGate pageGate,
        string loginUrl,
        int academicYear,
        int semester,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        try
        {
            progress?.Report("正在读取本学期成绩…");
            var grades = await FetchGradesAsync(
                dialog, pageGate, loginUrl, academicYear, semester, ct).ConfigureAwait(true);
            progress?.Report("正在统计全部学年学分与绩点…");
            var allGrades = await FetchGradesAsync(
                dialog, pageGate, loginUrl, null, null, ct).ConfigureAwait(true);
            progress?.Report("正在读取专业执行计划…");
            var studentPlan = allGrades.FirstOrDefault(item => item.MajorId.Length > 0);
            var plan = await FetchPlanAsync(dialog, pageGate, loginUrl, studentPlan, ct).ConfigureAwait(true);
            var stats = ComputeStats(allGrades);
            stats.MinimumGraduationCredits = plan.MinimumCredits;
            stats.PlannedCourseCount = plan.CourseCount;
            return new GradeFetchResult(true,
                $"已获取 {grades.Count} 条本学期成绩，累计 {stats.EarnedCredits:0.##} 学分",
                grades, stats);
        }
        catch (JsonException)
        {
            return new GradeFetchResult(false, "成绩或执行计划返回格式无法识别，请确认教务登录状态", [], new AcademicStats());
        }
        catch (InvalidOperationException ex)
        {
            return new GradeFetchResult(false, ex.Message, [], new AcademicStats());
        }
    }

    private static async Task<IReadOnlyList<GradeInfo>> FetchGradesAsync(
        NativeWebDialog dialog,
        PageReadyGate pageGate,
        string loginUrl,
        int? academicYear,
        int? semester,
        CancellationToken ct)
    {
        if (!await NavigateToAsync(dialog, pageGate, BuildGradePageUrl(loginUrl), GradePagePath, ct)
            .ConfigureAwait(true))
            throw new InvalidOperationException("请先登录教务系统并进入学生成绩查询页");

        var allItems = new List<GradeInfo>();
        var totalPages = 1;
        for (var page = 1; page <= totalPages && page <= MaxPages; page++)
        {
            ct.ThrowIfCancellationRequested();
            var parameters = new Dictionary<string, string>
            {
                ["xnm"] = academicYear?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                ["xqm"] = semester switch { 1 => "3", 2 => "12", 3 => "16", _ => string.Empty },
                ["sfzgcj"] = string.Empty,
                ["kcbj"] = string.Empty,
                ["pkey"] = string.Empty,
            };
            var json = await FetchTextAsync(
                dialog, pageGate, GradeQueryPath, parameters, page, ct).ConfigureAwait(true);
            var parsed = ParseGradePage(json, academicYear ?? 0, semester ?? 0);
            allItems.AddRange(parsed.Items);
            totalPages = parsed.TotalPages;
        }
        return allItems;
    }

    private static async Task<(double MinimumCredits, int CourseCount)> FetchPlanAsync(
        NativeWebDialog dialog, PageReadyGate pageGate, string loginUrl, GradeInfo? studentPlan,
        CancellationToken ct)
    {
        if (!await NavigateToAsync(dialog, pageGate, BuildPlanPageUrl(loginUrl), PlanPagePath, ct)
            .ConfigureAwait(true))
            throw new InvalidOperationException("请先登录教务系统并进入教学执行计划页");
        const string readFiltersScript = """
            (()=>{
              function read(name){
                var e=document.querySelector('[name="'+name+'"]')||document.getElementById(name);
                return e&&e.value?e.value:'';
              }
              return JSON.stringify({jg_id:read('jg_id'),njdm_id:read('njdm_id'),dlbs:read('dlbs'),zyh_id:read('zyh_id'),currentPage_cx:read('currentPage_cx')});
            })()
            """;
        var filtersJson = await JwCaptureService.TryEvalAsync(dialog, readFiltersScript, ct).ConfigureAwait(true);
        JwCaptureService.Log($"plan filters: 页面条件读取{(filtersJson is null ? "失败" : "成功")}");
        if (filtersJson is null) throw new InvalidOperationException("无法读取执行计划页面的专业筛选条件");
        Dictionary<string, string> parameters;
        using (var filters = JsonDocument.Parse(filtersJson))
        {
            parameters = new[] { "jg_id", "njdm_id", "dlbs", "zyh_id", "currentPage_cx" }
                .ToDictionary(name => name, name => Text(filters.RootElement, name));
        }
        if (studentPlan is not null)
        {
            if (parameters["jg_id"].Length == 0) parameters["jg_id"] = studentPlan.CollegeId;
            if (parameters["njdm_id"].Length == 0) parameters["njdm_id"] = studentPlan.AdmissionYear;
            if (parameters["zyh_id"].Length == 0) parameters["zyh_id"] = studentPlan.MajorId;
        }
        var json = await FetchTextAsync(dialog, pageGate, PlanQueryPath, parameters, 1, ct).ConfigureAwait(true);
        return ParsePlanPage(json, parameters);
    }

    internal static (double MinimumCredits, int CourseCount) ParsePlanPage(
        string json, IReadOnlyDictionary<string, string> parameters)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (!root.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("执行计划返回格式无法识别");
        var matches = items.EnumerateArray().Where(row =>
            MatchesPlanFilter(row, "jg_id", "jg_id", parameters)
            && MatchesPlanFilter(row, "njdm", "njdm_id", parameters)
            && MatchesPlanFilter(row, "dlbs", "dlbs", parameters)
            && MatchesPlanFilter(row, "zyh_id", "zyh_id", parameters)).ToList();
        JwCaptureService.Log($"plan: 返回 {items.GetArrayLength()} 行，按当前专业与年级匹配 {matches.Count} 行");
        if (matches.Count == 0) throw new InvalidOperationException("未找到与当前专业、年级匹配的执行计划");
        if (matches.Count > 1) throw new InvalidOperationException("执行计划不唯一，请在教务页面选定本专业和年级后重试");
        return (Double(matches[0], "zdxf"), Int(matches[0], "kcs"));
    }

    private static bool MatchesPlanFilter(
        JsonElement row, string responseName, string parameterName,
        IReadOnlyDictionary<string, string> parameters)
    {
        if (!parameters.TryGetValue(parameterName, out var expected) || expected.Length == 0) return true;
        if (!row.TryGetProperty(responseName, out var value)) return false;
        var actual = value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : value.ValueKind == JsonValueKind.Number ? value.GetRawText() : string.Empty;
        return string.Equals(actual.Trim(), expected, StringComparison.Ordinal);
    }

    private static async Task<bool> NavigateToAsync(
        NativeWebDialog dialog,
        PageReadyGate pageGate,
        string url,
        string expectedPath,
        CancellationToken ct)
    {
        var deadline = DateTimeOffset.Now + TimeSpan.FromMinutes(2);
        var navigating = false;
        while (DateTimeOffset.Now < deadline)
        {
            ct.ThrowIfCancellationRequested();
            if (pageGate.HasCompletedNavigation)
            {
                var path = await JwCaptureService.TryEvalAsync(dialog, "location.pathname", ct).ConfigureAwait(true);
                if (string.Equals(path, expectedPath, StringComparison.OrdinalIgnoreCase)) return true;
                if (!navigating)
                {
                    dialog.Navigate(new Uri(url));
                    navigating = true;
                }
            }
            await Task.Delay(250, ct).ConfigureAwait(true);
        }
        return false;
    }

    private static async Task<string> FetchTextAsync(
        NativeWebDialog dialog,
        PageReadyGate pageGate,
        string queryPath,
        IReadOnlyDictionary<string, string> parameters,
        int page,
        CancellationToken ct)
    {
        var started = await JwCaptureService.TryEvalAsync(
            dialog, BuildFetchScript(queryPath, parameters, page), ct).ConfigureAwait(true);
        if (started is null) throw new InvalidOperationException("教务查询脚本启动失败");
        var deadline = DateTimeOffset.Now + TimeSpan.FromSeconds(30);
        while (DateTimeOffset.Now < deadline)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Delay(200, ct).ConfigureAwait(true);
            if (!pageGate.HasCompletedNavigation) throw new InvalidOperationException("教务页面正在跳转");
            var raw = await JwCaptureService.TryEvalAsync(dialog, ReadFetchResultScript, ct).ConfigureAwait(true);
            if (string.IsNullOrEmpty(raw)) continue;
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            if (!root.GetProperty("ok").GetBoolean())
                throw new InvalidOperationException("教务查询请求失败");
            return root.GetProperty("text").GetString() ?? string.Empty;
        }
        throw new InvalidOperationException("教务查询超时");
    }

    internal static string BuildFetchScript(
        string queryPath, IReadOnlyDictionary<string, string> parameters, int page)
    {
        var pathJson = JsonSerializer.Serialize(queryPath);
        var parametersJs = string.Join(string.Empty, parameters.Select(pair =>
            $"p.set({JsonSerializer.Serialize(pair.Key)},{JsonSerializer.Serialize(pair.Value)});"));
        return $$"""
            (()=>{
              var p=new URLSearchParams();
              {{parametersJs}}
              p.set('_search','false');p.set('nd',String(Date.now()));
              p.set('queryModel.showCount','{{PageSize}}');
              p.set('queryModel.currentPage','{{page}}');
              p.set('queryModel.sortName',' ');p.set('queryModel.sortOrder','asc');
              p.set('time','{{page}}');
              window.__utGradePage={done:false,ok:false,status:0,text:''};
              fetch({{pathJson}},{
                method:'POST',credentials:'same-origin',
                headers:{'Content-Type':'application/x-www-form-urlencoded;charset=UTF-8','X-Requested-With':'XMLHttpRequest'},
                body:p.toString()
              }).then(function(r){return r.text().then(function(t){
                window.__utGradePage={done:true,ok:r.ok,status:r.status,text:t};
              });}).catch(function(e){
                window.__utGradePage={done:true,ok:false,status:0,text:String(e)};
              });
              return 'started';
            })()
            """;
    }

    private const string ReadFetchResultScript = """
        (()=>{
          var r=window.__utGradePage;
          if(!r||!r.done)return '';
          window.__utGradePage=null;
          return JSON.stringify(r);
        })()
        """;

    internal sealed record GradePage(IReadOnlyList<GradeInfo> Items, int TotalPages);

    internal static GradePage ParseGradePage(string json, int academicYear, int semester)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var items = new List<GradeInfo>();
        if (root.TryGetProperty("items", out var rows) && rows.ValueKind == JsonValueKind.Array)
        {
            foreach (var row in rows.EnumerateArray())
            {
                var name = Text(row, "kcmc");
                if (name.Length == 0) continue;
                var credits = Double(row, "xf");
                var gradePoint = Double(row, "jd");
                var percent = NullableDouble(row, "bfzcj") ?? NullableDouble(row, "cj");
                items.Add(new GradeInfo
                {
                    AcademicYear = Int(row, "xnm", academicYear),
                    Semester = Int(row, "xqmmc", semester),
                    CourseCode = Text(row, "kch"),
                    CourseName = name,
                    CourseNature = Text(row, "kcxzmc"),
                    Credits = credits,
                    GradeText = Text(row, "cj"),
                    PercentScore = percent,
                    GradePoint = gradePoint,
                    CreditPoint = Double(row, "xfjd", credits * gradePoint),
                    Assessment = Text(row, "khfsmc"),
                    ExamNature = Text(row, "ksxz"),
                    Teacher = Text(row, "jsxm"),
                    College = Text(row, "kkbmmc"),
                    CollegeId = Text(row, "jg_id"),
                    MajorId = Text(row, "zyh_id"),
                    AdmissionYear = Text(row, "njdm_id"),
                    IsVoid = Text(row, "cjsfzf") == "是",
                });
            }
        }
        return new GradePage(items, Int(root, "totalPage", 1));
    }

    internal static AcademicStats ComputeStats(IEnumerable<GradeInfo> grades)
    {
        var valid = grades.Where(item => !item.IsVoid).ToList();
        var earned = valid.Where(item => item.GradePoint > 0).Sum(item => item.Credits);
        var creditTotal = valid.Where(item => item.Credits > 0).Sum(item => item.Credits);
        var pointTotal = valid.Sum(item => item.CreditPoint);
        var percentages = valid.Where(item => item.PercentScore.HasValue)
            .Select(item => item.PercentScore!.Value).ToList();
        return new AcademicStats
        {
            EarnedCredits = earned,
            WeightedGradePoint = creditTotal > 0 ? Math.Round(pointTotal / creditTotal, 2) : 0,
            AverageScore = percentages.Count > 0 ? Math.Round(percentages.Average(), 2) : 0,
        };
    }

    private static string Text(JsonElement row, string name) =>
        row.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()?.Trim() ?? string.Empty
            : string.Empty;

    private static int Int(JsonElement row, string name, int fallback = 0)
    {
        if (!row.TryGetProperty(name, out var value)) return fallback;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)) return number;
        return value.ValueKind == JsonValueKind.String
            && int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number)
            ? number : fallback;
    }

    private static double Double(JsonElement row, string name, double fallback = 0) =>
        NullableDouble(row, name) ?? fallback;

    private static double? NullableDouble(JsonElement row, string name)
    {
        if (!row.TryGetProperty(name, out var value)) return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number)) return number;
        if (value.ValueKind == JsonValueKind.String
            && double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out number))
            return number;
        return null;
    }
}
