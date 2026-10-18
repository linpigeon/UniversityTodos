using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HomeworkReminder.Services;

namespace HomeworkReminder.Android;

/// <summary>
/// Android 平台钩子：Avalonia 的 WebView CookieManager 抽象在 Android 上不可用
/// （TryGetCookieManager 返回 null），改用原生 Android.Webkit.CookieManager——
/// 进程内所有 WebView 共享同一个 Cookie 存储，登录页写进的 sessionid 就在这里。
/// </summary>
internal static class AndroidPlatformHooks
{
    public static void Register()
    {
        YktLoginService.SessionReaderOverride = ReadSession;
        YktLoginService.CookieClearOverride = ClearCookies;

        // 学习通：Avalonia 的 CookieManager 抽象在 Android 不可用，走原生 CookieManager。
        ChaoxingLoginService.CookieReaderOverride = ReadChaoxingCookie;
        ChaoxingLoginService.CookieClearOverride = ClearChaoxingCookies;

        // 批改网：同上。
        PigaiLoginService.CookieReaderOverride = ReadPigaiCookie;
        PigaiLoginService.CookieClearOverride = ClearPigaiCookies;
    }

    /// <summary>读出 pigai.org 域的整条 Cookie（含 HttpOnly；PHPSESSID 必须在）。</summary>
    private static Task<string?> ReadPigaiCookie()
        => Task.FromResult(global::Android.Webkit.CookieManager.Instance?.GetCookie(PigaiUrl));

    private const string YktUrl = "https://changjiang.yuketang.cn";
    private const string ChaoxingUrl = "https://i.chaoxing.com";
    private const string PigaiUrl = "https://www.pigai.org";

    /// <summary>
    /// 原生 CookieManager 没有按域删除：给批改网的会话 cookie 写过期值使其失效
    /// （全清会误伤同库存放的雨课堂/学习通登录态）。
    /// </summary>
    private static Task ClearPigaiCookies()
    {
        var cm = global::Android.Webkit.CookieManager.Instance;
        foreach (var name in new[] { "_JKU", "_JUKU_USER", "PHPSESSID" })
            cm?.SetCookie("https://www.pigai.org", $"{name}=; Expires=Thu, 01 Jan 1970 00:00:00 GMT");
        cm?.Flush();
        return Task.CompletedTask;
    }

    /// <summary>读出 chaoxing.com 域的整条 Cookie（含 HttpOnly）。</summary>
    private static Task<string?> ReadChaoxingCookie()
        => Task.FromResult(global::Android.Webkit.CookieManager.Instance?.GetCookie("https://i.chaoxing.com"));

    /// <summary>
    /// 原生 CookieManager 没有按域删除：给超星常见的会话 cookie 写过期值使其失效
    /// （全清会误伤同库存放的雨课堂登录态）。
    /// </summary>
    private static Task ClearChaoxingCookies()
    {
        var cm = global::Android.Webkit.CookieManager.Instance;
        foreach (var name in new[] { "_uid", "vc3", "uf", "fid", "dsc", "ll", "lv", "_d" })
            cm?.SetCookie("https://i.chaoxing.com", $"{name}=; Expires=Thu, 01 Jan 1970 00:00:00 GMT");
        cm?.Flush();
        return Task.CompletedTask;
    }

    /// <summary>从原生 Cookie 存储读出 sessionid / csrftoken。</summary>
    private static Task<YktSession?> ReadSession()
    {
        var raw = global::Android.Webkit.CookieManager.Instance?.GetCookie(YktUrl);
        YktSession? session = null;
        if (!string.IsNullOrEmpty(raw))
        {
            string? sessionId = null, csrf = null, loginType = null;
            foreach (var part in raw.Split(';'))
            {
                var kv = part.Split('=', 2);
                if (kv.Length != 2) continue;
                switch (kv[0].Trim())
                {
                    case "sessionid": sessionId = kv[1].Trim(); break;
                    case "csrftoken": csrf = kv[1].Trim(); break;
                    case "login_type": loginType = kv[1].Trim(); break;
                }
            }
            if (!string.IsNullOrWhiteSpace(sessionId))
            {
                session = new YktSession
                {
                    SessionId = sessionId,
                    CsrfToken = csrf,
                    LoginType = loginType,
                };
            }
        }
        return Task.FromResult(session);
    }

    /// <summary>
    /// 退出雨课堂：清掉共享 WebView 存储里的雨课堂 Cookie。
    /// <para>
    /// <b>Android 的 CookieManager 没有「按域删除」</b>，只能 <c>RemoveAllCookies</c>。
    /// 而学习通/批改网的登录页与雨课堂共用一个存储——直接全清会把它们也登出
    /// （同文件另外两处特意只让自家 cookie 过期，就是为了不误伤）。
    /// 所以这里先把这两路的整条 cookie 备份下来，清空后再写回。
    /// </para>
    /// <para>
    /// 另外 <c>RemoveAllCookies</c> 是<b>异步</b>的：原先不传回调、立刻返回
    /// <c>(0,0,0,null)</c>，界面永远显示「已清除 0 个」且失败也看不出来。
    /// 现在轮询到真正读不到为止，并如实报告数量。
    /// </para>
    /// </summary>
    private static async Task<YktLoginService.CookieClearResult> ClearCookies()
    {
        var cm = global::Android.Webkit.CookieManager.Instance;
        if (cm is null)
            return new YktLoginService.CookieClearResult(0, 0, 0, "CookieManager 不可用");

        var found = CookieNames(cm.GetCookie(YktUrl)).Count;

        // 备份另两路的 WebView 会话，清空后原样写回。
        var chaoxing = cm.GetCookie(ChaoxingUrl);
        var pigai = cm.GetCookie(PigaiUrl);

        cm.RemoveAllCookies(null);
        cm.Flush();

        // 等异步删除落地（最多约 1 秒），再复核。
        for (var i = 0; i < 20 && CookieNames(cm.GetCookie(YktUrl)).Count > 0; i++)
            await Task.Delay(50);

        RestoreCookies(cm, ChaoxingUrl, chaoxing);
        RestoreCookies(cm, PigaiUrl, pigai);

        var remaining = CookieNames(cm.GetCookie(YktUrl)).Count;
        var error = remaining > 0 ? $"仍有 {remaining} 条雨课堂 Cookie 未清除" : null;
        return new YktLoginService.CookieClearResult(found, remaining, found - remaining, error);
    }

    private static List<string> CookieNames(string? raw) =>
        (raw ?? string.Empty)
            .Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('=', 2)[0].Trim())
            .Where(n => n.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>把整条 Cookie 头按「一条一个 Set-Cookie」写回（CookieManager 不接受整串）。</summary>
    private static void RestoreCookies(global::Android.Webkit.CookieManager cm, string url, string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return;
        foreach (var part in raw.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = part.Trim();
            if (pair.Length == 0 || !pair.Contains('=')) continue;
            cm.SetCookie(url, pair);
        }
        cm.Flush();
    }
}
