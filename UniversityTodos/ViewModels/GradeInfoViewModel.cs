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

public sealed class GradeListItemViewModel(GradeInfo item)
{
    public string CourseName => item.CourseName;
    public string CourseCodeText => string.IsNullOrWhiteSpace(item.CourseCode) ? "" : $"课程代码 {item.CourseCode}";
    public string GradeText => string.IsNullOrWhiteSpace(item.GradeText) ? "暂无成绩" : item.GradeText;
    public string PercentScoreText => item.PercentScore is { } score ? $"{score:0.##} 分" : GradeText;
    public string CreditsText => $"{item.Credits:0.##} 学分";
    public string GradePointText => $"绩点 {item.GradePoint:0.00}";
    public string CreditPointText => $"学分绩点 {item.CreditPoint:0.##}";
    public string CourseNatureText => item.CourseNature;
    public string AssessmentText => string.IsNullOrWhiteSpace(item.Assessment) ? "考核方式未注明" : item.Assessment;
    public string TeacherText => string.IsNullOrWhiteSpace(item.Teacher) ? "" : $"教师 {item.Teacher}";
    public string CollegeText => item.College;
    public bool IsVoid => item.IsVoid;
    public bool IsLowGrade => item.PercentScore is < 60 || item.GradePoint <= 0;
}

public sealed partial class GradeInfoViewModel : ViewModelBase
{
    private readonly GradeCacheStore _store;
    private List<GradeInfo> _allGrades = [];

    public GradeInfoViewModel(GradeCacheStore? store = null)
    {
        _store = store ?? new GradeCacheStore();
        var term = AppSettings.Current.Term;
        _academicYear = term / 100;
        _semesterIndex = Math.Clamp(term % 100 - 1, 0, 2);
    }

    [ObservableProperty]
    private ObservableCollection<GradeListItemViewModel> _items = [];

    [ObservableProperty]
    private int _academicYear;

    [ObservableProperty]
    private int _semesterIndex;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _statusMessage = "选择学期后，从教务系统获取成绩与学分";

    [ObservableProperty]
    private DateTimeOffset? _lastUpdated;

    [ObservableProperty]
    private AcademicStats _stats = new();

    public int Semester => SemesterIndex + 1;
    public bool HasItems => Items.Count > 0;
    public bool ShowEmpty => !IsLoading && !HasItems;
    public string TermText => $"{AcademicYear}-{AcademicYear + 1} 学年第 {Semester} 学期";
    public string CountText => $"{Items.Count} 门课程成绩";
    public string MinimumGraduationCreditsText => $"{Stats.MinimumGraduationCredits:0.##} 学分";
    public string PlannedCourseCountText => $"{Stats.PlannedCourseCount} 门";
    public string EarnedCreditsText => $"{Stats.EarnedCredits:0.##} 学分";
    public string WeightedGradePointText => Stats.WeightedGradePoint.ToString("0.00");
    public string AverageScoreText => $"{Stats.AverageScore:0.##} 分";
    public string RemainingCreditsText => $"{Math.Max(0, Stats.MinimumGraduationCredits - Stats.EarnedCredits):0.##} 学分";
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
        else if (cache.Grades
                 .OrderBy(item => item.AcademicYear)
                 .ThenBy(item => item.Semester)
                 .LastOrDefault() is { } last)
        {
            AcademicYear = last.AcademicYear;
            SemesterIndex = Math.Clamp(last.Semester - 1, 0, 2);
        }
        _allGrades = cache.Grades;
        Stats = cache.Stats;
        LastUpdated = cache.SyncedAt;
        ApplyFilter();
        if (HasItems) StatusMessage = "显示本地成绩缓存，可从教务系统更新";
    }

    public async Task SaveAsync(
        IReadOnlyList<GradeInfo> grades, AcademicStats stats, CancellationToken ct = default)
    {
        var now = DateTimeOffset.Now;
        var updated = _allGrades
            .Where(item => item.AcademicYear != AcademicYear || item.Semester != Semester)
            .Concat(grades)
            .ToList();
        await _store.SaveAsync(new GradeCacheFile
        {
            Grades = updated,
            Stats = stats,
            LastAcademicYear = AcademicYear,
            LastSemester = Semester,
            SyncedAt = now,
        }, ct).ConfigureAwait(true);
        _allGrades = updated;
        Stats = stats;
        LastUpdated = now;
        ApplyFilter();
    }

    public void ApplyItemsForCheck(IReadOnlyList<GradeInfo> grades, AcademicStats stats)
    {
        _allGrades = _allGrades
            .Where(item => item.AcademicYear != AcademicYear || item.Semester != Semester)
            .Concat(grades)
            .ToList();
        Stats = stats;
        LastUpdated = DateTimeOffset.Now;
        ApplyFilter();
    }

    partial void OnAcademicYearChanged(int value) => ApplyFilter();
    partial void OnSemesterIndexChanged(int value) => ApplyFilter();
    partial void OnStatsChanged(AcademicStats value)
    {
        OnPropertyChanged(nameof(MinimumGraduationCreditsText));
        OnPropertyChanged(nameof(PlannedCourseCountText));
        OnPropertyChanged(nameof(EarnedCreditsText));
        OnPropertyChanged(nameof(WeightedGradePointText));
        OnPropertyChanged(nameof(AverageScoreText));
        OnPropertyChanged(nameof(RemainingCreditsText));
    }
    partial void OnIsLoadingChanged(bool value) => OnPropertyChanged(nameof(ShowEmpty));
    partial void OnLastUpdatedChanged(DateTimeOffset? value) => OnPropertyChanged(nameof(LastUpdatedText));

    private void ApplyFilter()
    {
        Items = new ObservableCollection<GradeListItemViewModel>(_allGrades
            .Where(item => item.AcademicYear == AcademicYear && item.Semester == Semester)
            .OrderBy(item => item.CourseCode)
            .Select(item => new GradeListItemViewModel(item)));
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(ShowEmpty));
        OnPropertyChanged(nameof(TermText));
        OnPropertyChanged(nameof(CountText));
        if (!IsLoading)
        {
            StatusMessage = HasItems
                ? $"显示 {TermText} 的 {Items.Count} 门课程成绩"
                : $"{TermText}暂无成绩缓存，请从教务获取";
        }
    }
}
