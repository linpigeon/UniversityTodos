using System;
using System.Collections.Generic;

namespace UniversityTodos.Models;

public sealed class GradeInfo
{
    public int AcademicYear { get; set; }
    public int Semester { get; set; }
    public string CourseCode { get; set; } = string.Empty;
    public string CourseName { get; set; } = string.Empty;
    public string CourseNature { get; set; } = string.Empty;
    public double Credits { get; set; }
    public string GradeText { get; set; } = string.Empty;
    public double? PercentScore { get; set; }
    public double GradePoint { get; set; }
    public double CreditPoint { get; set; }
    public string Assessment { get; set; } = string.Empty;
    public string ExamNature { get; set; } = string.Empty;
    public string Teacher { get; set; } = string.Empty;
    public string College { get; set; } = string.Empty;
    public string CollegeId { get; set; } = string.Empty;
    public string MajorId { get; set; } = string.Empty;
    public string AdmissionYear { get; set; } = string.Empty;
    public bool IsVoid { get; set; }
}

public sealed class AcademicStats
{
    public double MinimumGraduationCredits { get; set; }
    public int PlannedCourseCount { get; set; }
    public double EarnedCredits { get; set; }
    public double WeightedGradePoint { get; set; }
    public double AverageScore { get; set; }
}

public sealed class GradeCacheFile
{
    public int SchemaVersion { get; set; } = Services.DataCompatibility.CurrentSchemaVersion;
    public List<GradeInfo> Grades { get; set; } = [];
    public AcademicStats Stats { get; set; } = new();
    public int? LastAcademicYear { get; set; }
    public int? LastSemester { get; set; }
    public DateTimeOffset? SyncedAt { get; set; }
}

public sealed record GradeFetchResult(
    bool Success,
    string Message,
    IReadOnlyList<GradeInfo> Grades,
    AcademicStats Stats);
