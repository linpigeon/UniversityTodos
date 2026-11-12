using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using UniversityTodos.Services;
using UniversityTodos.ViewModels;

namespace UniversityTodos.Views;

public partial class ExamInfoView : UserControl
{
    private readonly PageReadyGate _backgroundGate = new();
    private NativeWebDialog? _backgroundDialog;
    private bool _adapterCreated;
    private bool _dialogShown;
    private bool _navigationStarted;
    private bool _fetchStarted;

    public ExamInfoView()
    {
        InitializeComponent();
    }

    private MainViewModel? Vm => DataContext as MainViewModel;

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Vm?.CloseExamInfo();

    private void OnExamTabClick(object? sender, RoutedEventArgs e)
    {
        if (Vm is not null) Vm.ExamSectionIndex = 0;
    }

    private void OnGradeTabClick(object? sender, RoutedEventArgs e)
    {
        if (Vm is not null) Vm.ExamSectionIndex = 1;
    }

    private void OnImportClick(object? sender, RoutedEventArgs e)
    {
        if (OperatingSystem.IsWindows()) _ = StartBackgroundFetchAsync();
        else Vm?.OpenExamInfoImport();
    }

    public async Task StartBackgroundFetchAsync()
    {
        var vm = Vm;
        if (vm is null || vm.ExamInfo.IsLoading || _backgroundDialog is not null) return;
        if (!vm.HasJwProfile)
        {
            vm.OpenExamInfoImport();
            return;
        }

        NativeWebDialog dialog;
        try
        {
            dialog = new NativeWebDialog
            {
                Title = "教务系统 · 考试信息后台获取",
                CanUserResize = true,
                ShowFocused = false,
            };
            dialog.Resize(1180, 820);
        }
        catch (Exception ex)
        {
            vm.JwSessionStatus = $"教务系统：无法创建后台窗口：{ex.Message}";
            return;
        }

        vm.ExamInfoOpen = true;
        vm.ExamSectionIndex = 0;
        vm.JwSessionStatus = "教务系统：正在后台恢复登录状态…";
        _backgroundGate.Reset();
        _adapterCreated = false;
        _dialogShown = false;
        _navigationStarted = false;
        _fetchStarted = false;
        _backgroundDialog = dialog;
        MainViewModel.JwDialogAccessor = () => dialog;
        MainViewModel.JwPageGate = _backgroundGate;

        dialog.AdapterCreated += (_, _) =>
        {
            _adapterCreated = true;
            _backgroundGate.MarkAdapterCreated();
            _ = RestoreAndNavigateAsync(vm, dialog);
        };
        dialog.AdapterDestroyed += (_, _) => _backgroundGate.Reset();
        dialog.NavigationStarted += (_, _) => _backgroundGate.MarkNavigating();
        dialog.NavigationCompleted += (_, e) =>
        {
            _backgroundGate.MarkReady();
            if (!e.IsSuccess) return;
            if (ExamService.IsExamPage(e.Request))
            {
                if (!_fetchStarted)
                {
                    _fetchStarted = true;
                    _ = FetchExamInfoAsync(vm, dialog);
                }
            }
            else if (IsLoginPage(e.Request))
            {
                ShowManualLogin(vm, dialog);
            }
        };
        dialog.NewWindowRequested += (_, e) =>
        {
            e.Handled = true;
            if (e.Request is not null) dialog.Navigate(e.Request);
        };
        dialog.Closing += (_, _) =>
        {
            if (ReferenceEquals(_backgroundDialog, dialog))
            {
                vm.CloseJwImport();
                _backgroundDialog = null;
            }
            if (MainViewModel.JwDialogAccessor?.Invoke() == dialog) MainViewModel.JwDialogAccessor = null;
            if (ReferenceEquals(MainViewModel.JwPageGate, _backgroundGate)) MainViewModel.JwPageGate = null;
        };

        try
        {
            var owner = TopLevel.GetTopLevel(this) ?? throw new InvalidOperationException("考试信息页面尚未就绪");
            dialog.Show(owner);
            _dialogShown = true;
            dialog.Move(-10000, -10000);
            await RestoreAndNavigateAsync(vm, dialog).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            vm.JwSessionStatus = $"教务系统：后台获取启动失败：{ex.Message}";
            CloseBackgroundDialog();
        }
    }

    private async Task RestoreAndNavigateAsync(MainViewModel vm, NativeWebDialog dialog)
    {
        if (_navigationStarted || !_adapterCreated || !_dialogShown) return;
        _navigationStarted = true;
        try
        {
            await JwSessionService.RestoreAsync(dialog, vm.JwEffectiveLoginUrl).ConfigureAwait(true);
            if (ReferenceEquals(_backgroundDialog, dialog))
                dialog.Navigate(new Uri(ExamService.BuildExamPageUrl(vm.JwEffectiveLoginUrl)));
        }
        catch (Exception ex)
        {
            vm.JwSessionStatus = $"教务系统：会话恢复失败：{ex.Message}";
            CloseBackgroundDialog();
        }
    }

    private async Task FetchExamInfoAsync(MainViewModel vm, NativeWebDialog dialog)
    {
        try
        {
            if (await JwSessionService.SaveAsync(dialog, vm.JwEffectiveLoginUrl).ConfigureAwait(true))
                vm.JwSessionStatus = "教务系统：登录状态已保存，正在读取考试信息";
            await vm.FetchJwExamsNowAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            vm.JwSessionStatus = $"教务系统：获取失败：{ex.Message}";
        }
        finally
        {
            CloseBackgroundDialog();
        }
    }

    private void ShowManualLogin(MainViewModel vm, NativeWebDialog dialog)
    {
        if (TopLevel.GetTopLevel(this) is Window { Position: var position })
            dialog.Move(position.X + 40, position.Y + 40);
        else
            dialog.Move(100, 100);
        dialog.Focus();
        vm.JwSessionStatus = "教务系统：登录已失效，请在教务窗口手动登录";
    }

    private static bool IsLoginPage(Uri? uri) =>
        uri?.AbsolutePath.Contains("login", StringComparison.OrdinalIgnoreCase) == true;

    private void CloseBackgroundDialog()
    {
        var dialog = _backgroundDialog;
        _backgroundDialog = null;
        if (dialog is null) return;
        if (MainViewModel.JwDialogAccessor?.Invoke() == dialog) MainViewModel.JwDialogAccessor = null;
        if (ReferenceEquals(MainViewModel.JwPageGate, _backgroundGate)) MainViewModel.JwPageGate = null;
        try { dialog.Close(); } catch (Exception) { }
        try { dialog.Dispose(); } catch (Exception) { }
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (_backgroundDialog is not null) Vm?.CloseJwImport();
        CloseBackgroundDialog();
    }
}
