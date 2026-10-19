using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace UniversityTodos.Services;

/// <summary>
/// 应用内的本地状态（勾选完成、已读）。
/// <para>
/// 雨课堂没有「学生把作业标记为完成」的接口——完成状态由作答进度决定。所以应用里的
/// 勾选只影响本地视图，不改动雨课堂。这样勾选不会撒谎成 "已完成"，但也能让你把
/// 不想再看到的事项收起来。文件里记录的 id 是稳定 id，改标题不会丢。
/// </para>
/// </summary>
public interface ILocalStateStore
{
    Task<LocalState> LoadAsync(CancellationToken ct = default);
    Task SaveAsync(LocalState state, CancellationToken ct = default);
}

public sealed class LocalState
{
    /// <summary>数据格式版本；高于程序支持版本时进入只读（见 <see cref="DataCompatibility"/>）。</summary>
    public int SchemaVersion { get; set; } = DataCompatibility.CurrentSchemaVersion;

    /// <summary>在应用里被勾掉的事项 id。</summary>
    public HashSet<string> Done { get; set; } = [];

    /// <summary>在应用里标记为已读的公告 id。</summary>
    public HashSet<string> ReadAnnouncements { get; set; } = [];

    /// <summary>星标的事项 id（本地标记，不影响平台数据）。</summary>
    public HashSet<string> Starred { get; set; } = [];

    /// <summary>被用户手动加入「我的一天」的事项 id。</summary>
    public HashSet<string> InMyDay { get; set; } = [];
}
/// <inheritdoc />
public sealed class LocalStateStore : ILocalStateStore
{
    private readonly string _path;

    /// <summary>同一文件的写入串行化（UI 勾选与「全部已读」可能几乎同时发生）。</summary>
    private readonly SemaphoreSlim _writeGate = new(1, 1);

    public LocalStateStore(string? directory = null)
    {
        var dir = directory ?? AppPaths.DataDirectory;
        _path = Path.Combine(dir, "local-state.json");
    }

    public async Task<LocalState> LoadAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_path)) return new LocalState();
        try
        {
            var text = await File.ReadAllTextAsync(_path, ct).ConfigureAwait(false);
            var state = JsonSerializer.Deserialize(text, AppJsonContext.Default.LocalState) ?? new LocalState();
            DataCompatibility.Observe(state.SchemaVersion, _path);
            return state;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new LocalState();
        }
    }

    public async Task SaveAsync(LocalState state, CancellationToken ct = default)
    {
        if (DataCompatibility.IsReadOnly) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var json = JsonSerializer.Serialize(state, AppJsonContext.Default.LocalState);

            // 先写临时文件再原子替换：直接覆盖时若进程被杀/断电，会留下半截 JSON，
            // 下次 LoadAsync 按「损坏」返回空状态 —— 用户的全部勾选与已读会一次性丢失。
            await _writeGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var tmp = _path + ".tmp";
                await File.WriteAllTextAsync(tmp, json, ct).ConfigureAwait(false);
                File.Move(tmp, _path, overwrite: true);
            }
            finally
            {
                _writeGate.Release();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 本地状态写不进去不影响本次会话的使用。
        }
    }
}
