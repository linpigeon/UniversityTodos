using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using UniversityTodos.Models;
using UniversityTodos.Services;
using UniversityTodos.ViewModels;

namespace UniversityTodos.Views;

/// <summary>自建待办的共享表单；桌面窗口与 Android 全屏页共用字段和校验。</summary>
internal sealed class TodoEditorForm
{
    public TodoEditorForm(TodoItem? existing, bool mobile)
    {
        var title = new TextBox
        {
            Text = existing?.Title ?? string.Empty,
            PlaceholderText = "例如：打印实验报告",
            MaxLength = 200,
        };
        var dueField = new DateTimeField(
            "截止时间",
            existing?.DueAt,
            initiallySet: existing is null,
            DateTime.Today,
            new TimeSpan(18, 0, 0),
            "Due");
        var remindDefault = DateTimeSelection.NextHalfHour(DateTime.Now);
        var remindField = new DateTimeField(
            "提醒时间",
            existing?.RemindAt,
            initiallySet: false,
            remindDefault.Date,
            remindDefault.TimeOfDay,
            "Remind");
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

        var body = new StackPanel
        {
            Margin = new Thickness(mobile ? 20 : 22, mobile ? 20 : 22, mobile ? 20 : 22, mobile ? 8 : 22),
            Spacing = 12,
            Children =
            {
                new TextBlock
                {
                    Text = existing is null ? "新建待办" : "编辑待办",
                    FontSize = mobile ? 22 : 17,
                    FontWeight = FontWeight.SemiBold,
                    Foreground = Res("TodoTextPrimary", "#1B1B1B"),
                },
                Field("标题", title),
                Field("截止时间", dueField.View),
                Field("提醒时间", remindField.View),
                Field("优先级", priority),
                Field("标签", tags),
                Field("备注", note),
                error,
            },
        };

        if (mobile)
        {
            // 按钮固定在底部，不随表单滚动到屏幕外。
            var footer = new Border
            {
                Padding = new Thickness(20, 12, 20, 16),
                BorderBrush = Res("TodoBorder", "#E5E3E1"),
                BorderThickness = new Thickness(0, 1, 0, 0),
                Background = Res("TodoDialogBackground", "#FFFFFF"),
                Child = buttons,
            };
            var root = new Grid
            {
                RowDefinitions = new RowDefinitions("*,Auto"),
                Children =
                {
                    new ScrollViewer
                    {
                        VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                        Content = body,
                    },
                    footer,
                },
            };
            Grid.SetRow(footer, 1);
            View = root;
        }
        else
        {
            body.Children.Add(buttons);
            View = new ScrollViewer
            {
                MaxHeight = 620,
                VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                Content = body,
            };
        }

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

            if (!dueField.TryGetValue(out var dueAt, out var dueError))
            {
                Fail(dueError!);
                return;
            }

            if (!remindField.TryGetValue(out var remindAt, out var remindError))
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

            Result = new MainViewModel.ManualTodoDraft(
                text,
                dueAt,
                priorityValue,
                SplitTags(tags.Text),
                string.IsNullOrWhiteSpace(note.Text) ? null : note.Text.Trim(),
                remindAt);
            Completed?.Invoke(this, EventArgs.Empty);
        };

        cancel.Click += (_, _) => Completed?.Invoke(this, EventArgs.Empty);
    }

    public Control View { get; }

    public MainViewModel.ManualTodoDraft? Result { get; private set; }

    public event EventHandler? Completed;

    private static IBrush Res(string key, string fallback)
    {
        var app = Application.Current;
        if (app?.TryGetResource(key, app.ActualThemeVariant, out var v) == true && v is IBrush b)
            return b;
        return SolidColorBrush.Parse(fallback);
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
}
