using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using UniversityTodos.Services;

namespace UniversityTodos.Desktop.SelfChecks;

internal static class WebViewCheck
{
    public static void Run(TimeSpan budget)
    {
        App.DeferInitialLoad = true;
        App.WebViewTestHook = RunInWindow;
        App.WebViewTestBudget = budget;
        Program.BuildAvaloniaApp().StartWithClassicDesktopLifetime(["--webtest-runner"]);
    }

    private static void RunInWindow(IClassicDesktopStyleApplicationLifetime desktop, TimeSpan budget)
    {
        var webView = new Avalonia.Controls.NativeWebView();
        var window = new Window
        {
            Title = "WebView 链路测试",
            Width = 900,
            Height = 650,
            Content = webView,
        };
        desktop.MainWindow = window;
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        var adapterCreated = false;
        webView.AdapterCreated += (_, _) =>
        {
            adapterCreated = true;
            Console.WriteLine("  [事件] AdapterCreated");
        };
        webView.NavigationStarted += (_, e) => Console.WriteLine($"  [事件] NavigationStarted {e.Request}");
        webView.NavigationCompleted += (_, e) => Console.WriteLine($"  [事件] NavigationCompleted success={e.IsSuccess}");

        string? lastStatus = null;
        var probes = 0;
        Exception? failure = null;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += async (_, _) =>
        {
            try
            {
                var status = await YktLoginService.TryProbeAsync(webView);
                probes++;
                if (status is not null)
                {
                    lastStatus = status;
                    Console.WriteLine($"  [探测 {probes}] Source={webView.Source} 返回={status}");
                }
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        };

        window.Opened += (_, _) =>
        {
            try
            {
                webView.Navigate(new Uri(YktLoginService.LoginPageUrl));
            }
            catch (Exception ex)
            {
                failure = ex;
            }

            timer.Start();
            DispatcherTimer.RunOnce(async () =>
            {
                Console.WriteLine("=== InvokeScript 返回值形态 ===");
                foreach (var (label, script) in new (string, string)[]
                {
                    ("普通表达式返回字符串", "'abc'"),
                    ("async IIFE 返回字符串", "(async()=>{return 'xyz';})()"),
                    ("async IIFE 返回数字字符串", "(async()=>{return String(200);})()"),
                    ("同步 return String(200)", "String(200)"),
                    ("await 后直接返回数字", "(async()=>{await Promise.resolve();return 200;})()"),
                })
                {
                    try
                    {
                        var raw = await webView.InvokeScript(script);
                        Console.WriteLine($"  {label,-28} -> {raw ?? "(null)"}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"  {label,-28} -> 异常 {ex.GetType().Name}");
                    }
                }
            }, TimeSpan.FromSeconds(6));

            DispatcherTimer.RunOnce(() =>
            {
                timer.Stop();
                Console.WriteLine("=== WebView 链路测试 ===");
                Console.WriteLine($"  AdapterCreated : {adapterCreated}");
                Console.WriteLine($"  探测成功次数   : {probes}");
                Console.WriteLine($"  最后一次状态码 : {lastStatus ?? "(从未拿到)"}");
                var cookies = webView.TryGetCookieManager();
                Console.WriteLine($"  CookieManager  : {(cookies is null ? "不可用" : "可用")}");
                if (failure is not null) Console.WriteLine($"  异常           : {failure}");

                var ok = probes > 0 && lastStatus is not null && failure is null;
                Console.WriteLine(ok
                    ? "  结论：WebView 调用在 UI 线程上正常返回 —— 链路通畅。"
                    : "  结论：失败 —— 仍可能在 WebView 调用上挂死或环境缺 WebView2。");
                Environment.Exit(ok ? 0 : 5);
            }, budget);
        };

        window.Show();
    }
}
