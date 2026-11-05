using System;
using System.Threading.Tasks;
using Avalonia.Threading;

namespace UniversityTodos.Desktop.SelfChecks;

internal static class SelfCheckDispatcher
{
    public static void PumpUntil(Task task, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.Now + timeout;
        while (!task.IsCompleted && DateTimeOffset.Now < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            System.Threading.Thread.Sleep(20);
        }

        if (!task.IsCompleted)
            Console.WriteLine("自检：等待数据初始化超时，继续做界面自检。");
    }
}
