using System.Threading.Tasks;

namespace UniversityTodos.Services;

/// <summary>
/// 「不再 await 的任务」的观察工具。
/// <para>
/// <see cref="Task.WhenAny(Task[])"/> 抢输的那一侧、超时用的 <see cref="Task.Delay(TimeSpan, System.Threading.CancellationToken)"/>
/// 计时器、以及被取消后丢弃的探测任务，如果没人读取它们的 <see cref="Task.Exception"/>，
/// 取消/异常会在 GC 时冒到 <see cref="TaskScheduler.UnobservedTaskException"/>，
/// 在日志里表现为「UNOBSERVED TASK: A task was canceled.」，在调试器里表现为
/// 莫名其妙的「引发的异常: TaskCanceledException」。
/// </para>
/// <para>
/// 观察一次即可：读取 <c>Exception</c> 会把任务标记为已观察，且不会阻塞。
/// </para>
/// </summary>
internal static class TaskObservation
{
    /// <summary>把任务标记为已观察（成功、失败、取消都适用）。</summary>
    public static void Observe(this Task task) =>
        _ = task.ContinueWith(static t => _ = t.Exception, TaskScheduler.Default);
}
