using HomeworkReminder.Models;

namespace HomeworkReminder.ViewModels;

/// <summary>「我的一天」里的一条今日课程（只读展示，不参与勾选与提醒）。</summary>
public sealed class ClassSessionViewModel(ClassSession session)
{
    public ClassSession Model { get; } = session;

    public string Name => Model.Name;

    public string TimeText => $"{Model.StartTime}-{Model.EndTime}";

    public string Location => Model.Location ?? string.Empty;

    public bool HasLocation => !string.IsNullOrWhiteSpace(Model.Location);
}
