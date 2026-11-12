using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using UniversityTodos.Services;

namespace UniversityTodos.Desktop;

internal static class GradeCreditCheck
{
    public static void Run()
    {
        var profile = new JwProfileStore().LoadAsync().GetAwaiter().GetResult();
        if (string.IsNullOrWhiteSpace(profile.LoginUrl))
        {
            Console.WriteLine("未配置教务系统网址，无法执行成绩与学分自检");
            Environment.Exit(2);
            return;
        }

        App.DeferInitialLoad = true;
        App.WebViewTestBudget = TimeSpan.FromMinutes(2);
        App.WebViewTestHook = (desktop, _) => RunInWindow(desktop, profile.LoginUrl);
        Program.BuildAvaloniaApp().StartWithClassicDesktopLifetime(["--grade-credit-check-runner"]);
    }

    private static void RunInWindow(IClassicDesktopStyleApplicationLifetime desktop, string loginUrl)
    {
        var owner = new Window
        {
            Width = 800,
            Height = 600,
            Title = "成绩与学分自检宿主",
            Content = "教务成绩与学分正在读取",
        };
        desktop.MainWindow = owner;
        owner.Show();
        var dialog = new NativeWebDialog
        {
            Title = "成绩与学分真实链路自检（如未登录请手动登录）",
            CanUserResize = true,
            ShowFocused = true,
        };
        dialog.Resize(1100, 780);
        var gate = new PageReadyGate();
        var adapterCreated = false;
        var shown = false;
        var navigating = false;
        var sessionPrepared = false;
        var querying = false;
        var finished = false;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(100) };

        void Finish(bool success, string message)
        {
            if (finished) return;
            finished = true;
            timer.Stop();
            Console.WriteLine($"{(success ? "PASS" : "FAIL")}: {message}");
            try { dialog.Close(); } catch { }
            Environment.Exit(success ? 0 : 1);
        }

        void Navigate()
        {
            if (navigating || !adapterCreated || !shown) return;
            navigating = true;
            try { dialog.Navigate(new Uri(GradeService.BuildGradePageUrl(loginUrl))); }
            catch (Exception ex) { Finish(false, $"导航失败：{ex.Message}"); }
        }

        async Task PrepareSessionAndNavigateAsync()
        {
            if (sessionPrepared || !adapterCreated || !shown) return;
            sessionPrepared = true;
            await JwSessionService.RestoreAsync(dialog, loginUrl);
            Navigate();
        }

        async Task QueryAsync()
        {
            if (querying || finished) return;
            querying = true;
            try
            {
                var result = await GradeService.FetchAsync(
                    dialog, gate, loginUrl, 2025, 2,
                    progress: new Progress<string>(Console.WriteLine));
                if (!result.Success) Finish(false, result.Message);
                else
                {
                    var stats = result.Stats;
                    var ok = result.Grades.Count > 0 && stats.MinimumGraduationCredits > 0
                        && stats.PlannedCourseCount > 0 && stats.EarnedCredits > 0
                        && stats.WeightedGradePoint > 0 && stats.AverageScore > 0;
                    Finish(ok,
                        $"本学期 {result.Grades.Count} 条成绩；毕业要求 {stats.MinimumGraduationCredits:0.##} 学分，"
                        + $"计划 {stats.PlannedCourseCount} 门，已获 {stats.EarnedCredits:0.##} 学分，"
                        + $"绩点 {stats.WeightedGradePoint:0.00}，平均分 {stats.AverageScore:0.##}");
                }
            }
            catch (Exception ex)
            {
                Finish(false, $"{ex.GetType().Name}: {ex.Message}");
            }
        }

        async Task LogCookiesAsync()
        {
            var manager = dialog.TryGetCookieManager();
            if (manager is null)
            {
                Console.WriteLine("CookieManager 不可用");
                return;
            }
            var cookies = await manager.GetCookiesAsync();
            var items = cookies?.Cast<System.Net.Cookie>().ToList() ?? [];
            Console.WriteLine($"当前会话 Cookie 数量: {items.Count}");
            foreach (var cookie in items)
                Console.WriteLine($"  {cookie.Domain} · {cookie.Name} · 到期 {cookie.Expires:yyyy-MM-dd HH:mm}");
        }

        dialog.AdapterCreated += (_, _) =>
        {
            adapterCreated = true;
            gate.MarkAdapterCreated();
            _ = PrepareSessionAndNavigateAsync();
        };
        dialog.AdapterDestroyed += (_, _) => gate.Reset();
        dialog.NavigationStarted += (_, _) => gate.MarkNavigating();
        dialog.NavigationCompleted += (_, e) =>
        {
            gate.MarkReady();
            Console.WriteLine($"导航完成: {e.Request?.Host}{e.Request?.AbsolutePath}");
            _ = LogCookiesAsync();
            if (e.IsSuccess && GradeService.IsGradePage(e.Request)) _ = QueryAsync();
        };
        dialog.NewWindowRequested += (_, e) =>
        {
            e.Handled = true;
            if (e.Request is not null) dialog.Navigate(e.Request);
        };
        dialog.Closing += (_, _) =>
        {
            if (!finished) Environment.Exit(2);
        };
        timer.Tick += (_, _) => Finish(false, "等待教务登录或成绩接口超时");
        dialog.Show(owner);
        shown = true;
        timer.Start();
        _ = PrepareSessionAndNavigateAsync();
    }
}
