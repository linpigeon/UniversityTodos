using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using UniversityTodos.Models;

namespace UniversityTodos.Services;

/// <summary>自建待办文件（<c>manual-todos.json</c>）。</summary>
public sealed class ManualTodoFile
{
    /// <summary>数据格式版本；高于程序支持版本时进入只读（见 <see cref="DataCompatibility"/>）。</summary>
    public int SchemaVersion { get; set; } = DataCompatibility.CurrentSchemaVersion;

    public List<TodoItem> Items { get; set; } = [];
}

public interface IManualTodoStore
{
    Task<List<TodoItem>> LoadAsync(CancellationToken ct = default);
    Task SaveAsync(IReadOnlyList<TodoItem> items, CancellationToken ct = default);
}

/// <summary>
/// 自建待办的持久化。
/// <para>
/// 刻意与 <c>todos.json</c> 分开：后者是三个平台的同步缓存，每次同步都会整体重写，
/// 混在一起会把用户手写的条目覆盖掉。
/// </para>
/// </summary>
public sealed class ManualTodoStore : IManualTodoStore
{
    private readonly string _path;
    private readonly SemaphoreSlim _writeGate = new(1, 1);

    public ManualTodoStore(string? directory = null)
    {
        var dir = directory ?? AppPaths.DataDirectory;
        _path = Path.Combine(dir, "manual-todos.json");
    }

    public string Location => _path;

    public async Task<List<TodoItem>> LoadAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_path)) return [];
        try
        {
            var text = await File.ReadAllTextAsync(_path, ct).ConfigureAwait(false);
            var file = JsonSerializer.Deserialize(text, AppJsonContext.Default.ManualTodoFile);
            DataCompatibility.Observe(file?.SchemaVersion ?? 0, _path);
            return Validate(file?.Items);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // 损坏时按空处理，但**不删除**原文件——用户还可以手工抢救。
            return [];
        }
    }

    public async Task SaveAsync(IReadOnlyList<TodoItem> items, CancellationToken ct = default)
    {
        if (DataCompatibility.IsReadOnly) return;
        var file = new ManualTodoFile { Items = items.ToList() };
        var json = JsonSerializer.Serialize(file, AppJsonContext.Default.ManualTodoFile);

        await _writeGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var tmp = _path + ".tmp";
            await File.WriteAllTextAsync(tmp, json, ct).ConfigureAwait(false);
            File.Move(tmp, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 写不进去不影响本次会话；界面已经先改了内存。
        }
        finally
        {
            _writeGate.Release();
        }
    }

    /// <summary>校验并清洗条目：只保留自建事项，补齐/去重标签，丢弃坏数据。</summary>
    internal static List<TodoItem> Validate(IEnumerable<TodoItem>? items)
    {
        var result = new List<TodoItem>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in items ?? [])
        {
            if (item is null) continue;
            if (item.Kind != TodoKind.Custom) continue;
            if (string.IsNullOrWhiteSpace(item.Title)) continue;
            if (string.IsNullOrWhiteSpace(item.Id) || !item.Id.StartsWith("man-", StringComparison.Ordinal)) continue;
            if (!seen.Add(item.Id)) continue;

            item.Tags = NormalizeTags(item.Tags);
            if (!Enum.IsDefined(item.Priority)) item.Priority = TodoPriority.Normal;
            result.Add(item);
        }
        return result;
    }

    /// <summary>标签归一：去空白、去重、最多 8 个、单个最多 20 字。</summary>
    internal static List<string> NormalizeTags(IEnumerable<string>? tags)
    {
        if (tags is null) return [];
        return tags
            .Select(t => (t ?? string.Empty).Trim())
            .Where(t => t.Length > 0)
            .Select(t => t.Length > 20 ? t[..20] : t)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToList();
    }
}
