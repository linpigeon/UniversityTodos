using Avalonia.Markup.Xaml;
using Avalonia.Styling;

namespace UniversityTodos.Styles;

public partial class MobileTheme : Avalonia.Styling.Styles
{
    public MobileTheme() => AvaloniaXamlLoader.Load(this);
}
