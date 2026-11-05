using System;
using System.Linq;
using Avalonia;
using Avalonia.Threading;
using UniversityTodos.ViewModels;
using UniversityTodos.Views;

namespace UniversityTodos.Desktop.SelfChecks;

internal static class MainSelfCheck
{
    public static void Run(string? renderPath)
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

        if (vm.State == AppState.Ready)
        {
            var navKey = Environment.GetEnvironmentVariable("UNIVERSITYTODOS_SELFCHECK_NAV") ?? "homework";
            vm.SelectedNav = vm.NavItems.FirstOrDefault(n => n.Key == navKey) ?? vm.NavItems[0];
            vm.OnNavChanged();
        }

        Console.WriteLine(
            $"自检: state={vm.State} nav={vm.SelectedNav?.Key} items={vm.ScheduleItemCount} "
            + $"week={vm.Timetable.WeekNumber} periods={vm.Timetable.RowCount} "
            + $"blocks={vm.Timetable.Blocks.Count} thisWeek={vm.Timetable.BlockCount}");

        var window = new MainWindow { DataContext = vm };
        Environment.Exit(SelfCheck.Run(window, renderPath));
    }
}
