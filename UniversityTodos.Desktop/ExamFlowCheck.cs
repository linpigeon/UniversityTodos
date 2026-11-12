using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Avalonia.VisualTree;
using UniversityTodos.Services;
using UniversityTodos.ViewModels;
using UniversityTodos.Views;

namespace UniversityTodos.Desktop;

internal static class ExamFlowCheck
{
    public static void Run()
    {
        var profile = new JwProfileStore().LoadAsync().GetAwaiter().GetResult();
        if (string.IsNullOrWhiteSpace(profile.LoginUrl))
        {
            Console.WriteLine("未配置教务系统网址，无法执行考试主流程自检");
            Environment.Exit(2);
            return;
        }

        App.DeferInitialLoad = true;
        App.WebViewTestBudget = TimeSpan.FromMinutes(2);
        App.WebViewTestHook = (desktop, _) => RunInWindow(desktop, profile.LoginUrl);
        Program.BuildAvaloniaApp().StartWithClassicDesktopLifetime(["--exam-flow-check-runner"]);
    }

    private static void RunInWindow(IClassicDesktopStyleApplicationLifetime desktop, string loginUrl)
    {
        var vm = new MainViewModel
        {
            JwLoginUrlInput = loginUrl,
        };
        vm.ExamInfo.AcademicYear = 2025;
        vm.ExamInfo.SemesterIndex = 1;
        vm.OpenExamInfo();
        var window = new MainWindow
        {
            DataContext = vm,
            Title = "考试信息后台主窗口自检",
        };
        desktop.MainWindow = window;
        var finished = false;
        var started = false;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(100) };

        void Finish(bool success, string message)
        {
            if (finished) return;
            finished = true;
            timer.Stop();
            Console.WriteLine($"{(success ? "PASS" : "FAIL")}: {message}");
            vm.CloseJwImport();
            if (success && Environment.GetEnvironmentVariable("UNIVERSITYTODOS_EXAM_HOLD") == "1")
            {
                _ = Task.Delay(TimeSpan.FromSeconds(10)).ContinueWith(_ => Environment.Exit(0));
                return;
            }
            Environment.Exit(success ? 0 : 1);
        }

        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.JwSessionStatus)
                && Environment.GetEnvironmentVariable("UNIVERSITYTODOS_EXAM_EXPECT_MANUAL") == "1"
                && vm.JwSessionStatus.Contains("手动登录", StringComparison.Ordinal))
                Finish(true, "会话缺失时已显示失效横幅，并将教务窗口切到前台手动登录");
        };
        vm.ExamInfo.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(vm.ExamInfo.IsLoading) || finished) return;
            if (vm.ExamInfo.IsLoading)
            {
                started = true;
                return;
            }
            if (!started) return;
            var ok = vm.ExamInfoOpen && !vm.JwImportOpen && vm.ExamInfo.Items.Count > 0
                && vm.JwSessionStatus.Contains("已更新", StringComparison.Ordinal);
            Finish(ok, ok
                ? $"考试页保持不动，后台返回 {vm.ExamInfo.Items.Count} 场考试；横幅：{vm.JwSessionStatus}"
                : $"后台考试获取失败：{vm.ExamInfo.StatusMessage}；{vm.JwSessionStatus}");
        };
        timer.Tick += (_, _) => Finish(false, "等待后台登录、考试读取或横幅更新超时");
        window.Opened += async (_, _) =>
        {
            try
            {
                await vm.SaveJwLoginUrlAsync();
                var view = window.GetVisualDescendants().OfType<ExamInfoView>().First();
                timer.Start();
                await view.StartBackgroundFetchAsync();
            }
            catch (Exception ex)
            {
                Finish(false, ex.Message);
            }
        };
        window.Show();
    }
}
