using Avalonia.Controls;
using Avalonia.Interactivity;
using UniversityTodos.ViewModels;

namespace UniversityTodos.Views;

public partial class GradeInfoView : UserControl
{
    public GradeInfoView()
    {
        InitializeComponent();
    }

    private void OnImportClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm) vm.OpenGradeInfoImport();
    }
}
