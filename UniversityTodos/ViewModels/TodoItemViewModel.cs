using System;
using CommunityToolkit.Mvvm.ComponentModel;
using UniversityTodos.Models;

namespace UniversityTodos.ViewModels;

/// <summary>
/// 列表中的一条待办。
/// <para>
/// <see cref="IsDone"/> 是应用内的本地勾选，不代表雨课堂的完成状态——
/// <see cref="Status"/> 才是接口给出的真实进度。两者分开显示，避免把
/// "我勾掉了" 误读成 "雨课堂认为我做完了"。
/// </para>
/// </summary>
public partial class TodoItemViewModel(TodoItem model) : ViewModelBase
{
    public TodoItem Model { get; } = model;

    [ObservableProperty]
    private bool _isDone;

    partial void OnIsDoneChanged(bool value) => OnPropertyChanged(nameof(DoneLabel));

    /// <summary>应用内标记的可读文本。</summary>
    public string DoneLabel => IsDone ? "已在应用内标记" : "未标记";

    /// <summary>打开按钮文案：作业直达答题页，公告仍落到课程页。Android 上优先拉起雨课堂 App。</summary>
    public string OpenButtonText
    {
        get
        {
            var isHomework = Kind == TodoKind.Homework;
            if (OperatingSystem.IsAndroid())
                return isHomework ? "在雨课堂中打开该作业" : "在雨课堂中打开该课程";
            return isHomework ? "在浏览器中打开该作业" : "在浏览器中打开该课程";
        }
    }

    [ObservableProperty]
    private bool _isSelected;

    /// <summary>
    /// 这一条是否是当前分组的首行（分组标题画在它上面）。
    /// <para>
    /// 分组刻意不产生额外的行对象：标题贴在每组第一条上，勾选、选中与键盘导航仍然只面对真实条目。
    /// 由 <see cref="TodoListViewModel.Rebuild"/> 在每次重排后重新标记。
    /// </para>
    /// </summary>
    [ObservableProperty]
    private bool _isGroupStart;

    /// <summary>分组标题（如「雨课堂 · 3 项」）；非首行为空字符串。</summary>
    [ObservableProperty]
    private string _groupLabel = string.Empty;

    /// <summary>星标（本地标记）。</summary>
    [ObservableProperty]
    private bool _isStarred;

    /// <summary>是否被用户手动加入「我的一天」。</summary>
    [ObservableProperty]
    private bool _inMyDay;

    partial void OnIsStarredChanged(bool value)
    {
        OnPropertyChanged(nameof(StarGlyph));
        OnPropertyChanged(nameof(StarTooltip));
        OnPropertyChanged(nameof(StarButtonText));
    }

    /// <summary>列表行上的星标字形（只有星标时显示）。</summary>
    public string StarGlyph => "\uF005";

    public string StarTooltip => IsStarred ? "取消星标" : "星标";

    public string StarButtonText => IsStarred ? "取消星标" : "星标";

    public string MyDayTooltip => InMyDay ? "移出我的一天" : "加入我的一天";

    public string MyDayButtonText => InMyDay ? "移出我的一天" : "加入我的一天";

    partial void OnInMyDayChanged(bool value)
    {
        OnPropertyChanged(nameof(MyDayTooltip));
        OnPropertyChanged(nameof(MyDayButtonText));
    }

    /// <summary>是否为用户自建事项（可编辑/删除）。</summary>
    public bool IsManual => Model.IsManual;

    /// <summary>优先级展示（自建事项才显示）。</summary>
    public string PriorityText => Model.PriorityText;

    public bool ShowPriority => IsManual && Model.Priority != TodoPriority.Normal;

    public bool HasNote => !string.IsNullOrWhiteSpace(Model.Note);

    public string NoteText => Model.Note ?? string.Empty;

    public bool HasTags => Model.Tags.Count > 0;

    public string TagsText => HasTags ? "# " + string.Join("  # ", Model.Tags) : string.Empty;

    /// <summary>优先级为高的事项在列表里标一个点。</summary>
    public bool IsHighPriority => Model.Priority == TodoPriority.High;

    public string Id => Model.Id;
    public TodoKind Kind => Model.Kind;
    public string Title => Model.Title;
    public string CourseName => Model.CourseName;

    /// <summary>展示用标题：剥离学期前缀（「2026秋-」「26-27-1学期 」等）。</summary>
    public string DisplayTitle => DisplayText.StripTermPrefix(Model.Title);

    /// <summary>展示用学科名：优先 courses/list 内嵌 course.name（如「概率论与数理统计B」），
    /// 旧缓存没有时退回剥离学期前缀的班级名。自建事项没有课程，统一显示「自建待办」。</summary>
    public string DisplaySubject => IsManual
        ? "自建待办"
        : Model.Subject ?? DisplayText.StripTermPrefix(Model.CourseName);

    /// <summary>
    /// 列表/详情的主标题。平台事项显示学科（列表按课程归集），自建事项显示任务标题本身。
    /// </summary>
    public string HeaderPrimary => IsManual ? DisplayText.StripTermPrefix(Model.Title) : DisplaySubject;

    /// <summary>列表/详情的副标题（自建事项标注来源）。</summary>
    public string HeaderSecondary => IsManual ? "自建待办" : DisplayTitle;

    /// <summary>展示用课程名：剥离学期前缀。</summary>
    public string CourseDisplayName => DisplayText.StripTermPrefix(Model.CourseName);

    public string DueText => Model.DueText;
    public string RemainingText => Model.RemainingText;
    public string ProgressText => Model.ProgressText;
    public string StatusText => Model.StatusText;
    public bool IsOverdue => Model.IsOverdue;
    public string? SourceUrl => Model.SourceUrl;
    public long ClassroomId => Model.ClassroomId;

    /// <summary>作业的真实完成状态（来自接口）。</summary>
    public bool IsCompletedOnServer => Model.Status == TodoStatus.Completed;

    /// <summary>是否显示剩余时间。已完成的作业不显示，避免出现「已过期」这种无意义的提示。</summary>
    public bool ShowRemaining => !IsCompletedOnServer;

    public bool ShowRemainingNotOverdue => ShowRemaining && !IsOverdue;

    public bool ShowRemainingOverdue => ShowRemaining && IsOverdue;

    /// <summary>试卷类作业在标题上带「作业」字样，用它选图标。</summary>
    public string Glyph => Kind switch
    {
        TodoKind.Announcement => "\uF0A1",
        TodoKind.Custom => "\uF304",
        _ => "\uF19D",
    };

    /// <summary>自建事项没有平台页面可打开。</summary>
    public bool ShowOpenButton => !IsManual;

    /// <summary>未开始 vs 进行中，用于列表里的进度徽标着色。</summary>
    public bool ShowProgress => Kind == TodoKind.Homework && Model.Total is > 0;

    public void Refresh()
    {
        OnPropertyChanged(nameof(IsOverdue));
        OnPropertyChanged(nameof(RemainingText));
    }
}
