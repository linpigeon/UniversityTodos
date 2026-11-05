using System;
using Android.App;
using Android.Runtime;
using Avalonia;
using Avalonia.Android;
using Avalonia.Media;

namespace UniversityTodos.Android;

/// <summary>
/// Avalonia 12 的 Android 应用入口：App 的初始化从 MainActivity 挪到了这里
/// （AvaloniaMainActivity 不再有泛型参数，CustomizeAppBuilder 也移到了这个类上）。
/// </summary>
[Application]
public class AndroidApp : AvaloniaAndroidApplication<App>
{
    protected AndroidApp(IntPtr javaReference, JniHandleOwnership transfer)
        : base(javaReference, transfer)
    {
        AndroidPlatformHooks.Register();
        // 注入移动端外壳（抽屉式布局）：MainView 用它替换共享项目里的桌面三栏外壳。
        // 外壳本身在共享项目里（UniversityTodos.Views.MobileShellView），
        // 平台的差异通过 IPlatformServices 注入，见 PrepareExamWebSession。
        App.MobileShellFactory = () => new UniversityTodos.Views.MobileShellView();
        // Material 主题：只在 Android 头挂上，桌面端该钩子保持 null（仍用 Fluent）。
        App.MobileThemeHook = UniversityTodos.Views.MobileMaterialTheme.Apply;
        // 平台服务：浏览器打开 + 雨课堂 App 跳转。
        ViewModels.MainViewModel.PlatformServices = new AndroidPlatformServices();
        // 系统通知：Android 原生通知通道；权限由 MainActivity 在 Android 13+ 请求。
        ViewModels.MainViewModel.Notifier = new AndroidNotifier();
    }

    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        return base.CustomizeAppBuilder(builder)
            .WithInterFont()
            // Android 的系统 CJK 字体没有可直接引用的 family 名。这里把官方
            // Noto Sans CJK SC Regular（SIL OFL 1.1）作为 AvaloniaResource 随应用嵌入，
            // 通过 FontManager fallback 承担 CJK 字形；不修改任何控件的 FontFamily。
            // 桌面 Program.cs 仍单独使用 Inter，不受这里影响。
            .With(new FontManagerOptions
            {
                DefaultFamilyName = "avares://Avalonia.Fonts.Inter/Assets#Inter",
                FontFallbacks =
                [
                    new FontFallback
                    {
                        FontFamily = new FontFamily(
                            "avares://UniversityTodos/Assets/Fonts/NotoSansCJKsc-Regular.otf#Noto Sans CJK SC"),
                        UnicodeRange = UnicodeRange.Parse(
                            "U+3000-303F,U+3400-4DBF,U+4E00-9FFF,U+F900-FAFF,U+FF00-FFEF"),
                    },
                ],
            });
    }
}
