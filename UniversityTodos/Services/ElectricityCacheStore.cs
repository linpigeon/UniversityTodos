using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using UniversityTodos.Models;

namespace UniversityTodos.Services;

public sealed class ElectricityCacheStore
{
    private readonly string _path;

    public ElectricityCacheStore(string? directory = null)
    {
        _path = Path.Combine(directory ?? AppPaths.DataDirectory, "electricity.json");
    }

    public string Location => _path;

    public async Task<ElectricityCacheFile> LoadAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_path)) return new ElectricityCacheFile();
        try
        {
            var text = await File.ReadAllTextAsync(_path, ct).ConfigureAwait(false);
            var file = JsonSerializer.Deserialize(text, AppJsonContext.Default.ElectricityCacheFile)
                ?? new ElectricityCacheFile();
            DataCompatibility.Observe(file.SchemaVersion, _path);
            file.Rooms = file.Rooms
                .Where(r => !string.IsNullOrWhiteSpace(r.RoomVerify)
                    && double.IsFinite(r.Odd) && double.IsFinite(r.Use))
                .ToList();
            return file;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new ElectricityCacheFile();
        }
    }

    public async Task SaveAsync(ElectricityCacheFile file, CancellationToken ct = default)
    {
        if (DataCompatibility.IsReadOnly) return;
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var json = JsonSerializer.Serialize(file, AppJsonContext.Default.ElectricityCacheFile);
        var temp = _path + ".tmp";
        await File.WriteAllTextAsync(temp, json, ct).ConfigureAwait(false);
        File.Move(temp, _path, overwrite: true);
    }
}
