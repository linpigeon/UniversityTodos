using System;
using System.Linq;
using Avalonia;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Themes.Fluent;
using Material.Styles.Themes;
using Material.Styles.Themes.Base;

namespace UniversityTodos.Views;

/// <summary>
/// Android 端的 Material 主题适配层。
/// <para>
/// 只在平台头（<c>AndroidApp</c>）或离屏审计（<c>AndroidLayoutCheck</c>）里挂到
/// <see cref="App.MobileThemeHook"/> 上；桌面端该钩子保持 null，加载的仍是
/// App.axaml 里的 FluentTheme，不会混入任何 Material 样式。
/// </para>
/// <para>
/// 替换范围只有 FluentTheme：TodoTheme/WidgetTheme 与全部 Todo* 资源原样保留，
/// 这样列表行、选中态、逾期色等业务配色仍由现有 DynamicResource 驱动。
/// <see cref="MaterialTheme.BaseTheme"/> 用 <c>Inherit</c> 跟随
/// <see cref="Application.RequestedThemeVariant"/>，浅色 / 深色 / 跟随系统
/// 三种行为与桌面一致；主题切换时由 MaterialTheme 自己更新，不重复叠加样式。
/// </para>
/// </summary>
public static class MobileMaterialTheme
{
    private const string MobileThemeUri = "avares://UniversityTodos/Styles/MobileTheme.axaml";

    public static void Apply(Application app)
    {
        var material = app.Styles.OfType<MaterialTheme>().FirstOrDefault();
        if (material is null)
        {
            material = new MaterialTheme(null!)
            {
                // Inherit：跟随 Application.RequestedThemeVariant（浅色/深色/系统）。
                BaseTheme = BaseThemeMode.Inherit,
            };

            var fluent = app.Styles.OfType<FluentTheme>().FirstOrDefault();
            if (fluent is null)
            {
                app.Styles.Insert(0, material);
            }
            else
            {
                var index = app.Styles.IndexOf(fluent);
                app.Styles.RemoveAt(index);
                app.Styles.Insert(index, material);
            }
        }

        material.PrimaryColor = Material.Colors.PrimaryColor.Blue;
        material.SecondaryColor = Material.Colors.SecondaryColor.Blue;

        // MobileTheme 只随本钩子加载：桌面端不会拿到卡片边距、底部导航等移动端样式。
        if (!app.Styles.OfType<StyleInclude>().Any(s =>
                string.Equals(s.Source?.ToString(), MobileThemeUri, StringComparison.Ordinal)))
        {
            var include = new StyleInclude(new Uri("avares://UniversityTodos/"))
            {
                Source = new Uri(MobileThemeUri),
            };
            app.Styles.Add(include);
        }
    }
}
