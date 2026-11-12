using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using UniversityTodos.Models;

namespace UniversityTodos.Services;

public sealed class ElectricityReminderService : IDisposable
{
    private readonly Func<CancellationToken, Task<IReadOnlyList<ElectricityRoom>?>> _refresh;
    private readonly Func<double> _threshold;
    private readonly Func<bool> _notificationsEnabled;
    private readonly INotifier _notifier;
    private readonly string _statePath;
    private readonly SemaphoreSlim _observeGate = new(1, 1);
    private readonly object _lifecycleLock = new();
    private ElectricityReminderState? _state;
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private Task _shutdown = Task.CompletedTask;

    public ElectricityReminderService(
        Func<CancellationToken, Task<IReadOnlyList<ElectricityRoom>?>> refresh,
        Func<double> threshold,
        Func<bool> notificationsEnabled,
        INotifier notifier,
        string? directory = null)
    {
        _refresh = refresh;
        _threshold = threshold;
        _notificationsEnabled = notificationsEnabled;
        _notifier = notifier;
        _statePath = Path.Combine(directory ?? AppPaths.DataDirectory, "electricity-reminder-state.json");
    }

    public int IntervalMinutes { get; private set; }
    public string? LastReminderText { get; private set; }
    public bool IsRunning
    {
        get { lock (_lifecycleLock) return _loop is { IsCompleted: false } || !_shutdown.IsCompleted; }
    }

    public async Task StartAsync(int intervalMinutes)
    {
        await StopAsync().ConfigureAwait(false);
        IntervalMinutes = AppSettings.NormalizeRefreshMinutes(intervalMinutes);
        if (IntervalMinutes <= 0) return;

        lock (_lifecycleLock)
        {
            var cts = new CancellationTokenSource();
            _cts = cts;
            _loop = Task.Run(() => LoopAsync(cts.Token), cts.Token);
        }
    }

    public Task StopAsync()
    {
        lock (_lifecycleLock)
        {
            if (_cts is null) return _shutdown;
            var cts = _cts;
            var loop = _loop;
            _cts = null;
            _loop = null;
            _shutdown = StopCoreAsync(cts, loop);
            return _shutdown;
        }
    }

    private static async Task StopCoreAsync(CancellationTokenSource cts, Task? loop)
    {
        try { cts.Cancel(); } catch (ObjectDisposedException) { }
        if (loop is not null)
        {
            try { await loop.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }
        cts.Dispose();
    }

    public async Task<int> CheckOnceAsync(CancellationToken ct = default)
    {
        var rooms = await _refresh(ct).ConfigureAwait(false);
        return rooms is null ? 0 : await ObserveAsync(rooms, ct).ConfigureAwait(false);
    }

    public async Task<int> ObserveAsync(IReadOnlyList<ElectricityRoom> rooms, CancellationToken ct = default)
    {
        await _observeGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            _state ??= await LoadStateAsync(ct).ConfigureAwait(false);
            var state = _state;
            var threshold = _threshold();
            var lowRooms = rooms.Where(r => r.Odd < threshold).ToList();
            var lowIds = lowRooms.Select(r => r.RoomVerify).ToHashSet(StringComparer.Ordinal);

            if (!state.BaselineEstablished)
            {
                state.BaselineEstablished = true;
                state.LowRoomIds = lowIds;
                await SaveStateAsync(state, ct).ConfigureAwait(false);
                return 0;
            }

            var fresh = lowRooms.Where(r => !state.LowRoomIds.Contains(r.RoomVerify)).ToList();
            if (fresh.Count == 0)
            {
                state.LowRoomIds = lowIds;
                await SaveStateAsync(state, ct).ConfigureAwait(false);
                return 0;
            }
            if (!_notificationsEnabled() || !_notifier.IsAvailable) return 0;

            var notification = BuildNotification(fresh, threshold);
            var error = _notifier.Show(notification);
            if (error is not null) return 0;

            state.LowRoomIds = lowIds;
            await SaveStateAsync(state, ct).ConfigureAwait(false);
            LastReminderText = $"{notification.Title} · {notification.Body}";
            return 1;
        }
        finally
        {
            _observeGate.Release();
        }
    }

    public async Task ResetBaselineAsync(CancellationToken ct = default)
    {
        await _observeGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            _state = new ElectricityReminderState();
            await SaveStateAsync(_state, ct).ConfigureAwait(false);
        }
        finally
        {
            _observeGate.Release();
        }
    }

    public static AppNotification BuildNotification(IReadOnlyList<ElectricityRoom> rooms, double threshold)
    {
        var body = rooms.Count == 1
            ? $"{rooms[0].RoomName} 仅剩 {rooms[0].Odd:F2} 度"
            : string.Join("\n", rooms.Take(3).Select(r => $"· {r.RoomName}：{r.Odd:F2} 度"))
              + (rooms.Count > 3 ? $"\n…另有 {rooms.Count - 3} 个宿舍" : string.Empty);
        return new AppNotification(
            "宿舍电费提醒",
            body,
            $"低于 {threshold:F2} 度，请及时充值");
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                try { await CheckOnceAsync(ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
                catch (Exception) { }
                await Task.Delay(TimeSpan.FromMinutes(IntervalMinutes), ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task<ElectricityReminderState> LoadStateAsync(CancellationToken ct)
    {
        if (!File.Exists(_statePath)) return new ElectricityReminderState();
        try
        {
            var text = await File.ReadAllTextAsync(_statePath, ct).ConfigureAwait(false);
            var state = JsonSerializer.Deserialize(text, AppJsonContext.Default.ElectricityReminderState)
                ?? new ElectricityReminderState();
            DataCompatibility.Observe(state.SchemaVersion, _statePath);
            return state;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new ElectricityReminderState();
        }
    }

    private async Task SaveStateAsync(ElectricityReminderState state, CancellationToken ct)
    {
        if (DataCompatibility.IsReadOnly) return;
        Directory.CreateDirectory(Path.GetDirectoryName(_statePath)!);
        var json = JsonSerializer.Serialize(state, AppJsonContext.Default.ElectricityReminderState);
        var temp = _statePath + ".tmp";
        await File.WriteAllTextAsync(temp, json, ct).ConfigureAwait(false);
        File.Move(temp, _statePath, overwrite: true);
    }

    public void Dispose()
    {
        try { StopAsync().Wait(TimeSpan.FromSeconds(3)); }
        catch (Exception) { }
        _observeGate.Dispose();
    }
}
