using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;

namespace UniversityTodos.Services;

public sealed class WaterSessionCookie
{
    public string Name { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string Domain { get; set; } = string.Empty;
    public string Path { get; set; } = "/";
    public bool Secure { get; set; }
    public bool HttpOnly { get; set; }
    public DateTime? ExpiresAt { get; set; }
}

public sealed class WaterSessionFile
{
    public int SchemaVersion { get; set; } = DataCompatibility.CurrentSchemaVersion;
    public List<WaterSessionCookie> Cookies { get; set; } = [];
    public DateTimeOffset SavedAt { get; set; }
}

public sealed class WaterSessionStore
{
    private readonly string _path;

    public WaterSessionStore(string? directory = null)
    {
        _path = Path.Combine(directory ?? AppPaths.DataDirectory, "water-session.json");
    }

    public string Location => _path;

    public async Task<WaterSessionFile?> LoadAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_path) || !CredentialStore.SupportsSecureStorage) return null;
        try
        {
            var text = await File.ReadAllTextAsync(_path, ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            if (!root.TryGetProperty("protected", out var flag) || flag.ValueKind != JsonValueKind.True
                || !root.TryGetProperty("data", out var dataElement))
                return null;
            var base64 = dataElement.GetString();
            if (string.IsNullOrWhiteSpace(base64)) return null;
            var protectedBytes = Convert.FromBase64String(base64);
            var plain = OperatingSystem.IsWindows()
                ? Dpapi.Unprotect(protectedBytes)
                : CredentialStore.UnprotectOverride!(protectedBytes);
            var file = JsonSerializer.Deserialize(
                Encoding.UTF8.GetString(plain), AppJsonContext.Default.WaterSessionFile);
            if (file is null) return null;
            DataCompatibility.Observe(file.SchemaVersion, _path);
            return file.Cookies.Count > 0 ? file : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException
                                   or FormatException or Win32Exception or ExternalException or CryptographicException)
        {
            return null;
        }
    }

    public async Task SaveAsync(WaterSessionFile file, CancellationToken ct = default)
    {
        if (!CredentialStore.SupportsSecureStorage)
            throw new PlatformNotSupportedException("当前平台不支持加密会话保存");

        file.SavedAt = DateTimeOffset.Now;
        var payload = JsonSerializer.Serialize(file, AppJsonContext.Default.WaterSessionFile);
        var plain = Encoding.UTF8.GetBytes(payload);
        var protectedBytes = OperatingSystem.IsWindows()
            ? Dpapi.Protect(plain)
            : CredentialStore.ProtectOverride!(plain);
        var text = JsonSerializer.Serialize(
            new SessionStore.ProtectedEnvelope { Data = Convert.ToBase64String(protectedBytes) },
            AppJsonContext.Default.ProtectedEnvelope);
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temp = _path + ".tmp";
        await File.WriteAllTextAsync(temp, text, ct).ConfigureAwait(false);
        File.Move(temp, _path, overwrite: true);
    }

    public Task ClearAsync(CancellationToken ct = default)
    {
        if (File.Exists(_path)) File.Delete(_path);
        return Task.CompletedTask;
    }
}

public static class WaterSessionService
{
    private static readonly TimeSpan RestoredSessionLifetime = TimeSpan.FromDays(30);

    public static async Task<bool> SaveAsync(NativeWebView webView)
    {
        try
        {
            var manager = webView.TryGetCookieManager();
            if (manager is null) return false;
            var cookies = await manager.GetCookiesAsync().ConfigureAwait(true);
            var items = (cookies ?? [])
                .Where(cookie => IsWaterDomain(cookie.Domain))
                .Select(cookie => new WaterSessionCookie
                {
                    Name = cookie.Name,
                    Value = cookie.Value,
                    Domain = cookie.Domain,
                    Path = string.IsNullOrWhiteSpace(cookie.Path) ? "/" : cookie.Path,
                    Secure = cookie.Secure,
                    HttpOnly = cookie.HttpOnly,
                    ExpiresAt = cookie.Expires == DateTime.MinValue ? null : cookie.Expires.ToUniversalTime(),
                })
                .GroupBy(cookie => (cookie.Name, cookie.Domain, cookie.Path))
                .Select(group => group.Last())
                .ToList();
            if (!items.Any(cookie => MatchesHost("mapp.zcst.edu.cn", cookie.Domain))) return false;
            await new WaterSessionStore().SaveAsync(new WaterSessionFile { Cookies = items }).ConfigureAwait(false);
            JwCaptureService.Log($"统一认证会话已加密保存：{items.Count} 个 Cookie（{string.Join(",", items.Select(cookie => $"{cookie.Name}@{cookie.Domain}{cookie.Path}"))}）");
            return true;
        }
        catch (Exception ex)
        {
            JwCaptureService.Log($"统一认证会话保存失败：{ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    public static async Task<int> RestoreAsync(NativeWebView webView)
    {
        try
        {
            var file = await new WaterSessionStore().LoadAsync().ConfigureAwait(true);
            if (file is null)
            {
                JwCaptureService.Log("统一认证会话文件不存在、为空或无法解密");
                return 0;
            }
            var manager = webView.TryGetCookieManager();
            for (var i = 0; manager is null && i < 20; i++)
            {
                await Task.Delay(100).ConfigureAwait(true);
                manager = webView.TryGetCookieManager();
            }
            if (manager is null)
            {
                JwCaptureService.Log("统一认证 CookieManager 等待后仍不可用");
                return 0;
            }
            var expected = file.Cookies.Where(cookie => IsWaterDomain(cookie.Domain)).ToList();
            foreach (var item in expected) manager.AddOrUpdateCookie(ToCookie(item));
            var current = await manager.GetCookiesAsync().ConfigureAwait(true);
            var restored = expected.Count(item => (current ?? []).Any(cookie =>
                cookie.Name == item.Name && cookie.Value == item.Value
                && string.Equals(cookie.Domain, item.Domain, StringComparison.OrdinalIgnoreCase)
                && cookie.Path == item.Path));
            JwCaptureService.Log($"已恢复统一认证会话：{restored}/{expected.Count} 个 Cookie；当前可读出 {current?.Count ?? 0} 个");
            if (restored != expected.Count)
                JwCaptureService.Log("恢复后核对不符，目标为：" + string.Join(",", expected.Select(cookie => $"{cookie.Name}@{cookie.Domain}{cookie.Path}")));
            return restored;
        }
        catch (Exception ex)
        {
            JwCaptureService.Log($"统一认证会话恢复失败：{ex.GetType().Name}: {ex.Message}");
            return 0;
        }
    }

    internal static bool IsWaterDomain(string cookieDomain)
    {
        var domain = cookieDomain.TrimStart('.');
        return domain.Equals("zcst.edu.cn", StringComparison.OrdinalIgnoreCase)
            || domain.Equals("sos.zcst.edu.cn", StringComparison.OrdinalIgnoreCase)
            || domain.Equals("mapp.zcst.edu.cn", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool MatchesHost(string host, string cookieDomain)
    {
        var domain = cookieDomain.TrimStart('.');
        return host.Equals(domain, StringComparison.OrdinalIgnoreCase)
            || host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase);
    }

    internal static System.Net.Cookie ToCookie(WaterSessionCookie item)
    {
        var expires = item.ExpiresAt is { } value && value > DateTime.UtcNow
            ? value
            : DateTime.UtcNow.Add(RestoredSessionLifetime);
        return new System.Net.Cookie(item.Name, item.Value, item.Path, item.Domain)
        {
            Secure = item.Secure,
            HttpOnly = item.HttpOnly,
            Expires = expires,
        };
    }
}
