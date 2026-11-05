using System;
using UniversityTodos.ViewModels;

namespace UniversityTodos.Desktop.SelfChecks;

internal static class SourceSyncCheck
{
    public static int Run()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        App.DeferInitialLoad = true;
        try
        {
            var vm = new MainViewModel();
            vm.InitializeAsync().GetAwaiter().GetResult();
            Console.WriteLine($"状态     : {vm.State}");
            Console.WriteLine($"同步     : {vm.SyncStatus}");
            Console.WriteLine($"结果     : {vm.StatusDetail}");
            Console.WriteLine($"界面事项 : {vm.List.Count} 项");
            if (!string.IsNullOrWhiteSpace(vm.AuthWarning)) Console.WriteLine(vm.AuthWarning);
            if (vm.HasWarnings) Console.WriteLine(vm.WarningText);
            return vm.State == AppState.Ready && !vm.IsSyncing ? 0 : 4;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"同步自检异常：{ex.GetType().Name}: {ex.Message}");
            return 5;
        }
    }
}
