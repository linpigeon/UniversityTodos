using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace UniversityTodos.Services;

/// <summary>更新无法进行的原因（用于给用户明确提示，而不是笼统的「失败」）。</summary>
public enum UpdateApplyBlock
{
    /// <summary>可以更新。</summary>
    None,
    /// <summary>开发态运行（dotnet 宿主），替换会破坏开发环境。</summary>
    DevelopmentMode,
    /// <summary>拿不到自身 exe 路径。</summary>
    UnknownExecutable,
    /// <summary>安装目录不可写（Program Files / 权限 / 杀软保护）。</summary>
    TargetNotWritable,
    /// <summary>找不到释放出来的替换脚本。</summary>
    ScriptUnavailable,
    /// <summary>启动替换进程失败。</summary>
    LaunchFailed,
}

/// <summary>准备更新的结果。</summary>
public sealed record UpdateApplyPlan(
    bool Ready,
    UpdateApplyBlock Block,
    string Message,
    string? ScriptPath = null,
    string? TargetExe = null,
    string? NewExe = null);

/// <summary>
/// 把下载好的更新包落到磁盘上，并启动<b>替换进程</b>完成替换。
/// <para>
/// 为什么要有独立进程：Windows 上运行中的 exe 不能覆盖、不能删除（实测），
/// 但可以改名。所以流程必须是「本进程启动替换器 → 本进程退出 → 替换器替换 → 替换器重启」。
/// 替换逻辑本身在嵌入的 <c>apply-update.ps1</c> 里（可读、可单独审阅）。
/// </para>
/// <para>
/// <b>安全边界</b>：这里只做「解出 exe + 释放脚本 + 启动 + 退出」，
/// 全程只触碰下载目录里的文件；目标安装目录交给替换器，且替换器只改那一个 exe。
/// </para>
/// </summary>
public static class UpdateApplier
{
    private const string ScriptResourceName = "UniversityTodos.Assets.apply-update.ps1";

    /// <summary>下载目录（放在临时目录下，替换器会自行清理）。</summary>
    public static string DownloadDirectory =>
        Path.Combine(Path.GetTempPath(), "UniversityTodos", "update");

    /// <summary>本次运行的可执行文件路径；非单文件发布（dotnet 宿主）时为 null。</summary>
    public static string? CurrentExecutablePath
    {
        get
        {
            var path = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(path)) return null;

            var name = Path.GetFileNameWithoutExtension(path);
            // 开发态：dotnet run / dotnet xxx.dll 时 ProcessPath 指向 dotnet 宿主。
            // 替换它会破坏开发环境，必须拒绝。
            if (name.Equals("dotnet", StringComparison.OrdinalIgnoreCase)) return null;

            // 只有真的存在才算数（理论上必然存在，这里只是防御性判断）。
            return File.Exists(path) ? path : null;
        }
    }

    /// <summary>解包结果：解出的 exe 路径 + **该 exe 自身**的 SHA256。</summary>
    /// <param name="ExePath">解出的 exe。</param>
    /// <param name="Sha256">
    /// 解出的 exe 的 SHA256（不是 zip 的）。
    /// 更新器替换后校验的是目标 exe，所以必须给它 exe 的哈希；
    /// 传 zip 的哈希会让校验必然失败（实测踩到：日志报「新文件哈希不符」）。
    /// </param>
    public sealed record StagedPackage(string ExePath, string Sha256);

    /// <summary>
    /// 解出更新包里的 exe，确认它不会把用户降到更旧的版本，并算出**exe 自身**的 SHA256。
    /// </summary>
    /// <param name="packagePath">下载好的 zip。</param>
    /// <param name="ct">取消令牌。</param>
    public static async Task<StagedPackage> StageAsync(string packagePath, CancellationToken ct = default)
    {
        var staged = await Task.Run(
            () => UpdateService.ExtractPackageExe(packagePath, DownloadDirectory), ct).ConfigureAwait(false);

        // 防降级兜底：比较基准是**当前已安装的版本**，不是 release tag。
        // 用 tag 当基准是错的——tag 与包内 exe 的版本号并不保证一致
        // （本项目实测过 tag=v1.3.1 而包内是 1.0.0.0 的历史缺陷），
        // 拿 tag 比会把正常更新误判成降级而拒绝。
        if (!UpdateService.PackageIsNotOlderThan(packagePath, UpdateService.CurrentVersion))
        {
            var packed = UpdateService.ReadPackageExeVersion(packagePath) ?? "(读不出)";
            throw new InvalidDataException(
                $"更新包版本低于当前版本：包内程序为 {packed}，当前为 {UpdateService.CurrentVersion}。"
                + "已中止以免降级。");
        }

        // 算出 exe 自己的哈希交给更新器：它替换后校验的是这个 exe。
        var exeHash = await Task.Run(() => UpdateService.ComputeSha256(staged), ct).ConfigureAwait(false);
        return new StagedPackage(staged, exeHash);
    }

    /// <summary>
    /// 释放替换脚本、启动替换进程。
    /// <para>
    /// 调用方拿到 <see cref="UpdateApplyPlan.Ready"/> = true 后<b>必须尽快退出应用</b>：
    /// 替换器正在等本进程退出，不退它就一直等到超时再强制结束。
    /// </para>
    /// </summary>
    /// <param name="newExePath">已解出的新版本 exe。</param>
    /// <param name="expectedSha256">新 exe 的 SHA256（清单没有时可传 null）。</param>
    /// <param name="restart">替换后是否重启。</param>
    /// <param name="targetExeOverride">
    /// 覆盖「要替换哪个 exe」。**仅供测试使用**：正常路径始终替换当前进程自己的 exe。
    /// 有了它，可以在临时沙箱里放一份 exe 副本作为替换目标，完整演练一遍
    /// 「下载 → 校验 → 替换 → 重启」而不碰真实安装目录。
    /// </param>
    public static UpdateApplyPlan StartReplacement(
        string newExePath, string? expectedSha256, bool restart = true, string? targetExeOverride = null)
    {
        var target = targetExeOverride ?? CurrentExecutablePath;
        if (target is null)
        {
            return new UpdateApplyPlan(false, UpdateApplyBlock.DevelopmentMode,
                "当前是开发态运行（由 dotnet 启动），自动替换会破坏开发环境。"
                + "请改用发布版，或手动下载新版本。");
        }

        if (!File.Exists(target))
        {
            return new UpdateApplyPlan(false, UpdateApplyBlock.UnknownExecutable,
                $"替换目标不存在：{target}");
        }

        if (!File.Exists(newExePath))
        {
            return new UpdateApplyPlan(false, UpdateApplyBlock.UnknownExecutable,
                "找不到已解出的新版本文件，请重新下载。");
        }

        // 目标目录不可写时**必须在退出前就拒绝**：否则应用按 Ready 退出、替换器
        // 重试 20 次后失败，只在临时目录留一行日志，用户看到的是程序凭空消失。
        if (!DirectoryWritable(Path.GetDirectoryName(target)!))
        {
            return new UpdateApplyPlan(false, UpdateApplyBlock.TargetNotWritable,
                "安装目录不可写（可能位于 Program Files，或受权限/杀毒软件保护）。"
                + "自动更新无法进行：请把程序放到用户目录，或以管理员身份手动替换。");
        }

        string scriptPath;
        try
        {
            scriptPath = ExtractScript();
        }
        catch (Exception ex)
        {
            return new UpdateApplyPlan(false, UpdateApplyBlock.ScriptUnavailable,
                $"无法释放替换脚本：{ex.Message}");
        }

        // 重启时要转发的命令行参数。
        // **本程序没有面向用户的启动参数**：全部是诊断/自检用的，其中
        // --pigaitest / --pigailogin 还带整条 cookie / 密码。这些绝不能写进
        // restart-args.txt，更不能转发给新进程（原先只过滤了 --selfcheck）。
        var keepArgs = Array.Empty<string>();

        var args = new List<string>
        {
            "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass",
            "-File", scriptPath,
            "-TargetExe", target,
            "-NewExe", newExePath,
            "-ParentPid", Environment.ProcessId.ToString(),
        };
        if (!string.IsNullOrWhiteSpace(expectedSha256))
        {
            args.Add("-ExpectedSha256");
            args.Add(expectedSha256);
        }
        if (keepArgs.Length > 0)
        {
            var argsFile = WriteRestartArgsFile(keepArgs);
            if (argsFile is not null)
            {
                args.Add("-ArgsFile");
                args.Add(argsFile);
            }
        }
        if (restart) args.Add("-Restart");

        try
        {
            var psi = new ProcessStartInfo
            {
                // 用绝对路径：Windows 的搜索顺序会先找应用目录/当前目录，
                // 便携版放在可写目录时可能被同名 powershell.exe 劫持。
                FileName = ResolveWindowsPowerShell(),
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            // 用 ArgumentList（而不是拼 Args 字符串）：路径含空格/引号时不会被拆错。
            foreach (var a in args) psi.ArgumentList.Add(a);
            Process.Start(psi);
        }
        catch (Exception ex)
        {
            return new UpdateApplyPlan(false, UpdateApplyBlock.LaunchFailed,
                $"无法启动替换进程：{ex.Message}");
        }

        return new UpdateApplyPlan(true, UpdateApplyBlock.None,
            "更新已就绪，应用即将退出并完成替换，随后会自动重新启动。",
            scriptPath, target, newExePath);
    }

    /// <summary>
    /// 目录是否可写：实际建一个探针文件再删掉。
    /// <para>比 <c>File.Exists</c> 靠谱：Program Files 下文件存在但没有写权限。</para>
    /// </summary>
    private static bool DirectoryWritable(string directory)
    {
        try
        {
            var probe = Path.Combine(directory, $".hr-write-{Guid.NewGuid():N}.tmp");
            using (File.Create(probe)) { }
            File.Delete(probe);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return false;
        }
    }

    /// <summary>取系统自带的 Windows PowerShell 绝对路径（找不到则回落到 PATH 查找）。</summary>
    private static string ResolveWindowsPowerShell()
    {
        var system = Environment.GetFolderPath(Environment.SpecialFolder.System);
        if (!string.IsNullOrWhiteSpace(system))
        {
            var full = Path.Combine(system, "WindowsPowerShell", "v1.0", "powershell.exe");
            if (File.Exists(full)) return full;
        }
        return "powershell.exe";
    }

    /// <summary>
    /// 把要转发的命令行参数**每行一个**写进临时文件，返回路径（失败返回 null）。
    /// <para>
    /// 为什么用文件：见 <see cref="StartReplacement"/> 里的说明——PowerShell 无法从命令行
    /// 可靠地绑定一个字符串数组参数。用文件还顺带解决了参数含空格/引号/&amp; 的转义问题。
    /// </para>
    /// </summary>
    public static string? WriteRestartArgsFile(IEnumerable<string> keepArgs)
    {
        try
        {
            var dir = Path.Combine(Path.GetTempPath(), "UniversityTodos", "updater");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "restart-args.txt");
            // 每行一个参数。参数本身若含换行会被拆成两个，这是可接受的边界
            // （命令行参数含换行在实际使用中不会出现）。
            File.WriteAllLines(path, keepArgs, new System.Text.UTF8Encoding(false));
            return path;
        }
        catch
        {
            // 写不了就退化为不带参数重启，不要让更新因此失败。
            return null;
        }
    }

    /// <summary>
    /// 把嵌入的替换脚本写到临时目录，返回路径。
    /// <para>
    /// <b>必须保证恰好一个 UTF-8 BOM</b>：脚本里有中文注释与中文错误消息，而
    /// Windows PowerShell 5.1 对**无 BOM** 的文件按系统 ANSI 码页解码，
    /// 中文字节会被解成乱码并破坏语法——实测无 BOM 时解析器报 7 个语法错误
    /// （「Try 语句缺少 Catch」「字符串缺少终止符」），加 BOM 后为 0 个。
    /// </para>
    /// <para>
    /// <b>不能盲目追加 BOM</b>：仓库里的脚本文件本身已经带 BOM，写进嵌入资源时会一起被带上；
    /// 再无脑写一个就会变成**双重 BOM**，解析器会把第二个 BOM 当成
    /// 「`[CmdletBinding()]` 前的意外标记」而直接拒绝整个脚本（实测踩到）。
    /// 所以这里先探测流开头是否已有 BOM，只在缺失时补。
    /// </para>
    /// </summary>
    public static string ExtractScript()
    {
        var dir = Path.Combine(Path.GetTempPath(), "UniversityTodos", "updater");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "apply-update.ps1");

        using var stream = typeof(UpdateApplier).Assembly.GetManifestResourceStream(ScriptResourceName)
            ?? throw new InvalidOperationException(
                $"嵌入资源缺失：{ScriptResourceName}。请检查 csproj 的 EmbeddedResource 配置。");

        // 规范化成「恰好一个 UTF-8 BOM」：
        //   ① 先用**确定可靠的 byte[] 重载**探测嵌入内容开头的 3 字节；
        //   ② 若已有 BOM 就跳过它（避免双重 BOM）；
        //   ③ 无条件写一个 BOM（PowerShell 5.1 需要）。
        // 刻意不做「有就复用、没有才写」的条件分支：那样结果取决于读操作是否生效，
        // 而这里实测过 Span 重载会读到 0 字节却又不报错，导致 BOM 时有时无。
        // 现在的写法只依赖 byte[] 重载，结果确定。
        var head = new byte[3];
        var read = stream.Read(head, 0, 3);
        var embeddedHasBom = read == 3 && head[0] == 0xEF && head[1] == 0xBB && head[2] == 0xBF;
        if (!embeddedHasBom) stream.Seek(0, SeekOrigin.Begin);

        using var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        file.Write([0xEF, 0xBB, 0xBF]);
        stream.CopyTo(file);
        return path;
    }

    /// <summary>
    /// 清理下载目录（用户取消、失败重试、或启动前回收）。
    /// 只删临时目录下属于本程序的下载目录。
    /// </summary>
    public static void CleanDownloadDirectory()
    {
        try
        {
            var dir = DownloadDirectory;
            var tempRoot = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar);
            // 双保险：必须在临时目录下，才敢递归删。
            if (!dir.StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase)) return;
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
        catch
        {
            // 清理失败不影响主流程（下次更新会覆盖）。
        }
    }

    /// <summary>供自检使用：确认嵌入资源确实存在且非空。</summary>
    public static bool ScriptResourceAvailable()
    {
        try
        {
            using var s = typeof(UpdateApplier).Assembly.GetManifestResourceStream(ScriptResourceName);
            return s is { Length: > 0 };
        }
        catch
        {
            return false;
        }
    }
}
