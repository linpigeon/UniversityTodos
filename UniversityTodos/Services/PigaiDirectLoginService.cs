using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace UniversityTodos.Services;

public sealed record DirectLoginResult(bool Success, bool RequiresWebView, string Message)
{
    public static DirectLoginResult Ok(string message) => new(true, false, message);
    public static DirectLoginResult Fallback(string message) => new(false, true, message);
    public static DirectLoginResult Error(string message) => new(false, false, message);
}

public static class PigaiDirectLoginService
{
    private const string HomeUrl = "https://www.pigai.org/";
    private const string LoginUrl = "https://www.pigai.org/index.php?a=login";
    private const string ProbeUrl = "https://www.pigai.org/index.php?c=write&a=waitComplete&bf=4";
    private const string PublicKey = "MIGfMA0GCSqGSIb3DQEBAQUAA4GNADCBiQKBgQCYlII16dSwQErDgjIl8BzU4NEL2IzvyWiLNxie3mkpw6eseF/iUVb3bisAFH+lzgnrv/mBOKUMkbqtW2+8en/6r0hj6ctvGT+UOtg4P5LF/jxkbE+cA2fVJK2RaBzeEEbrKOvauVnGkEOvPVl1/NK4NgeN6aSPIK9ECfXcjlEOHwIDAQAB";

    public static async Task<DirectLoginResult> LoginAsync(
        string account, string password, CancellationToken ct = default)
    {
        try
        {
            using var handler = new HttpClientHandler
            {
                CookieContainer = new CookieContainer(),
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            };
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
                "(KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36");
            client.DefaultRequestHeaders.Referrer = new Uri(HomeUrl);

            using var response = await client.PostAsync(
                LoginUrl,
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["username"] = account.Trim(),
                    ["password_encrypt"] = EncryptPassword(password.Trim()),
                    ["remember"] = "1",
                    ["checkhash"] = string.Empty,
                }), ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return DirectLoginResult.Error($"登录请求失败（HTTP {(int)response.StatusCode}）");

            var html = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (html.Contains("请先登录", StringComparison.Ordinal)
                || html.Contains("登录失败", StringComparison.Ordinal)
                || html.Contains("密码错误", StringComparison.Ordinal)
                || html.Contains("账号或密码", StringComparison.Ordinal))
            {
                return DirectLoginResult.Fallback("账密登录未通过，请检查账号密码，或改用网页登录");
            }

            using var probe = await client.GetAsync(ProbeUrl, ct).ConfigureAwait(false);
            var probeHtml = await probe.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (probeHtml.Contains("请先登录", StringComparison.Ordinal))
                return DirectLoginResult.Fallback("账密登录未通过，请检查账号密码，或改用网页登录");

            var cookies = handler.CookieContainer.GetCookies(new Uri(HomeUrl));
            var header = string.Join("; ", cookies.Select(c => $"{c.Name}={c.Value}"));
            var session = PigaiLoginService.BuildSessionFromVerifiedLogin(header);
            if (session is null)
                return DirectLoginResult.Error("登录成功但未能读取会话 Cookie，请改用网页登录");

            await new PigaiSessionStore().SaveAsync(session, ct).ConfigureAwait(false);
            return DirectLoginResult.Ok("静默登录成功，已保存会话");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return DirectLoginResult.Error($"静默登录网络请求失败：{ex.Message}，可改用网页登录");
        }
    }

    private static string EncryptPassword(string password)
    {
        using var rsa = RSA.Create();
        rsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(PublicKey), out _);
        return Convert.ToBase64String(rsa.Encrypt(
            System.Text.Encoding.UTF8.GetBytes(password), RSAEncryptionPadding.Pkcs1));
    }
}
