using System.Collections.Generic;
using System.Linq;
using UniversityTodos.Models;
using UniversityTodos.ViewModels;

namespace UniversityTodos.Services;

internal sealed record TodoCacheReconciliation(
    IReadOnlyList<TodoItemViewModel> Items,
    int ReusedCount);

internal static class TodoCacheCoordinator
{
    public static TodoCacheReconciliation Reconcile(
        IReadOnlyList<TodoItemViewModel> current,
        SyncResult result,
        IReadOnlyList<TodoItem> manualTodos,
        LocalState localState)
    {
        var existing = current
            .GroupBy(i => i.Id)
            .ToDictionary(g => g.Key, g => g.First());
        var models = result.Homework
            .Concat(result.Announcements)
            .Concat(manualTodos);
        var items = new List<TodoItemViewModel>();
        var reused = 0;

        foreach (var model in models)
        {
            var isDone = model.Kind == TodoKind.Announcement
                ? localState.ReadAnnouncements.Contains(model.Id) || model.IsRead
                : localState.Done.Contains(model.Id);

            if (existing.TryGetValue(model.Id, out var item)
                && (ReferenceEquals(item.Model, model) || Equivalent(item.Model, model)))
            {
                if (ReferenceEquals(item.Model, model)) item.RefreshModelBindings();
                item.IsDone = isDone;
                item.IsStarred = localState.Starred.Contains(model.Id);
                item.InMyDay = localState.InMyDay.Contains(model.Id);
                items.Add(item);
                reused++;
                continue;
            }

            var replacement = new TodoItemViewModel(model)
            {
                IsDone = isDone,
                IsStarred = localState.Starred.Contains(model.Id),
                InMyDay = localState.InMyDay.Contains(model.Id),
            };
            if (existing.TryGetValue(model.Id, out var previous))
                replacement.IsSelected = previous.IsSelected;
            items.Add(replacement);
        }

        return new TodoCacheReconciliation(items, reused);
    }

    internal static bool Equivalent(TodoItem left, TodoItem right) =>
        left.Id == right.Id
        && left.Kind == right.Kind
        && left.Title == right.Title
        && left.CourseName == right.CourseName
        && left.Subject == right.Subject
        && left.ClassroomId == right.ClassroomId
        && left.DueAt == right.DueAt
        && left.DeadlineAnchor == right.DeadlineAnchor
        && left.RemainingAtSync == right.RemainingAtSync
        && left.PublishedAt == right.PublishedAt
        && left.Status == right.Status
        && left.Done == right.Done
        && left.Total == right.Total
        && left.Score == right.Score
        && left.SourceUrl == right.SourceUrl
        && left.NoticeId == right.NoticeId
        && left.Priority == right.Priority
        && left.Tags.SequenceEqual(right.Tags)
        && left.Note == right.Note
        && left.RemindAt == right.RemindAt
        && left.IsRead == right.IsRead;
}
