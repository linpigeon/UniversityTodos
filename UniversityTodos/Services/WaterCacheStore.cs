using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using UniversityTodos.Models;

namespace UniversityTodos.Services;

public sealed class WaterCacheStore
{
    private readonly string _path;

    public WaterCacheStore(string? directory = null)
    {
        _path = Path.Combine(directory ?? AppPaths.DataDirectory, "water.json");
    }

    public async Task<WaterCacheFile> LoadAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_path)) return new WaterCacheFile();
        try
        {
            var text = await File.ReadAllTextAsync(_path, ct).ConfigureAwait(false);
            var file = JsonSerializer.Deserialize(text, AppJsonContext.Default.WaterCacheFile)
                ?? new WaterCacheFile();
            DataCompatibility.Observe(file.SchemaVersion, _path);
            file.LatestBills = file.LatestBills
                .Where(b => !string.IsNullOrWhiteSpace(b.WaterType)
                    && double.IsFinite(b.Usage) && double.IsFinite(b.Fee))
                .ToList();
            file.HistoryBills = file.HistoryBills
                .Where(b => !string.IsNullOrWhiteSpace(b.WaterType)
                    && double.IsFinite(b.Usage) && double.IsFinite(b.Fee))
                .ToList();
            return file;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new WaterCacheFile();
        }
    }

    public async Task SaveAsync(WaterCacheFile file, CancellationToken ct = default)
    {
        if (DataCompatibility.IsReadOnly) return;
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var json = JsonSerializer.Serialize(file, AppJsonContext.Default.WaterCacheFile);
        var temp = _path + ".tmp";
        await File.WriteAllTextAsync(temp, json, ct).ConfigureAwait(false);
        File.Move(temp, _path, overwrite: true);
    }
}
