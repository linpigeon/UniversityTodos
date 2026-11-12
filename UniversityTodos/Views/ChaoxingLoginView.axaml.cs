using System;
using System.ComponentModel;
using Avalonia.Controls;
using UniversityTodos.Services;
using UniversityTodos.ViewModels;

namespace UniversityTodos.Views;

/// <summary>学习通登录页的交互：导航到超星统一登录页，登录成功自动捕获会话。</summary>
public partial class ChaoxingLoginView : UserControl
{
    private bool _webViewReady;
    private bool _navigated;
    private bool _credentialSubmitted;
    private System.Threading.CancellationTokenSource? _silentCts;
    private readonly PageReadyGate _pageGate = new();

    public ChaoxingLoginView()
    {
        InitializeComponent();

        ChaoxingWebView.AdapterCreated += (_, _) =>
        {
            _webViewReady = true;
            _pageGate.MarkAdapterCreated();
            WebViewPlaceholder.IsVisible = false;
            ChaoxingWebView.IsVisible = true;
            NavigateIfNeeded();
        };
        ChaoxingWebView.AdapterDestroyed += (_, _) =>
        {
            _webViewReady = false;
            _pageGate.Reset();
        };
        ChaoxingWebView.NavigationStarted += (_, _) => _pageGate.MarkNavigating();
        ChaoxingWebView.NavigationCompleted += async (_, _) =>
        {
            _pageGate.MarkReady();
            await TryAutoLoginAsync();
            await TryCaptureAsync();
        };
    }

    private async System.Threading.Tasks.Task TryAutoLoginAsync()
    {
        if (!_pageGate.HasCompletedNavigation) return;
        if (_credentialSubmitted || Vm?.ChaoxingCredential.HasCredential != true) return;
        _credentialSubmitted = true;
        if (Vm is { } vm)
        {
            var result = await JwCaptureService.TryEvalAsync(
                ChaoxingWebView,
                JwCaptureService.BuildCredentialLoginScript(
                    vm.ChaoxingCredential.AccountInput, vm.ChaoxingCredential.PasswordInput));
            if (vm.LoginBackground && result?.Trim().Trim('"') != "submitted")
                vm.ShowManualLogin("chaoxing", "无法后台提交账密，请在当前页面手动登录…");
        }
    }

    private void StartSilentFallback()
    {
        StopSilentFallback();
        if (Vm?.LoginBackground != true) return;
        _silentCts = new System.Threading.CancellationTokenSource();
        _ = WatchSilentLoginAsync(_silentCts.Token);
    }

    private async System.Threading.Tasks.Task WatchSilentLoginAsync(System.Threading.CancellationToken ct)
    {
        try { await System.Threading.Tasks.Task.Delay(TimeSpan.FromSeconds(45), ct); }
        catch (System.OperationCanceledException) { return; }
        if (!ct.IsCancellationRequested && Vm is { LoginBackground: true, ChaoxingLoginOpen: true } vm)
            vm.ShowManualLogin("chaoxing", "静默登录未完成，请处理验证或在当前页面手动登录…");
    }

    private void StopSilentFallback()
    {
        var cts = _silentCts;
        _silentCts = null;
        if (cts is null) return;
        try { cts.Cancel(); } catch (System.ObjectDisposedException) { }
        cts.Dispose();
    }

    private MainViewModel? Vm => DataContext as MainViewModel;

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        // 让 ViewModel 在退出学习通时能清掉这个 WebView 内部的 Cookie。
        // 注意不要复用雨课堂的 MainViewModel.WebViewAccessor——那是另一个 WebView。
        MainViewModel.ChaoxingWebViewAccessor = () => ChaoxingWebView;

        if (Vm is { } vm)
        {
            vm.PropertyChanged += OnVmPropertyChanged;
        }

        NavigateIfNeeded();
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        StopSilentFallback();
        if (Vm is { } vm) vm.PropertyChanged -= OnVmPropertyChanged;
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.LoginBackground))
        {
            if (Vm?.LoginBackground == false)
            {
                ChaoxingWebView.InvalidateMeasure();
                ChaoxingWebView.InvalidateArrange();
            }
            return;
        }
        if (e.PropertyName != nameof(MainViewModel.LoginPageOpen)) return;
        if (Vm is not { ChaoxingLoginOpen: true })
        {
            StopSilentFallback();
            return;
        }

        _navigated = false;
        _credentialSubmitted = false;
        WebViewPlaceholder.IsVisible = !_webViewReady;
        ChaoxingWebView.IsVisible = _webViewReady;
        NavigateIfNeeded();
        StartSilentFallback();

        // 让托管布局重跑一遍，把原生控件按最终边界重新定位（与 LoginView.ShowWebView 同理）。
        ChaoxingWebView.InvalidateMeasure();
        ChaoxingWebView.InvalidateArrange();
    }

    private void NavigateIfNeeded()
    {
        if (!_webViewReady || _navigated) return;
        _navigated = true;
        try { ChaoxingWebView.Navigate(new Uri(ChaoxingLoginService.LoginUrl)); }
        catch (Exception) { /* 导航失败停留在占位 */ }
    }

    /// <summary>每次导航完成后尝试捕获：登录成功页面跳转后 cookie 里会出现 _uid。</summary>
    private async System.Threading.Tasks.Task TryCaptureAsync()
    {
        if (Vm is not { } vm) return;
        try
        {
            var session = await ChaoxingLoginService.CaptureAsync(ChaoxingWebView);
            if (session is not null)
                await vm.AdoptChaoxingSessionAsync(session);
        }
        catch (Exception)
        {
            // 捕获失败留在登录页，用户可继续操作。
        }
    }
}
