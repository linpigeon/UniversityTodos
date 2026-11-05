using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using UniversityTodos.Models;
using UniversityTodos.Services;
using UniversityTodos.Views;

namespace UniversityTodos.Desktop.SelfChecks;

internal static class TimetableSelfCheck
{
    public static void Run(string? renderPath, bool useOtherWeeks)
    {
        var trace = Path.Combine(Path.GetTempPath(), "hr-ttcheck.log");
        void Trace(string step)
        {
            try { File.AppendAllText(trace, $"[{DateTime.Now:HH:mm:ss.fff}] {step}{Environment.NewLine}"); }
            catch (IOException) { }
        }

        try { File.WriteAllText(trace, $"=== ttcheck {DateTimeOffset.Now:O} ==={Environment.NewLine}"); }
        catch (IOException) { }

        var verbose = Environment.GetEnvironmentVariable("TTCHECK_VERBOSE") == "1";
        App.DeferInitialLoad = true;
        Program.BuildAvaloniaApp().SetupWithoutStarting();

        var app = (App)Application.Current!;
        var vm = app.ViewModel ?? throw new InvalidOperationException("App did not create a view model");
        var file = Task.Run(() => new ScheduleStore().LoadAsync()).GetAwaiter().GetResult();
        var useSample = Environment.GetEnvironmentVariable("TTCHECK_SAMPLE") == "1";
        var selectBlock = Environment.GetEnvironmentVariable("TTCHECK_SELECT") == "1";
        var source = "样本";
        if (file.Items.Count == 0 || useSample) file = SampleSchedule();
        else source = "已导入的课表";

        vm.Timetable.TodayDate = DateTime.Now.ToString("yyyy-MM-dd");
        vm.LoadScheduleForCheck(file);
        vm.Timetable.ShowOtherWeeks = useOtherWeeks;
        vm.OpenTimetable();
        if (selectBlock && vm.Timetable.Blocks.Count > 0)
        {
            vm.Timetable.SelectedBlock = vm.Timetable.Blocks[0];
            Trace($"已选中课块: {vm.Timetable.SelectedBlock?.Name} HasSelection={vm.Timetable.HasSelection}");
        }

        Trace(
            $"课表自检: 来源={source} 条目={file.Items.Count} 节次行={vm.Timetable.RowCount} "
            + $"课块={vm.Timetable.Blocks.Count} 本周={vm.Timetable.BlockCount} "
            + $"周次={vm.Timetable.WeekNumber} 本周标记={vm.Timetable.IsCurrentWeekDisplayed} "
            + $"网格高={vm.Timetable.GridHeight:0} 天区宽={vm.Timetable.DaysWidth:0} "
            + $"下一节={vm.Timetable.NextClassText}");

        if (verbose)
        {
            foreach (var blk in vm.Timetable.Blocks)
                Trace($"  block {blk.Name} day={blk.Model.Weekday} x={blk.X:0} y={blk.Y:0} w={blk.Width:0} h={blk.Height:0} 本周={blk.IsCurrentWeek}");
            foreach (var p in vm.Timetable.Periods)
                Trace($"  period {p.Period} top={p.Top:0} h={p.Height:0} time={p.TimeText}");
        }

        var window = new MainWindow { DataContext = vm };
        var sizeText = Environment.GetEnvironmentVariable("TTCHECK_SIZE");
        if (!string.IsNullOrWhiteSpace(sizeText) && sizeText.Split('x') is [var ws, var hs]
            && double.TryParse(ws, out var w) && double.TryParse(hs, out var h))
        {
            window.Width = w;
            window.Height = h;
            Trace($"窗口尺寸设为 {w}x{h}");
        }

        var code = SelfCheck.Run(window, renderPath);

        if (Environment.GetEnvironmentVariable("TTCHECK_ALIGN") == "1")
        {
            var view = window.GetVisualDescendants().OfType<TimetableView>().FirstOrDefault();
            var periodRows = view?.GetVisualDescendants().OfType<Border>()
                .Where(x => x.Classes.Contains("tt-period")).ToList();
            var blocks = view?.GetVisualDescendants().OfType<Border>()
                .Where(x => x.Classes.Contains("tt-block")).ToList();

            if (view is not null && periodRows is not null && blocks is not null)
            {
                void BoundsOf(string label, Visual? v)
                {
                    if (v is null) { Trace($"  {label}: 找不到"); return; }
                    var tl = v.TranslatePoint(new Avalonia.Point(0, 0), view);
                    Trace($"  {label}: X={tl?.X:0.##} Y={tl?.Y:0.##} W={v.Bounds.Width:0.##} H={v.Bounds.Height:0.##}");
                }

                var dayHeaderItems = view.GetVisualDescendants().OfType<ItemsControl>()
                    .FirstOrDefault(c => c.Name == "DayHeaderItems");
                var emptyText = view.GetVisualDescendants().OfType<TextBlock>()
                    .FirstOrDefault(t => t.Classes.Contains("faint"));
                BoundsOf("节次列(PeriodScroll)", view.GetVisualDescendants().OfType<ScrollViewer>()
                    .FirstOrDefault(s => s.Name == "PeriodScroll"));
                BoundsOf("列头(DayHeaderItems)", dayHeaderItems);
                BoundsOf("课块区(GridScroll)", view.GetVisualDescendants().OfType<ScrollViewer>()
                    .FirstOrDefault(s => s.Name == "GridScroll"));
                BoundsOf("空状态文字", emptyText);

                if (dayHeaderItems is not null)
                {
                    var texts = dayHeaderItems.GetVisualDescendants().OfType<TextBlock>().ToList();
                    Trace($"  列头文字数={texts.Count}（列头自身 H={dayHeaderItems.Bounds.Height:0.##}）");
                    foreach (var t in texts.Take(4))
                    {
                        var tl = t.TranslatePoint(new Avalonia.Point(0, 0), view);
                        Trace($"    「{t.Text}」X={tl?.X:0.##} Y={tl?.Y:0.##} W={t.Bounds.Width:0.##} H={t.Bounds.Height:0.##} 裁剪={t.ClipToBounds}");
                    }
                    if (texts.Count > 0)
                    {
                        var maxBottom = texts.Max(t => (t.TranslatePoint(new Avalonia.Point(0, 0), view)?.Y ?? 0) + t.Bounds.Height);
                        var headerBottom = (dayHeaderItems.TranslatePoint(new Avalonia.Point(0, 0), view)?.Y ?? 0) + dayHeaderItems.Bounds.Height;
                        Trace($"  列头文字最低点={maxBottom:0.##} 列头底={headerBottom:0.##} "
                              + (maxBottom > headerBottom + 0.5 ? "→ 溢出（会压到下面的内容）" : "→ 未溢出"));
                    }
                }

                var leftmostPeriod = periodRows.FirstOrDefault();
                var firstHeader = dayHeaderItems?.GetVisualDescendants().OfType<Border>()
                    .FirstOrDefault(x => x.Classes.Contains("tt-dayhead"));
                if (leftmostPeriod is not null && firstHeader is not null)
                {
                    Trace($"  第 1 节刻度 X={leftmostPeriod.TranslatePoint(new Avalonia.Point(0, 0), view)?.X:0.##}");
                    Trace($"  第 1 个列头 X={firstHeader.TranslatePoint(new Avalonia.Point(0, 0), view)?.X:0.##}");
                }

                Trace($"对齐自检：节次行数={periodRows.Count} 课块数={blocks.Count}");
                foreach (var p in periodRows.Take(4))
                    Trace($"  节次 屏幕Y={p.TranslatePoint(new Avalonia.Point(0, 0), view)?.Y:0} 高={p.Bounds.Height:0.##}");
                foreach (var b in blocks.OrderBy(x => x.TranslatePoint(new Avalonia.Point(0, 0), view)?.Y ?? 0).Take(4))
                    Trace($"  课块 屏幕Y={b.TranslatePoint(new Avalonia.Point(0, 0), view)?.Y:0} 高={b.Bounds.Height:0.##}");

                var firstBlock = blocks.OrderBy(x => x.TranslatePoint(new Avalonia.Point(0, 0), view)?.Y ?? 0).FirstOrDefault();
                var firstPeriod = periodRows.OrderBy(x => x.TranslatePoint(new Avalonia.Point(0, 0), view)?.Y ?? 0).FirstOrDefault();
                if (firstBlock is not null && firstPeriod is not null)
                {
                    var blockY = firstBlock.TranslatePoint(new Avalonia.Point(0, 0), view)?.Y ?? 0;
                    var periodY = firstPeriod.TranslatePoint(new Avalonia.Point(0, 0), view)?.Y ?? 0;
                    Trace($"  第 1 节课块 Y={blockY:0}，第 1 节刻度 Y={periodY:0}，差={blockY - periodY:0.#}");
                    Trace(Math.Abs(blockY - periodY) < 1
                        ? "  对齐自检：PASS —— 课块与节次刻度同一条基准线"
                        : "  对齐自检：FAIL —— 课块与节次刻度错位");
                }
            }
        }

        var hScroll = double.TryParse(Environment.GetEnvironmentVariable("TTCHECK_HSCROLL"), out var hx) ? hx : 0;
        var vScroll = double.TryParse(Environment.GetEnvironmentVariable("TTCHECK_VSCROLL"), out var vy) ? vy : 0;
        if ((hScroll > 0 || vScroll > 0) && renderPath is not null)
        {
            var view = window.GetVisualDescendants().OfType<TimetableView>().FirstOrDefault();
            if (view is null)
            {
                Trace("滚动自检：找不到 TimetableView");
            }
            else
            {
                ScrollViewer? Find(string name) => view.GetVisualDescendants().OfType<ScrollViewer>()
                    .FirstOrDefault(s => s.Name == name);
                var gridScroll = Find("GridScroll");
                if (gridScroll is not null)
                {
                    gridScroll.Offset = gridScroll.Offset.WithX(hScroll).WithY(vScroll);
                    Dispatcher.UIThread.RunJobs();
                    Dispatcher.UIThread.RunJobs();
                    Trace($"滚动到 ({gridScroll.Offset.X:0},{gridScroll.Offset.Y:0}) 范围 {gridScroll.Extent.Width:0}x{gridScroll.Extent.Height:0}");

                    var headerX = Find("DayHeaderScroll")?.Offset.X ?? double.NaN;
                    var periodY = Find("PeriodScroll")?.Offset.Y ?? double.NaN;
                    Trace($"  列头横向偏移={headerX:0}（应={gridScroll.Offset.X:0}）");
                    Trace($"  节次列纵向偏移={periodY:0}（应={gridScroll.Offset.Y:0}）");
                    Trace(Math.Abs(headerX - gridScroll.Offset.X) < 0.5 && Math.Abs(periodY - gridScroll.Offset.Y) < 0.5
                        ? "  对齐自检：PASS —— 列头随横向滚动、节次列随纵向滚动"
                        : "  对齐自检：FAIL —— 偏移不一致，课块会落在错误的星期/节次下");

                    Dispatcher.UIThread.RunJobs();
                    var size = new PixelSize((int)window.Width, (int)window.Height);
                    using var bitmap = new RenderTargetBitmap(size, new Vector(96, 96));
                    bitmap.Render(window);
                    bitmap.Save(renderPath);
                    Trace("  已按滚动后的状态重新截图");
                }
            }
        }

        var navTimes = int.TryParse(Environment.GetEnvironmentVariable("TTCHECK_NAV"), out var n) ? n : 0;
        if (navTimes > 0)
        {
            var before = vm.Timetable.WeekNumber;
            for (var i = 0; i < navTimes; i++)
            {
                vm.ShiftTimetableWeek(1);
                Trace($"  下一周[{i + 1}] -> WeekNumber={vm.Timetable.WeekNumber} WeekLabel={vm.Timetable.WeekLabel} 课块={vm.Timetable.Blocks.Count}");
            }
            for (var i = 0; i < navTimes; i++)
            {
                vm.ShiftTimetableWeek(-1);
                Trace($"  上一周[{i + 1}] -> WeekNumber={vm.Timetable.WeekNumber}");
            }
            Trace($"导航自检: 起始={before} 前进{navTimes}次再后退{navTimes}次 -> 结束={vm.Timetable.WeekNumber}");
            vm.GotoCurrentTimetableWeek();
            Trace($"  回本周 -> WeekNumber={vm.Timetable.WeekNumber}");
        }

        if (Environment.GetEnvironmentVariable("TTCHECK_EMPTY") == "1" && renderPath is not null)
        {
            vm.Timetable.WeekNumber = Timetable.MaxWeek;
            vm.Timetable.SelectedBlock = null;
            Dispatcher.UIThread.RunJobs();
            Trace($"空状态自检：周次={vm.Timetable.WeekNumber} 本周课块={vm.Timetable.BlockCount} 空状态可见={!vm.Timetable.HasBlocks}");
            var size = new PixelSize((int)window.Width, (int)window.Height);
            using var bitmap = new RenderTargetBitmap(size, new Vector(96, 96));
            bitmap.Render(window);
            bitmap.Save(renderPath);
            Trace("  已按空状态重新截图");
        }

        if (verbose && renderPath is not null)
        {
            var view = window.GetVisualDescendants().OfType<TimetableView>().FirstOrDefault();
            if (view is not null)
            {
                Trace($"渲染后 HasSelection={vm.Timetable.HasSelection} Selected={vm.Timetable.SelectedBlock?.Name}");
                foreach (var b in view.GetVisualDescendants().OfType<Border>()
                             .Where(x => x.Classes.Contains("tt-block")).Take(6))
                {
                    var pos = b.TranslatePoint(new Avalonia.Point(0, 0), view);
                    Trace($"  实测 tt-block canvas=({Canvas.GetLeft(b)},{Canvas.GetTop(b)}) 屏幕={pos}");
                }
                var detail = view.GetVisualDescendants().OfType<Border>()
                    .FirstOrDefault(x => x.Classes.Contains("tt-detail"));
                Trace(detail is null
                    ? "  详情面板：未实例化"
                    : $"  详情面板：IsVisible={detail.IsVisible} 可见={detail.IsEffectivelyVisible} "
                      + $"位置={detail.TranslatePoint(new Avalonia.Point(0, 0), view)} 尺寸={detail.Bounds.Size}");
            }
        }

        Trace($"selfcheck 完成 code={code}");
        Environment.Exit(code);
    }

    private static ScheduleFile SampleSchedule()
    {
        var periods = Timetable.CreateDefaultPeriods();
        var items = new List<ScheduleItem>
        {
            Make("高等数学A", 1, 1, 2, 1, 16, WeekParity.Every, "明德楼B107", "张三"),
            Make("大学英语Ⅲ", 1, 3, 4, 1, 16, WeekParity.Every, "外语楼305", "李四"),
            Make("体育（篮球）", 1, 7, 8, 1, 16, WeekParity.Every, "田径场", "王五"),
            Make("程序设计基础", 2, 1, 2, 1, 16, WeekParity.Every, "机房A", "赵六"),
            Make("线性代数", 2, 3, 4, 1, 8, WeekParity.Every, "博文楼203", "钱七"),
            Make("大学物理", 2, 5, 6, 1, 15, WeekParity.Odd, "理科楼401", "孙八"),
            Make("数据结构", 3, 1, 2, 1, 16, WeekParity.Every, "信息楼501", "周九"),
            Make("数据结构实验", 3, 1, 2, 2, 16, WeekParity.Every, "机房B", "周九"),
            Make("概率论与数理统计", 3, 5, 6, 1, 16, WeekParity.Every, "博文楼E203", "吴十"),
            Make("模拟电子技术", 4, 3, 4, 1, 16, WeekParity.Every, "六教M305", "宋刚"),
            Make("信号与系统", 4, 7, 8, 1, 16, WeekParity.Every, "六教M211", "邱杨"),
            Make("中国近现代史纲要", 5, 1, 2, 1, 14, WeekParity.Every, "明德楼阶梯B107", "郑一"),
            Make("人工智能与法律", 5, 5, 6, 9, 16, WeekParity.Every, "博文楼A201", "陈一"),
            Make("创新创业实践", 6, 3, 4, 2, 16, WeekParity.Even, "创业园", "林二"),
        };
        foreach (var item in items)
        {
            var time = Timetable.ResolveSessionTime(item, periods);
            item.StartTime = time.StartTime;
            item.EndTime = time.EndTime;
        }
        return new ScheduleFile { Items = items, Periods = periods };
    }

    private static ScheduleItem Make(
        string name, int weekday, int startPeriod, int endPeriod, int startWeek, int endWeek,
        WeekParity parity, string location, string teacher) => new()
    {
        Id = $"{name}|{weekday}|{startPeriod}|{endPeriod}|{startWeek}|{endWeek}|{parity}",
        Name = name,
        Weekday = weekday,
        StartPeriod = startPeriod,
        EndPeriod = endPeriod,
        StartWeek = startWeek,
        EndWeek = endWeek,
        WeekParity = parity,
        Location = location,
        Teacher = teacher,
        Term = AppSettings.Current.Term,
    };
}
