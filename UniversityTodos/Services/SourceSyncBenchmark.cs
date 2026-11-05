using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace UniversityTodos.Services;

public static class SourceSyncBenchmark
{
    public static async Task<int> RunAsync(CancellationToken ct = default)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        var yktSession = await new SessionStore().LoadAsync(ct).ConfigureAwait(false);
        var chaoxingSession = await new ChaoxingSessionStore().LoadAsync(ct).ConfigureAwait(false);
        var pigaiSession = await new PigaiSessionStore().LoadAsync(ct).ConfigureAwait(false);

        if (yktSession is not { HasValue: true }
            || chaoxingSession is not { HasValue: true }
            || pigaiSession is not { HasValue: true })
        {
            Console.WriteLine("需要雨课堂、学习通、批改网三端都已登录，才能运行三源在线基准。");
            return 2;
        }

        var session = yktSession.ToSession();
        if (session.IsExpired)
        {
            Console.WriteLine("雨课堂登录态已过期，请重新登录后再测。");
            return 2;
        }

        using var api = new YktApiClient(session);
        var previousCache = await new TodoCacheStore().LoadAsync(ct).ConfigureAwait(false);
        var total = Stopwatch.StartNew();
        var durations = new ConcurrentDictionary<string, TimeSpan>(StringComparer.Ordinal);

        async Task<T> Measure<T>(string name, Func<Task<T>> action)
        {
            var started = total.Elapsed;
            Console.WriteLine($"[{started:mm\\:ss\\.fff}] {name}：开始");
            var stopwatch = Stopwatch.StartNew();
            try
            {
                return await action().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{total.Elapsed:mm\\:ss\\.fff}] {name}：{ex.GetType().Name}: {ex.Message}");
                throw;
            }
            finally
            {
                stopwatch.Stop();
                durations[name] = stopwatch.Elapsed;
                Console.WriteLine($"[{total.Elapsed:mm\\:ss\\.fff}] {name}：完成，耗时 {stopwatch.Elapsed.TotalSeconds:F2} 秒");
            }
        }

        var dependencies = new SourceSyncDependencies
        {
            Ykt = (ykt, previous, _, token) => Measure("雨课堂", () =>
                new SyncService(ykt!).SyncAsync(previous, null, token)),
            Chaoxing = (cookie, previous, syncedAt, token) => Measure("学习通", async () =>
            {
                var itemsTask = Measure("学习通·作业考试", () =>
                    ChaoxingSyncService.SyncAsync(cookie!, syncedAt, token));
                var coursesTask = Measure("学习通·课程任务点", () =>
                    ChaoxingSyncService.SyncCoursesAsync(
                        cookie!, null, previous?.ChaoxingCourses, token));
                await Task.WhenAll(itemsTask, coursesTask).ConfigureAwait(false);
                var (items, forCache) = itemsTask.Result;
                var (courses, warnings) = coursesTask.Result;
                return new ChaoxingSyncOutcome(
                    items, forCache, courses, ExtraSourceState.Success, false, warnings);
            }),
            Pigai = (cookie, token) => Measure("批改网", async () =>
            {
                var items = await PigaiSyncService.SyncAsync(cookie!, token).ConfigureAwait(false);
                return new PigaiSyncOutcome(items.ToList(), ExtraSourceState.Success, false, []);
            }),
        };

        SourceSyncBatch? batch = null;
        string? failure = null;
        try
        {
            batch = await SourceSyncCoordinator.SyncAsync(
                api, previousCache, chaoxingSession.CookieHeader, pigaiSession.CookieHeader,
                null, ct, dependencies).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            failure = $"{ex.GetType().Name}: {ex.Message}";
        }
        total.Stop();

        Console.WriteLine();
        Console.WriteLine("=== 三源并行实测 ===");
        foreach (var name in new[] { "雨课堂", "学习通", "学习通·作业考试", "学习通·课程任务点", "批改网" })
        {
            if (durations.TryGetValue(name, out var elapsed))
                Console.WriteLine($"{name,-12}: {elapsed.TotalSeconds,8:F2} 秒");
        }

        var outer = new[] { "雨课堂", "学习通", "批改网" }
            .Where(durations.ContainsKey)
            .Select(name => durations[name])
            .ToList();
        var sequential = outer.Aggregate(TimeSpan.Zero, (sum, elapsed) => sum + elapsed);
        Console.WriteLine($"并行实际总耗时    : {total.Elapsed.TotalSeconds:F2} 秒");
        Console.WriteLine($"串行估算总耗时    : {sequential.TotalSeconds:F2} 秒（本轮各端耗时求和）");
        if (total.Elapsed > TimeSpan.Zero && sequential > TimeSpan.Zero)
            Console.WriteLine($"估算节省          : {(sequential - total.Elapsed).TotalSeconds:F2} 秒"
                              + $"（{(1 - total.Elapsed / sequential) * 100:F1}%）");

        if (batch is not null)
        {
            Console.WriteLine($"合并结果          : {batch.Result.Homework.Count} 项作业，"
                              + $"{batch.Result.ChaoxingCourses.Count} 门学习通课程，"
                              + $"{batch.Result.Announcements.Count} 条公告");
            foreach (var warning in batch.Result.Warnings) Console.WriteLine($"警告：{warning}");
        }
        if (failure is not null)
        {
            Console.WriteLine($"同步失败          : {failure}");
            return 3;
        }

        Console.WriteLine("在线基准完成：三端请求均已返回；未写入待办缓存。");
        return 0;
    }
}
