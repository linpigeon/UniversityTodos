using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Threading;
using UniversityTodos.Services;
using UniversityTodos.ViewModels;
using UniversityTodos.Views;

namespace UniversityTodos.Desktop;

internal static class WaterSilentFlowCheck
{
    public static void Run()
    {
        var credential = new CredentialStore("water-credentials.json").LoadAsync().GetAwaiter().GetResult();
        if (credential is null)
        {
            Console.WriteLine("未找到已保存的统一认证凭据，无法执行后台主流程自检");
            Environment.Exit(2);
            return;
        }

        var requireRestoredSession = Environment.GetEnvironmentVariable(
            "UNIVERSITYTODOS_WATER_REQUIRE_RESTORE") == "1";
        App.DeferInitialLoad = true;
        App.WebViewTestBudget = TimeSpan.FromMinutes(2);
        App.WebViewTestHook = (desktop, _) => RunInWindow(
            desktop, credential.Account, credential.Password, requireRestoredSession);
        Program.BuildAvaloniaApp().StartWithClassicDesktopLifetime(["--water-silent-flow-check-runner"]);
    }

    private static void RunInWindow(
        IClassicDesktopStyleApplicationLifetime desktop, string account, string password,
        bool requireRestoredSession)
    {
        var vm = new MainViewModel
        {
            WaterAccountInput = account,
            WaterPasswordInput = password,
        };
        var window = new MainWindow
        {
            DataContext = vm,
            Title = "水费后台主窗口自检（不输出凭据）",
        };
        desktop.MainWindow = window;
        var stopwatch = Stopwatch.StartNew();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(55) };
        var diagnostics = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        diagnostics.Tick += (_, _) =>
        {
            var current = MainViewModel.WaterWebViewAccessor?.Invoke();
            Console.WriteLine($"{stopwatch.Elapsed.TotalSeconds:F1}s 诊断: WebView={current is not null}, "
                + $"Adapter={current?.AdapterInfo is not null}, "
                + $"NavigationCompleted={MainViewModel.WaterPageGate?.HasCompletedNavigation}");
        };
        var finished = false;

        void Finish(bool success, string message)
        {
            if (finished) return;
            finished = true;
            timer.Stop();
            diagnostics.Stop();
            Console.WriteLine($"{stopwatch.Elapsed.TotalSeconds:F1}s {(success ? "PASS" : "FAIL")}: {message}");
            if (success && Environment.GetEnvironmentVariable("UNIVERSITYTODOS_WATER_HOLD") == "1")
            {
                _ = Task.Delay(TimeSpan.FromSeconds(10)).ContinueWith(_ => Environment.Exit(0));
                return;
            }
            Environment.Exit(success ? 0 : 1);
        }

        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.LoginOverlayVisible)
                && vm.LoginOverlayVisible
                && vm.Water.StatusMessage.Contains("后台获取未完成", StringComparison.Ordinal))
                Finish(false, "后台查询超时并转入前台认证页");
        };
        var successfulQueries = 0;
        async Task RunSecondQuery()
        {
            await Task.Delay(500);
            await vm.RefreshWaterCommand.ExecuteAsync(null);
            await Task.Delay(500);
            if (vm.LoginOverlayVisible) Finish(false, "复用已加载页面时后台宿主留在前台");
        }
        vm.Water.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(vm.Water.StatusMessage))
            {
                Console.WriteLine($"{stopwatch.Elapsed.TotalSeconds:F1}s {vm.Water.StatusMessage}");
                if (vm.Water.StatusMessage.Contains("后台获取未完成", StringComparison.Ordinal))
                    Finish(false, "后台查询超时并转入前台认证页");
            }
            if (e.PropertyName == nameof(vm.Water.IsLoading) && !vm.Water.IsLoading && !finished)
            {
                if (vm.Water.HasData && !vm.LoginOverlayVisible)
                {
                    successfulQueries++;
                    if (Environment.GetEnvironmentVariable("UNIVERSITYTODOS_WATER_HISTORY") == "1")
                        Finish(true, $"历史读取期间保持后台，缓存 {vm.Water.LatestBills.Count} 条本学期和 {vm.Water.HistoryBills.Count} 条往期账单");
                    else if (successfulQueries == 1) _ = RunSecondQuery();
                    else Finish(true, $"连续两次后台直接读取 {vm.Water.LatestBills.Count} 条本学期账单");
                }
                else if (!vm.LoginOverlayVisible)
                    Finish(false, vm.Water.StatusMessage);
            }
        };
        timer.Tick += (_, _) => Finish(false, "后台主流程超过 55 秒");
        window.Opened += async (_, _) =>
        {
            await vm.SaveWaterCredentialsCommand.ExecuteAsync(null);
            diagnostics.Start();
            timer.Start();
            vm.OpenElectricity();
            if (Environment.GetEnvironmentVariable("UNIVERSITYTODOS_WATER_HISTORY") == "1")
                vm.Water.ShowHistory = true;
            var query = vm.RefreshWaterCommand.ExecuteAsync(null);
            Avalonia.Controls.NativeWebView? webView = null;
            for (var i = 0; i < 50 && webView is null; i++)
            {
                webView = MainViewModel.WaterWebViewAccessor?.Invoke();
                if (webView is null) await Task.Delay(100);
            }
            if (webView is null)
            {
                Finish(false, "水费 WebView 尚未挂载");
                return;
            }
            webView.NavigationCompleted += (_, e) =>
            {
                Console.WriteLine($"{stopwatch.Elapsed.TotalSeconds:F1}s 导航完成: {e.Request?.Host}{e.Request?.AbsolutePath}");
                if (requireRestoredSession
                    && string.Equals(e.Request?.Host, "sos.zcst.edu.cn", StringComparison.OrdinalIgnoreCase))
                    Finish(false, "恢复持久会话后仍进入统一认证登录页");
            };
            await query;
        };
        window.Show();
    }
}
