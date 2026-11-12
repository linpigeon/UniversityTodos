using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using UniversityTodos.ViewModels;

namespace UniversityTodos.Views;

/// <summary>
/// Android 外壳（抽屉式移动布局）的交互。
/// <para>
/// 位于共享项目（而非 Android 头）：界面遮挡审计 <c>--androidcheck</c> 在桌面头
/// 离屏渲染手机尺寸，必须编译**同一个**外壳才能验到真实的层叠与返回栏。
/// 唯一平台专属的那句（铺答题页 cookie）已抽成
/// <see cref="Services.IPlatformServices.PrepareExamWebSession"/>。
/// </para>
/// </summary>
public partial class MobileShellView : UserControl
{
    public MobileShellView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => HookVm(DataContext as MainViewModel);
        MobileTodoList.AddTodoRequested += (_, _) => OpenManualEditor();
        MobileTodoList.AddHandler(PointerPressedEvent, OnTodoPointerPressed, RoutingStrategies.Tunnel);
        MobileTodoList.AddHandler(PointerReleasedEvent, OnTodoPointerReleased, RoutingStrategies.Tunnel);
    }

    private NativeWebView? _examWebView;
    private TimetableView? _timetableView;
    private ElectricityView? _electricityView;
    private bool _examWebReady;
    private bool _trackingDrawerGesture;
    private Avalonia.Point _drawerGestureStart;

    /// <summary>当前挂上的 PropertyChanged 处理器；退订必须用同一个委托实例。</summary>
    private System.ComponentModel.PropertyChangedEventHandler? _vmHandler;

    private MainViewModel? Vm => DataContext as MainViewModel;

    /// <summary>
    /// 订阅主界面的属性变化。
    /// <para>
    /// 必须能在视图销毁时退订，且不能重复订阅：<c>MainViewModel</c> 是应用级单例，
    /// 而 <c>IActivityApplicationLifetime.MainViewFactory</c> 每次 Activity 重建/旋转
    /// 都会 new 一个新的 MobileShellView。原先用匿名 lambda 订阅且从不退订，
    /// 于是每个已销毁的外壳（连同整棵可视树与 NativeWebView）都被永久持有，
    /// 处理器还会累积——一次 ExamWebUrl 变化会在所有僵尸 WebView 上跑 NavigateExamWeb。
    /// </para>
    /// </summary>
    private void HookVm(MainViewModel? vm)
    {
        UnhookVm();
        if (vm is null) return;

        _vmHandler = (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.ExamWebUrl))
            {
                NavigateExamWeb();
            }
            else if (e.PropertyName == nameof(MainViewModel.LoginPageOpen))
            {
                UpdateLoginPage();
            }
            else if (e.PropertyName == nameof(MainViewModel.DrawerOpen))
            {
                if (vm.DrawerOpen) SwipeTransition.Play(DrawerPanel, -300);
            }
            else if (e.PropertyName == nameof(MainViewModel.TimetableOpen))
            {
                UpdateTimetablePage();
                SyncMobileNav();
            }
            else if (e.PropertyName == nameof(MainViewModel.ElectricityOpen))
            {
                UpdateElectricityPage();
                SyncMobileNav();
            }
            else if (e.PropertyName is nameof(MainViewModel.SelectedNav)
                     or nameof(MainViewModel.SettingsOpen))
            {
                // 底部导航的选中态只跟随这几个属性，没有额外的导航状态。
                SyncMobileNav();
            }
        };
        vm.PropertyChanged += _vmHandler;
        SyncMobileNav();
        UpdateTimetablePage();
        UpdateElectricityPage();
        UpdateLoginPage();
        if (vm.ExamWebUrl is not null) NavigateExamWeb();
    }

    private void UnhookVm()
    {
        if (_vmHandler is null) return;
        if (DataContext is MainViewModel vm) vm.PropertyChanged -= _vmHandler;
        _vmHandler = null;
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        UnhookVm();
        UnhookBackRequested();
        CloseExamWebView();
        TimetableContentHost.Content = null;
        _timetableView = null;
        ElectricityContentHost.Content = null;
        _electricityView = null;
        LoginContentHost.Content = null;
    }

    private Avalonia.Controls.TopLevel? _hookedTopLevel;

    /// <summary>
    /// 接管系统返回键（<see cref="Avalonia.Controls.TopLevel.BackRequested"/>）：
    /// 由内到外关掉一层全屏覆盖，都关完了才让系统处理（退出应用）。
    /// <para>
    /// <b>为什么必须接管</b>：课表页是整页覆盖，会把抽屉（进设置的唯一入口）一起盖住。
    /// 不接管的话，用户在课表页按返回会直接退出应用——既回不到列表，也进不了设置。
    /// 桌面端有表头上的返回按钮，手机端返回键才是主要返回方式。
    /// </para>
    /// <para>
    /// 必须在 <c>AttachedToVisualTree</c> 里挂：构造函数执行时还没挂上可视树，
    /// <c>TopLevel.GetTopLevel(this)</c> 拿不到宿主窗口。
    /// </para>
    /// </summary>
    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        // DataContext 可能在构造之后才由父级传下来；这里补一次同步，订阅仍只走 HookVm。
        SyncMobileNav();
        UnhookBackRequested();
        if (Avalonia.Controls.TopLevel.GetTopLevel(this) is { } top)
        {
            _hookedTopLevel = top;
            top.BackRequested += OnBackRequested;
        }
    }

    private void UnhookBackRequested()
    {
        if (_hookedTopLevel is null) return;
        _hookedTopLevel.BackRequested -= OnBackRequested;
        _hookedTopLevel = null;
    }

    private void OnBackRequested(object? sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm) return;

        // 顺序与叠放层次一致（后加的在上）：编辑器 → 登录页 → 答题页 → 教务导入 → 设置 → 详情 → 课表/电费 → 抽屉。
        if (ManualEditorOverlay.IsVisible) { CloseManualEditor(); e.Handled = true; return; }
        if (vm.LoginPageOpen is not null) { vm.CloseLoginPage(); e.Handled = true; return; }
        if (vm.ExamWebUrl is not null) { vm.CloseExamWeb(); e.Handled = true; return; }
        if (vm.JwImportOpen) { vm.CloseJwImport(); e.Handled = true; return; }
        if (vm.ExamInfoOpen) { vm.CloseExamInfo(); e.Handled = true; return; }
        if (vm.SettingsOpen) { vm.SettingsOpen = false; e.Handled = true; return; }
        if (vm.SelectedItem is not null) { vm.SelectedItem = null; e.Handled = true; return; }
        if (vm.TimetableOpen) { vm.CloseTimetable(); e.Handled = true; return; }
        if (vm.ElectricityOpen) { vm.CloseElectricityCommand.Execute(null); e.Handled = true; return; }
        if (vm.DrawerOpen) { vm.DrawerOpen = false; e.Handled = true; }
    }

    private void UpdateTimetablePage()
    {
        if (Vm?.TimetableOpen != true)
        {
            TimetableContentHost.Content = null;
            return;
        }

        _timetableView ??= new TimetableView { CompactMode = true };
        if (!ReferenceEquals(TimetableContentHost.Content, _timetableView))
            TimetableContentHost.Content = _timetableView;
    }

    private void UpdateElectricityPage()
    {
        if (Vm?.ElectricityOpen != true)
        {
            ElectricityContentHost.Content = null;
            return;
        }

        _electricityView ??= new ElectricityView();
        if (!ReferenceEquals(ElectricityContentHost.Content, _electricityView))
            ElectricityContentHost.Content = _electricityView;
    }

    private void UpdateLoginPage()
    {
        if (Vm is not { } vm) return;

        LoginContentHost.Content = vm.LoginPageOpen switch
        {
            "ykt" => new LoginView(),
            "chaoxing" => new ChaoxingLoginView(),
            "pigai" => new PigaiLoginView(),
            "water" => new WaterLoginView(),
            _ => null,
        };
    }

    private void EnsureExamWebView()
    {
        if (_examWebView is not null) return;

        var webView = new NativeWebView();
        webView.AdapterCreated += (_, _) =>
        {
            _examWebReady = true;
            NavigateExamWeb();
        };
        webView.AdapterDestroyed += (_, _) => _examWebReady = false;
        webView.NavigationCompleted += async (_, _) =>
        {
            // 页面是桌面布局，手机屏放不下：固定布局视口后整体缩放。
            try
            {
                await webView.InvokeScript(
                    "(()=>{var m=document.querySelector('meta[name=\"viewport\"]');" +
                    "if(!m){m=document.createElement('meta');m.name='viewport';(document.head||document.documentElement).appendChild(m);}" +
                    "m.setAttribute('content','width=1000');" +
                    "var h=document.getElementById('ie-hint');if(h)h.style.display='none';" +
                    "return 'ok';})()");
            }
            catch (Exception) { /* 视觉退化不影响使用 */ }
        };

        _examWebView = webView;
        ExamWebHost.Children.Add(webView);
    }

    private void CloseExamWebView()
    {
        _examWebReady = false;
        if (_examWebView is not null) ExamWebHost.Children.Remove(_examWebView);
        _examWebView = null;
    }

    /// <summary>打开应用内答题页：先把会话 cookie 铺到 exam 子域，再导航。</summary>
    private void NavigateExamWeb()
    {
        if (Vm?.ExamWebUrl is not { } url)
        {
            CloseExamWebView();
            return;
        }

        EnsureExamWebView();
        // WebView 适配器异步创建，未就绪时等 AdapterCreated 再导航。
        if (!_examWebReady || _examWebView is not { } webView) return;

        MainViewModel.PlatformServices?.PrepareExamWebSession();
        webView.Navigate(new Uri(url));
    }

    /// <summary>
    /// 底部导航点击：待办统一映射到 NavItems 的 key=all，
    /// 课表/电费/设置复用原逻辑；不复制筛选逻辑，也不新增业务导航状态。
    /// </summary>
    private void OnMobileNavClick(object? sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm) return;
        if ((sender as Button)?.Tag is not string key || string.IsNullOrEmpty(key)) return;

        if (key == "settings")
        {
            // 课表打开时用户仍能从底栏进设置。先按正常语义关闭课表
            // （恢复打开课表前的列表），否则返回后 SelectedNav 会停在 timetable，
            // 而课表页已经关掉，底栏与列表内容对不上。
            if (vm.TimetableOpen) vm.CloseTimetable();
            if (vm.ExamInfoOpen) vm.CloseExamInfo();
            if (vm.ElectricityOpen) vm.CloseElectricityCommand.Execute(null);
            vm.OpenSettings();
        }
        else
        {
            var navKey = key == "todo" ? "all" : key;
            var nav = vm.NavItems.FirstOrDefault(n => n.Key == navKey);
            if (nav is null) return;
            vm.SelectedNav = nav;
            vm.OnNavChanged();
        }

        SyncMobileNav();
    }

    /// <summary>
    /// 把四个底部按钮的 selected class 与当前状态对齐。
    /// <para>
    /// 设置页打开时高亮“设置”；课表/电费页打开时高亮各自入口；其余任何列表筛选
    /// （myday / homework / planned / all 等）都高亮“待办”。只改 class，
    /// 不维护第二份导航状态。
    /// </para>
    /// </summary>
    private void SyncMobileNav()
    {
        var vm = Vm;
        var key = vm?.SettingsOpen == true
            ? "settings"
            : vm?.TimetableOpen == true
                ? "timetable"
                : vm?.ElectricityOpen == true
                    ? "electricity"
                    : "todo";

        TodoNavButton.Classes.Set("selected", key == "todo");
        TimetableNavButton.Classes.Set("selected", key == "timetable");
        ElectricityNavButton.Classes.Set("selected", key == "electricity");
        SettingsNavButton.Classes.Set("selected", key == "settings");
    }

    private void OpenManualEditor()
    {
        if (Vm is null) return;

        CloseManualEditor();
        var form = new TodoEditorForm(null, mobile: true);
        ManualEditorOverlay.Child = form.View;
        ManualEditorOverlay.IsVisible = true;
        form.Completed += async (_, _) =>
        {
            var draft = form.Result;
            CloseManualEditor();
            if (draft is null || Vm is not { } vm) return;

            try
            {
                await vm.AddManualTodoAsync(draft);
            }
            catch (Exception ex)
            {
                SidebarView.ReportHandlerFailure(vm, "新建待办", ex);
            }
        };
    }

    private void CloseManualEditor()
    {
        ManualEditorOverlay.IsVisible = false;
        ManualEditorOverlay.Child = null;
    }

    private void OnTodoPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.Pointer.IsPrimary || Vm is not { } vm || vm.DrawerOpen
            || vm.SettingsOpen || vm.TimetableOpen || vm.ElectricityOpen || vm.JwImportOpen || vm.SelectedItem is not null)
            return;

        _trackingDrawerGesture = true;
        _drawerGestureStart = e.GetCurrentPoint(MobileTodoList).Position;
    }

    private void OnTodoPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_trackingDrawerGesture || !e.Pointer.IsPrimary) return;
        _trackingDrawerGesture = false;
        if (Vm is not { } vm || vm.DrawerOpen) return;

        var end = e.GetCurrentPoint(MobileTodoList).Position;
        var dx = end.X - _drawerGestureStart.X;
        var dy = Math.Abs(end.Y - _drawerGestureStart.Y);
        if (dy > 40) return;

        // 主列表向左滑直接呼出；同时保留主流系统的左边缘右滑手势。
        if (dx <= -60 || (dx >= 60 && _drawerGestureStart.X <= 24))
        {
            vm.DrawerOpen = true;
            e.Handled = true;
        }
    }

    /// <summary>点抽屉遮罩：收起抽屉。</summary>
    private void OnDrawerScrimPressed(object? sender, PointerPressedEventArgs e)
    {
        if (Vm is { } vm) vm.DrawerOpen = false;
    }

    /// <summary>详情二级页的返回键。</summary>
    private void OnDetailBackClick(object? sender, RoutedEventArgs e)
    {
        if (Vm is { } vm) vm.SelectedItem = null;
    }

    /// <summary>设置页的返回键。</summary>
    private void OnSettingsBackClick(object? sender, RoutedEventArgs e)
    {
        if (Vm is { } vm) vm.SettingsOpen = false;
    }

    /// <summary>应用内答题页的返回键。</summary>
    private void OnExamWebBackClick(object? sender, RoutedEventArgs e)
    {
        Vm?.CloseExamWeb();
    }

    /// <summary>登录覆盖层的返回键。</summary>
    private void OnLoginBackClick(object? sender, RoutedEventArgs e)
    {
        Vm?.CloseLoginPage();
    }
}
