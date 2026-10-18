using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HomeworkReminder.Models;

namespace HomeworkReminder.Services;

/// <summary>
/// 截止提醒与逾期汇总的轮询服务。
/// <para>
/// 与 <see cref="ClassReminderService"/> 同款结构（构造注入取数据/通知器，便于离线测试），
/// 区别是它管的是「截止」而不是「上课」：临近截止、自建事项的显式提醒、每日逾期汇总。
/// 上课提醒仍由 <see cref="ClassReminderService"/> 负责，两者不重叠。
/// </para>
/// <para>
/// 去重状态落盘（<c>reminder-state.json</c>）：进程重启后不会把同一批事项再提醒一遍；
/// 休眠恢复后只补发最近 30 分钟内错过的提醒，不会一次性轰炸。
/// </para>
/// </summary>
public sealed class ReminderService : IDisposable
{
    /// <summary>轮询间隔。30 秒足够贴合「提前 N 分钟」，也能在休眠恢复后尽快补发。</summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(30);

    /// <summary>状态文件的最小落盘间隔（避免每 30 秒写一次盘）。</summary>
    private static readonly TimeSpan PersistInterval = TimeSpan.FromMinutes(5);

    private readonly Func<IReadOnlyList<TodoItem>> _todos;
    private readonly INotifier _notifier;
    private readonly IReminderStateStore _store;
    private readonly Action<string>? _log;
    private readonly Func<DateTimeOffset> _now;
    private readonly object _gate = new();

    private ReminderState? _state;
    private DateTimeOffset _lastPersistAt;
    private CancellationTokenSource? _cts;
    private Task? _loop;

    public ReminderService(
        Func<IReadOnlyList<TodoItem>> todos,
        INotifier notifier,
        IReminderStateStore? store = null,
        Action<string>? log = null,
        Func<DateTimeOffset>? now = null)
    {
        _todos = todos;
        _notifier = notifier;
        _store = store ?? new ReminderStateStore();
        _log = log;
        _now = now ?? (() => DateTimeOffset.Now);
    }

    /// <summary>最近一条提醒的文案（供状态栏/自检观察）。</summary>
    public string? LastReminderText { get; private set; }

    /// <summary>是否有轮询在跑。</summary>
    public bool IsRunning
    {
        get { lock (_gate) return _loop is { IsCompleted: false }; }
    }

    /// <summary>加载去重状态并启动轮询（已在跑则先停）。</summary>
    public async Task StartAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _state ??= await _store.LoadAsync().ConfigureAwait(false);
        _lastPersistAt = _now();

        lock (_gate)
        {
            var cts = new CancellationTokenSource();
            _cts = cts;
            _loop = Task.Run(() => LoopAsync(cts.Token), cts.Token);
        }
        _log?.Invoke("截止提醒：已启动");
    }

    /// <summary>停止轮询。</summary>
    public Task StopAsync()
    {
        CancellationTokenSource? cts;
        Task? loop;
        lock (_gate)
        {
            cts = _cts;
            loop = _loop;
            _cts = null;
            _loop = null;
        }

        if (cts is null) return Task.CompletedTask;
        return StopCoreAsync(cts, loop);
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

    /// <summary>
    /// 检查一轮并发送该发的提醒，返回实际发出的条数。
    /// <para>公开是为了让自检能直接驱动它，不必等真实的 30 秒轮询。</para>
    /// </summary>
    public async Task<int> CheckOnceAsync(CancellationToken ct = default)
    {
        _state ??= await _store.LoadAsync(ct).ConfigureAwait(false);
        var state = _state;
        var now = _now();
        var items = _todos();
        var settings = AppSettings.Current;

        // 通知平台不可用时只推进「上次检查」，不记「已提醒」：
        // 等平台恢复后，仍在窗口内的事项还能正常提醒，也不会每轮刷失败日志。
        if (!_notifier.IsAvailable)
        {
            state.LastCheckUnixMs = now.ToUnixTimeMilliseconds();
            await MaybePersistAsync(force: false, ct).ConfigureAwait(false);
            return 0;
        }

        var sent = 0;
        var lastCheck = state.LastCheckUnixMs is { } ms
            ? DateTimeOffset.FromUnixTimeMilliseconds(ms)
            : (DateTimeOffset?)null;

        var hits = ReminderRules.Evaluate(
            items,
            now,
            settings.NotifyDueSoon ? AppSettings.NormalizeDueSoonMinutes(settings.DueSoonMinutes) : 0,
            state.RemindedDue,
            state.RemindedExplicit,
            lastCheck);

        foreach (var hit in hits)
        {
            ct.ThrowIfCancellationRequested();
            var notification = ReminderRules.BuildNotification(hit);
            var error = _notifier.Show(notification);
            if (error is null)
            {
                if (hit.Kind == ReminderKind.Explicit)
                    state.RemindedExplicit.Add(ReminderRules.ExplicitKey(hit.Item, hit.Target));
                else
                    state.RemindedDue.Add(ReminderRules.DueKey(hit.Item, hit.Target));

                sent++;
                LastReminderText = $"{notification.Title} · {notification.Body}";
                _log?.Invoke($"截止提醒：{notification.Title} {notification.Body}（{notification.Attribution}）");
            }
            else
            {
                _log?.Invoke($"截止提醒发送失败：{error}");
            }
        }

        if (settings.NotifyOverdue
            && ReminderRules.IsDigestDue(now, settings.OverdueDigestTime, state.LastOverdueDigestDate))
        {
            var overdue = ReminderRules.Overdue(items, now);
            if (overdue.Count > 0)
            {
                var notification = new AppNotification(
                    "逾期提醒",
                    $"有 {overdue.Count} 项待办已逾期，请及时处理",
                    "打开应用查看");
                var error = _notifier.Show(notification);
                if (error is null)
                {
                    state.LastOverdueDigestDate = now.ToString("yyyy-MM-dd");
                    sent++;
                    LastReminderText = notification.Body;
                    _log?.Invoke($"逾期汇总：{notification.Body}");
                }
                else
                {
                    _log?.Invoke($"逾期汇总发送失败：{error}");
                }
            }
        }

        // 清理已消失的事项，避免长跑时集合无限增长。
        var itemIds = items.Select(i => i.Id).ToHashSet(StringComparer.Ordinal);
        state.RemindedDue.RemoveWhere(IsGone);
        state.RemindedExplicit.RemoveWhere(IsGone);

        bool IsGone(string key)
        {
            var bar = key.IndexOf('|');
            if (bar <= 0) return true;
            return !itemIds.Contains(key[..bar]);
        }

        state.LastCheckUnixMs = now.ToUnixTimeMilliseconds();
        await MaybePersistAsync(force: sent > 0, ct).ConfigureAwait(false);
        return sent;
    }

    private async Task MaybePersistAsync(bool force, CancellationToken ct)
    {
        if (_state is null) return;
        var now = _now();
        if (!force && now - _lastPersistAt < PersistInterval) return;
        await _store.SaveAsync(_state, ct).ConfigureAwait(false);
        _lastPersistAt = now;
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(PollInterval, ct).ConfigureAwait(false);
                if (ct.IsCancellationRequested) break;

                try
                {
                    await CheckOnceAsync(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _log?.Invoke($"截止提醒检查失败：{ex.GetType().Name}: {ex.Message}");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 正常停止。
        }
        catch (ObjectDisposedException)
        {
            // 退出路径。
        }
    }

    public void Dispose()
    {
        try
        {
            StopAsync().Wait(TimeSpan.FromSeconds(3));
        }
        catch (Exception)
        {
            // 退出路径不再上报。
        }
    }
}
