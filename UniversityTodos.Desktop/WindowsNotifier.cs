using System;
using System.IO;
using System.Runtime.Versioning;
using UniversityTodos.Services;
using Microsoft.Win32;

namespace UniversityTodos.Desktop;

/// <summary>
/// Windows 版通知实现：自注册 AppUserModelId + 写开始菜单快捷方式 + WinRT toast。
/// <para>
/// <b>为什么需要自注册</b>：未打包（unpackaged）的 Win32 应用没有包标识，
/// <c>ToastNotificationManager.CreateToastNotifier(aumid)</c> 要求该 AUMID
/// 对应一个「已安装的应用」——只写注册表项不够。
/// </para>
/// <para>
/// 实测（<c>tools/ToastProbe</c>）：仅注册 <c>HKCU\Software\Classes\AppUserModelId\&lt;aumid&gt;</c>
/// 时 <c>CreateToastNotifier</c> 返回 <c>0x80070490</c>（找不到元素）；
/// 同时还需要一个带 <c>System.AppUserModel.ID</c> 属性的开始菜单快捷方式，
/// 这也是 Tor.Net 等项目采用的做法。
/// </para>
/// <para>
/// 全部写入都在 HKCU 与用户自己的开始菜单目录，<b>不需要管理员权限</b>。
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsNotifier : INotifier
{
    /// <summary>
    /// 自注册的 AppUserModelId。命名遵循「公司.产品」惯例。
    /// <para>
    /// 2026-10 全量改名后从 <c>HomeworkReminder.Desktop</c> 换成新 ID：
    /// 旧注册表项由 <see cref="CleanupLegacyRegistration"/> 删除，
    /// 开始菜单快捷方式会因 AUMID 不一致而重建（见 <see cref="EnsureStartMenuShortcut"/>）。
    /// </para>
    /// </summary>
    private const string Aumid = "UniversityTodos.Desktop";

    /// <summary>改名前的 AUMID，只用于清理遗留注册表项。</summary>
    private const string LegacyAumid = "HomeworkReminder.Desktop";

    private const string DisplayName = "大学事项综合待办";

    private bool _registered;
    private string? _unavailableReason;

    public WindowsNotifier()
    {
        if (!OperatingSystem.IsWindows())
        {
            _unavailableReason = "当前平台不支持系统通知";
            return;
        }

        try
        {
            CleanupLegacyRegistration();
            RegisterAumid();
            EnsureStartMenuShortcut();
            _registered = true;
        }
        catch (Exception ex)
        {
            _unavailableReason = $"通知初始化失败：{ex.GetType().Name}: {ex.Message}";
        }
    }

    public bool IsAvailable => _registered && _unavailableReason is null;

    public string? UnavailableReason => _unavailableReason;

    public string? Show(AppNotification notification)
    {
        if (!IsAvailable) return _unavailableReason ?? "通知不可用";

        var xml = WinRtToast.BuildToastXml(
            notification.Title,
            notification.Body,
            notification.Attribution,
            notification.LaunchUrl);

        var result = WinRtToast.TryShow(Aumid, xml);
        return result.Ok ? null : result.Describe();
    }

    /// <summary>
    /// 注册 AUMID。已存在且 DisplayName 一致时跳过——避免每次启动都写注册表。
    /// </summary>
    private static void RegisterAumid()
    {
        var subKey = $@"Software\Classes\AppUserModelId\{Aumid}";

        using var existing = Registry.CurrentUser.OpenSubKey(subKey);
        if (existing?.GetValue("DisplayName") as string == DisplayName) return;

        using var key = Registry.CurrentUser.CreateSubKey(subKey, writable: true)
                        ?? throw new InvalidOperationException("无法创建 AUMID 注册表项");
        key.SetValue("DisplayName", DisplayName, RegistryValueKind.String);
        // 有意取舍：ShowInSettings=0 让本应用不出现在「设置 → 通知」列表里，
        // 减少对用户的干扰；代价是用户无法在系统设置里单独关闭本应用的通知，
        // 只能在应用内（或整体通知开关）管理。
        key.SetValue("ShowInSettings", 0, RegistryValueKind.DWord);
    }

    /// <summary>
    /// 删掉改名前的 AUMID 注册表项。
    /// 通知身份换了之后旧项不会再被使用，留着只会在系统设置里留下无效条目。
    /// </summary>
    private static void CleanupLegacyRegistration()
    {
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(
                $@"Software\Classes\AppUserModelId\{LegacyAumid}", throwOnMissingSubKey: false);
        }
        catch
        {
            // 删不掉不影响新 AUMID 的注册。
        }
    }

    /// <summary>
    /// 在开始菜单创建一个指向本程序的快捷方式，并设置 <c>System.AppUserModel.ID</c>。
    /// 这个属性是通知平台认定「该 AUMID 属于一个已安装应用」的依据。
    /// </summary>
    private static void EnsureStartMenuShortcut()
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe) || !File.Exists(exe)) return;

        var startMenu = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            @"Microsoft\Windows\Start Menu\Programs");
        if (!Directory.Exists(startMenu)) return;

        var linkPath = Path.Combine(startMenu, $"{DisplayName}.lnk");

        // 改名前的快捷方式（v1.4.2 及更早叫「作业提醒」）一并清掉，
        // 避免开始菜单里出现两个指向同一 exe 的入口。
        try
        {
            var legacy = Path.Combine(startMenu, "作业提醒.lnk");
            if (File.Exists(legacy)) File.Delete(legacy);
        }
        catch
        {
            // 删不掉只是多一个旧图标，不影响通知。
        }

        // 已存在、指向同一个 exe、且 AUMID 已是新值时无需重建。
        // 显示名没变，所以旧快捷方式的路径与新的一致——只看目标会漏掉 AUMID 变更，
        // 那会导致新 AUMID 找不到「已安装应用」，通知直接发不出去。
        if (File.Exists(linkPath))
        {
            if (ShortcutTargets(linkPath, exe)
                && ShellLink.TryGetAumid(linkPath, out var aumid)
                && string.Equals(aumid, Aumid, StringComparison.Ordinal))
            {
                return;
            }

            try
            {
                File.Delete(linkPath);
            }
            catch
            {
                // 删不掉时下面仍会尝试覆盖写入。
            }
        }

        ShellLink.Create(linkPath, exe, DisplayName, Aumid, exe);
    }

    /// <summary>
    /// 现有快捷方式是否已指向我们的 exe。发布目录变更后旧快捷方式指向的 exe
    /// 已不存在，必须识别出来并重建，否则点通知会启动一个已不存在的旧路径。
    /// </summary>
    private static bool ShortcutTargets(string linkPath, string exe)
    {
        return ShellLink.TryGetTarget(linkPath, out var target)
               && string.Equals(target, exe, StringComparison.OrdinalIgnoreCase);
    }
}
