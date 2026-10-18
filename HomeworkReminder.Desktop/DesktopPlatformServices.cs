using System;
using System.Diagnostics;
using System.IO;
using HomeworkReminder.Services;
using Microsoft.Win32;

namespace HomeworkReminder.Desktop;

/// <summary>
/// 桌面平台的 <see cref="IPlatformServices"/> 实现。
/// 在 Program.Main 启动早期注入到 MainViewModel.PlatformServices。
/// </summary>
internal sealed class DesktopPlatformServices : IPlatformServices
{
    public void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception)
        {
            // 打不开浏览器不影响应用其它功能。
        }
    }

    /// <summary>在资源管理器中打开数据目录（目录不存在时不弹错误）。</summary>
    public void OpenFolder(string path)
    {
        try
        {
            if (!Directory.Exists(path)) return;
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception)
        {
            // 打不开目录不影响应用其它功能。
        }
    }

    /// <summary>
    /// 为安装更新而退出。
    /// <para>
    /// 优先走 <see cref="MainViewModel.ExitForUpdate"/>（桌面头指派到
    /// <c>MainWindow.QuitForReal</c>，它会先持久化小组件位置再关停所有窗口）。
    /// 未指派时退回直接关停应用——更新流程比小组件位置重要，不能卡在退出上。
    /// </para>
    /// </summary>
    public void RequestExitForUpdate()
    {
        if (ViewModels.MainViewModel.ExitForUpdate is { } exit)
        {
            exit();
            return;
        }

        if (Avalonia.Application.Current?.ApplicationLifetime
            is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }

    /// <summary>Windows 用 HKCU\...\Run 实现开机自启（不需要管理员权限）。</summary>
    public bool SupportsAutoLaunch => OperatingSystem.IsWindows();

    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "HomeworkReminder";

    public string? SetAutoLaunch(bool enabled)
    {
        if (!OperatingSystem.IsWindows()) return "当前平台不支持开机自启";
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                            ?? Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            if (key is null) return "无法打开注册表的启动项（HKCU\\...\\Run）";

            if (!enabled)
            {
                key.DeleteValue(RunValueName, throwOnMissingValue: false);
                return null;
            }

            var exe = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(exe))
                return "拿不到当前程序路径，无法设置开机自启";
            if (string.Equals(Path.GetFileNameWithoutExtension(exe), "dotnet", StringComparison.OrdinalIgnoreCase))
                return "开发态运行（dotnet 宿主），不会写开机自启；请用发布版测试";

            // 路径加引号：安装目录含空格时注册表启动项会被拆成两个参数。
            key.SetValue(RunValueName, $"\"{exe}\"", RegistryValueKind.String);
            return null;
        }
        catch (Exception ex)
        {
            return $"{ex.GetType().Name}: {ex.Message}";
        }
    }
}
