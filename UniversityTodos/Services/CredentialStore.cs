using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using UniversityTodos.Models;

namespace UniversityTodos.Services;

public sealed class CredentialStore
{
    internal sealed class CredentialEnvelope
    {
        [JsonPropertyName("protected")] public bool Protected { get; set; } = true;
        [JsonPropertyName("data")] public string Data { get; set; } = string.Empty;
    }

    public static Func<byte[], byte[]>? ProtectOverride { get; set; }
    public static Func<byte[], byte[]>? UnprotectOverride { get; set; }

    private readonly string _path;

    public CredentialStore(string fileName, string? directory = null)
    {
        if (string.IsNullOrWhiteSpace(fileName) || Path.GetFileName(fileName) != fileName)
            throw new ArgumentException("凭据文件名无效", nameof(fileName));
        _path = Path.Combine(directory ?? AppPaths.DataDirectory, fileName);
    }

    public string Location => _path;

    public static bool SupportsSecureStorage => OperatingSystem.IsWindows()
        || (ProtectOverride is not null && UnprotectOverride is not null);

    public async Task<CredentialFile?> LoadAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_path) || !SupportsSecureStorage) return null;
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
                : UnprotectOverride!(protectedBytes);
            var file = JsonSerializer.Deserialize(
                Encoding.UTF8.GetString(plain), AppJsonContext.Default.CredentialFile);
            if (file is null) return null;
            DataCompatibility.Observe(file.SchemaVersion, _path);
            return string.IsNullOrWhiteSpace(file.Account) || string.IsNullOrEmpty(file.Password)
                ? null
                : file;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public async Task SaveAsync(CredentialFile file, CancellationToken ct = default)
    {
        if (!SupportsSecureStorage)
            throw new PlatformNotSupportedException("当前平台不支持加密凭据保存");

        file.SavedAt = DateTimeOffset.Now;
        var payload = JsonSerializer.Serialize(file, AppJsonContext.Default.CredentialFile);
        var plain = Encoding.UTF8.GetBytes(payload);
        var protectedBytes = OperatingSystem.IsWindows()
            ? Dpapi.Protect(plain)
            : ProtectOverride!(plain);
        var text = JsonSerializer.Serialize(
            new CredentialEnvelope { Data = Convert.ToBase64String(protectedBytes) },
            AppJsonContext.Default.CredentialEnvelope);

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
