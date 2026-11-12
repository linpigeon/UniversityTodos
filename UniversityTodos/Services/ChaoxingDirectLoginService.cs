using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace UniversityTodos.Services;

public static class ChaoxingDirectLoginService
{
    private const string LoginUrl = "https://passport2.chaoxing.com/fanyalogin";
    private const string Referer = "http://i.chaoxing.com";

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
            client.DefaultRequestHeaders.Referrer = new Uri(Referer);

            using var response = await client.PostAsync(
                LoginUrl,
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["fid"] = "-1",
                    ["uname"] = account.Trim(),
                    ["password"] = password,
                    ["refer"] = Referer,
                }), ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return DirectLoginResult.Error($"登录请求失败（HTTP {(int)response.StatusCode}）");

            var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var success = root.TryGetProperty("status", out var status)
                && status.ValueKind == JsonValueKind.True;
            if (!success)
            {
                var message = root.TryGetProperty("msg2", out var msg)
                    ? msg.GetString() ?? "账密登录未通过"
                    : "账密登录未通过";
                return DirectLoginResult.Fallback($"{message}，可改用网页登录");
            }

            var cookies = handler.CookieContainer.GetCookies(new Uri("https://passport2.chaoxing.com"));
            var uid = cookies["_uid"]?.Value;
            if (string.IsNullOrWhiteSpace(uid))
                return DirectLoginResult.Error("登录成功但未收到 _uid 会话，请改用网页登录");

            var allCookies = new List<Cookie>();
            foreach (var uri in new[]
            {
                new Uri("https://passport2.chaoxing.com"),
                new Uri("https://i.chaoxing.com"),
                new Uri("https://mooc1.chaoxing.com"),
            })
            {
                foreach (Cookie cookie in handler.CookieContainer.GetCookies(uri)) allCookies.Add(cookie);
            }
            var header = string.Join("; ", allCookies
                .GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .Select(g => $"{g.First().Name}={g.First().Value}"));
            var session = new ChaoxingSessionFile
            {
                CookieHeader = header,
                Uid = uid,
                SavedAt = DateTimeOffset.Now,
            };
            await new ChaoxingSessionStore().SaveAsync(session, ct).ConfigureAwait(false);
            return DirectLoginResult.Ok("静默登录成功，已保存会话");
        }
        catch (JsonException)
        {
            return DirectLoginResult.Fallback("学习通返回了验证页面，请改用网页登录完成验证");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return DirectLoginResult.Error($"静默登录网络请求失败：{ex.Message}，可改用网页登录");
        }
    }
}
