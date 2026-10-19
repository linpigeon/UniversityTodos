using Android.App;
using Android.Content.PM;
using Avalonia.Android;

namespace UniversityTodos.Android;

/// <summary>
/// Android 入口 Activity。
/// <para>
/// 系统返回键**不在这里处理**：用 Avalonia 的
/// <see cref="Avalonia.Controls.TopLevel.BackRequested"/>（见
/// <see cref="UniversityTodos.Views.MobileShellView"/>）。实测这里 override
/// <c>OnBackPressed</c> 拿不到返回键——键被 Avalonia 在更上层消费掉了，
/// 覆盖方法根本不会被调用。
/// </para>
/// <para>
/// 移动外壳已移入共享项目（<c>UniversityTodos.Views.MobileShellView</c>），
/// 这样桌面头的界面遮挡审计（<c>--androidcheck</c>）能编译并渲染同一份外壳。
/// </para>
/// </summary>
[Activity(
    // 启动器上显示的就是这个 Label（Activity 的 label 覆盖 application 的）。
    // 原先保持模板默认值 "HomeworkReminder.Android"，桌面图标下面会原样显示这串。
    Label = "大学事项综合待办",
    Theme = "@style/MyTheme.NoActionBar",
    Icon = "@drawable/icon",
    MainLauncher = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public class MainActivity : AvaloniaMainActivity
{
}
