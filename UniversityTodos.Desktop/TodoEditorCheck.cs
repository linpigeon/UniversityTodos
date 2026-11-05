using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using UniversityTodos.Models;
using UniversityTodos.Services;
using UniversityTodos.ViewModels;
using Calendar = Avalonia.Controls.Calendar;

namespace UniversityTodos.Desktop;

/// <summary>
/// 新建待办对话框的真实交互与布局检查（<c>--todoeditorcheck [png]</c>）。
/// <para>
/// 这里不构造假控件树，而是真实调用 <c>TodoEditorWindow</c>，打开摘要按钮的 Popup，
/// 驱动“快捷日期 → 确定 / light dismiss / 清除”这几条用户路径，并断言摘要与边界。
/// 弹层用 <c>ShouldUseOverlayLayer</c>，因此同一张窗口离屏渲染里应包含 Popup。
/// </para>
/// </summary>
internal static class TodoEditorCheck
{
    public static int Run(string? renderPath)
    {
        Console.OutputEncoding = Encoding.UTF8;
        App.DeferInitialLoad = true;

        try
        {
            Program.BuildAvaloniaApp().SetupWithoutStarting();

            var owner = new Window
            {
                Title = "todo-editor-check-owner",
                Width = 640,
                Height = 480,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Position = new PixelPoint(40, 40),
            };
            owner.Show();
            Pump();

            var showTask = OpenEditor(owner, null);

            var dialog = WaitForDialog(owner, "新建待办", TimeSpan.FromSeconds(10))
                         ?? throw new InvalidOperationException("10 秒内没有出现「新建待办」对话框");
            Stabilize(dialog);

            var failures = new List<string>();
            var size = dialog.Bounds.Size;
            var content = FindScrollContent(dialog);
            var contentRight = content?.TranslatePoint(new Point(content.Bounds.Width, 0), dialog)?.X
                               ?? size.Width;
            var contentWidth = content?.Bounds.Width ?? size.Width;
            Console.WriteLine($"窗口: {size.Width:0.#}x{size.Height:0.#}");
            Console.WriteLine($"内容区: width={contentWidth:0.#} right={contentRight:0.#}");

            var dueButton = Find<Button>(dialog, "DueSummaryButton");
            var remindButton = Find<Button>(dialog, "RemindSummaryButton");
            var dueText = Find<TextBlock>(dialog, "DueSummaryText");
            var remindText = Find<TextBlock>(dialog, "RemindSummaryText");

            CheckControl(dueButton, "DueSummaryButton", contentRight, size.Width, size.Height, dialog, failures);
            CheckControl(remindButton, "RemindSummaryButton", contentRight, size.Width, size.Height, dialog, failures);
            if (dueButton.Bounds.Width < contentWidth - 2)
                failures.Add($"截止摘要按钮未占满内容宽度：{dueButton.Bounds.Width:0.#} < {contentWidth:0.#}");
            if (remindButton.Bounds.Width < contentWidth - 2)
                failures.Add($"提醒摘要按钮未占满内容宽度：{remindButton.Bounds.Width:0.#} < {contentWidth:0.#}");
            if (dueText.Text is not { } dueSummary || !dueSummary.Contains("今天", StringComparison.Ordinal)
                || !dueSummary.Contains("18:00", StringComparison.Ordinal))
                failures.Add($"新建截止摘要应为「今天 18:00」，实际「{dueText.Text}」");
            if (remindText.Text != "未设置")
                failures.Add($"新建提醒摘要应为「未设置」，实际「{remindText.Text}」");

            var pngPaths = ResolvePngPaths(renderPath);
            if (pngPaths is { } paths)
                Render(dialog, paths.Redesign);

            // ---- 日期弹层 ----
            Click(dueButton);
            var duePopup = Find<Popup>(dialog, "DuePopup");
            if (!duePopup.IsLightDismissEnabled) failures.Add("日期弹层应启用 light dismiss");
            if (!duePopup.ShouldUseOverlayLayer) failures.Add("日期弹层应使用 OverlayLayer（保证截图与窗口内显示）");
            var dueCard = WaitForPopupCard(duePopup);
            CheckPopupCard(dueCard, "DuePopupCard", size, dialog, failures);

            var dueTabs = Find<TabControl>(dialog, "DueTabs");
            var dueDateTab = Find<TabItem>(dialog, "DueDateTab");
            var dueTimeTab = Find<TabItem>(dialog, "DueTimeTab");
            if (dueDateTab.Header?.ToString() != "日期") failures.Add("第一个页签不是「日期」");
            if (dueTimeTab.Header?.ToString() != "时间") failures.Add("第二个页签不是「时间」");

            var dueCalendar = Find<Calendar>(dialog, "DueCalendar");
            var dueQuickToday = Find<Button>(dialog, "DueQuickToday");
            var dueQuickTomorrow = Find<Button>(dialog, "DueQuickTomorrow");
            var dueQuickNextWeek = Find<Button>(dialog, "DueQuickNextWeek");
            var dueQuickNone = Find<Button>(dialog, "DueQuickNone");
            var dueConfirm = Find<Button>(dialog, "DueConfirm");
            var dueClear = Find<Button>(dialog, "DueClear");
            if (dueCalendar.SelectionMode != CalendarSelectionMode.SingleDate)
                failures.Add($"Calendar 应单选，实际 {dueCalendar.SelectionMode}");
            if (dueCalendar.SelectedDate?.Date != DateTime.Today)
                failures.Add($"截止弹层初始日期应为今天，实际 {dueCalendar.SelectedDate:yyyy-MM-dd}");
            var cardRight = dueCard.TranslatePoint(new Point(dueCard.Bounds.Width, 0), dialog)?.X
                            ?? dueCard.Bounds.Width;
            foreach (var (control, name) in new (Control, string)[]
                     {
                         (dueTabs, "DueTabs"), (dueCalendar, "DueCalendar"),
                         (dueQuickToday, "DueQuickToday"), (dueQuickTomorrow, "DueQuickTomorrow"),
                         (dueQuickNextWeek, "DueQuickNextWeek"), (dueQuickNone, "DueQuickNone"),
                         (dueConfirm, "DueConfirm"), (dueClear, "DueClear"),
                     })
                CheckControl(control, name, cardRight, size.Width, size.Height, dialog, failures);

            if (pngPaths is { } calendarPaths)
                Render(dialog, calendarPaths.Calendar);

            // ---- 时间页 ----
            dueTabs.SelectedIndex = 1;
            Pump();
            var timeCard = WaitForPopupCard(duePopup);
            var timeCardRight = timeCard.TranslatePoint(new Point(timeCard.Bounds.Width, 0), dialog)?.X
                                ?? timeCard.Bounds.Width;
            CheckControl(timeCard, "DuePopupCard（时间页）", size.Width, size.Width, size.Height, dialog, failures);
            var dueTimePicker = Find<TimePicker>(dialog, "DueTimePicker");
            if (dueTimePicker.ClockIdentifier != "24HourClock")
                failures.Add($"TimePicker 应使用 24 小时制，实际 {dueTimePicker.ClockIdentifier}");
            if (dueTimePicker.UseSeconds) failures.Add("TimePicker 不应显示秒");
            CheckControl(dueTimePicker, "DueTimePicker", timeCardRight, size.Width, size.Height, dialog, failures);
            if (pngPaths is { } timePaths)
                Render(dialog, timePaths.Time);

            // ---- 交互：缺日期不得静默保存 ----
            dueTabs.SelectedIndex = 0;
            Pump();
            var dueError = Find<TextBlock>(dialog, "DuePopupError");
            dueCalendar.SelectedDate = null;
            Click(dueConfirm);
            if (!duePopup.IsOpen) failures.Add("缺少日期时点「确定」不应关闭弹层");
            if (!dueError.IsVisible) failures.Add("缺少日期时应在弹层内显示校验错误");
            if (dueText.Text is not { } unchanged
                || !unchanged.Contains("今天", StringComparison.Ordinal)
                || !unchanged.Contains("18:00", StringComparison.Ordinal))
                failures.Add($"缺少日期时不应修改摘要，实际「{dueText.Text}」");

            // ---- 交互：快捷日期保留 18:00，确定后摘要更新 ----
            Click(dueQuickTomorrow);
            if (dueCalendar.SelectedDate?.Date != DateTime.Today.AddDays(1))
                failures.Add($"点「明天」后日历日期应变为明天，实际 {dueCalendar.SelectedDate:yyyy-MM-dd}");
            Click(dueConfirm);
            Pump();
            if (duePopup.IsOpen) failures.Add("点「确定」后弹层应关闭");
            var tomorrowSummary = dueText.Text ?? string.Empty;
            if (!tomorrowSummary.StartsWith("明天 ", StringComparison.Ordinal)
                || !tomorrowSummary.EndsWith(" 18:00", StringComparison.Ordinal))
                failures.Add($"确定后截止摘要应为「明天 … 18:00」，实际「{tomorrowSummary}」");

            // ---- light dismiss：未确认的改动不能污染已保存值 ----
            Click(dueButton);
            WaitForPopupCard(duePopup);
            if (dueCalendar.SelectedDate?.Date != DateTime.Today.AddDays(1))
                failures.Add("重新打开后草稿应从已保存的明天开始");
            dueTabs.SelectedIndex = 1;
            Pump();
            Click(dueQuickNextWeek); // 日期页控件仍在逻辑树里，快捷按钮改的是同一个 Calendar
            duePopup.IsOpen = false;  // 模拟点击弹层外部
            Pump();
            if (dueText.Text != tomorrowSummary)
                failures.Add($"light dismiss 不应修改摘要：原「{tomorrowSummary}」，现「{dueText.Text}」");
            Click(dueButton);
            WaitForPopupCard(duePopup);
            if (dueCalendar.SelectedDate?.Date != DateTime.Today.AddDays(1))
                failures.Add("light dismiss 后重新打开，草稿应回到已保存值（明天）");
            duePopup.IsOpen = false;
            Pump();

            // ---- 提醒：空 → 确定 → 清除 ----
            Click(remindButton);
            var remindPopup = Find<Popup>(dialog, "RemindPopup");
            var remindCard = WaitForPopupCard(remindPopup);
            CheckPopupCard(remindCard, "RemindPopupCard", size, dialog, failures);
            var remindCalendar = Find<Calendar>(dialog, "RemindCalendar");
            var remindTimePicker = Find<TimePicker>(dialog, "RemindTimePicker");
            var remindConfirm = Find<Button>(dialog, "RemindConfirm");
            var remindClear = Find<Button>(dialog, "RemindClear");
            var remindQuickNone = Find<Button>(dialog, "RemindQuickNone");
            if (remindText.Text != "未设置")
                failures.Add($"提醒初始摘要应为「未设置」，实际「{remindText.Text}」");
            if (remindCalendar.SelectedDate is not { } remindDate
                || (remindDate.Date != DateTime.Today && remindDate.Date != DateTime.Today.AddDays(1)))
                failures.Add($"空提醒的草稿日期应是今天或明天，实际 {remindCalendar.SelectedDate:yyyy-MM-dd}");
            if (remindTimePicker.SelectedTime is not { } remindTime
                || remindTime.Seconds != 0 || (remindTime.Minutes != 0 && remindTime.Minutes != 30))
                failures.Add($"空提醒的草稿时间应是 30 分钟刻度，实际 {remindTimePicker.SelectedTime}");
            Click(remindQuickNone);
            Pump();
            if (remindPopup.IsOpen) failures.Add("提醒点「无日期」后弹层应关闭");
            if (remindText.Text != "未设置")
                failures.Add($"提醒点「无日期」后摘要应保持「未设置」，实际「{remindText.Text}」");

            Click(remindButton);
            WaitForPopupCard(remindPopup);
            Click(remindConfirm);
            Pump();
            if (remindText.Text == "未设置")
                failures.Add("提醒点「确定」后摘要不应仍为「未设置」");
            Click(remindButton);
            WaitForPopupCard(remindPopup);
            Click(remindClear);
            Pump();
            if (remindText.Text != "未设置")
                failures.Add($"提醒点「清除」后摘要应恢复「未设置」，实际「{remindText.Text}」");
            if (remindPopup.IsOpen) failures.Add("提醒点「清除」后弹层应关闭");

            dialog.Close();
            PumpUntil(showTask, TimeSpan.FromSeconds(5));

            // ---- 编辑已有待办：值准确回显，空提醒保持「未设置」 ----
            var existing = new TodoItem
            {
                Id = "man-editor-check",
                Kind = TodoKind.Custom,
                Title = "编辑回显检查",
                DueAt = new DateTimeOffset(2026, 11, 5, 9, 30, 0, TimeSpan.FromHours(8)),
                RemindAt = null,
            };
            var editTask = OpenEditor(owner, existing);
            var editDialog = WaitForDialog(owner, "编辑待办", TimeSpan.FromSeconds(10))
                             ?? throw new InvalidOperationException("10 秒内没有出现「编辑待办」对话框");
            Stabilize(editDialog);
            var editDueText = Find<TextBlock>(editDialog, "DueSummaryText");
            var editRemindText = Find<TextBlock>(editDialog, "RemindSummaryText");
            if (editDueText.Text != "2026-11-05 09:30")
                failures.Add($"编辑回显截止时间不符：实际「{editDueText.Text}」");
            if (editRemindText.Text != "未设置")
                failures.Add($"编辑时原本为空的提醒应保持「未设置」，实际「{editRemindText.Text}」");
            editDialog.Close();
            PumpUntil(editTask, TimeSpan.FromSeconds(5));

            owner.Close();

            if (pngPaths is { } finalPaths)
            {
                Console.WriteLine($"PNG 初始: {finalPaths.Redesign}");
                Console.WriteLine($"PNG 月历: {finalPaths.Calendar}");
                Console.WriteLine($"PNG 时间: {finalPaths.Time}");
            }

            if (failures.Count == 0)
            {
                Console.WriteLine("交互与布局检查通过。");
                return 0;
            }

            Console.WriteLine("交互与布局检查失败：");
            foreach (var failure in failures) Console.WriteLine("  - " + failure);
            return 2;
        }
        catch (Exception ex)
        {
            Console.WriteLine("检查异常：");
            Console.WriteLine(ex);
            return 1;
        }
    }

    private static (string Redesign, string Calendar, string Time)? ResolvePngPaths(string? renderPath)
    {
        if (renderPath is null) return null;
        var full = Path.GetFullPath(renderPath);
        var dir = Path.GetDirectoryName(full)!;
        return (
            full,
            Path.Combine(dir, "todo-editor-calendar.png"),
            Path.Combine(dir, "todo-editor-time.png"));
    }

    private static Task OpenEditor(Window owner, TodoItem? existing)
    {
        var editorType = typeof(MainViewModel).Assembly.GetType(
                             "UniversityTodos.Views.TodoEditorWindow", throwOnError: true)
                         ?? throw new InvalidOperationException("找不到 TodoEditorWindow 类型");
        var showAsync = editorType.GetMethod(
                            "ShowAsync", BindingFlags.Public | BindingFlags.Static)
                        ?? throw new InvalidOperationException("找不到 TodoEditorWindow.ShowAsync");
        return (Task)showAsync.Invoke(null, [owner, existing])!;
    }

    private static Window? WaitForDialog(Window owner, string title, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.Now + timeout;
        while (DateTimeOffset.Now < deadline)
        {
            Pump();
            var dialog = owner.OwnedWindows.FirstOrDefault(w => w.Title == title);
            if (dialog is not null) return dialog;
        }
        return null;
    }

    private static Control? FindScrollContent(Window dialog)
    {
        var scroll = dialog.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        return scroll?.Content as Control;
    }

    private static void Stabilize(Window window)
    {
        for (var i = 0; i < 20; i++)
        {
            Pump();
            var size = window.Bounds.Size;
            if (size.Width > 0 && size.Height > 0)
            {
                window.Measure(size);
                window.Arrange(new Rect(size));
            }
        }
    }

    private static Border WaitForPopupCard(Popup popup)
    {
        var deadline = DateTimeOffset.Now.AddSeconds(5);
        while (DateTimeOffset.Now < deadline)
        {
            Pump();
            if (popup.IsOpen && popup.Child is Border { Bounds.Width: > 0 } card)
                return card;
        }
        throw new InvalidOperationException($"Popup「{popup.Name}」未在 5 秒内完成布局");
    }

    private static void Click(Button button)
    {
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Pump();
    }

    private static void Pump()
    {
        Dispatcher.UIThread.RunJobs();
        Thread.Sleep(30);
        Dispatcher.UIThread.RunJobs();
    }

    private static T Find<T>(Visual root, string name) where T : Control
    {
        var found = root.GetVisualDescendants().OfType<T>().FirstOrDefault(c => c.Name == name);
        if (found is not null) return found;

        // Popup 的宿主可能挂在 OverlayLayer 上，视觉树完全展开前逻辑树里也可能找得到。
        if (root is ILogical logical)
        {
            found = logical.GetLogicalDescendants().OfType<T>().FirstOrDefault(c => c.Name == name);
            if (found is not null) return found;
        }

        throw new InvalidOperationException($"找不到控件 {typeof(T).Name}「{name}」");
    }

    private static void CheckPopupCard(
        Border card, string name, Size windowSize, Window dialog, List<string> failures)
    {
        CheckControl(card, name, windowSize.Width, windowSize.Width, windowSize.Height, dialog, failures);
        if (card.Bounds.Width < 300 || card.Bounds.Width > 360)
            failures.Add($"{name}: 宽度应约 320-350，实际 {card.Bounds.Width:0.#}");
    }

    private static void CheckControl(
        Control control,
        string name,
        double contentRight,
        double windowWidth,
        double windowHeight,
        Window dialog,
        List<string> failures)
    {
        var topLeft = control.TranslatePoint(new Point(0, 0), dialog);
        if (topLeft is not { } origin)
        {
            failures.Add($"{name}: 无法换算到对话框坐标");
            return;
        }

        var right = origin.X + control.Bounds.Width;
        var bottom = origin.Y + control.Bounds.Height;
        Console.WriteLine(
            $"{name}: x={origin.X:0.#} y={origin.Y:0.#} " +
            $"w={control.Bounds.Width:0.#} h={control.Bounds.Height:0.#} right={right:0.#} bottom={bottom:0.#}");

        if (control.Bounds.Width <= 0 || control.Bounds.Height <= 0)
            failures.Add($"{name}: 尺寸无效 {control.Bounds.Width:0.#}x{control.Bounds.Height:0.#}");
        if (right > contentRight + 0.5)
            failures.Add($"{name}: 右边界 {right:0.#} 超出可视区 {contentRight:0.#}");
        if (right > windowWidth + 0.5)
            failures.Add($"{name}: 右边界 {right:0.#} 超出窗口 {windowWidth:0.#}");
        if (origin.X < -0.5)
            failures.Add($"{name}: 左边界 {origin.X:0.#} 超出窗口");
        if (origin.Y < -0.5)
            failures.Add($"{name}: 上边界 {origin.Y:0.#} 超出窗口");
        if (bottom > windowHeight + 0.5)
            failures.Add($"{name}: 下边界 {bottom:0.#} 超出窗口 {windowHeight:0.#}");

        if (control.GetVisualParent() is Control parent
            && parent.TranslatePoint(new Point(parent.Bounds.Width, 0), dialog) is { } parentRight
            && right > parentRight.X + 0.5)
            failures.Add($"{name}: 右边界 {right:0.#} 超出父容器 {parentRight.X:0.#}");
    }

    private static void Render(Window dialog, string path)
    {
        Pump();
        var size = dialog.Bounds.Size;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var pixelSize = new PixelSize(
            Math.Max(1, (int)Math.Ceiling(size.Width)),
            Math.Max(1, (int)Math.Ceiling(size.Height)));
        using var bitmap = new RenderTargetBitmap(pixelSize, new Vector(96, 96));
        bitmap.Render(dialog);
        bitmap.Save(path);
    }

    private static void PumpUntil(Task task, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.Now + timeout;
        while (!task.IsCompleted && DateTimeOffset.Now < deadline)
            Pump();

        if (task.IsCompleted)
        {
            try { task.GetAwaiter().GetResult(); }
            catch (Exception ex) { Console.WriteLine("关闭编辑器时异常：" + ex.Message); }
        }
    }
}
