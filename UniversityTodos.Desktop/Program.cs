using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using UniversityTodos.Desktop.SelfChecks;
using UniversityTodos.Services;

namespace UniversityTodos.Desktop;

sealed class Program
{
    private static string LogPath => Path.Combine(AppPaths.DataDirectory, "startup.log");

    private static void ConfigurePortablePaths()
    {
        if (!AppPaths.IsPortable) return;

        try
        {
            Directory.CreateDirectory(AppPaths.WebView2UserDataFolder);
            Environment.SetEnvironmentVariable(
                "WEBVIEW2_USER_DATA_FOLDER", AppPaths.WebView2UserDataFolder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
        }
    }

    [STAThread]
    public static void Main(string[] args)
    {
        ConfigurePortablePaths();
        ViewModels.MainViewModel.PlatformServices = new DesktopPlatformServices();

        if (args.Contains("--jsoncheck"))
            Environment.Exit(JsonSelfTest.RunAsync().GetAwaiter().GetResult());

        var installerCheckIndex = Array.IndexOf(args, "--installercheck");
        if (installerCheckIndex >= 0)
        {
            var downloadDir = args.Length > installerCheckIndex + 1
                && !args[installerCheckIndex + 1].StartsWith("--", StringComparison.Ordinal)
                ? args[installerCheckIndex + 1]
                : null;
            Environment.Exit(InstallerSelfTest.Run(downloadDir));
        }

        var selfCheckIndex = Array.IndexOf(args, "--selfcheck");
        var renderPath = selfCheckIndex >= 0 && args.Length > selfCheckIndex + 1
            ? args[selfCheckIndex + 1]
            : null;

        var widgetCheckIndex = Array.IndexOf(args, "--selfcheck-widget");
        var widgetRenderPath = widgetCheckIndex >= 0 && args.Length > widgetCheckIndex + 1
            ? args[widgetCheckIndex + 1]
            : null;

        var loginCheckIndex = Array.IndexOf(args, "--selfcheck-login");
        var loginRenderPath = loginCheckIndex >= 0 && args.Length > loginCheckIndex + 1
            ? args[loginCheckIndex + 1]
            : null;
        if (loginCheckIndex >= 0) App.ForceLoginPage = true;

        var settingsCheckIndex = Array.IndexOf(args, "--selfcheck-settings");
        var settingsRenderPath = settingsCheckIndex >= 0 && args.Length > settingsCheckIndex + 1
            ? args[settingsCheckIndex + 1]
            : null;

        if (Environment.GetEnvironmentVariable("UNIVERSITYTODOS_FORCE_LOGIN") == "1")
            App.ForceLoginPage = true;

        var loginTestIndex = Array.IndexOf(args, "--logintest");
        var webTestIndex = Array.IndexOf(args, "--webtest");
        var jwCheckIndex = Array.IndexOf(args, "--jwcheck");

        Log($"=== start {DateTimeOffset.Now:O} (selfcheck={selfCheckIndex >= 0}) ===");
        Log($"args       : [{RedactArgs(args)}]");
        Log($"exe        : {Environment.ProcessPath}");
        Log($"data dir   : {AppPaths.DataDirectory}");
        Log($"OS         : {RuntimeInformation.OSDescription}");
        Log($".NET       : {RuntimeInformation.FrameworkDescription}");

        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log("UNHANDLED: " + e.ExceptionObject);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log("UNOBSERVED TASK: " + e.Exception);
            e.SetObserved();
        };

        if (args.Contains("--toastcheck"))
            Environment.Exit(ToastCheck.Run());

        if (args.Contains("--synccheck"))
            Environment.Exit(SourceSyncCheck.Run());

        if (args.Contains("--syncbench"))
            Environment.Exit(SourceSyncBenchmark.RunAsync().GetAwaiter().GetResult());

        if (webTestIndex >= 0)
        {
            var seconds = webTestIndex + 1 < args.Length && int.TryParse(args[webTestIndex + 1], out var s) ? s : 30;
            WebViewCheck.Run(TimeSpan.FromSeconds(seconds));
            return;
        }

        var cookieTestIndex = Array.IndexOf(args, "--cookietest");
        var detailCheckIndex = Array.IndexOf(args, "--detailcheck");
        if (detailCheckIndex >= 0)
        {
            var png = args.Length > detailCheckIndex + 1 && !args[detailCheckIndex + 1].StartsWith('-')
                ? args[detailCheckIndex + 1]
                : null;
            DetailCheck.Run(png);
            return;
        }

        var todoEditorCheckIndex = Array.IndexOf(args, "--todoeditorcheck");
        if (todoEditorCheckIndex >= 0)
        {
            var png = args.Length > todoEditorCheckIndex + 1 && !args[todoEditorCheckIndex + 1].StartsWith('-')
                ? args[todoEditorCheckIndex + 1]
                : null;
            Environment.Exit(TodoEditorCheck.Run(png));
        }

        if (args.Contains("--updatecheck"))
        {
            var targetIdx = Array.IndexOf(args, "--target");
            var targetOverride = targetIdx >= 0 && args.Length > targetIdx + 1 ? args[targetIdx + 1] : null;
            if (args.Contains("--prerelease")) AppSettings.Current.IncludePrereleaseUpdates = true;
            Environment.Exit(UpdateCheck.Run(targetOverride));
        }

        if (args.Contains("--cxcheck"))
            Environment.Exit(ChaoxingCheck.Run());

        var pigaiTestIndex = Array.IndexOf(args, "--pigaitest");
        var pigaiLoginIndex = Array.IndexOf(args, "--pigailogin");

        if (jwCheckIndex >= 0)
        {
            var secs = args.Length > jwCheckIndex + 1 && int.TryParse(args[jwCheckIndex + 1], out var jwSecs)
                ? jwSecs
                : 45;
            JwDialogCheck.Run(TimeSpan.FromSeconds(secs));
            return;
        }
        if (pigaiTestIndex >= 0 || pigaiLoginIndex >= 0)
        {
            if (pigaiTestIndex >= 0)
            {
                if (args.Length < pigaiTestIndex + 2)
                {
                    Console.WriteLine("用法：--pigaitest \"<整条cookie>\" [秒数]");
                    Environment.Exit(2);
                }

                var secs = args.Length > pigaiTestIndex + 2
                           && int.TryParse(args[pigaiTestIndex + 2], out var s1) ? s1 : 120;
                PigaiFlowTest.Run(args[pigaiTestIndex + 1], null, TimeSpan.FromSeconds(secs));
                return;
            }

            if (args.Length < pigaiLoginIndex + 3)
            {
                Console.WriteLine("用法：--pigailogin <账号> <密码> [秒数]");
                Environment.Exit(2);
            }

            var secs2 = args.Length > pigaiLoginIndex + 3
                        && int.TryParse(args[pigaiLoginIndex + 3], out var s2) ? s2 : 150;
            PigaiFlowTest.Run(string.Empty, (args[pigaiLoginIndex + 1], args[pigaiLoginIndex + 2]),
                TimeSpan.FromSeconds(secs2));
            return;
        }

        if (cookieTestIndex >= 0)
        {
            var cs = cookieTestIndex + 1 < args.Length && int.TryParse(args[cookieTestIndex + 1], out var cv) ? cv : 120;
            CookieTest.Run(TimeSpan.FromSeconds(cs));
            return;
        }

        if (loginTestIndex >= 0)
        {
            var seconds = loginTestIndex + 1 < args.Length && int.TryParse(args[loginTestIndex + 1], out var ls) ? ls : 90;
            LoginFlowTest.Run(TimeSpan.FromSeconds(seconds), verifyLogout: args.Contains("--logout"));
            return;
        }

        var androidCheckIndex = Array.IndexOf(args, "--androidcheck");
        if (androidCheckIndex >= 0)
        {
            var outDir = args.Length > androidCheckIndex + 1 && !args[androidCheckIndex + 1].StartsWith('-')
                ? args[androidCheckIndex + 1]
                : Path.Combine(Path.GetTempPath(), "hr-androidcheck");
            Environment.Exit(AndroidLayoutCheck.Run(
                outDir,
                includeWebViewStates: args.Contains("webview"),
                includeDesktopShell: args.Contains("desktop")));
        }

        var ttCheckIndex = Array.IndexOf(args, "--ttcheck");
        if (ttCheckIndex >= 0)
        {
            var png = args.Length > ttCheckIndex + 1 && !args[ttCheckIndex + 1].StartsWith('-')
                ? args[ttCheckIndex + 1]
                : null;
            TimetableSelfCheck.Run(png, useOtherWeeks: args.Contains("otherweeks"));
            return;
        }

        if (widgetCheckIndex >= 0)
        {
            WidgetSelfCheck.Run(widgetRenderPath);
            return;
        }

        if (loginCheckIndex >= 0)
        {
            MainSelfCheck.Run(loginRenderPath);
            return;
        }

        if (settingsCheckIndex >= 0)
        {
            var tab = args.Contains("about") ? 3
                : args.Contains("timetable") ? 2
                : args.Contains("account") ? 1
                : 0;
            SettingsSelfCheck.Run(settingsRenderPath, tab);
            return;
        }

        if (selfCheckIndex >= 0)
        {
            MainSelfCheck.Run(renderPath);
            return;
        }

        ViewModels.MainViewModel.Notifier = OperatingSystem.IsWindows()
            ? new WindowsNotifier()
            : new NullNotifier();

        if (OperatingSystem.IsWindows())
            App.TrayIconHook = TrayIconHost.Attach;

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            Log("=== clean exit ===");
        }
        catch (Exception ex)
        {
            Log("FATAL: " + ex);
            throw;
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    private static void Log(string message)
    {
        try
        {
            File.AppendAllText(LogPath, message + Environment.NewLine, Encoding.UTF8);
        }
        catch (Exception)
        {
        }
    }

    private static string RedactArgs(IReadOnlyList<string> args)
    {
        var values = new List<string>(args.Count);
        for (var i = 0; i < args.Count; i++)
        {
            values.Add(args[i]);

            if (string.Equals(args[i], "--pigailogin", StringComparison.Ordinal))
            {
                for (var k = 0; k < 2 && i + 1 < args.Count; k++)
                {
                    values.Add("***");
                    i++;
                }
            }
            else if (string.Equals(args[i], "--pigaitest", StringComparison.Ordinal)
                     && i + 1 < args.Count)
            {
                values.Add("***");
                i++;
            }
        }
        return string.Join(", ", values);
    }
}
