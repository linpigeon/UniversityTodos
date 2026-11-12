using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using UniversityTodos.Services;
using UniversityTodos.ViewModels;
using UniversityTodos.Views;

namespace UniversityTodos.Desktop;

internal static class WaterColdCheck
{
    public static void Run()
    {
        var credential = new CredentialStore("water-credentials.json").LoadAsync().GetAwaiter().GetResult();
        if (credential is null)
        {
            Console.WriteLine("未找到已保存的统一认证凭据，无法执行冷启动自检");
            Environment.Exit(2);
            return;
        }

        var profile = Path.Combine(Path.GetTempPath(), "hw-water-cold-" + Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER", profile);
        App.DeferInitialLoad = true;
        App.WebViewTestBudget = TimeSpan.FromMinutes(2);
        App.WebViewTestHook = (desktop, _) => RunInWindow(desktop, credential.Account, credential.Password, profile);
        Program.BuildAvaloniaApp().StartWithClassicDesktopLifetime(["--water-cold-check-runner"]);
    }

    private static void RunInWindow(
        IClassicDesktopStyleApplicationLifetime desktop,
        string account,
        string password,
        string profile)
    {
        var view = new WaterLoginView();
        var window = new Window
        {
            Width = 900,
            Height = 700,
            Title = "统一认证冷启动自检（不输出凭据）",
            Content = view,
        };
        desktop.MainWindow = window;
        var stopwatch = Stopwatch.StartNew();
        var gate = MainViewModel.WaterPageGate;

        window.Opened += async (_, _) =>
        {
            void Cleanup()
            {
                try { Directory.Delete(profile, recursive: true); } catch { }
            }
            if (MainViewModel.WaterWebViewAccessor?.Invoke() is not { } webView)
            {
                Console.WriteLine("FAIL: 水费 WebView 尚未挂载");
                Cleanup();
                Environment.Exit(2);
                return;
            }
            webView.NavigationCompleted += (_, e) =>
                Console.WriteLine($"{stopwatch.Elapsed.TotalSeconds:F1}s 导航完成: {e.Request?.Host}");
            var exitCode = 0;
            try
            {
                var bills = await WaterService.WaitForLatestAsync(
                    webView,
                    new Progress<string>(message =>
                        Console.WriteLine($"{stopwatch.Elapsed.TotalSeconds:F1}s {message}")),
                    timeout: TimeSpan.FromSeconds(90),
                    account: account,
                    password: password,
                    pageGate: gate,
                    includeHistory: true);
                var periodCount = bills?.Select(bill => $"{bill.StartDate}–{bill.EndDate}")
                    .Distinct().Count() ?? 0;
                Console.WriteLine(bills is null
                    ? $"FAIL: {stopwatch.Elapsed.TotalSeconds:F1}s 冷启动查询失败"
                    : $"PASS: {stopwatch.Elapsed.TotalSeconds:F1}s 冷启动读取到 {bills.Count} 条账单、{periodCount} 个账期");
                exitCode = bills is null ? 1 : 0;
                if (bills is not null)
                {
                    var classificationDir = Path.Combine(
                        Path.GetTempPath(), "hw-water-classification-" + Guid.NewGuid().ToString("N"));
                    try
                    {
                        var water = new WaterViewModel(new WaterCacheStore(classificationDir));
                        await water.SaveAsync(bills, includeHistory: true);
                        Console.WriteLine($"学期基准: {AppSettings.Current.TermStartDate ?? "未配置"}；"
                            + $"本学期 {water.LatestBills.Count} 条，往期 {water.HistoryBills.Count} 条");
                        Console.WriteLine("本学期最新账单: " + string.Join("；", water.LatestBills
                            .Select(bill => $"{bill.WaterType} {bill.StartDate}–{bill.EndDate}")));
                        if (Timetable.ParseDate(AppSettings.Current.TermStartDate) is not null
                            && water.LatestBills.Count == 0)
                        {
                            Console.WriteLine("FAIL: 已配置学期基准但本学期账单被错误清空");
                            exitCode = 1;
                        }
                    }
                    finally
                    {
                        try { Directory.Delete(classificationDir, recursive: true); } catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FAIL: {ex.GetType().Name}: {ex.Message}");
                exitCode = 1;
            }
            Cleanup();
            Environment.Exit(exitCode);
        };
        window.Show();
    }
}
