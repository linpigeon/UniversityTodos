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

public static class WaterService
{
    public const string WaterPageUrl =
        "https://mapp.zcst.edu.cn/_web/_apps/lightapp/waterCharges/mobile/permit/pc/pub/index.jsp";

    private static string BuildLatestPageScript(string? account, string? password)
    {
        var accountJson = JsonSerializer.Serialize(account ?? string.Empty);
        var passwordJson = JsonSerializer.Serialize(password ?? string.Empty);
        return $$"""
            (()=>{
              if(location.host==='sos.zcst.edu.cn' && {{accountJson}}.length>0 && !window.__utWaterCredentialSubmitted){
                var a=document.querySelector('input[placeholder="请输入学工号"]');
                var p=document.querySelector('input[placeholder="请输入密码"]');
                if(a&&p){
                  a.value={{accountJson}};
                  p.value={{passwordJson}};
                  a.dispatchEvent(new Event('input',{bubbles:true}));
                  p.dispatchEvent(new Event('input',{bubbles:true}));
                  var b=Array.from(document.querySelectorAll('button')).find(x=>(x.textContent||'').includes('登'));
                  if(b){window.__utWaterCredentialSubmitted=true;b.click();return 'credentials';}
                }
              }
              if(location.host!=='mapp.zcst.edu.cn') return '';
              var rows=document.querySelectorAll('table tbody tr');
              if(rows.length===0) return '';
              if(!window.__utWaterLatest){
                var target=null,max=0;
                document.querySelectorAll('.paginate_button').forEach(function(a){
                  var n=parseInt((a.textContent||'').trim(),10);
                  if(Number.isFinite(n)&&n>max){max=n;target=a;}
                });
                window.__utWaterLatest=Date.now();
                if(target) target.click();
                return 'paging';
              }
              if(Date.now()-window.__utWaterLatest<1200) return 'waiting';
              return 'ready';
            })()
            """;
    }

    private const string ReadTableScript = """
        (()=>JSON.stringify(Array.from(document.querySelectorAll('table tbody tr')).map(
          r=>Array.from(r.cells).map(c=>(c.innerText||'').trim()))))()
        """;

    private const string PreviousPageScript = """
        (()=>{
          var buttons=Array.from(document.querySelectorAll('.paginate_button.previous'));
          var target=buttons.find(a=>!a.classList.contains('disabled')
            && a.getAttribute('aria-disabled')!=='true');
          if(!target) return 'done';
          target.click();
          return 'previous';
        })()
        """;

    public static async Task<IReadOnlyList<WaterBill>?> WaitForLatestAsync(
        NativeWebView webView,
        IProgress<string>? progress = null,
        TimeSpan? timeout = null,
        string? account = null,
        string? password = null,
        bool navigate = true,
        PageReadyGate? pageGate = null,
        DateOnly? semesterStart = null,
        DateOnly? semesterEnd = null,
        bool includeHistory = false,
        CancellationToken ct = default)
    {
        var deadline = DateTimeOffset.Now + (timeout ?? TimeSpan.FromMinutes(5));
        if (navigate)
        {
            try
            {
                webView.Navigate(new Uri(WaterPageUrl));
            }
            catch (Exception)
            {
                return null;
            }
        }

        var loginHintShown = false;
        var firstPoll = true;
        var pollInterval = TimeSpan.FromMilliseconds(250);
        while (DateTimeOffset.Now < deadline)
        {
            ct.ThrowIfCancellationRequested();
            if (!firstPoll) await Task.Delay(pollInterval, ct).ConfigureAwait(true);
            firstPoll = false;
            if (pageGate is { HasCompletedNavigation: false }) continue;
            string? state;
            try
            {
                state = await webView.InvokeScript(BuildLatestPageScript(account, password)).ConfigureAwait(true);
            }
            catch (Exception)
            {
                continue;
            }

            var normalized = state?.Trim().Trim('"');
            if (string.IsNullOrEmpty(normalized))
            {
                if (!loginHintShown)
                {
                    progress?.Report("请在学校统一身份认证页面完成登录，成功后自动读取水费");
                    loginHintShown = true;
                }
                continue;
            }

            if (normalized != "ready")
            {
                progress?.Report("正在读取最近一期水费账单…");
                continue;
            }

            try
            {
                var raw = await webView.InvokeScript(ReadTableScript).ConfigureAwait(true);
                if (!includeHistory)
                {
                    var bills = ParseTableJson(raw);
                    if (bills.Count > 0) return FilterBySemester(bills, semesterStart, semesterEnd);
                    continue;
                }

                var allBills = ParseTablePageJson(raw).ToList();
                if (allBills.Count == 0) continue;
                for (var page = 1; page < 100 && DateTimeOffset.Now < deadline; page++)
                {
                    progress?.Report($"正在读取往期水费账单，已收集 {allBills.Count} 条…");
                    var previous = await ReadPreviousPageAsync(webView, raw, deadline, ct).ConfigureAwait(true);
                    if (previous is not { } previousPage) break;
                    raw = previousPage.Raw;
                    allBills.AddRange(previousPage.Bills);
                }
                return MergeBills(allBills);
            }
            catch (Exception)
            {
                // 表格尚未稳定时下一轮重试。
            }
        }

        progress?.Report("水费查询超时，请检查统一认证是否完成");
        return null;
    }

    private static async Task<(string Raw, IReadOnlyList<WaterBill> Bills)?> ReadPreviousPageAsync(
        NativeWebView webView, string? previousRaw, DateTimeOffset deadline, CancellationToken ct)
    {
        string? state;
        try
        {
            state = await webView.InvokeScript(PreviousPageScript).ConfigureAwait(true);
        }
        catch (Exception)
        {
            return null;
        }
        if (state?.Trim().Trim('"') != "previous") return null;

        var pageDeadline = DateTimeOffset.Now + TimeSpan.FromSeconds(3);
        if (pageDeadline > deadline) pageDeadline = deadline;
        while (DateTimeOffset.Now < pageDeadline)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Delay(200, ct).ConfigureAwait(true);
            try
            {
                var raw = await webView.InvokeScript(ReadTableScript).ConfigureAwait(true);
                if (raw is null || raw == previousRaw) continue;
                var bills = ParseTablePageJson(raw);
                if (bills.Count > 0) return (raw, bills);
            }
            catch (Exception)
            {
                return null;
            }
        }
        return null;
    }

    internal static IReadOnlyList<WaterBill> FilterBySemester(
        IReadOnlyList<WaterBill> bills, DateOnly? semesterStart, DateOnly? semesterEnd)
    {
        if (semesterStart is null) return bills;
        return bills.Where(bill =>
            DateOnly.TryParse(bill.EndDate, out var billEnd)
            && billEnd >= semesterStart.Value
            && (semesterEnd is null || billEnd <= semesterEnd.Value)).ToList();
    }

    internal static IReadOnlyList<WaterBill> FilterHistoryBySemester(
        IReadOnlyList<WaterBill> bills, DateOnly? semesterStart)
    {
        if (semesterStart is null) return [];
        return bills.Where(bill =>
            DateOnly.TryParse(bill.EndDate, out var billEnd)
            && billEnd < semesterStart.Value).ToList();
    }

    internal static IReadOnlyList<WaterBill> MergeBills(IEnumerable<WaterBill> bills)
    {
        return bills
            .GroupBy(bill => (bill.Sequence, bill.WaterType, bill.StartDate, bill.EndDate))
            .Select(group => group.First())
            .OrderByDescending(bill =>
                DateOnly.TryParse(bill.EndDate, out var endDate) ? endDate : DateOnly.MinValue)
            .ThenBy(bill => bill.WaterType == "冷水" ? 0 : 1)
            .ThenBy(bill => bill.Sequence)
            .ToList();
    }

    public static IReadOnlyList<WaterBill> ParseTableJson(string? scriptResult)
    {
        var bills = ParseTablePageJson(scriptResult);
        if (bills.Count == 0) return [];
        var latestDate = bills.Max(b => b.EndDate)!;
        return bills
            .Where(b => b.EndDate == latestDate)
            .OrderBy(b => b.WaterType == "冷水" ? 0 : 1)
            .ThenBy(b => b.Sequence)
            .ToList();
    }

    internal static IReadOnlyList<WaterBill> ParseTablePageJson(string? scriptResult)
    {
        if (string.IsNullOrWhiteSpace(scriptResult)) return [];
        var json = scriptResult;
        if (json.StartsWith('"'))
            json = JsonDocument.Parse(json).RootElement.GetString() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(json)) return [];

        using var doc = JsonDocument.Parse(json);
        var bills = new List<WaterBill>();
        foreach (var row in doc.RootElement.EnumerateArray())
        {
            var cells = row.EnumerateArray().Select(c => c.GetString() ?? string.Empty).ToArray();
            if (cells.Length < 7
                || !int.TryParse(cells[0], out var sequence)
                || !double.TryParse(cells[5], NumberStyles.Float, CultureInfo.InvariantCulture, out var usage)
                || !double.TryParse(cells[6], NumberStyles.Float, CultureInfo.InvariantCulture, out var fee)
                || !double.IsFinite(usage) || !double.IsFinite(fee))
                continue;

            bills.Add(new WaterBill
            {
                Sequence = sequence,
                RoomNumber = cells[1],
                WaterType = cells[2],
                StartDate = cells[3],
                EndDate = cells[4],
                Usage = usage,
                Fee = fee
            });
        }
        return bills;
    }
}
