using System;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using UniversityTodos.Services;

namespace UniversityTodos.Desktop;

internal static class LoginGateCheck
{
    private static int _earlyScriptErrors;
    private static int _unobservedTaskErrors;
    private static CancellationTokenSource? _activeCts;

    public static void Run()
    {
        App.DeferInitialLoad = true;
        App.WebViewTestBudget = TimeSpan.FromSeconds(45);
        App.WebViewTestHook = (desktop, _) => RunInWindow(desktop);
        Program.BuildAvaloniaApp().StartWithClassicDesktopLifetime(["--login-gate-check-runner"]);
    }

    private static void RunInWindow(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var webView = new NativeWebView();
        var gate = new PageReadyGate();
        var window = new Window
        {
            Width = 900,
            Height = 700,
            Title = "登录页面就绪闸门自检",
            Content = webView,
        };
        desktop.MainWindow = window;

        EventHandler<FirstChanceExceptionEventArgs> firstChance = (_, e) =>
        {
            if (e.Exception is InvalidOperationException
                && e.Exception.Message.Contains("before any page was loaded", StringComparison.Ordinal))
            {
                _earlyScriptErrors++;
            }
        };
        EventHandler<UnobservedTaskExceptionEventArgs> unobserved = (_, e) =>
        {
            _unobservedTaskErrors++;
            e.SetObserved();
        };
        AppDomain.CurrentDomain.FirstChanceException += firstChance;
        TaskScheduler.UnobservedTaskException += unobserved;

        webView.AdapterCreated += (_, _) => gate.MarkAdapterCreated();
        webView.AdapterDestroyed += (_, _) => gate.Reset();
        webView.NavigationStarted += (_, _) => gate.MarkNavigating();
        webView.NavigationCompleted += (_, _) =>
        {
            gate.MarkReady();
            _activeCts?.CancelAfter(TimeSpan.FromMilliseconds(500));
        };
        window.Opened += async (_, _) =>
        {
            try
            {
                var yktReady = await RunProviderAsync(webView, gate, rainClassroom: true);
                var pigaiReady = await RunProviderAsync(webView, gate, rainClassroom: false);
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();

                Console.WriteLine($"雨课堂导航完成: {yktReady}");
                Console.WriteLine($"批改网导航完成: {pigaiReady}");
                Console.WriteLine($"首轮加载前脚本异常: {_earlyScriptErrors}");
                Console.WriteLine($"未观察任务异常: {_unobservedTaskErrors}");
                var ok = yktReady && pigaiReady && _earlyScriptErrors == 0 && _unobservedTaskErrors == 0;
                Console.WriteLine(ok ? "PASS: 登录页面闸门与取消观察正常" : "FAIL: 登录页面闸门自检失败");
                Environment.Exit(ok ? 0 : 1);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FAIL: {ex.GetType().Name}: {ex.Message}");
                Environment.Exit(2);
            }
            finally
            {
                AppDomain.CurrentDomain.FirstChanceException -= firstChance;
                TaskScheduler.UnobservedTaskException -= unobserved;
            }
        };
        window.Show();
    }

    private static async Task<bool> RunProviderAsync(
        NativeWebView webView, PageReadyGate gate, bool rainClassroom)
    {
        gate.Reset();
        var completed = false;
        void MarkCompleted()
        {
            if (gate.HasCompletedNavigation) completed = true;
        }

        using var cts = new CancellationTokenSource();
        _activeCts = cts;
        try
        {
            if (rainClassroom)
            {
                await new YktLoginService().WaitForLoginAsync(
                    webView,
                    pollInterval: TimeSpan.FromMilliseconds(100),
                    timeout: TimeSpan.FromSeconds(10),
                    pageGate: gate,
                    ct: cts.Token);
            }
            else
            {
                webView.Navigate(new Uri(PigaiLoginService.LoginUrl));
                await PigaiLoginService.WaitForLoginAsync(
                    webView,
                    pollInterval: TimeSpan.FromMilliseconds(100),
                    timeout: TimeSpan.FromSeconds(10),
                    pageGate: gate,
                    ct: cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            MarkCompleted();
            _activeCts = null;
        }

        await Task.Delay(200);
        return completed;
    }
}
