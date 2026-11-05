using System;
using System.Text;
using UniversityTodos.Services;
using UniversityTodos.ViewModels;

namespace UniversityTodos.Desktop.SelfChecks;

internal static class UpdateCheck
{
    public static int Run(string? targetExeOverride)
    {
        Console.OutputEncoding = Encoding.UTF8;
        MainViewModel.PlatformServices = new DesktopPlatformServices();

        Console.WriteLine($"当前版本      : {UpdateService.CurrentVersion}");
        Console.WriteLine($"替换目标      : {targetExeOverride ?? "(本进程自身)"}");
        Console.WriteLine("正在检查更新…");

        App.DeferInitialLoad = true;
        try
        {
            var vm = new MainViewModel();
            vm.ConfirmUpdateRequested = (_, _) =>
            {
                Console.WriteLine("确认框        : （无头模式，视为用户同意）");
                return System.Threading.Tasks.Task.FromResult(true);
            };

            vm.RunUpdateCheckAsync(autoConfirmSource: "自检", targetExeOverride: targetExeOverride)
              .GetAwaiter().GetResult();

            Console.WriteLine($"状态          : {vm.UpdateStatus}");
            if (!string.IsNullOrEmpty(vm.UpdateDownloadProgress))
                Console.WriteLine($"下载/进度     : {vm.UpdateDownloadProgress}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"自检异常：{ex.GetType().Name}: {ex.Message}");
            return 5;
        }
    }
}
