using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using UniversityTodos.Models;
using UniversityTodos.Services;

namespace UniversityTodos.ViewModels;

public sealed partial class WaterViewModel : ViewModelBase
{
    private readonly WaterCacheStore _store;

    public WaterViewModel(WaterCacheStore? store = null)
    {
        _store = store ?? new WaterCacheStore();
    }

    [ObservableProperty]
    private ObservableCollection<WaterBill> _latestBills = [];

    [ObservableProperty]
    private DateTimeOffset? _lastUpdated;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _statusMessage = "点击「查询水费」，通过统一身份认证后读取本学期账单";

    [ObservableProperty]
    private ObservableCollection<WaterBill> _historyBills = [];

    [ObservableProperty]
    private bool _showHistory;

    public bool HasData => LatestBills.Count > 0;
    public bool HasHistoryBills => HistoryBills.Count > 0;
    public bool ShowHistoryBills => ShowHistory && HasHistoryBills;
    public string HistoryCountText => $"{HistoryBills.Count} 条往期账单";
    public string PeriodText => LatestBills.Count == 0
        ? "暂无账期"
        : $"本学期 {LatestBills.Count} 条账单";
    public string RoomText => LatestBills.Count == 0 ? "" : $"房间 {LatestBills[0].RoomNumber}";
    public double TotalFee => LatestBills.Sum(b => b.Fee);
    public double TotalUsage => LatestBills.Sum(b => b.Usage);
    public string LastUpdatedText => LastUpdated is { } time
        ? $"更新于 {time:MM-dd HH:mm}"
        : "尚未更新";

    public async Task LoadAsync(CancellationToken ct = default)
    {
        var cache = await _store.LoadAsync(ct).ConfigureAwait(true);
        var semesterStart = Services.Timetable.ParseDate(AppSettings.Current.TermStartDate);
        var cachedBills = cache.LatestBills.Concat(cache.HistoryBills).ToList();
        var latestBills = WaterService.FilterBySemester(
            cachedBills, semesterStart, semesterEnd: null);
        ApplyBills(latestBills, cache.SyncedAt);
        ApplyHistoryBills(
            WaterService.FilterHistoryBySemester(cachedBills, semesterStart), cache.SyncedAt);
        if (HasData || HasHistoryBills) StatusMessage = "显示本地水费缓存，可点击查询更新";
    }

    public async Task SaveAsync(
        IReadOnlyList<WaterBill> bills, CancellationToken ct = default, bool includeHistory = false)
    {
        var now = DateTimeOffset.Now;
        var semesterStart = Services.Timetable.ParseDate(AppSettings.Current.TermStartDate);
        var latestBills = WaterService.FilterBySemester(
            bills, semesterStart, semesterEnd: null).ToList();
        var cachedHistory = WaterService.FilterHistoryBySemester(HistoryBills, semesterStart);
        var historyBills = includeHistory
            ? WaterService.MergeBills(WaterService.FilterHistoryBySemester(bills, semesterStart)
                .Concat(cachedHistory)).ToList()
            : cachedHistory.ToList();
        await _store.SaveAsync(new WaterCacheFile
        {
            LatestBills = latestBills,
            HistoryBills = historyBills,
            SyncedAt = now
        }, ct).ConfigureAwait(true);
        ApplyBills(latestBills, now);
        ApplyHistoryBills(historyBills, now);
    }

    public void ApplyBills(IReadOnlyList<WaterBill> bills, DateTimeOffset? updated)
    {
        LatestBills = new ObservableCollection<WaterBill>(bills);
        LastUpdated = updated;
        OnPropertyChanged(nameof(HasData));
        OnPropertyChanged(nameof(PeriodText));
        OnPropertyChanged(nameof(RoomText));
        OnPropertyChanged(nameof(TotalFee));
        OnPropertyChanged(nameof(TotalUsage));
    }

    public void ApplyHistoryBills(IReadOnlyList<WaterBill> bills, DateTimeOffset? updated)
    {
        HistoryBills = new ObservableCollection<WaterBill>(WaterService.MergeBills(bills));
        LastUpdated = updated;
        OnPropertyChanged(nameof(HasHistoryBills));
        OnPropertyChanged(nameof(ShowHistoryBills));
        OnPropertyChanged(nameof(HistoryCountText));
    }

    partial void OnShowHistoryChanged(bool value) => OnPropertyChanged(nameof(ShowHistoryBills));

    partial void OnLastUpdatedChanged(DateTimeOffset? value) => OnPropertyChanged(nameof(LastUpdatedText));
}
