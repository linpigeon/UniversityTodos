using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace HomeworkReminder.Services;

/// <summary>
/// 提醒去重状态（<c>reminder-state.json</c>）。
/// <para>
/// 为什么要落盘：只在内存里记「提醒过谁」的话，每次重启都会重复提醒同一批事项；
/// 而不记状态又会在每次轮询时重复发。日期/时刻统一用 Unix 毫秒，避免时区歧义。
/// </para>
/// </summary>
public sealed class ReminderState
{
    /// <summary>数据格式版本；高于程序支持版本时进入只读（见 <see cref="DataCompatibility"/>）。</summary>
    public int SchemaVersion { get; set; } = DataCompatibility.CurrentSchemaVersion;

    /// <summary>已见过的事项 id（「新事项」基线）。</summary>
    public HashSet<string> KnownIds { get; set; } = [];

    /// <summary>「新事项」基线是否已建立（首次同步只建基线，不通知）。</summary>
    public bool BaselineEstablished { get; set; }

    /// <summary>已提醒过的截止键（事项 id + 截止时刻；改期会变值，从而重新提醒）。</summary>
    public HashSet<string> RemindedDue { get; set; } = [];

    /// <summary>已提醒过的显式提醒键（事项 id + 提醒时刻）。</summary>
    public HashSet<string> RemindedExplicit { get; set; } = [];

    /// <summary>上一次发逾期汇总的日期（yyyy-MM-dd）。</summary>
    public string? LastOverdueDigestDate { get; set; }

    /// <summary>上一次检查的时刻（Unix ms）；用于休眠/挂起恢复后的补发判定。</summary>
    public long? LastCheckUnixMs { get; set; }
}

public interface IReminderStateStore
{
    Task<ReminderState> LoadAsync(CancellationToken ct = default);
    Task SaveAsync(ReminderState state, CancellationToken ct = default);
}

/// <summary>提醒去重状态的持久化（临时文件 + 原子替换）。</summary>
public sealed class ReminderStateStore : IReminderStateStore
{
    private readonly string _path;
    private readonly SemaphoreSlim _writeGate = new(1, 1);

    public ReminderStateStore(string? directory = null)
    {
        var dir = directory ?? AppPaths.DataDirectory;
        _path = Path.Combine(dir, "reminder-state.json");
    }

    public string Location => _path;

    public async Task<ReminderState> LoadAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_path)) return new ReminderState();
        try
        {
            var text = await File.ReadAllTextAsync(_path, ct).ConfigureAwait(false);
            var state = JsonSerializer.Deserialize(text, AppJsonContext.Default.ReminderState);
            DataCompatibility.Observe(state?.SchemaVersion ?? 0, _path);
            return state ?? new ReminderState();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new ReminderState();
        }
    }

    public async Task SaveAsync(ReminderState state, CancellationToken ct = default)
    {
        if (DataCompatibility.IsReadOnly) return;
        var json = JsonSerializer.Serialize(state, AppJsonContext.Default.ReminderState);
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
            // 状态写不进去最多导致重启后重复提醒一次，不影响主流程。
        }
        finally
        {
            _writeGate.Release();
        }
    }
}
