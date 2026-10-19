using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace HomeworkReminder.Services;

/// <summary>
/// 数据备份与恢复（导出统一目录下的业务 JSON）。
/// <para>
/// 便携程序的数据只在本机，误删/换机/升级出问题时没有云端可回滚。
/// 这里只备份「用户自己的状态」：设置、登录态、待办缓存、自建待办、课表、本地标记与提醒状态；
/// 日志、WebView 缓存等一律不进包。
/// </para>
/// <para>
/// 登录态是 DPAPI 密文，只能在同一台机器的同一 Windows 用户下解密；换机恢复后需要重新登录。
/// </para>
/// </summary>
public static class DataBackupService
{
    /// <summary>备份包含的文件（顺序即写入顺序）。</summary>
    public static readonly IReadOnlyList<string> BusinessFiles =
    [
        "settings.json",
        "session.json",
        "chaoxing-session.json",
        "pigai-session.json",
        "todos.json",
        "manual-todos.json",
        "local-state.json",
        "reminder-state.json",
        "schedule.json",
        "jw-profile.json",
    ];

    /// <summary>默认文件名（带时间戳，避免覆盖上一次备份）。</summary>
    public static string SuggestedFileName() => $"UniversityTodos-backup-{DateTime.Now:yyyyMMdd-HHmm}.zip";

    /// <summary>
    /// 把业务文件打包到 <paramref name="zipPath"/>，返回写入的文件数。
    /// 同目录下的旧备份不会被误打包（白名单固定）。
    /// </summary>
    public static int CreateBackup(string dataDirectory, string zipPath)
    {
        var dir = Path.GetFullPath(dataDirectory);
        var target = Path.GetFullPath(zipPath);
        var targetDir = Path.GetDirectoryName(target);
        if (!string.IsNullOrEmpty(targetDir)) Directory.CreateDirectory(targetDir);

        var count = 0;
        using (var zip = ZipFile.Open(target, ZipArchiveMode.Create))
        {
            foreach (var name in BusinessFiles)
            {
                var source = Path.Combine(dir, name);
                if (!File.Exists(source)) continue;
                zip.CreateEntryFromFile(source, name, CompressionLevel.Optimal);
                count++;
            }
        }
        return count;
    }

    /// <summary>
    /// 从备份恢复业务文件，返回恢复的文件数。
    /// <para>
    /// 只接受白名单里的**文件名**（忽略 zip 内路径），因此恶意 zip 无法写出数据目录。
    /// </para>
    /// </summary>
    public static int RestoreBackup(string zipPath, string dataDirectory)
    {
        var dir = Path.GetFullPath(dataDirectory);
        Directory.CreateDirectory(dir);

        var count = 0;
        using var zip = ZipFile.OpenRead(zipPath);
        foreach (var entry in zip.Entries)
        {
            // 只认白名单文件名：entry.Name 去掉目录部分，配合白名单即可杜绝路径穿越。
            if (!BusinessFiles.Contains(entry.Name, StringComparer.OrdinalIgnoreCase)) continue;
            var destination = Path.Combine(dir, entry.Name);
            entry.ExtractToFile(destination, overwrite: true);
            count++;
        }
        return count;
    }

    /// <summary>恢复前把当前数据另存一份到临时目录，返回路径（失败返回 null）。</summary>
    public static string? BackupBeforeRestore(string dataDirectory)
    {
        try
        {
            var path = Path.Combine(Path.GetTempPath(),
                $"UniversityTodos-before-restore-{DateTime.Now:yyyyMMdd-HHmmss}.zip");
            CreateBackup(dataDirectory, path);
            return path;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
