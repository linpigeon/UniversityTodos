using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using UniversityTodos.Services;

namespace UniversityTodos.Desktop;

internal static class ExamInfoCheck
{
    public static void Run()
    {
        var profile = new JwProfileStore().LoadAsync().GetAwaiter().GetResult();
        if (string.IsNullOrWhiteSpace(profile.LoginUrl))
        {
            Console.WriteLine("未配置教务系统网址，无法执行考试信息自检");
            Environment.Exit(2);
            return;
        }

        var academicYear = int.TryParse(
            Environment.GetEnvironmentVariable("UNIVERSITYTODOS_EXAM_YEAR"), out var year) ? year : 2025;
        var semester = int.TryParse(
            Environment.GetEnvironmentVariable("UNIVERSITYTODOS_EXAM_SEMESTER"), out var term) ? term : 2;
        App.DeferInitialLoad = true;
        App.WebViewTestBudget = TimeSpan.FromMinutes(2);
        App.WebViewTestHook = (desktop, _) => RunInWindow(desktop, profile.LoginUrl, academicYear, semester);
        Program.BuildAvaloniaApp().StartWithClassicDesktopLifetime(["--exam-info-check-runner"]);
    }

    private static void RunInWindow(
        IClassicDesktopStyleApplicationLifetime desktop, string loginUrl, int academicYear, int semester)
    {
        var owner = new Window
        {
            Width = 800,
            Height = 600,
            Title = "考试信息自检宿主",
            Content = "教务考试信息自检正在运行",
        };
        desktop.MainWindow = owner;
        owner.Show();
        var dialog = new NativeWebDialog
        {
            Title = "考试信息真实链路自检（如未登录请手动登录）",
            CanUserResize = true,
            ShowFocused = true,
        };
        dialog.Resize(1100, 780);
        var gate = new PageReadyGate();
        var adapterCreated = false;
        var shown = false;
        var navigating = false;
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
            try { dialog.Navigate(new Uri(ExamService.BuildExamPageUrl(loginUrl))); }
            catch (Exception ex) { Finish(false, $"导航失败：{ex.Message}"); }
        }

        async Task QueryAsync()
        {
            if (querying || finished) return;
            querying = true;
            try
            {
                var result = await ExamService.FetchExamsAsync(
                    dialog, gate, academicYear, semester,
                    timeout: TimeSpan.FromSeconds(80),
                    progress: new Progress<string>(Console.WriteLine));
                if (!result.Success) Finish(false, result.Message);
                else Finish(true, $"应用内 WebView 成功读取 {result.Items.Count} 场考试");
            }
            catch (Exception ex)
            {
                Finish(false, $"{ex.GetType().Name}: {ex.Message}");
            }
        }

        dialog.AdapterCreated += (_, _) =>
        {
            adapterCreated = true;
            gate.MarkAdapterCreated();
            Navigate();
        };
        dialog.AdapterDestroyed += (_, _) => gate.Reset();
        dialog.NavigationStarted += (_, _) => gate.MarkNavigating();
        dialog.NavigationCompleted += (_, e) =>
        {
            gate.MarkReady();
            Console.WriteLine($"导航完成: {e.Request?.Host}{e.Request?.AbsolutePath}");
            if (e.IsSuccess && ExamService.IsExamPage(e.Request)) _ = QueryAsync();
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
        timer.Tick += (_, _) => Finish(false, "等待教务登录或考试接口超时");
        dialog.Show(owner);
        shown = true;
        timer.Start();
        Navigate();
    }
}
