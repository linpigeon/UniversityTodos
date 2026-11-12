using System;
using System.Collections.Generic;

namespace UniversityTodos.Models;

public sealed class ExamInfo
{
    public int AcademicYear { get; set; }
    public int Semester { get; set; }
    public string CourseName { get; set; } = string.Empty;
    public string CourseCode { get; set; } = string.Empty;
    public string ExamName { get; set; } = string.Empty;
    public string ExamMethod { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public string Campus { get; set; } = string.Empty;
    public string SeatNumber { get; set; } = string.Empty;
    public string TeachingClass { get; set; } = string.Empty;
    public string College { get; set; } = string.Empty;
    public string RetakeFlag { get; set; } = string.Empty;
    public string Remark { get; set; } = string.Empty;
    public DateTime StartAt { get; set; }
    public DateTime EndAt { get; set; }
}

public sealed class ExamCacheFile
{
    public int SchemaVersion { get; set; } = Services.DataCompatibility.CurrentSchemaVersion;
    public List<ExamInfo> Items { get; set; } = [];
    public int? LastAcademicYear { get; set; }
    public int? LastSemester { get; set; }
    public DateTimeOffset? SyncedAt { get; set; }
}

public sealed record ExamFetchResult(bool Success, string Message, IReadOnlyList<ExamInfo> Items);
