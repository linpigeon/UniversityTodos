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

public sealed class JwSessionCookie
{
    public string Name { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string Domain { get; set; } = string.Empty;
    public string Path { get; set; } = "/";
    public bool Secure { get; set; }
    public bool HttpOnly { get; set; }
    public DateTime? ExpiresAt { get; set; }
}

public sealed class JwSessionFile
{
    public int SchemaVersion { get; set; } = DataCompatibility.CurrentSchemaVersion;
    public List<JwSessionCookie> Cookies { get; set; } = [];
    public DateTimeOffset SavedAt { get; set; }
}

public sealed class JwSessionStore
{
    private readonly string _path;

    public JwSessionStore(string? directory = null)
    {
        _path = Path.Combine(directory ?? AppPaths.DataDirectory, "jw-session.json");
    }

    public string Location => _path;

    public async Task<JwSessionFile?> LoadAsync(CancellationToken ct = default)
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
                Encoding.UTF8.GetString(plain), AppJsonContext.Default.JwSessionFile);
            if (file is null) return null;
            DataCompatibility.Observe(file.SchemaVersion, _path);
            return file.Cookies.Any(cookie => cookie.Name == "JSESSIONID") ? file : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException
                                   or FormatException or Win32Exception or ExternalException or CryptographicException)
        {
            return null;
        }
    }

    public async Task SaveAsync(JwSessionFile file, CancellationToken ct = default)
    {
        if (!CredentialStore.SupportsSecureStorage)
            throw new PlatformNotSupportedException("当前平台不支持加密会话保存");

        file.SavedAt = DateTimeOffset.Now;
        var payload = JsonSerializer.Serialize(file, AppJsonContext.Default.JwSessionFile);
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

public static class JwSessionService
{
    private static readonly TimeSpan RestoredSessionLifetime = TimeSpan.FromDays(30);

    public static async Task<bool> SaveAsync(NativeWebDialog dialog, string loginUrl)
    {
        try
        {
            if (!Uri.TryCreate(loginUrl, UriKind.Absolute, out var baseUri)) return false;
            var manager = dialog.TryGetCookieManager();
            if (manager is null) return false;
            var cookies = await manager.GetCookiesAsync().ConfigureAwait(true);
            var items = (cookies ?? [])
                .Where(cookie => IsSessionCookie(cookie.Name) && MatchesHost(baseUri.Host, cookie.Domain))
                .Select(cookie => new JwSessionCookie
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
            if (!items.Any(cookie => cookie.Name == "JSESSIONID")) return false;
            await new JwSessionStore().SaveAsync(new JwSessionFile { Cookies = items }).ConfigureAwait(false);
            JwCaptureService.Log($"教务会话已加密保存：{items.Count} 个 Cookie");
            return true;
        }
        catch (Exception ex)
        {
            JwCaptureService.Log($"教务会话保存失败：{ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    public static async Task<int> RestoreAsync(NativeWebDialog dialog, string loginUrl)
    {
        try
        {
            if (!Uri.TryCreate(loginUrl, UriKind.Absolute, out var baseUri)) return 0;
            var file = await new JwSessionStore().LoadAsync().ConfigureAwait(true);
            if (file is null) return 0;
            var manager = dialog.TryGetCookieManager();
            if (manager is null) return 0;
            var restored = 0;
            foreach (var item in file.Cookies.Where(cookie =>
                         IsSessionCookie(cookie.Name) && MatchesHost(baseUri.Host, cookie.Domain)))
            {
                manager.AddOrUpdateCookie(ToCookie(item));
                restored++;
            }
            if (restored > 0) JwCaptureService.Log($"已恢复教务会话：{restored} 个 Cookie");
            return restored;
        }
        catch (Exception ex)
        {
            JwCaptureService.Log($"教务会话恢复失败：{ex.GetType().Name}: {ex.Message}");
            return 0;
        }
    }

    internal static bool IsSessionCookie(string name) =>
        name.Equals("JSESSIONID", StringComparison.OrdinalIgnoreCase)
        || name.Equals("route", StringComparison.OrdinalIgnoreCase);

    internal static bool MatchesHost(string host, string cookieDomain)
    {
        var domain = cookieDomain.TrimStart('.');
        return host.Equals(domain, StringComparison.OrdinalIgnoreCase)
            || host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase);
    }

    internal static System.Net.Cookie ToCookie(JwSessionCookie item)
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
