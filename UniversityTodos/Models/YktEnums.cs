namespace UniversityTodos.Models;

/// <summary>雨课堂学习日志的 activity type。</summary>
public static class YktActivityType
{
    /// <summary>试卷类作业——截止时间来自章节树的 score_deadline。</summary>
    public const int ExamHomework = 5;

    /// <summary>公告。</summary>
    public const int Announcement = 9;

    /// <summary>上课记录。</summary>
    public const int Lesson = 14;

    /// <summary>章节作业——截止时间由日志自身的 content.score_d 提供。</summary>
    public const int ChapterHomework = 19;
}

/// <summary>待办事项的种类。</summary>
public enum TodoKind
{
    /// <summary>作业（含试卷类与章节作业）。</summary>
    Homework,

    /// <summary>公告 / 通知。</summary>
    Announcement,

    /// <summary>用户自建事项（不来自任何平台）。</summary>
    Custom,
}

/// <summary>事项来源。</summary>
public enum TodoSource
{
    /// <summary>雨课堂。</summary>
    Ykt,

    /// <summary>学习通。</summary>
    Chaoxing,

    /// <summary>批改网。</summary>
    Pigai,

    /// <summary>用户自建。</summary>
    Manual,

    /// <summary>课表展开出的上课安排。</summary>
    Class,
}

/// <summary>自建事项的优先级（平台事项统一按 Normal 处理）。</summary>
public enum TodoPriority
{
    /// <summary>低。</summary>
    Low,

    /// <summary>普通（默认）。</summary>
    Normal,

    /// <summary>高。</summary>
    High,
}

/// <summary>
/// 完成状态。取值来自 pub_new_pro 的 leaf_schedules，而不是"截止时间是否已过"——
/// 后者会把尚未到期的未完成作业误判为已完成，这正是上一轮会话踩过的坑。
/// </summary>
public enum TodoStatus
{
    /// <summary>接口未返回该 leaf 的进度，无法判定。</summary>
    Unknown,

    /// <summary>done == 0。</summary>
    NotStarted,

    /// <summary>0 &lt; done &lt; total。</summary>
    InProgress,

    /// <summary>done == total。</summary>
    Completed,
}
