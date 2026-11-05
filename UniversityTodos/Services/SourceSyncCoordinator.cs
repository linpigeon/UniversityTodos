using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UniversityTodos.Models;

namespace UniversityTodos.Services;

internal enum ExtraSourceState
{
    NotLoggedIn,
    Success,
    Failed,
}

internal sealed record SourceSyncBatch(
    SyncResult Result,
    bool ChaoxingAuthExpired,
    bool PigaiAuthExpired);

internal sealed record ChaoxingSyncOutcome(
    IReadOnlyList<TodoItem> Items,
    IReadOnlyList<TodoItem> ForCache,
    IReadOnlyList<ChaoxingCourse> Courses,
    ExtraSourceState State,
    bool AuthExpired,
    IReadOnlyList<string> Warnings);

internal sealed record PigaiSyncOutcome(
    IReadOnlyList<TodoItem> Items,
    ExtraSourceState State,
    bool AuthExpired,
    IReadOnlyList<string> Warnings);

internal sealed class SourceSyncDependencies
{
    public Func<IYktApi?, TodoCacheFile?, IProgress<string>?, CancellationToken, Task<SyncResult>>? Ykt { get; init; }
    public Func<string?, TodoCacheFile?, DateTimeOffset, CancellationToken, Task<ChaoxingSyncOutcome>>? Chaoxing { get; init; }
    public Func<string?, CancellationToken, Task<PigaiSyncOutcome>>? Pigai { get; init; }
}

internal static class SourceSyncCoordinator
{
    public static async Task<SourceSyncBatch> SyncAsync(
        IYktApi? api,
        TodoCacheFile? previousCache,
        string? chaoxingCookie,
        string? pigaiCookie,
        IProgress<string>? progress,
        CancellationToken ct,
        SourceSyncDependencies? dependencies = null)
    {
        var yktTask = dependencies?.Ykt is { } ykt
            ? ykt(api, previousCache, progress, ct)
            : api is null
                ? Task.FromResult(new SyncResult())
                : new SyncService(api).SyncAsync(previousCache, progress, ct);
        var chaoxingTask = dependencies?.Chaoxing is { } chaoxingSync
            ? chaoxingSync(chaoxingCookie, previousCache, DateTimeOffset.Now, ct)
            : SyncChaoxingAsync(chaoxingCookie, previousCache, DateTimeOffset.Now, ct);
        var pigaiTask = dependencies?.Pigai is { } pigaiSync
            ? pigaiSync(pigaiCookie, ct)
            : SyncPigaiAsync(pigaiCookie, ct);

        await Task.WhenAll(yktTask, chaoxingTask, pigaiTask).ConfigureAwait(false);

        var chaoxing = chaoxingTask.Result;
        var pigai = pigaiTask.Result;
        var warnings = chaoxing.Warnings.Concat(pigai.Warnings).ToList();
        var result = MergeExtraSources(
            yktTask.Result,
            previousCache,
            chaoxing.Items,
            chaoxing.ForCache,
            chaoxing.Courses,
            chaoxing.State,
            pigai.Items,
            pigai.State,
            warnings);

        return new SourceSyncBatch(result, chaoxing.AuthExpired, pigai.AuthExpired);
    }

    private static async Task<ChaoxingSyncOutcome> SyncChaoxingAsync(
        string? cookie,
        TodoCacheFile? previousCache,
        DateTimeOffset syncedAt,
        CancellationToken ct)
    {
        if (cookie is null) return new([], [], [], ExtraSourceState.NotLoggedIn, false, []);

        try
        {
            var itemsTask = ChaoxingSyncService.SyncAsync(cookie, syncedAt, ct);
            var coursesTask = ChaoxingSyncService.SyncCoursesAsync(
                cookie, null, previousCache?.ChaoxingCourses, ct);
            await Task.WhenAll(itemsTask, coursesTask).ConfigureAwait(false);

            var (items, forCache) = itemsTask.Result;
            var (courses, warnings) = coursesTask.Result;
            return new(items, forCache, courses, ExtraSourceState.Success, false, warnings);
        }
        catch (ChaoxingAuthExpiredException)
        {
            return new([], [], [], ExtraSourceState.Failed, true, ["学习通登录态已失效，请重新登录。"]);
        }
        catch (ChaoxingApiException ex)
        {
            return new([], [], [], ExtraSourceState.Failed, false, [$"学习通同步失败：{ex.Message}"]);
        }
    }

    private static async Task<PigaiSyncOutcome> SyncPigaiAsync(string? cookie, CancellationToken ct)
    {
        if (cookie is null) return new([], ExtraSourceState.NotLoggedIn, false, []);

        try
        {
            var items = await PigaiSyncService.SyncAsync(cookie, ct).ConfigureAwait(false);
            PigaiLoginService.Log($"sync: 批改网返回 {items.Count} 条未完成作文");
            return new(items.ToList(), ExtraSourceState.Success, false, []);
        }
        catch (PigaiAuthExpiredException)
        {
            PigaiLoginService.Log("sync: 批改网登录态已失效");
            return new([], ExtraSourceState.Failed, true, ["批改网登录态已失效，请重新登录。"]);
        }
        catch (PigaiApiException ex)
        {
            PigaiLoginService.Log($"sync: 批改网同步失败 {ex.Message}");
            return new([], ExtraSourceState.Failed, false, [$"批改网同步失败：{ex.Message}"]);
        }
    }

    internal static SyncResult MergeExtraSources(
        SyncResult result,
        TodoCacheFile? previousCache,
        IReadOnlyList<TodoItem> cxItems,
        IReadOnlyList<TodoItem> cxForCache,
        IReadOnlyList<ChaoxingCourse> cxCourses,
        ExtraSourceState cxState,
        IReadOnlyList<TodoItem> pgItems,
        ExtraSourceState pgState,
        IReadOnlyList<string> extraWarnings)
    {
        static IReadOnlyList<T> Pick<T>(
            ExtraSourceState state, IReadOnlyList<T> fresh, IReadOnlyList<T>? previous) =>
            state == ExtraSourceState.Success ? fresh : previous ?? [];

        var cxDisplay = Pick(cxState, cxItems, previousCache?.Chaoxing);
        var cxCache = Pick(cxState, cxForCache, previousCache?.Chaoxing);
        var cxCourseList = Pick(cxState, cxCourses, previousCache?.ChaoxingCourses);
        var pgDisplay = Pick(pgState, pgItems, previousCache?.Pigai);

        return new SyncResult
        {
            Homework = result.Homework.Concat(cxDisplay).Concat(pgDisplay).ToList(),
            HomeworkForCache = result.HomeworkForCache,
            ChaoxingForCache = cxCache,
            PigaiForCache = pgDisplay,
            ChaoxingCourses = cxCourseList,
            Announcements = result.Announcements,
            CourseCount = result.CourseCount,
            ActivityCount = result.ActivityCount,
            UnreadNotificationCount = result.UnreadNotificationCount,
            SyncedAt = result.SyncedAt,
            Warnings = result.Warnings.Concat(extraWarnings).ToList(),
            CourseSignatures = result.CourseSignatures,
            CourseFullSyncedAt = result.CourseFullSyncedAt,
            ReusedCourses = result.ReusedCourses,
            ArchivedCourses = result.ArchivedCourses,
        };
    }
}
