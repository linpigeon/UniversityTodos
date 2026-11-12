using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using UniversityTodos.Models;

namespace UniversityTodos.Services;

public sealed class GradeCacheStore
{
    private readonly string _path;

    public GradeCacheStore(string? directory = null)
    {
        _path = Path.Combine(directory ?? AppPaths.DataDirectory, "grades.json");
    }

    public string Location => _path;

    public async Task<GradeCacheFile> LoadAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_path)) return new GradeCacheFile();
        try
        {
            var text = await File.ReadAllTextAsync(_path, ct).ConfigureAwait(false);
            var file = JsonSerializer.Deserialize(text, AppJsonContext.Default.GradeCacheFile)
                ?? new GradeCacheFile();
            DataCompatibility.Observe(file.SchemaVersion, _path);
            file.Grades = file.Grades.Where(item =>
                item.AcademicYear is >= 2000 and <= 2100
                && item.Semester is >= 1 and <= 3
                && !string.IsNullOrWhiteSpace(item.CourseName)
                && double.IsFinite(item.Credits)
                && double.IsFinite(item.GradePoint)
                && double.IsFinite(item.CreditPoint)).ToList();
            return file;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new GradeCacheFile();
        }
    }

    public async Task SaveAsync(GradeCacheFile file, CancellationToken ct = default)
    {
        if (DataCompatibility.IsReadOnly) return;
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var json = JsonSerializer.Serialize(file, AppJsonContext.Default.GradeCacheFile);
        var temp = _path + ".tmp";
        await File.WriteAllTextAsync(temp, json, ct).ConfigureAwait(false);
        File.Move(temp, _path, overwrite: true);
    }
}
