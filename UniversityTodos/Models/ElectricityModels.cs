using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace UniversityTodos.Models;

public sealed class ElectricityRoom
{
    public string RoomName { get; set; } = string.Empty;
    public string RoomVerify { get; set; } = string.Empty;
    public double Odd { get; set; }
    public double Use { get; set; }
    public int Status { get; set; }

    [JsonIgnore]
    public string StatusText => Status == 1 ? "正常" : "异常";
}

public sealed class ElectricityCacheFile
{
    public int SchemaVersion { get; set; } = Services.DataCompatibility.CurrentSchemaVersion;
    public List<ElectricityRoom> Rooms { get; set; } = [];
    public DateTimeOffset? SyncedAt { get; set; }
}

public sealed class ElectricityReminderState
{
    public int SchemaVersion { get; set; } = Services.DataCompatibility.CurrentSchemaVersion;
    public bool BaselineEstablished { get; set; }
    public HashSet<string> LowRoomIds { get; set; } = [];
}
