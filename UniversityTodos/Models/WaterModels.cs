using System;
using System.Collections.Generic;

namespace UniversityTodos.Models;

public sealed class WaterBill
{
    public int Sequence { get; set; }
    public string RoomNumber { get; set; } = string.Empty;
    public string WaterType { get; set; } = string.Empty;
    public string StartDate { get; set; } = string.Empty;
    public string EndDate { get; set; } = string.Empty;
    public double Usage { get; set; }
    public double Fee { get; set; }
}

public sealed class WaterCacheFile
{
    public int SchemaVersion { get; set; } = Services.DataCompatibility.CurrentSchemaVersion;
    public List<WaterBill> LatestBills { get; set; } = [];
    public List<WaterBill> HistoryBills { get; set; } = [];
    public DateTimeOffset? SyncedAt { get; set; }
}
