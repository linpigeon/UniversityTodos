using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using UniversityTodos.Models;
using UniversityTodos.Services;

namespace UniversityTodos.ViewModels;

public sealed class ExamListItemViewModel(ExamInfo item)
{
    public string CourseName => item.CourseName;
    public string CourseCodeText => string.IsNullOrWhiteSpace(item.CourseCode) ? "" : $"课程代码 {item.CourseCode}";
    public string ExamName => string.IsNullOrWhiteSpace(item.ExamName) ? "考试" : item.ExamName;
    public string DateText => item.StartAt.ToString("yyyy-MM-dd ddd", CultureInfo.GetCultureInfo("zh-CN"));
    public string TimeText => $"{item.StartAt:HH:mm} – {item.EndAt:HH:mm}";
    public string LocationText => string.IsNullOrWhiteSpace(item.Campus)
        ? item.Location
        : $"{item.Location} · {item.Campus}";
    public string SeatText => string.IsNullOrWhiteSpace(item.SeatNumber) ? "座号未注明" : $"座号 {item.SeatNumber}";
    public string MethodText => string.IsNullOrWhiteSpace(item.ExamMethod) ? "考试方式未注明" : item.ExamMethod;
    public string TeachingClassText => string.IsNullOrWhiteSpace(item.TeachingClass) ? "" : item.TeachingClass;
    public string CollegeText => string.IsNullOrWhiteSpace(item.College) ? "" : item.College;
    public string RetakeText => item.RetakeFlag == "是" ? "重修" : "";
    public string RemarkText => string.IsNullOrWhiteSpace(item.Remark) ? "" : $"备注：{item.Remark}";
}

public sealed partial class ExamInfoViewModel : ViewModelBase
{
    private readonly ExamCacheStore _store;
    private List<ExamInfo> _allItems = [];

    public ExamInfoViewModel(ExamCacheStore? store = null)
    {
        _store = store ?? new ExamCacheStore();
        var term = AppSettings.Current.Term;
        _academicYear = term / 100;
        _semesterIndex = Math.Clamp(term % 100 - 1, 0, 2);
    }

    [ObservableProperty]
    private ObservableCollection<ExamListItemViewModel> _items = [];

    [ObservableProperty]
    private int _academicYear;

    [ObservableProperty]
    private int _semesterIndex;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _statusMessage = "选择学期后，从教务系统获取考试安排";

    [ObservableProperty]
    private DateTimeOffset? _lastUpdated;

    public int Semester => SemesterIndex + 1;
    public bool HasItems => Items.Count > 0;
    public bool ShowEmpty => !IsLoading && !HasItems;
    public string TermText => $"{AcademicYear}-{AcademicYear + 1} 学年第 {Semester} 学期";
    public string CountText => $"{Items.Count} 场考试";
    public string LastUpdatedText => LastUpdated is { } time
        ? $"最近获取于 {time:MM-dd HH:mm}"
        : "尚未获取";

    public async Task LoadAsync(CancellationToken ct = default)
    {
        var cache = await _store.LoadAsync(ct).ConfigureAwait(true);
        if (cache.LastAcademicYear is >= 2000 and <= 2100
            && cache.LastSemester is >= 1 and <= 3)
        {
            AcademicYear = cache.LastAcademicYear.Value;
            SemesterIndex = cache.LastSemester.Value - 1;
        }
        else if (cache.Items
                 .OrderBy(item => item.AcademicYear)
                 .ThenBy(item => item.Semester)
                 .LastOrDefault() is { } last)
        {
            AcademicYear = last.AcademicYear;
            SemesterIndex = Math.Clamp(last.Semester - 1, 0, 2);
        }
        _allItems = cache.Items;
        LastUpdated = cache.SyncedAt;
        ApplyFilter();
        if (HasItems) StatusMessage = "显示本地考试缓存，可从教务系统更新";
    }

    public async Task SaveAsync(IReadOnlyList<ExamInfo> items, CancellationToken ct = default)
    {
        var now = DateTimeOffset.Now;
        var updated = _allItems
            .Where(item => item.AcademicYear != AcademicYear || item.Semester != Semester)
            .Concat(items)
            .ToList();
        await _store.SaveAsync(new ExamCacheFile
        {
            Items = updated,
            LastAcademicYear = AcademicYear,
            LastSemester = Semester,
            SyncedAt = now,
        }, ct).ConfigureAwait(true);
        _allItems = updated;
        LastUpdated = now;
        ApplyFilter();
    }

    public void ApplyItemsForCheck(IReadOnlyList<ExamInfo> items)
    {
        _allItems = _allItems
            .Where(item => item.AcademicYear != AcademicYear || item.Semester != Semester)
            .Concat(items)
            .ToList();
        LastUpdated = DateTimeOffset.Now;
        ApplyFilter();
    }

    partial void OnAcademicYearChanged(int value) => ApplyFilter();
    partial void OnSemesterIndexChanged(int value) => ApplyFilter();
    partial void OnIsLoadingChanged(bool value) => OnPropertyChanged(nameof(ShowEmpty));
    partial void OnLastUpdatedChanged(DateTimeOffset? value) => OnPropertyChanged(nameof(LastUpdatedText));

    private void ApplyFilter()
    {
        Items = new ObservableCollection<ExamListItemViewModel>(_allItems
            .Where(item => item.AcademicYear == AcademicYear && item.Semester == Semester)
            .OrderBy(item => item.StartAt)
            .ThenBy(item => item.CourseName)
            .Select(item => new ExamListItemViewModel(item)));
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(ShowEmpty));
        OnPropertyChanged(nameof(TermText));
        OnPropertyChanged(nameof(CountText));
        if (!IsLoading)
        {
            StatusMessage = HasItems
                ? $"显示 {TermText} 的 {Items.Count} 场考试缓存"
                : $"{TermText}暂无缓存，请从教务获取";
        }
    }
}
