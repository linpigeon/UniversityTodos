using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using UniversityTodos.Services;
using Calendar = Avalonia.Controls.Calendar;

namespace UniversityTodos.Views;

/// <summary>
/// 待办编辑框的日期时间字段：平时只显示摘要按钮，点击后在 Popup 卡片里选择。
/// <para>
/// 草稿只存在弹层控件里；点“确定”才写回字段，点外部（light dismiss）不会污染已保存值。
/// “清除 / 无日期”表示不设置，摘要显示“未设置”。
/// </para>
/// </summary>
internal sealed class DateTimeField
{
    private readonly string _fieldName;
    private readonly DateTime _defaultDate;
    private readonly TimeSpan _defaultTime;
    private readonly TextBlock _summaryText;
    private readonly Button _summaryButton;
    private readonly Popup _popup;
    private readonly TabControl _tabs;
    private readonly Calendar _calendar;
    private readonly TimePicker _timePicker;
    private readonly TextBlock _error;
    private DateTimeOffset? _value;

    public Control View { get; }

    public DateTimeField(
        string fieldName,
        DateTimeOffset? existing,
        bool initiallySet,
        DateTime defaultDate,
        TimeSpan defaultTime,
        string namePrefix)
    {
        _fieldName = fieldName;
        _defaultDate = defaultDate.Date;
        _defaultTime = new TimeSpan(defaultTime.Hours, defaultTime.Minutes, 0);

        // 已有值统一按本地时分归一化：界面展示到分钟，保存也必须把秒/毫秒归零。
        _value = existing is { } old
            ? DateTimeSelection.Compose(
                fieldName, true, old.LocalDateTime.Date, old.LocalDateTime.TimeOfDay, out _)
            : initiallySet
                ? DateTimeSelection.Compose(fieldName, true, _defaultDate, _defaultTime, out _)
                : null;

        _summaryText = new TextBlock
        {
            Name = $"{namePrefix}SummaryText",
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = Res("TodoTextPrimary", "#1B1B1B"),
        };
        var chevron = new TextBlock
        {
            Text = "\uF078",
            Classes = { "icon" },
            FontSize = 12,
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Res("TodoTextTertiary", "#8A8A8A"),
        };
        var summaryContent = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Children = { _summaryText, chevron },
        };
        Grid.SetColumn(chevron, 1);

        _summaryButton = new Button
        {
            Name = $"{namePrefix}SummaryButton",
            Content = summaryContent,
            Classes = { "outline" },
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(12, 8),
        };
        ToolTip.SetTip(_summaryButton, "点击选择日期和时间");
        _summaryButton.Click += (_, _) => TogglePopup();

        _calendar = new Calendar
        {
            Name = $"{namePrefix}Calendar",
            SelectionMode = CalendarSelectionMode.SingleDate,
            IsTodayHighlighted = true,
            MinWidth = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0, 4, 0, 0),
        };
        _timePicker = new TimePicker
        {
            Name = $"{namePrefix}TimePicker",
            ClockIdentifier = "24HourClock",
            UseSeconds = false,
            MinuteIncrement = 1,
            Width = 180,
            MinWidth = 0,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        _error = new TextBlock
        {
            Name = $"{namePrefix}PopupError",
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Res("TodoOverdue", "#C42B1C"),
            IsVisible = false,
        };

        var quickRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Children =
            {
                QuickButton("今天", $"{namePrefix}QuickToday", () => SetDraftDate(DateTime.Today)),
                QuickButton("明天", $"{namePrefix}QuickTomorrow", () => SetDraftDate(DateTime.Today.AddDays(1))),
                QuickButton("下周", $"{namePrefix}QuickNextWeek", () => SetDraftDate(DateTime.Today.AddDays(7))),
                QuickButton("无日期", $"{namePrefix}QuickNone", ClearValue),
            },
        };

        var datePage = new StackPanel
        {
            Spacing = 8,
            Children = { quickRow, _calendar },
        };

        var timePage = new StackPanel
        {
            Spacing = 8,
            Margin = new Thickness(0, 8, 0, 8),
            Children =
            {
                new TextBlock
                {
                    Text = "选择时间（24 小时制）",
                    FontSize = 12,
                    Foreground = Res("TodoTextSecondary", "#5C5C5C"),
                },
                _timePicker,
            },
        };

        var dateTab = new TabItem
        {
            Name = $"{namePrefix}DateTab",
            Header = "日期",
            Content = datePage,
        };
        var timeTab = new TabItem
        {
            Name = $"{namePrefix}TimeTab",
            Header = "时间",
            Content = timePage,
        };
        _tabs = new TabControl
        {
            Name = $"{namePrefix}Tabs",
            Items = { dateTab, timeTab },
            SelectedIndex = 0,
            Padding = new Thickness(0),
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            MinHeight = 0,
        };

        var clear = new Button
        {
            Name = $"{namePrefix}Clear",
            Content = "清除",
            Classes = { "outline" },
            MinWidth = 72,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        clear.Click += (_, _) => ClearValue();

        var confirm = new Button
        {
            Name = $"{namePrefix}Confirm",
            Content = "确定",
            Classes = { "primary" },
            MinWidth = 72,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        confirm.Click += (_, _) => Confirm();

        var footer = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Thickness(0, 2, 0, 0),
            Children = { clear, confirm },
        };
        Grid.SetColumn(confirm, 1);

        var card = new Border
        {
            Name = $"{namePrefix}PopupCard",
            Width = 330,
            Background = Res("TodoDialogBackground", "#FFFFFF"),
            BorderBrush = Res("TodoBorder", "#E0E0E0"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14),
            BoxShadow = BoxShadows.Parse("0 6 20 0 #33000000"),
            Child = new StackPanel
            {
                Spacing = 10,
                Children = { _tabs, _error, footer },
            },
        };

        _popup = new Popup
        {
            Name = $"{namePrefix}Popup",
            PlacementTarget = _summaryButton,
            Placement = PlacementMode.BottomEdgeAlignedLeft,
            VerticalOffset = 4,
            IsLightDismissEnabled = true,
            ShouldUseOverlayLayer = true,
            Child = card,
        };

        View = new Panel { Children = { _summaryButton, _popup } };
        UpdateSummary();
    }

    /// <summary>读取已提交的值；未设置时返回 null。</summary>
    public bool TryGetValue(out DateTimeOffset? value, out string? error)
    {
        value = _value;
        error = null;
        return true;
    }

    private static Button QuickButton(string text, string name, Action action)
    {
        var button = new Button
        {
            Name = name,
            Content = text,
            Classes = { "outline" },
            Padding = new Thickness(10, 5),
            FontSize = 12,
            MinWidth = 0,
        };
        button.Click += (_, _) => action();
        return button;
    }

    private void TogglePopup()
    {
        if (_popup.IsOpen)
        {
            // 弹层已开时再点摘要按钮视为收起，不提交草稿。
            _popup.IsOpen = false;
            return;
        }

        var draft = _value ?? DateTimeSelection.Compose(
            _fieldName, true, _defaultDate, _defaultTime, out _);
        var local = draft?.LocalDateTime ?? _defaultDate.Add(_defaultTime);

        _calendar.SelectedDate = local.Date;
        _calendar.DisplayDate = local.Date;
        _timePicker.SelectedTime = new TimeSpan(local.Hour, local.Minute, 0);
        _tabs.SelectedIndex = 0;
        HideError();
        _popup.IsOpen = true;
    }

    private void SetDraftDate(DateTime date)
    {
        // 快捷日期只改日期，时分保留当前草稿值。
        _calendar.SelectedDate = date;
        _calendar.DisplayDate = date;
        HideError();
    }

    private void Confirm()
    {
        var composed = DateTimeSelection.Compose(
            _fieldName, true, _calendar.SelectedDate, _timePicker.SelectedTime, out var error);
        if (composed is null)
        {
            ShowError(error ?? $"{_fieldName}：请选择完整的日期和时间。");
            return;
        }

        _value = composed;
        UpdateSummary();
        HideError();
        _popup.IsOpen = false;
    }

    private void ClearValue()
    {
        _value = null;
        UpdateSummary();
        HideError();
        _popup.IsOpen = false;
    }

    private void UpdateSummary() =>
        _summaryText.Text = _value is { } value ? FormatSummary(value) : "未设置";

    private static string FormatSummary(DateTimeOffset value)
    {
        var local = value.LocalDateTime;
        var time = local.ToString("HH:mm", CultureInfo.InvariantCulture);
        return (local.Date - DateTime.Today).Days switch
        {
            0 => $"今天 {time}",
            1 => $"明天 {time}",
            _ => local.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
        };
    }

    private void ShowError(string message)
    {
        _error.Text = message;
        _error.IsVisible = true;
    }

    private void HideError()
    {
        _error.Text = string.Empty;
        _error.IsVisible = false;
    }

    private static IBrush Res(string key, string fallback)
    {
        var app = Application.Current;
        if (app?.TryGetResource(key, app.ActualThemeVariant, out var value) == true
            && value is IBrush brush)
            return brush;
        return SolidColorBrush.Parse(fallback);
    }
}
