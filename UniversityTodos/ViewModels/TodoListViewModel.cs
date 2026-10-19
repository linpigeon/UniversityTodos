using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using UniversityTodos.Models;
namespace UniversityTodos.ViewModels;

/// <summary>左侧导航项。</summary>
public sealed partial class NavItem(string key, string label, string glyph, string description) : ViewModelBase
{
    public string Key { get; } = key;
    public string Label { get; } = label;

    /// <summary>Segoe Fluent Icons / Segoe MDL2 Assets 字形。</summary>
    public string Glyph { get; } = glyph;
    public string Description { get; } = description;

    /// <summary>是否是当前选中的导航项（由 MainViewModel 维护）。</summary>
    [ObservableProperty]
    private bool _isSelected;

    /// <summary>右侧的计数角标，空字符串表示不显示。</summary>
    [ObservableProperty]
    private string _badgeText = string.Empty;

    public bool HasBadge => !string.IsNullOrEmpty(BadgeText);

    partial void OnBadgeTextChanged(string value) => OnPropertyChanged(nameof(HasBadge));
}

/// <summary>「计划内 / 作业」列表的分组方式。</summary>
public enum TodoGroupMode
{
    /// <summary>平铺（默认）。</summary>
    None = 0,

    /// <summary>按来源：雨课堂 / 学习通 / 批改网 / 自建。</summary>
    Source = 1,

    /// <summary>按课程：学科名；自建事项归到「自建待办」。</summary>
    Course = 2,
}

/// <summary>
/// 中间栏的待办列表：持有一个过滤器下的全部事项，并派生计数。
/// </summary>
public sealed partial class TodoListViewModel : ViewModelBase
{
    public ObservableCollection<TodoItemViewModel> Items { get; } = [];

    public ObservableCollection<TodoItemViewModel> VisibleItems { get; } = [];

    /// <summary>
    /// 当前分组方式（仅计划内 / 作业使用；其他视图由 MainViewModel 强制 None）。
    /// <para>
    /// 改这个属性本身不触发重排——调用方先设它再 <see cref="Replace"/>，
    /// 避免一次筛选触发两遍布局。
    /// </para>
    /// </summary>
    public TodoGroupMode GroupMode { get; set; } = TodoGroupMode.None;

    /// <summary>
    /// 「课程」视图的条目（学习通课程维度的任务点汇总）。
    /// <para>
    /// 与 <see cref="Items"/> 分开两条通道：课程行没有截止时间/勾选/进度，
    /// 混进待办集合会污染 <see cref="Rebuild"/> 与各项派生计数。
    /// 视图按 <c>ShowCourseList</c> 决定渲染哪一条。
    /// </para>
    /// </summary>
    public ObservableCollection<ChaoxingCourseViewModel> Courses { get; } = [];

    /// <summary>当前是否展示课程列表（由 MainViewModel 在切换导航时设置）。</summary>
    [ObservableProperty]
    private bool _showCourseList;

    /// <summary>课程数（用于列表头副标题）。</summary>
    public int CourseCount => Courses.Count;

    /// <summary>有未完成任务点的课程数。</summary>
    public int CoursesWithPending => Courses.Count(c => c.HasPending);

    /// <summary>课程视图：替换整份课程列表。</summary>
    public void ReplaceCourses(IEnumerable<ChaoxingCourseViewModel> courses)
    {
        Courses.Clear();
        foreach (var c in courses) Courses.Add(c);
        OnPropertyChanged(nameof(CourseCount));
        OnPropertyChanged(nameof(CoursesWithPending));
        OnPropertyChanged(nameof(HasAnyContent));
    }

    /// <summary>当前选中的课程（课程视图的详情栏依据）。</summary>
    [ObservableProperty]
    private ChaoxingCourseViewModel? _selectedCourse;

    /// <summary>是否选中了课程（详情栏切换用）。</summary>
    public bool HasSelectedCourse => SelectedCourse is not null;

    partial void OnSelectedCourseChanged(ChaoxingCourseViewModel? value)
    {
        OnPropertyChanged(nameof(HasSelectedCourse));
        // 与待办行的 IsSelected 保持一致：选中一门就把其他课程的高亮去掉。
        foreach (var c in Courses) c.IsSelected = ReferenceEquals(c, value);
    }

    /// <summary>
    /// 清掉课程选中态（含行高亮）。切换视图或选中待办时调用——
    /// 两个详情面板各绑一个属性，不清会让两块详情叠加显示。
    /// </summary>
    public void ClearCourseSelection() => SelectedCourse = null;

    /// <summary>
    /// 当前视图是否有内容（空状态文案的显隐依据）。
    /// 课程视图看课程数，其余看待办数——两条通道不能只看一边。
    /// </summary>
    public bool HasAnyContent => ShowCourseList ? Courses.Count > 0 : VisibleItems.Count > 0;

    partial void OnShowCourseListChanged(bool value) => OnPropertyChanged(nameof(HasAnyContent));

    /// <summary>勾选后隐藏（仅作业视图有关闭开关时使用）。</summary>
    [ObservableProperty]
    private bool _hideLocallyDone = true;

    [ObservableProperty]
    private string _emptyMessage = "这里没有待办事项。";

    public int Count => VisibleItems.Count;

    /// <summary>列表是否有内容，用于在空列表时隐藏滚动区域、只显示空状态文案。</summary>
    public bool HasItems => VisibleItems.Count > 0;

    public int OverdueCount => VisibleItems.Count(i => i.IsOverdue);

    public int DueTodayCount => VisibleItems.Count(i =>
        i.Model.EffectiveDueAt is { } d && d.Date == DateTimeOffset.Now.Date);

    public void Replace(IEnumerable<TodoItemViewModel> items)
    {
        Items.Clear();
        foreach (var i in items) Items.Add(i);
        Rebuild();
    }

    public void Rebuild()
    {
        var query = Items.AsEnumerable();
        // 「隐藏已勾选」只作用于作业：公告标记已读后从列表消失、公告页又没有
        // 关闭过滤的开关，会让人误以为公告丢了（issue #1）。
        if (HideLocallyDone)
            query = query.Where(i => i.Kind != TodoKind.Homework || !i.IsDone);

        var visible = query.ToList();

        // 条目是复用的：每次重排先清掉上一次的分组标记，避免旧标题留到新位置。
        foreach (var i in Items)
        {
            i.IsGroupStart = false;
            i.GroupLabel = string.Empty;
        }

        VisibleItems.Clear();
        if (GroupMode == TodoGroupMode.None || visible.Count == 0)
        {
            foreach (var i in visible) VisibleItems.Add(i);
        }
        else
        {
            // 组顺序 = 组内第一条在排序结果里的位置：按时间排时「最近截止的组」在前，
            // 按优先级排时「优先级最高的组」在前；组内保持 MainViewModel 给的顺序。
            var groups = visible
                .Select((item, index) => (item, index))
                .GroupBy(x => GroupKey(x.item), StringComparer.CurrentCulture)
                .OrderBy(g => g.Min(x => x.index));

            foreach (var group in groups)
            {
                var members = group.Select(x => x.item).ToList();
                var label = $"{group.Key} · {members.Count} 项";

                for (var index = 0; index < members.Count; index++)
                {
                    if (index == 0)
                    {
                        members[index].IsGroupStart = true;
                        members[index].GroupLabel = label;
                    }

                    VisibleItems.Add(members[index]);
                }
            }
        }

        OnPropertyChanged(nameof(Count));
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(OverdueCount));
        OnPropertyChanged(nameof(DueTodayCount));
        OnPropertyChanged(nameof(HasAnyContent));
    }

    /// <summary>分组键：按课程时自建事项统一归到「自建待办」，平台事项用学科名。</summary>
    private string GroupKey(TodoItemViewModel item) => GroupMode switch
    {
        TodoGroupMode.Source => item.Model.Source switch
        {
            TodoSource.Chaoxing => "学习通",
            TodoSource.Pigai => "批改网",
            TodoSource.Manual => "自建待办",
            _ => "雨课堂",
        },
        TodoGroupMode.Course => item.IsManual
            ? "自建待办"
            : (string.IsNullOrWhiteSpace(item.DisplaySubject) ? "未分类课程" : item.DisplaySubject),
        _ => string.Empty,
    };

    partial void OnHideLocallyDoneChanged(bool value) => Rebuild();
}
