using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using UniversityTodos.Models;
using UniversityTodos.Services;

namespace UniversityTodos.Views;

/// <summary>
/// 课表管理对话框：手工增删改课程 + 从 CSV/TSV 模板导入。
/// <para>
/// 存在的理由：教务导入只支持正方网页。换学校、页面改版，或只想补几门课时，
/// 用户至少还能手工维护；导入沿用「先解析预览、再整批提交」的口径。
/// </para>
/// </summary>
internal static class ScheduleManagerWindow
{
    private static IBrush Res(string key, string fallback)
    {
        var app = Application.Current;
        if (app?.TryGetResource(key, app.ActualThemeVariant, out var v) == true && v is IBrush b)
            return b;
        return SolidColorBrush.Parse(fallback);
    }

    /// <summary>返回编辑后的课表；取消返回 null。</summary>
    public static async Task<ScheduleFile?> ShowAsync(Window owner, ScheduleFile source, int term)
    {
        var file = new ScheduleFile
        {
            Items = source.Items.Select(Clone).ToList(),
            Periods = source.Periods.Select(p => new PeriodSlot
            {
                Period = p.Period, StartTime = p.StartTime, EndTime = p.EndTime,
            }).ToList(),
        };

        var list = new ListBox { MinHeight = 220, MaxHeight = 320 };
        var error = new TextBlock
        {
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Res("TodoOverdue", "#C42B1C"),
            IsVisible = false,
        };

        var name = new TextBox { PlaceholderText = "课程名" };
        var teacher = new TextBox { PlaceholderText = "教师（可空）" };
        var location = new TextBox { PlaceholderText = "地点（可空）" };
        var weekday = new ComboBox
        {
            ItemsSource = Timetable.WeekdayLabels.ToList(),
            SelectedIndex = 0,
            MinWidth = 110,
        };
        var startPeriod = new NumericUpDown { Minimum = 1, Maximum = Timetable.MaxPeriod, Value = 1, FormatString = "0", Width = 90 };
        var endPeriod = new NumericUpDown { Minimum = 1, Maximum = Timetable.MaxPeriod, Value = 2, FormatString = "0", Width = 90 };
        var startWeek = new NumericUpDown { Minimum = 1, Maximum = Timetable.MaxWeek, Value = 1, FormatString = "0", Width = 90 };
        var endWeek = new NumericUpDown { Minimum = 1, Maximum = Timetable.MaxWeek, Value = 16, FormatString = "0", Width = 90 };
        var parity = new ComboBox
        {
            ItemsSource = new List<string> { "每周", "单周", "双周" },
            SelectedIndex = 0,
            MinWidth = 100,
        };
        var startTime = new TextBox { PlaceholderText = "08:00（留空按节次推导）", Width = 170 };
        var endTime = new TextBox { PlaceholderText = "09:40（留空按节次推导）", Width = 170 };

        string? editingId = null;

        void RefreshList(string? selectId = null)
        {
            var ordered = file.Items
                .OrderBy(i => i.Weekday)
                .ThenBy(i => i.StartPeriod)
                .ThenBy(i => i.Name, StringComparer.CurrentCulture)
                .ToList();
            list.ItemsSource = ordered.Select(DisplayText).ToList();
            if (selectId is not null)
            {
                var index = ordered.FindIndex(i => i.Id == selectId);
                if (index >= 0) list.SelectedIndex = index;
            }
        }

        void ResetForm()
        {
            editingId = null;
            name.Text = string.Empty;
            teacher.Text = string.Empty;
            location.Text = string.Empty;
            weekday.SelectedIndex = 0;
            startPeriod.Value = 1;
            endPeriod.Value = 2;
            startWeek.Value = 1;
            endWeek.Value = 16;
            parity.SelectedIndex = 0;
            startTime.Text = string.Empty;
            endTime.Text = string.Empty;
            error.IsVisible = false;
        }

        void LoadForm(ScheduleItem item)
        {
            editingId = item.Id;
            name.Text = item.Name;
            teacher.Text = item.Teacher ?? string.Empty;
            location.Text = item.Location ?? string.Empty;
            weekday.SelectedIndex = Math.Clamp(item.Weekday - 1, 0, 6);
            startPeriod.Value = item.StartPeriod;
            endPeriod.Value = item.EndPeriod;
            startWeek.Value = item.StartWeek;
            endWeek.Value = item.EndWeek;
            parity.SelectedIndex = item.WeekParity switch
            {
                WeekParity.Odd => 1,
                WeekParity.Even => 2,
                _ => 0,
            };
            startTime.Text = item.StartTime;
            endTime.Text = item.EndTime;
            error.IsVisible = false;
        }

        void ShowError(string message)
        {
            error.Text = message;
            error.IsVisible = true;
        }

        async Task<bool> SaveRowAsync()
        {
            error.IsVisible = false;
            var title = name.Text?.Trim() ?? string.Empty;
            if (title.Length == 0)
            {
                ShowError("课程名不能为空。");
                return false;
            }

            var sPeriod = (int)(startPeriod.Value ?? 1);
            var ePeriod = (int)(endPeriod.Value ?? 1);
            var sWeek = (int)(startWeek.Value ?? 1);
            var eWeek = (int)(endWeek.Value ?? 1);

            string? startText = string.IsNullOrWhiteSpace(startTime.Text) ? null : startTime.Text.Trim();
            string? endText = string.IsNullOrWhiteSpace(endTime.Text) ? null : endTime.Text.Trim();
            if (startText is null || endText is null)
            {
                var fromPeriods = Timetable.ResolvePeriodTime(file.Periods, sPeriod);
                var endFromPeriods = Timetable.ResolvePeriodTime(file.Periods, ePeriod);
                if (fromPeriods is null || endFromPeriods is null)
                {
                    ShowError($"第 {sPeriod}-{ePeriod} 节不在节次时间表内，请填写开始/结束时间。");
                    return false;
                }
                startText ??= fromPeriods.Value.StartTime;
                endText ??= endFromPeriods.Value.EndTime;
            }

            var item = new ScheduleItem
            {
                Id = editingId ?? Guid.NewGuid().ToString("N"),
                Name = title,
                Teacher = string.IsNullOrWhiteSpace(teacher.Text) ? null : teacher.Text.Trim(),
                Location = string.IsNullOrWhiteSpace(location.Text) ? null : location.Text.Trim(),
                Weekday = weekday.SelectedIndex + 1,
                StartPeriod = sPeriod,
                EndPeriod = ePeriod,
                StartWeek = sWeek,
                EndWeek = eWeek,
                WeekParity = parity.SelectedIndex switch
                {
                    1 => WeekParity.Odd,
                    2 => WeekParity.Even,
                    _ => WeekParity.Every,
                },
                StartTime = startText,
                EndTime = endText,
                Term = term,
            };

            try
            {
                ScheduleStore.ValidateOne(item);
            }
            catch (ArgumentException ex)
            {
                ShowError(ex.Message);
                return false;
            }

            var conflicts = FindConflicts(file.Items, item).ToList();
            if (conflicts.Count > 0)
            {
                if (owner is null) return false;
                var names = string.Join("、", conflicts.Take(3).Select(c => c.Name));
                if (!await MessageBox.ConfirmAsync(owner, "时间冲突",
                        $"「{item.Name}」与 {names} 的上课时间重叠。仍要保存吗？"))
                    return false;
            }

            var index = file.Items.FindIndex(i => i.Id == item.Id);
            if (index >= 0) file.Items[index] = item;
            else file.Items.Add(item);

            RefreshList(item.Id);
            return true;
        }

        var saveRow = new Button { Content = "保存条目", Classes = { "primary" }, MinWidth = 96 };        var newRow = new Button { Content = "新增", Classes = { "outline" }, MinWidth = 80 };
        var editRow = new Button { Content = "编辑", Classes = { "outline" }, MinWidth = 80 };
        var deleteRow = new Button { Content = "删除", Classes = { "outline" }, MinWidth = 80 };
        var import = new Button { Content = "从 CSV/TSV 导入…", Classes = { "outline" }, MinWidth = 150 };
        var done = new Button { Content = "完成", Classes = { "primary" }, MinWidth = 96 };
        var cancel = new Button { Content = "取消", Classes = { "outline" }, MinWidth = 96 };

        saveRow.Click += async (_, _) => { if (await SaveRowAsync()) ResetForm(); };
        newRow.Click += (_, _) => ResetForm();
        editRow.Click += (_, _) =>
        {
            if (list.SelectedIndex < 0) return;
            var ordered = file.Items
                .OrderBy(i => i.Weekday).ThenBy(i => i.StartPeriod).ThenBy(i => i.Name, StringComparer.CurrentCulture)
                .ToList();
            LoadForm(ordered[list.SelectedIndex]);
        };
        deleteRow.Click += async (_, _) =>
        {
            if (list.SelectedIndex < 0) return;
            var ordered = file.Items
                .OrderBy(i => i.Weekday).ThenBy(i => i.StartPeriod).ThenBy(i => i.Name, StringComparer.CurrentCulture)
                .ToList();
            var item = ordered[list.SelectedIndex];
            if (!await MessageBox.ConfirmAsync(owner, "删除课程", $"确定删除「{item.Name}」吗？")) return;
            file.Items.RemoveAll(i => i.Id == item.Id);
            if (editingId == item.Id) ResetForm();
            RefreshList();
        };

        import.Click += async (_, _) =>
        {
            try
            {
                var picker = owner.StorageProvider;
                var picked = await picker.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = "选择课表模板（CSV / TSV / 文本）",
                    AllowMultiple = false,
                    FileTypeFilter =
                    [
                        new FilePickerFileType("课表模板") { Patterns = ["*.csv", "*.tsv", "*.txt"] },
                    ],
                });
                if (picked.Count == 0) return;

                await using var stream = await picked[0].OpenReadAsync();
                using var reader = new StreamReader(stream);
                var text = await reader.ReadToEndAsync();
                var rows = ScheduleImport.Parse(text, term, file.Periods);
                var valid = rows.Where(r => r.Item is not null).Select(r => r.Item!).ToList();
                var invalid = rows.Where(r => r.Item is null).ToList();

                if (valid.Count == 0 && invalid.Count == 0)
                {
                    await MessageBox.ShowAsync(owner, "导入课表", "文件里没有可用的数据行。");
                    return;
                }

                var preview = $"可导入 {valid.Count} 行，无法识别 {invalid.Count} 行。";
                if (invalid.Count > 0)
                {
                    preview += "\n\n" + string.Join("\n",
                        invalid.Take(5).Select(r => $"第 {r.RowNumber} 行：{r.Error}"))
                        + (invalid.Count > 5 ? $"\n…另有 {invalid.Count - 5} 行" : string.Empty);
                }

                if (valid.Count == 0)
                {
                    await MessageBox.ShowAsync(owner, "导入课表", preview);
                    return;
                }

                if (!await MessageBox.ConfirmAsync(owner, "导入课表", preview + "\n\n现在导入这些课程吗？"))
                    return;

                foreach (var item in valid) file.Items.Add(item);
                RefreshList();
            }
            catch (Exception ex)
            {
                await MessageBox.ShowAsync(owner, "导入失败", ex.Message);
            }
        };

        var accepted = false;
        Window? dialog = null;
        done.Click += (_, _) => { accepted = true; dialog?.Close(); };
        cancel.Click += (_, _) => dialog?.Close();

        var form = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                Row("课程名", name),
                Row("教师 / 地点", new StackPanel
                {
                    Orientation = Orientation.Horizontal, Spacing = 8,
                    Children = { teacher, location },
                }),
                Row("星期 / 单双周", new StackPanel
                {
                    Orientation = Orientation.Horizontal, Spacing = 8,
                    Children = { weekday, parity },
                }),
                Row("节次", new StackPanel
                {
                    Orientation = Orientation.Horizontal, Spacing = 8,
                    Children = { startPeriod, new TextBlock { Text = "~", VerticalAlignment = VerticalAlignment.Center }, endPeriod },
                }),
                Row("周次", new StackPanel
                {
                    Orientation = Orientation.Horizontal, Spacing = 8,
                    Children = { startWeek, new TextBlock { Text = "~", VerticalAlignment = VerticalAlignment.Center }, endWeek },
                }),
                Row("时间", new StackPanel
                {
                    Orientation = Orientation.Horizontal, Spacing = 8,
                    Children = { startTime, new TextBlock { Text = "~", VerticalAlignment = VerticalAlignment.Center }, endTime },
                }),
                error,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal, Spacing = 8,
                    Children = { newRow, saveRow },
                },
            },
        };

        var left = new StackPanel
        {
            Spacing = 8,
            Width = 330,
            Children =
            {
                new TextBlock
                {
                    Text = "课程条目（每周次段一条）",
                    FontSize = 12,
                    Foreground = Res("TodoTextSecondary", "#5C5C5C"),
                },
                list,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal, Spacing = 8,
                    Children = { editRow, deleteRow, import },
                },
            },
        };

        var rightScroll = new ScrollViewer
        {
            MaxHeight = 420,
            Content = new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    new TextBlock
                    {
                        Text = "编辑课程",
                        FontSize = 13,
                        FontWeight = FontWeight.SemiBold,
                        Foreground = Res("TodoTextPrimary", "#1B1B1B"),
                    },
                    form,
                },
            },
        };
        Grid.SetColumn(rightScroll, 1);

        var body = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            ColumnSpacing = 20,
            Children = { left, rightScroll },
        };

        var window = new Window
        {
            Title = "课表管理",
            Width = 760,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Res("TodoDialogBackground", "#FFFFFF"),
            Icon = MessageBox.LoadIcon(),
            Content = new StackPanel
            {
                Margin = new Thickness(22),
                Spacing = 14,
                Children =
                {
                    new TextBlock
                    {
                        Text = "课表管理",
                        FontSize = 17,
                        FontWeight = FontWeight.SemiBold,
                        Foreground = Res("TodoTextPrimary", "#1B1B1B"),
                    },
                    body,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 8,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Children = { cancel, done },
                    },
                },
            },
        };

        RefreshList();
        dialog = window;
        await window.ShowDialog(owner);
        return accepted ? file : null;
    }

    private static Control Row(string label, Control input) => new StackPanel
    {
        Spacing = 3,
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

    /// <summary>同一星期、节次重叠、周次区间重叠、单双周不互斥 → 冲突。</summary>
    private static IEnumerable<ScheduleItem> FindConflicts(
        IEnumerable<ScheduleItem> items, ScheduleItem candidate)
    {
        foreach (var item in items)
        {
            if (item.Id == candidate.Id) continue;
            if (item.Weekday != candidate.Weekday) continue;
            if (item.StartPeriod > candidate.EndPeriod || candidate.StartPeriod > item.EndPeriod) continue;
            if (item.StartWeek > candidate.EndWeek || candidate.StartWeek > item.EndWeek) continue;

            // 一单一双在同一区间内不会同时上课。
            if (item.WeekParity != WeekParity.Every
                && candidate.WeekParity != WeekParity.Every
                && item.WeekParity != candidate.WeekParity)
                continue;

            yield return item;
        }
    }

    private static ScheduleItem Clone(ScheduleItem item) => new()
    {
        Id = item.Id,
        Name = item.Name,
        Teacher = item.Teacher,
        Location = item.Location,
        Weekday = item.Weekday,
        StartPeriod = item.StartPeriod,
        EndPeriod = item.EndPeriod,
        StartWeek = item.StartWeek,
        EndWeek = item.EndWeek,
        WeekParity = item.WeekParity,
        StartTime = item.StartTime,
        EndTime = item.EndTime,
        Term = item.Term,
    };

    private static string DisplayText(ScheduleItem item)
    {
        var parity = item.WeekParity switch
        {
            WeekParity.Odd => " 单周",
            WeekParity.Even => " 双周",
            _ => string.Empty,
        };
        return $"{item.Name}｜{item.Weekday switch
        {
            1 => "周一", 2 => "周二", 3 => "周三", 4 => "周四",
            5 => "周五", 6 => "周六", 7 => "周日", _ => "?",
        }} 第 {item.StartPeriod}-{item.EndPeriod} 节 · {item.StartWeek}-{item.EndWeek} 周{parity}";
    }
}
