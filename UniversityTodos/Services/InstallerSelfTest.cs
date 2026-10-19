using System;
using System.IO;
using System.Linq;

namespace UniversityTodos.Services;

/// <summary>
/// 安装包下载链路自检：`--installercheck [下载目录]`。
/// <para>
/// 不依赖界面与登录态，验证「查最新 release → 找 Setup 资产 → 按清单哈希下载」。
/// 传下载目录时会依次尝试各源，成功一个即停止（GitHub 的下载域名在国内常不可达）。
/// </para>
/// </summary>
public static class InstallerSelfTest
{
    public static int Run(string? downloadDirectory)
    {
        Console.WriteLine("=== 安装包下载自检 ===");
        try
        {
            var choices = UpdateService.FindInstallersAsync(includePrerelease: true)
                .GetAwaiter().GetResult();

            if (choices.Count == 0)
            {
                Console.WriteLine("未找到安装包：最新 release 里没有 *-Setup.exe。");
                return 2;
            }

            foreach (var c in choices)
            {
                var hash = c.Manifest?.Installer?.Sha256;
                Console.WriteLine(
                    $"  {c.Source,-6} {c.Version,-8} {c.Asset.Name} "
                    + $"({c.Asset.Size / 1048576.0:0.0} MB) hash={(hash is { Length: > 0 } ? "有" : "无")}");
                Console.WriteLine($"         {c.Asset.BrowserDownloadUrl}");
            }

            if (string.IsNullOrWhiteSpace(downloadDirectory))
            {
                Console.WriteLine("结论：解析成功（未传下载目录，跳过实际下载）。");
                return 0;
            }

            Directory.CreateDirectory(downloadDirectory);
            var failures = new System.Collections.Generic.List<string>();
            foreach (var c in choices)
            {
                try
                {
                    Console.WriteLine($"正在从 {c.Source} 下载…");
                    var manifest = c.Manifest?.Installer is { } ins
                        ? new UpdateManifest
                        {
                            Version = c.Version, File = ins.File, Sha256 = ins.Sha256, Size = ins.Size,
                        }
                        : null;

                    var result = UpdateService.DownloadAsync(
                            new UpdateService.PackageChoice(c.Asset, manifest, c.Source),
                            downloadDirectory)
                        .GetAwaiter().GetResult();

                    var info = new FileInfo(result.FilePath);
                    Console.WriteLine($"  已下载 : {result.FilePath}");
                    Console.WriteLine($"  大小   : {info.Length} 字节");
                    Console.WriteLine($"  SHA256 : {result.Sha256}");
                    Console.WriteLine($"  校验   : {(result.HashVerified ? "通过（清单哈希一致）" : "未提供哈希，跳过")}");
                    Console.WriteLine("结论：安装包下载链路可用。");
                    return 0;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    failures.Add($"{c.Source}: {UpdateService.DescribeFailure(ex)}");
                    Console.WriteLine($"  {c.Source} 失败：{UpdateService.DescribeFailure(ex)}");
                }
            }

            Console.WriteLine("结论：所有源都失败 —— " + string.Join("；", failures));
            return 1;
        }
        catch (Exception ex)
        {
            Console.WriteLine("自检异常：" + UpdateService.DescribeFailure(ex));
            return 1;
        }
    }
}
