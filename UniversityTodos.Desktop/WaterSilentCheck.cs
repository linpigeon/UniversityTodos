using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using UniversityTodos.ViewModels;
using UniversityTodos.Views;

namespace UniversityTodos.Desktop;

internal static class WaterSilentCheck
{
    public static void Run()
    {
        App.DeferInitialLoad = true;
        App.WebViewTestBudget = TimeSpan.FromSeconds(35);
        App.WebViewTestHook = (desktop, _) => RunInWindow(desktop);
        Program.BuildAvaloniaApp().StartWithClassicDesktopLifetime(["--water-silent-check-runner"]);
    }

    private static void RunInWindow(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var vm = new MainViewModel();
        var view = new WaterLoginView { DataContext = vm };
        var window = new Window
        {
            Width = 900,
            Height = 700,
            Title = "水费后台导航自检（不使用凭据）",
            Content = view,
        };
        desktop.MainWindow = window;

        var navigated = false;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            Console.WriteLine("FAIL: 后台 WebView 在 20 秒内未完成导航");
            Environment.Exit(1);
        };
        window.Opened += (_, _) =>
        {
            if (MainViewModel.WaterWebViewAccessor?.Invoke() is not { } webView)
            {
                Console.WriteLine("FAIL: 水费 WebView 尚未挂载");
                Environment.Exit(2);
                return;
            }
            webView.NavigationCompleted += (_, e) =>
            {
                if (navigated) return;
                navigated = true;
                timer.Stop();
                Console.WriteLine($"后台导航完成: {e.Request?.Host}");
                var ok = vm.LoginPageOpen == "water" && !vm.LoginOverlayVisible;
                Console.WriteLine(ok
                    ? "PASS: 水费后台宿主已激活，认证覆盖层保持隐藏"
                    : "FAIL: 后台宿主或覆盖层状态不正确");
                Environment.Exit(ok ? 0 : 1);
            };
            timer.Start();
            _ = vm.RefreshWaterCommand.ExecuteAsync(null);
        };
        window.Show();
    }
}
