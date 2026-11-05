using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UniversityTodos.Models;
using UniversityTodos.ViewModels;

namespace UniversityTodos.Services;

internal sealed class ManualTodoCoordinator(IManualTodoStore? store = null)
{
    private readonly IManualTodoStore _store = store ?? new ManualTodoStore();
    private readonly List<TodoItem> _items = [];

    public IReadOnlyList<TodoItem> Items => _items;

    public async Task LoadAsync(CancellationToken ct = default)
    {
        var loaded = await _store.LoadAsync(ct).ConfigureAwait(false);
        _items.Clear();
        _items.AddRange(loaded);
    }

    public async Task<string?> AddAsync(MainViewModel.ManualTodoDraft draft, CancellationToken ct = default)
    {
        var title = (draft.Title ?? string.Empty).Trim();
        if (title.Length == 0) return null;

        var item = new TodoItem
        {
            Id = $"man-{Guid.NewGuid():N}",
            Kind = TodoKind.Custom,
            Title = title,
            CourseName = string.Empty,
            Status = TodoStatus.NotStarted,
            DueAt = draft.DueAt,
            Priority = draft.Priority,
            Tags = ManualTodoStore.NormalizeTags(draft.Tags),
            Note = string.IsNullOrWhiteSpace(draft.Note) ? null : draft.Note.Trim(),
            RemindAt = draft.RemindAt,
        };
        _items.Add(item);
        await _store.SaveAsync(_items, ct).ConfigureAwait(false);
        return item.Id;
    }

    public async Task<bool> UpdateAsync(
        string id, MainViewModel.ManualTodoDraft draft, CancellationToken ct = default)
    {
        var target = _items.FirstOrDefault(i => i.Id == id);
        var title = (draft.Title ?? string.Empty).Trim();
        if (target is null || title.Length == 0) return false;

        target.Title = title;
        target.DueAt = draft.DueAt;
        target.Priority = draft.Priority;
        target.Tags = ManualTodoStore.NormalizeTags(draft.Tags);
        target.Note = string.IsNullOrWhiteSpace(draft.Note) ? null : draft.Note.Trim();
        target.RemindAt = draft.RemindAt;
        await _store.SaveAsync(_items, ct).ConfigureAwait(false);
        return true;
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken ct = default)
    {
        if (_items.RemoveAll(i => i.Id == id) == 0) return false;
        await _store.SaveAsync(_items, ct).ConfigureAwait(false);
        return true;
    }
}
