using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using HomeworkReminder.Services;
using HomeworkReminder.ViewModels;

namespace HomeworkReminder.Views;

/// <summary>待办详情（宽屏右列与窄屏二级页共用）的交互。</summary>
public partial class TodoDetailView : UserControl
{
    public TodoDetailView()
    {
        InitializeComponent();
    }

    private MainViewModel? Vm => DataContext as MainViewModel;

    private void OnCheckboxClick(object? sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm) return;

        if (sender is Control { DataContext: TodoItemViewModel item })
        {
            _ = vm.ToggleDoneAsync(item);
        }
    }

    private async void OnToggleStarClick(object? sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm) return;
        try
        {
            var item = (sender as Control)?.DataContext as TodoItemViewModel ?? vm.SelectedItem;
            if (item is not null) await vm.ToggleStarAsync(item);
        }
        catch (Exception ex)
        {
            SidebarView.ReportHandlerFailure(vm, "星标", ex);
        }
    }

    private async void OnToggleMyDayClick(object? sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm) return;
        try
        {
            var item = (sender as Control)?.DataContext as TodoItemViewModel ?? vm.SelectedItem;
            if (item is not null) await vm.ToggleMyDayAsync(item);
        }
        catch (Exception ex)
        {
            SidebarView.ReportHandlerFailure(vm, "加入我的一天", ex);
        }
    }

    private async void OnEditManualClick(object? sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm) return;
        try
        {
            if (vm.SelectedItem is not { IsManual: true } item) return;
            if (TopLevel.GetTopLevel(this) is not Window owner) return;

            var draft = await TodoEditorWindow.ShowAsync(owner, item.Model);
            if (draft is null) return;
            await vm.UpdateManualTodoAsync(item.Model, draft);
        }
        catch (Exception ex)
        {
            SidebarView.ReportHandlerFailure(vm, "编辑待办", ex);
        }
    }

    private async void OnDeleteManualClick(object? sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm) return;
        try
        {
            if (vm.SelectedItem is not { IsManual: true } item) return;
            if (TopLevel.GetTopLevel(this) is not Window owner) return;

            if (!await MessageBox.ConfirmAsync(owner, "删除待办",
                    $"确定删除「{item.Title}」吗？此操作不可撤销。"))
                return;

            var model = item.Model;
            vm.SelectedItem = null;
            await vm.DeleteManualTodoAsync(model);
        }
        catch (Exception ex)
        {
            SidebarView.ReportHandlerFailure(vm, "删除待办", ex);
        }
    }

    private void OnOpenSourceClick(object? sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm) return;

        var item = (sender as Control)?.DataContext as TodoItemViewModel ?? vm.SelectedItem;
        if (item is not null) vm.OpenSource(item);
    }
}
