using System;
using System.Linq;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using UniversityTodos.ViewModels;
using UniversityTodos.Views;

namespace UniversityTodos.Desktop.SelfChecks;

internal static class SettingsSelfCheck
{
    public static void Run(string? renderPath, int settingsTab)
    {
        App.DeferInitialLoad = true;
        Program.BuildAvaloniaApp().SetupWithoutStarting();

        var app = (App)Application.Current!;
        var vm = app.ViewModel ?? throw new InvalidOperationException("App did not create a view model");
        var initTask = Dispatcher.UIThread.InvokeAsync(async () =>
        {
            try
            {
                await vm.InitializeAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"数据初始化失败（继续做界面自检）：{ex.Message}");
            }
        });
        SelfCheckDispatcher.PumpUntil(initTask, TimeSpan.FromMinutes(4));

        if (vm.State != AppState.Ready) vm.State = AppState.Ready;

        var window = new MainWindow { DataContext = vm };
        window.Opened += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            vm.OpenSettings();
            if (settingsTab != 0) vm.ShowSettingsTab(settingsTab);

            if (Environment.GetEnvironmentVariable("SETTINGS_BAD_PERIODS") == "1"
                && vm.PeriodsEditor.Rows.Count >= 2)
            {
                vm.PeriodsEditor.Rows[0].StartTime = "10:00";
                vm.PeriodsEditor.Rows[0].EndTime = "10:45";
                vm.PeriodsEditor.Rows[1].StartTime = "10:30";
                vm.PeriodsEditor.Rows[1].EndTime = "11:15";
                _ = vm.PeriodsEditor.SaveAsync();
            }

            var timePickers = window.GetVisualDescendants().OfType<TimePicker>().ToList();
            var requiredTimePickers = settingsTab == 2 ? 2 : 1;
            if (timePickers.Count < requiredTimePickers)
                throw new InvalidOperationException($"设置页时间选择器数量不足：{timePickers.Count}");
            if (settingsTab == 0
                && !window.GetVisualDescendants().OfType<CalendarDatePicker>().Any())
                throw new InvalidOperationException("学期开始日期未使用日历选择控件");
            if (window.GetVisualDescendants().OfType<TextBox>()
                .Any(box => box.Classes.Contains("period-time")))
                throw new InvalidOperationException("节次时间仍残留手动文本框");

            if (Environment.GetEnvironmentVariable("SETTINGS_SCHEDULE_MANAGER") == "1")
            {
                _ = vm.OpenScheduleManagerAsync(window);
                for (var i = 0; i < 100 && !window.OwnedWindows.Any(w => w.Title == "课表管理"); i++)
                {
                    Dispatcher.UIThread.RunJobs();
                    Thread.Sleep(30);
                }
                var dialog = window.OwnedWindows.FirstOrDefault(w => w.Title == "课表管理")
                    ?? throw new InvalidOperationException("课表管理窗口未打开");
                if (dialog.GetVisualDescendants().OfType<TimePicker>().Count() != 2)
                    throw new InvalidOperationException("课表管理窗口应包含两个可选时间选择器");
                dialog.Close();
            }
        });
        Environment.Exit(SelfCheck.Run(window, renderPath));
    }
}
