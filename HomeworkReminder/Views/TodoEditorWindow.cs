using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using HomeworkReminder.Models;
using HomeworkReminder.ViewModels;

namespace HomeworkReminder.Views;

/// <summary>
/// 自建待办的编辑对话框（新建 / 编辑共用）。
/// <para>
/// 与 <see cref="MessageBox"/> 同一思路：用临时 Window 实现，不引第三方对话框库；
/// 颜色在打开时按当前主题解析。
/// </para>
/// </summary>
internal static class TodoEditorWindow
{
    private static IBrush Res(string key, string fallback)
    {
        var app = Application.Current;
        if (app?.TryGetResource(key, app.ActualThemeVariant, out var v) == true && v is IBrush b)
            return b;
        return SolidColorBrush.Parse(fallback);
    }

    /// <summary>打开编辑框；用户取消时返回 null。</summary>
    public static async Task<MainViewModel.ManualTodoDraft?> ShowAsync(Window owner, TodoItem? existing)
    {
        var title = new TextBox
        {
            Text = existing?.Title ?? string.Empty,
            PlaceholderText = "例如：打印实验报告",
            MaxLength = 200,
        };
        var due = new TextBox
        {
            Text = existing?.DueAt?.ToString("yyyy-MM-dd HH:mm") ?? string.Empty,
            PlaceholderText = "2026-10-20 18:00（可留空）",
        };
        var remind = new TextBox
        {
            Text = existing?.RemindAt?.ToString("yyyy-MM-dd HH:mm") ?? string.Empty,
            PlaceholderText = "提前单独提醒的时间（可留空）",
        };
        var priority = new ComboBox
        {
            ItemsSource = new List<string> { "普通", "高", "低" },
            SelectedIndex = existing is null ? 0
                : existing.Priority switch
                {
                    TodoPriority.High => 1,
                    TodoPriority.Low => 2,
                    _ => 0,
                },
            MinWidth = 120,
        };
        var tags = new TextBox
        {
            Text = existing is null ? string.Empty : string.Join(", ", existing.Tags),
            PlaceholderText = "用逗号分隔，例如：实验, 本周",
        };
        var note = new TextBox
        {
            Text = existing?.Note ?? string.Empty,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Height = 70,
            PlaceholderText = "备注（可选）",
        };
        var error = new TextBlock
        {
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Res("TodoOverdue", "#C42B1C"),
            IsVisible = false,
        };

        var save = new Button { Content = "保存", Classes = { "primary" }, MinWidth = 88 };
        var cancel = new Button { Content = "取消", Classes = { "outline" }, MinWidth = 88 };
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            Children = { cancel, save },
        };

        MainViewModel.ManualTodoDraft? result = null;

        var window = new Window
        {
            Title = existing is null ? "新建待办" : "编辑待办",
            Width = 480,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Res("TodoDialogBackground", "#FFFFFF"),
            Icon = MessageBox.LoadIcon(),
            Content = new ScrollViewer
            {
                MaxHeight = 620,
                Content = new StackPanel
                {
                    Margin = new Thickness(22),
                    Spacing = 12,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = existing is null ? "新建待办" : "编辑待办",
                            FontSize = 17,
                            FontWeight = FontWeight.SemiBold,
                            Foreground = Res("TodoTextPrimary", "#1B1B1B"),
                        },
                        Field("标题", title),
                        Field("截止时间", due),
                        Field("提醒时间", remind),
                        Field("优先级", priority),
                        Field("标签", tags),
                        Field("备注", note),
                        error,
                        buttons,
                    },
                },
            },
        };

        save.Click += (_, _) =>
        {
            void Fail(string message)
            {
                error.Text = message;
                error.IsVisible = true;
            }

            error.IsVisible = false;
            var text = title.Text?.Trim() ?? string.Empty;
            if (text.Length == 0)
            {
                Fail("标题不能为空。");
                return;
            }

            if (!TryParseWhen(due.Text, out var dueAt, out var dueError))
            {
                Fail(dueError!);
                return;
            }

            if (!TryParseWhen(remind.Text, out var remindAt, out var remindError))
            {
                Fail(remindError!);
                return;
            }

            if (remindAt is { } ra && dueAt is { } da && ra > da)
            {
                Fail("提醒时间不能晚于截止时间。");
                return;
            }

            var priorityValue = priority.SelectedIndex switch
            {
                1 => TodoPriority.High,
                2 => TodoPriority.Low,
                _ => TodoPriority.Normal,
            };

            result = new MainViewModel.ManualTodoDraft(
                text,
                dueAt,
                priorityValue,
                SplitTags(tags.Text),
                string.IsNullOrWhiteSpace(note.Text) ? null : note.Text.Trim(),
                remindAt);
            window.Close();
        };

        cancel.Click += (_, _) => window.Close();

        await window.ShowDialog(owner);
        return result;
    }

    private static Control Field(string label, Control input) => new StackPanel
    {
        Spacing = 4,
        Children =
        {
            new TextBlock
            {
                Text = label,
                FontSize = 12,
                Foreground = Res("TodoTextSecondary", "#5C5C5C"),
            },
            input,
        },
    };

    private static IReadOnlyList<string> SplitTags(string? text) =>
        (text ?? string.Empty)
            .Split([',', '，', ';', '；'], StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Trim())
            .Where(t => t.Length > 0)
            .ToList();

    /// <summary>解析 <c>yyyy-MM-dd HH:mm</c> 或其它常见格式；空串表示不设置。</summary>
    private static bool TryParseWhen(string? text, out DateTimeOffset? value, out string? error)
    {
        value = null;
        error = null;
        var trimmed = (text ?? string.Empty).Trim();
        if (trimmed.Length == 0) return true;

        var formats = new[]
        {
            "yyyy-MM-dd HH:mm", "yyyy/M/d H:mm", "yyyy-MM-dd HH:mm:ss",
            "yyyy/M/d H:mm:ss", "yyyy-MM-dd", "yyyy/M/d",
        };
        if (DateTime.TryParseExact(trimmed, formats, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeLocal, out var parsed))
        {
            value = new DateTimeOffset(parsed);
            return true;
        }

        if (DateTimeOffset.TryParse(trimmed, CultureInfo.CurrentCulture,
                DateTimeStyles.AssumeLocal, out var loose))
        {
            value = loose;
            return true;
        }

        error = $"时间格式无法识别：{trimmed}（示例 2026-10-20 18:00）";
        return false;
    }
}
