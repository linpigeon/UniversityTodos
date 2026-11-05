using System;
using Avalonia;
using Avalonia.Threading;
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
        });
        Environment.Exit(SelfCheck.Run(window, renderPath));
    }
}
