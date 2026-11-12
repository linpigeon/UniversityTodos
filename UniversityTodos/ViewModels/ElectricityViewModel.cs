using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using UniversityTodos.Models;
using UniversityTodos.Services;

namespace UniversityTodos.ViewModels;

public sealed partial class ElectricityViewModel : ViewModelBase
{
    private readonly IElectricityApi _api;
    private readonly ElectricityCacheStore _store;
    private readonly SemaphoreSlim _queryGate = new(1, 1);

    private const double DefaultLowBalanceThreshold = 20;

    public ElectricityViewModel(IElectricityApi? api = null, ElectricityCacheStore? store = null)
    {
        _api = api ?? new ElectricityApiClient();
        _store = store ?? new ElectricityCacheStore();
        _account = AppSettings.Current.ElectricityAccount;
        _lowBalanceThresholdInput = AppSettings.Current.ElectricityLowBalanceThreshold;
        _notifyLowBalance = AppSettings.Current.NotifyElectricityLowBalance;
    }

    public event Action<bool>? SettingsChanged;

    [ObservableProperty]
    private string _account;

    [ObservableProperty]
    private ObservableCollection<ElectricityRoom> _rooms = [];

    [ObservableProperty]
    private ElectricityRoom? _selectedRoom;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _statusMessage = "输入学号后查询宿舍电费";

    [ObservableProperty]
    private DateTimeOffset? _lastUpdated;

    [ObservableProperty]
    private double? _lowBalanceThresholdInput;

    [ObservableProperty]
    private bool _notifyLowBalance;

    public double LowBalanceThreshold => LowBalanceThresholdInput is { } value && double.IsFinite(value)
        ? Math.Clamp(value, 0, 100000)
        : DefaultLowBalanceThreshold;

    public bool HasRooms => Rooms.Count > 0;
    public bool HasSelectedRoom => SelectedRoom is not null;
    public bool SelectedRoomLowBalance => SelectedRoom is not null && SelectedRoom.Odd < LowBalanceThreshold;
    public string LastUpdatedText => LastUpdated is { } time
        ? $"更新于 {time:MM-dd HH:mm}"
        : "尚未更新";
    public string EmptyMessage => string.IsNullOrWhiteSpace(Account)
        ? "请先在上方输入并保存完美校园学号。"
        : "还没有电费数据，点击右上角「刷新」查询。";
    public string SelectedRoomName => SelectedRoom?.RoomName ?? string.Empty;
    public string SelectedRoomOverviewName => SelectedRoom?.RoomName ?? "暂无电费数据";
    public string SelectedRoomStatusText => SelectedRoom?.StatusText ?? string.Empty;
    public string SelectedRoomOddText => SelectedRoom?.Odd.ToString("F2") ?? "待查询";
    public string SelectedRoomUseText => SelectedRoom is null ? "0.00 度" : $"{SelectedRoom.Use:F2} 度";
    public string SelectedRoomLowBalanceText => SelectedRoom is null
        ? string.Empty
        : $"电费提醒：{SelectedRoom.RoomName} 剩余电量已低于阈值，请及时充值。";

    public async Task LoadAsync(CancellationToken ct = default)
    {
        var cache = await _store.LoadAsync(ct).ConfigureAwait(true);
        Rooms = new ObservableCollection<ElectricityRoom>(cache.Rooms);
        LastUpdated = cache.SyncedAt;
        SelectedRoom = Rooms.FirstOrDefault();
        StatusMessage = HasRooms ? "显示本地电费缓存，可点击刷新更新" : "输入学号后查询宿舍电费";
        OnPropertyChanged(nameof(HasRooms));
    }

    public async Task<bool> RefreshAsync(CancellationToken ct = default)
    {
        var account = Account.Trim();
        if (string.IsNullOrWhiteSpace(account))
        {
            StatusMessage = "请先输入并保存完美校园学号";
            return false;
        }
        if (!await _queryGate.WaitAsync(0, ct).ConfigureAwait(true)) return false;

        try
        {
            IsLoading = true;
            StatusMessage = "正在查询宿舍电费…";
            var rooms = await _api.GetBoundRoomsAsync(account, ct).ConfigureAwait(true);
            if (rooms is null)
            {
                StatusMessage = _api is ElectricityApiClient { LastError: { } reason }
                    ? $"电费查询失败：{reason}，已保留上次数据"
                    : "电费查询失败，已保留上次数据，请稍后重试";
                return false;
            }
            if (rooms.Count == 0)
            {
                StatusMessage = "该学号没有绑定宿舍，已保留上次数据";
                return false;
            }

            var now = DateTimeOffset.Now;
            await _store.SaveAsync(new ElectricityCacheFile
            {
                Rooms = rooms,
                SyncedAt = now
            }, ct).ConfigureAwait(true);

            var selectedId = SelectedRoom?.RoomVerify;
            Rooms = new ObservableCollection<ElectricityRoom>(rooms);
            SelectedRoom = Rooms.FirstOrDefault(r => r.RoomVerify == selectedId) ?? Rooms[0];
            LastUpdated = now;
            StatusMessage = $"已更新 {rooms.Count} 个宿舍的电费";
            OnPropertyChanged(nameof(HasRooms));
            return true;
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "电费查询已取消";
            return false;
        }
        catch (Exception ex)
        {
            StatusMessage = $"电费查询失败：{ex.Message}，已保留上次数据";
            return false;
        }
        finally
        {
            IsLoading = false;
            _queryGate.Release();
        }
    }

    public async Task SaveAccountAsync()
    {
        var account = Account.Trim();
        if (string.IsNullOrWhiteSpace(account))
        {
            StatusMessage = "学号不能为空";
            return;
        }

        var changed = !string.Equals(AppSettings.Current.ElectricityAccount, account, StringComparison.Ordinal);
        AppSettings.Current.ElectricityAccount = account;
        AppSettings.Current.Save();
        Account = account;
        StatusMessage = "学号已保存，点击刷新查询电费";

        if (changed)
        {
            Rooms.Clear();
            SelectedRoom = null;
            LastUpdated = null;
            await _store.SaveAsync(new ElectricityCacheFile()).ConfigureAwait(true);
            OnPropertyChanged(nameof(HasRooms));
        }
        SettingsChanged?.Invoke(changed);
    }

    partial void OnAccountChanged(string value) => OnPropertyChanged(nameof(EmptyMessage));

    partial void OnSelectedRoomChanged(ElectricityRoom? value)
    {
        OnPropertyChanged(nameof(HasSelectedRoom));
        OnPropertyChanged(nameof(SelectedRoomLowBalance));
        OnPropertyChanged(nameof(SelectedRoomName));
        OnPropertyChanged(nameof(SelectedRoomOverviewName));
        OnPropertyChanged(nameof(SelectedRoomStatusText));
        OnPropertyChanged(nameof(SelectedRoomOddText));
        OnPropertyChanged(nameof(SelectedRoomUseText));
        OnPropertyChanged(nameof(SelectedRoomLowBalanceText));
    }

    partial void OnLowBalanceThresholdInputChanged(double? value)
    {
        var normalized = value is { } current && double.IsFinite(current)
            ? Math.Clamp(current, 0, 100000)
            : DefaultLowBalanceThreshold;
        if (value is not null && normalized != value)
        {
            LowBalanceThresholdInput = normalized;
            return;
        }

        AppSettings.Current.ElectricityLowBalanceThreshold = normalized;
        AppSettings.Current.Save();
        OnPropertyChanged(nameof(LowBalanceThreshold));
        OnPropertyChanged(nameof(SelectedRoomLowBalance));
        SettingsChanged?.Invoke(false);
    }

    partial void OnNotifyLowBalanceChanged(bool value)
    {
        AppSettings.Current.NotifyElectricityLowBalance = value;
        AppSettings.Current.Save();
        SettingsChanged?.Invoke(false);
    }

    partial void OnLastUpdatedChanged(DateTimeOffset? value) => OnPropertyChanged(nameof(LastUpdatedText));
}
