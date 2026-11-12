using System;

namespace UniversityTodos.Models;

public sealed class CredentialFile
{
    public int SchemaVersion { get; set; } = Services.DataCompatibility.CurrentSchemaVersion;
    public string Account { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public DateTimeOffset SavedAt { get; set; }
}
