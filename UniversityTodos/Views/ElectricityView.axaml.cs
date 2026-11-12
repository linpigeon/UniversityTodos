using System;
using Avalonia;
using Avalonia.Controls;

namespace UniversityTodos.Views;

public partial class ElectricityView : UserControl
{
    private bool? _twoColumns;

    public ElectricityView()
    {
        InitializeComponent();
        SizeChanged += (_, _) => UpdateColumns();
    }

    private void UpdateColumns()
    {
        var twoColumns = Bounds.Width >= 760;
        if (_twoColumns == twoColumns) return;
        _twoColumns = twoColumns;
        UtilityColumns.ColumnDefinitions = new ColumnDefinitions(twoColumns ? "*,*" : "*");
        UtilityColumns.RowDefinitions = new RowDefinitions(twoColumns ? "Auto" : "Auto,Auto");
        Grid.SetColumn(WaterColumn, twoColumns ? 1 : 0);
        Grid.SetRow(WaterColumn, twoColumns ? 0 : 1);
        ElectricityColumn.Margin = twoColumns ? new Thickness(0, 0, 12, 0) : new Thickness(0);
        WaterColumn.Margin = twoColumns ? new Thickness(12, 0, 0, 0) : new Thickness(0, 14, 0, 0);
    }
}
