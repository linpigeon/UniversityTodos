using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using UniversityTodos.Models;
using UniversityTodos.ViewModels;

namespace UniversityTodos.Views;

/// <summary>桌面端的自建待办编辑窗口；表单实现由 TodoEditorForm 与移动页共享。</summary>
internal static class TodoEditorWindow
{
    /// <summary>打开编辑框；用户取消时返回 null。</summary>
    public static async Task<MainViewModel.ManualTodoDraft?> ShowAsync(Window owner, TodoItem? existing)
    {
        var form = new TodoEditorForm(existing, mobile: false);
        var window = new Window
        {
            Title = existing is null ? "新建待办" : "编辑待办",
            Width = 480,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Res("TodoDialogBackground", "#FFFFFF"),
            Icon = MessageBox.LoadIcon(),
            Content = form.View,
        };
        form.Completed += (_, _) => window.Close();

        await window.ShowDialog(owner);
        return form.Result;
    }

    private static IBrush Res(string key, string fallback)
    {
        var app = Application.Current;
        if (app?.TryGetResource(key, app.ActualThemeVariant, out var v) == true && v is IBrush b)
            return b;
        return SolidColorBrush.Parse(fallback);
    }
}
