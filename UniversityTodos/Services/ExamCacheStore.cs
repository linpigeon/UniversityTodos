using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using UniversityTodos.Models;

namespace UniversityTodos.Services;

public sealed class ExamCacheStore
{
    private readonly string _path;

    public ExamCacheStore(string? directory = null)
    {
        _path = Path.Combine(directory ?? AppPaths.DataDirectory, "exams.json");
    }

    public string Location => _path;

    public async Task<ExamCacheFile> LoadAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_path)) return new ExamCacheFile();
        try
        {
            var text = await File.ReadAllTextAsync(_path, ct).ConfigureAwait(false);
            var file = JsonSerializer.Deserialize(text, AppJsonContext.Default.ExamCacheFile)
                ?? new ExamCacheFile();
            DataCompatibility.Observe(file.SchemaVersion, _path);
            file.Items = file.Items.Where(item =>
                item.AcademicYear is >= 2000 and <= 2100
                && item.Semester is >= 1 and <= 3
                && !string.IsNullOrWhiteSpace(item.CourseName)
                && item.StartAt < item.EndAt).ToList();
            return file;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new ExamCacheFile();
        }
    }

    public async Task SaveAsync(ExamCacheFile file, CancellationToken ct = default)
    {
        if (DataCompatibility.IsReadOnly) return;
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var json = JsonSerializer.Serialize(file, AppJsonContext.Default.ExamCacheFile);
        var temp = _path + ".tmp";
        await File.WriteAllTextAsync(temp, json, ct).ConfigureAwait(false);
        File.Move(temp, _path, overwrite: true);
    }
}
