using System;
using System.IO;

namespace HomeworkReminder.Services;

/// <summary>
/// 应用数据的存放位置。
/// <para>
/// 两种模式：
/// </para>
/// <list type="bullet">
/// <item><b>安装/便携模式</b>：exe 同目录存在 <c>portable.flag</c> 时，数据放
/// <c>&lt;程序目录&gt;\Data</c>，WebView2 缓存放 <c>&lt;程序目录&gt;\Cache\WebView2</c>。
/// 安装程序会创建这个标记，从而把数据与缓存都收在同一个安装路径下，卸载/备份都只面对一个目录。</item>
/// <item><b>兼容模式</b>：没有标记时沿用旧行为 <c>%LOCALAPPDATA%\HomeworkReminder</c>，
/// 保证自动更新过来的老用户与直接解压 zip 的用户数据不断档。</item>
/// </list>
/// <para>
/// 环境变量 <c>HOMEWORKREMINDER_DATA_DIR</c> 仍然优先级最高——受限环境（沙箱、CI）
/// 用它把数据指到临时目录。
/// </para>
/// </summary>
public static class AppPaths
{
    public const string DataDirEnvironmentVariable = "HOMEWORKREMINDER_DATA_DIR";

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
        if (!string.IsNullOrWhiteSpace(overridden) && TryPrepare(overridden, out var custom))
            return custom;

        // 安装/便携模式：数据与缓存都放在程序目录下。
        if (IsPortable)
        {
            var portable = Path.Combine(ApplicationDirectory, "Data");
            if (TryPrepare(portable, out var okPortable)) return okPortable;
            // Data 写不进去（例如装到了 Program Files）就退回旧目录：
            // 应用仍可用，但设置里的「数据位置」会显示旧路径，便于用户发现装错位置。
        }

        var preferred = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HomeworkReminder");
        if (TryPrepare(preferred, out var ok)) return ok;

        // 兜底：临时目录。数据在重启后可能丢失，但应用仍可用。
        var fallback = Path.Combine(Path.GetTempPath(), "HomeworkReminder");
        if (TryPrepare(fallback, out var temp)) return temp;

        // 连临时目录都写不了时，至少要有一个不会抛异常的路径。
        return preferred;
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
