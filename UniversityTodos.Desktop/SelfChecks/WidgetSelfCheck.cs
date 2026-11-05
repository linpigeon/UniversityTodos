using System;
using Avalonia;
using Avalonia.Threading;
using UniversityTodos.Views;

namespace UniversityTodos.Desktop.SelfChecks;

internal static class WidgetSelfCheck
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

        var widget = new WidgetWindow(vm);
        widget.SelectGroupForCheck("all");
        Environment.Exit(SelfCheck.Run(widget, renderPath));
    }
}
