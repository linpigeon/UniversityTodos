using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using UniversityTodos.Services;

namespace UniversityTodos.Desktop.SelfChecks;

internal static class JwDialogCheck
{
    public static void Run(TimeSpan budget)
    {
        App.DeferInitialLoad = true;
        App.WebViewTestHook = RunInWindow;
        App.WebViewTestBudget = budget;
        Program.BuildAvaloniaApp().StartWithClassicDesktopLifetime(["--jwcheck-runner"]);
    }

    private static void RunInWindow(IClassicDesktopStyleApplicationLifetime desktop, TimeSpan budget)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        var window = new Window
        {
            Title = "教务弹窗链路测试（宿主窗口）",
            Width = 520,
            Height = 320,
        };
        desktop.MainWindow = window;

        var loginUrl = Environment.GetEnvironmentVariable("UNIVERSITYTODOS_JW_URL");
        if (string.IsNullOrWhiteSpace(loginUrl))
        {
            Console.WriteLine("  未设置 UNIVERSITYTODOS_JW_URL，使用内置示例地址（仅验证弹窗与脚本链路）。");
            loginUrl = "https://example.com/";
        }

        Avalonia.Controls.NativeWebDialog? dialog = null;
        var adapterCreated = false;
        var navCompleted = 0;
        var sawNewWindow = 0;
        var clickedLink = false;
        var injected = false;
        string? scriptResult = null;
        Exception? failure = null;
        var gate = new PageReadyGate();

        window.Opened += (_, _) =>
        {
            try
            {
                dialog = new Avalonia.Controls.NativeWebDialog
                {
                    Title = "教务系统 · 链路测试",
                    CanUserResize = true,
                    ShowFocused = true,
                };
                dialog.Resize(1100, 800);
                dialog.AdapterCreated += (_, _) =>
                {
                    adapterCreated = true;
                    gate.MarkAdapterCreated();
                    Console.WriteLine("  [事件] AdapterCreated");
                };
                dialog.NavigationStarted += (_, e) =>
                {
                    gate.MarkNavigating();
                    Console.WriteLine($"  [事件] NavigationStarted {e.Request}");
                };
                dialog.NavigationCompleted += (_, e) =>
                {
                    navCompleted++;
                    gate.MarkReady();
                    Console.WriteLine($"  [事件] NavigationCompleted success={e.IsSuccess} {e.Request}");
                };
                dialog.NewWindowRequested += (_, e) =>
                {
                    sawNewWindow++;
                    Console.WriteLine($"  [事件] NewWindowRequested {e.Request}");
                    e.Handled = true;
                    try
                    {
                        if (e.Request is not null) dialog!.Navigate(e.Request);
                        Console.WriteLine("         → 已改为同窗口导航（Handled=true）");
                    }
                    catch (Exception ex) { Console.WriteLine("         → 导航失败: " + ex.Message); }
                };

                Console.WriteLine($"  [步骤] 创建弹窗并 Show，目标 {loginUrl}");
                dialog.Show(window);
                Console.WriteLine("  [步骤] Show 返回（未抛异常）");

                if (Environment.GetEnvironmentVariable("UNIVERSITYTODOS_JW_LOCAL") == "1")
                {
                    const string page = "<html><head><meta charset='utf-8'><title>个人课表查询</title></head>"
                        + "<body style='font:14px sans-serif;margin:0;background:#f5f6f8'>"
                        + "<div style='background:#2564CF;color:#fff;padding:14px 24px;font-size:18px'>珠海科技学院 · 教学综合服务平台</div>"
                        + "<div style='padding:18px 20px'>"
                        + "<h3 style='margin:0 0 12px'>个人课表查询（本地模拟页）</h3>"
                        + "<p style='color:#444'>用于验证：弹窗为独立窗口、二级页在同一窗口内跳转、悬浮球正常注入。</p>"
                        + "<p><a id='go' href='second.html' target='_blank' style='color:#2564CF'>→ 点我打开二级页（target=_blank，应在本窗口内跳转）</a></p>"
                        + "<table border='1' cellspacing='0' cellpadding='8' style='border-collapse:collapse;margin-top:16px;background:#fff'>"
                        + "<tr style='background:#eef3fb'><th>节次</th><th>星期一</th><th>星期二</th><th>星期三</th><th>星期四</th><th>星期五</th></tr>"
                        + "<tr><td>1-2</td><td>高等数学<br>张三<br>1-16周<br>教一楼101</td><td></td><td>大学物理<br>李四<br>1-8,10-16周<br>理科楼203</td><td></td><td>大学英语<br>王五<br>1-15周(单)<br>外语楼305</td></tr>"
                        + "<tr><td>3-4</td><td></td><td>程序设计基础<br>赵六<br>1-16周<br>机房A</td><td></td><td>体育<br>1-16周(双)<br>田径场</td><td></td></tr>"
                        + "</table></div></body></html>";
                    const string second = "<html><head><meta charset='utf-8'><title>二级页</title></head>"
                        + "<body style='font:14px sans-serif;padding:34px'><h2>二级页已打开</h2>"
                        + "<p>如果你看到本页出现在<b>同一个弹窗</b>中（而不是新窗口），说明拦截生效。</p></body></html>";
                    var dir = Path.Combine(Path.GetTempPath(), "hr-jw-local");
                    Directory.CreateDirectory(dir);
                    var pagePath = Path.Combine(dir, "first.html");
                    File.WriteAllText(pagePath, page, System.Text.Encoding.UTF8);
                    File.WriteAllText(Path.Combine(dir, "second.html"), second, System.Text.Encoding.UTF8);
                    dialog.Navigate(new Uri(pagePath));
                    Console.WriteLine($"  [步骤] 已加载本地测试页 {pagePath}");
                }
                else
                {
                    dialog.Navigate(new Uri(loginUrl));
                }

                var probeLog = Path.Combine(Path.GetTempPath(), "hr-jw-probe.log");
                File.WriteAllText(probeLog, $"=== jwcheck {DateTimeOffset.Now:O} ==={Environment.NewLine}");
                void Note(string s)
                {
                    try { File.AppendAllText(probeLog, s + Environment.NewLine); } catch { }
                }

                try
                {
                    var preLoad = dialog.InvokeScript("(()=>{return 'pre';})()");
                    if (preLoad.IsCompleted && preLoad.Exception is { } ex)
                        Note($"[A-前提] 页面未加载即调用 -> {ex.InnerException?.GetType().Name}: {ex.InnerException?.Message}");
                    else
                        Note($"[A-前提] 未复现（status={preLoad.Status}）");
                }
                catch (Exception ex)
                {
                    Note($"[A-前提] 同步抛出 {ex.GetType().Name}: {ex.Message}");
                }

                gate.ResetCounters();
                var ct = new CancellationTokenSource();
                _ = JwCaptureService.RunBallLoopAsync(dialog, gate, () => Task.FromResult("(测试不点击)"), ct.Token);

                DispatcherTimer.RunOnce(() =>
                {
                    Note($"[B-注入循环] 下发次数={gate.InvokeAttempts} 因未就绪被拦下={gate.SkippedNotReady}"
                         + $" PageReady={gate.IsReady} Source={dialog.Source}");
                    Note(gate.InvokeAttempts > 0
                        ? "[B-注入循环] PASS：页面就绪后确实下发了脚本"
                        : "[B-注入循环] FAIL：页面就绪后仍然没有下发过脚本");

                    var closedGate = new PageReadyGate();
                    closedGate.ResetCounters();
                    var blocked = JwCaptureService.CapturePageAsync(dialog, closedGate);
                    Note($"[C-抓取闸门] 未就绪时抓取 -> IsCompleted={blocked.IsCompleted}"
                         + $" Result={(blocked.IsCompleted ? blocked.Result ?? "(null)" : "(pending)")}"
                         + $" 下发次数={closedGate.InvokeAttempts}（期望 0）");
                    Note(closedGate.InvokeAttempts == 0 && closedGate.SkippedNotReady > 0
                        ? "[C-抓取闸门] PASS：未就绪时一次都没有下发 InvokeScript"
                        : "[C-抓取闸门] FAIL：未就绪时仍然下发了脚本");
                    Note($"[D-闸门语义] 全新闸门 IsReady={new PageReadyGate().IsReady}（期望 False）");
                    _ = ProbeValueUnwrapAsync(dialog, Note);
                }, TimeSpan.FromSeconds(7));

                async Task ProbeValueUnwrapAsync(Avalonia.Controls.NativeWebDialog d, Action<string> note)
                {
                    static string Repr(string? s) => s is null
                        ? "(null)"
                        : "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n") + "\"";
                    const string html = "<div class=\"x\">中</div>";
                    var got = await JwCaptureService.TryEvalAsync(
                        d, "(()=>{return " + System.Text.Json.JsonSerializer.Serialize(html) + ";})()")
                        .ConfigureAwait(true);
                    note($"[E-拆壳] 脚本返回 {Repr(html)} -> C# 侧拿到 {Repr(got)}");
                    note(got == html
                        ? "[E-拆壳] PASS：返回值已还原成脚本真正返回的文本"
                        : "[E-拆壳] FAIL：返回值仍带 JSON 壳（解析器会一条课都读不出来）");
                    note("=== jwcheck end ===");
                }
            }
            catch (Exception ex)
            {
                failure = ex;
                Console.WriteLine($"  [失败] 创建/显示弹窗异常：{ex.GetType().Name}: {ex.Message}");
            }
        };

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        timer.Tick += async (_, _) =>
        {
            if (dialog is null) return;
            try
            {
                if (Environment.GetEnvironmentVariable("UNIVERSITYTODOS_JW_LOCAL") == "1" && !clickedLink)
                {
                    clickedLink = true;
                    var r = await JwCaptureService.TryEvalAsync(
                        dialog,
                        "(()=>{var a=document.getElementById('go');if(!a)return 'no-link';a.click();return 'clicked';})()");
                    Console.WriteLine($"  [步骤] 触发二级页点击 → {r}");
                }

                await JwCaptureService.InjectBallAsync(dialog);
                var raw = await JwCaptureService.TryEvalAsync(
                    dialog,
                    "(()=>{var b=document.getElementById('__hrJwBall');if(!b)return 'no-ball';"
                    + "var r=b.getBoundingClientRect();return 'ball '+Math.round(r.width)+'x'+Math.round(r.height)"
                    + "+' right='+Math.round(innerWidth-r.right)+' bottom='+Math.round(innerHeight-r.bottom)"
                    + "+' text='+JSON.stringify(b.textContent);})()");
                scriptResult = raw;
                injected = (raw ?? string.Empty).StartsWith("ball ", StringComparison.Ordinal)
                           && !(raw ?? string.Empty).StartsWith("ball 0x0", StringComparison.Ordinal);
                if (scriptResult is not null)
                    Console.WriteLine($"  [探测] InvokeScript 返回={scriptResult.Trim()} Source={dialog.Source}");
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        };

        var ended = false;
        var deadline = new DispatcherTimer { Interval = budget };
        deadline.Tick += (_, _) =>
        {
            if (ended) return;
            ended = true;
            timer.Stop();
            deadline.Stop();

            Console.WriteLine();
            Console.WriteLine("  === 教务弹窗链路结论 ===");
            Console.WriteLine($"    弹窗创建并 Show      : {(dialog is not null ? "是" : "否")}");
            Console.WriteLine($"    适配器创建(AdapterCreated): {(adapterCreated ? "是" : "否")}");
            Console.WriteLine($"    页面加载完成次数      : {navCompleted}");
            Console.WriteLine($"    拦截新窗口次数        : {sawNewWindow}");
            Console.WriteLine($"    InvokeScript 可返回  : {(scriptResult is not null ? "是" : "否")}");
            Console.WriteLine($"    悬浮球可见(有尺寸且贴右下): {(injected ? "是" : "否")}");
            if (scriptResult is not null) Console.WriteLine($"    悬浮球实测          : {scriptResult.Trim()}");

            var ok = dialog is not null && adapterCreated && navCompleted > 0
                     && scriptResult is not null && failure is null;
            Console.WriteLine($"    结论：{(ok ? "通过 —— 教务弹窗链路可用" : "失败 —— 见上方输出")}");
            if (failure is not null)
                Console.WriteLine($"    异常：{failure.GetType().Name}: {failure.Message}");

            try { dialog?.Close(); dialog?.Dispose(); } catch (Exception) { }
            Environment.Exit(ok ? 0 : 1);
        };

        timer.Start();
        deadline.Start();
        window.Show();
    }
}
