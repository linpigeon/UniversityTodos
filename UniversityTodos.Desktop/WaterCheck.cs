using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using UniversityTodos.Services;

namespace UniversityTodos.Desktop;

internal static class WaterCheck
{
    public static void Run()
    {
        var account = Environment.GetEnvironmentVariable("ZCST_WATER_ACCOUNT");
        var password = Environment.GetEnvironmentVariable("ZCST_WATER_PASSWORD");
        if (string.IsNullOrWhiteSpace(account) || string.IsNullOrWhiteSpace(password))
        {
            Console.WriteLine("请通过 ZCST_WATER_ACCOUNT / ZCST_WATER_PASSWORD 环境变量提供统一认证账号。");
            Environment.Exit(2);
        }

        App.DeferInitialLoad = true;
        App.WebViewTestBudget = TimeSpan.FromMinutes(2);
        App.WebViewTestHook = (desktop, _) => RunInWindow(desktop, account, password);
        Program.BuildAvaloniaApp().StartWithClassicDesktopLifetime(["--watercheck-runner"]);
    }

    private static void RunInWindow(
        IClassicDesktopStyleApplicationLifetime desktop, string account, string password)
    {
        var webView = new NativeWebView();
        var window = new Window
        {
            Width = 1000,
            Height = 760,
            Title = "水费查询链路自检",
            Content = webView,
        };
        desktop.MainWindow = window;
        window.Opened += async (_, _) => await QueryAsync(webView, window, account, password);
        window.Show();
    }

    private static async Task QueryAsync(
        NativeWebView webView, Window window, string account, string password)
    {
        var bills = await WaterService.WaitForLatestAsync(
            webView,
            new Progress<string>(message => Console.WriteLine(message)),
            TimeSpan.FromMinutes(2),
            account,
            password);
        window.Close();
        if (bills is null || bills.Count == 0)
        {
            Console.WriteLine("FAIL: 未读取到水费账单");
            Environment.Exit(1);
        }

        foreach (var bill in bills)
            Console.WriteLine($"{bill.StartDate}–{bill.EndDate} {bill.WaterType} {bill.Usage} m³ {bill.Fee} 元");
        Console.WriteLine($"PASS: {bills.Count} 条，合计 {bills.Sum(b => b.Fee):F2} 元");
        Environment.Exit(0);
    }

}
