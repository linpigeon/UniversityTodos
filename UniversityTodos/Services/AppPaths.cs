using System;
using System.IO;

namespace UniversityTodos.Services;

/// <summary>
/// 应用数据的存放位置。
/// <para>
/// 两种模式：
/// </para>
/// <list type="bullet">
/// <item><b>安装/便携模式</b>：exe 同目录存在 <c>portable.flag</c> 时，数据放
/// <c>&lt;程序目录&gt;\Data</c>，WebView2 缓存放 <c>&lt;程序目录&gt;\Cache\WebView2</c>。
/// 安装程序会创建这个标记，从而把数据与缓存都收在同一个安装路径下，卸载/备份都只面对一个目录。</item>
/// <item><b>兼容模式</b>：没有标记时用 <c>%LOCALAPPDATA%\UniversityTodos</c>，
/// 保证自动更新过来的老用户与直接解压 zip 的用户数据不断档。</item>
/// </list>
/// <para>
/// 环境变量 <c>UNIVERSITYTODOS_DATA_DIR</c> 仍然优先级最高——受限环境（沙箱、CI）
/// 用它把数据指到临时目录；改名前的 <c>HOMEWORKREMINDER_DATA_DIR</c> 继续兼容。
/// </para>
/// <para>
/// 从旧名版本升级、且新目录尚不存在时，会把旧目录
/// <c>%LOCALAPPDATA%\HomeworkReminder</c> 里的业务 JSON 一次性复制过来，
/// 避免用户丢待办、课表与登录态。
/// </para>
/// </summary>
public static class AppPaths
{
    public const string DataDirEnvironmentVariable = "UNIVERSITYTODOS_DATA_DIR";

    /// <summary>改名前的环境变量，CI 与老脚本仍在用，作为兜底保留。</summary>
    public const string LegacyDataDirEnvironmentVariable = "HOMEWORKREMINDER_DATA_DIR";

    /// <summary>改名前的用户数据目录名（位于 %LOCALAPPDATA% 下）。</summary>
    private const string LegacyDataDirectoryName = "HomeworkReminder";

    /// <summary>当前用户数据目录名。</summary>
    private const string DataDirectoryName = "UniversityTodos";

    /// <summary>便携/安装模式标记文件名（放在 exe 同目录）。</summary>
    public const string PortableMarkerFileName = "portable.flag";

    private static readonly Lazy<string> LazyDataDirectory = new(Resolve);

    /// <summary>程序所在目录（单文件发布时就是 exe 所在目录）。</summary>
    public static string ApplicationDirectory => AppContext.BaseDirectory;

    /// <summary>是否安装/便携模式（exe 旁有 <see cref="PortableMarkerFileName"/>）。</summary>
    public static bool IsPortable =>
        File.Exists(Path.Combine(ApplicationDirectory, PortableMarkerFileName));

    /// <summary>安装/便携模式下的 WebView2 缓存目录。</summary>
    public static string CacheDirectory => Path.Combine(ApplicationDirectory, "Cache");

    /// <summary>安装/便携模式下 WebView2 的用户数据目录。</summary>
    public static string WebView2UserDataFolder => Path.Combine(CacheDirectory, "WebView2");

    public static string DataDirectory => LazyDataDirectory.Value;

    private static string Resolve()
    {
        var overridden = Environment.GetEnvironmentVariable(DataDirEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(overridden))
            overridden = Environment.GetEnvironmentVariable(LegacyDataDirEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(overridden) && TryPrepare(overridden, out var custom))
            return custom;

        // 安装/便携模式：数据与缓存都放在程序目录下。
        if (IsPortable)
        {
            var portable = Path.Combine(ApplicationDirectory, "Data");
            if (TryPrepare(portable, out var okPortable)) return okPortable;
            // Data 写不进去（例如装到了 Program Files）就退回用户目录：
            // 应用仍可用，但设置里的「数据位置」会显示用户目录，便于用户发现装错位置。
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var preferred = Path.Combine(localAppData, DataDirectoryName);
        TryMigrateLegacyDirectory(Path.Combine(localAppData, LegacyDataDirectoryName), preferred);
        if (TryPrepare(preferred, out var ok)) return ok;

        // 兜底：临时目录。数据在重启后可能丢失，但应用仍可用。
        var fallback = Path.Combine(Path.GetTempPath(), DataDirectoryName);
        if (TryPrepare(fallback, out var temp)) return temp;

        // 连临时目录都写不了时，至少要有一个不会抛异常的路径。
        return preferred;
    }

    /// <summary>
    /// 旧目录存在、新目录还没建时，把业务 JSON 复制到新目录。
    /// <para>
    /// 只复制顶层 <c>*.json</c>（设置、登录态、待办、课表等），日志与 WebView 缓存不迁移；
    /// 目标已存在同名文件时跳过，不覆盖。
    /// </para>
    /// <para>
    /// 迁移失败不阻断启动：新目录照常创建，用户仍可手动复制。
    /// </para>
    /// </summary>
    private static void TryMigrateLegacyDirectory(string legacy, string target)
    {
        try
        {
            if (Directory.Exists(target) || !Directory.Exists(legacy)) return;

            Directory.CreateDirectory(target);
            foreach (var source in Directory.EnumerateFiles(legacy, "*.json", SearchOption.TopDirectoryOnly))
            {
                var destination = Path.Combine(target, Path.GetFileName(source));
                if (File.Exists(destination)) continue;
                File.Copy(source, destination);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // 迁移是尽力而为：失败后新目录里缺什么，用户还能从旧目录手动补。
        }
    }

    private static bool TryPrepare(string dir, out string resolved)
    {
        resolved = dir;
        try
        {
            Directory.CreateDirectory(dir);
            // CreateDirectory 对已存在的只读目录不会报错，所以再实际探测一次可写性。
            var probe = Path.Combine(dir, ".write-probe");
            File.WriteAllText(probe, string.Empty);
            File.Delete(probe);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return false;
        }
    }
}
