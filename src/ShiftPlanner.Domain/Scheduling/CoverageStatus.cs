using System.Text.Json.Serialization;

namespace ShiftPlanner.Domain.Scheduling;

// issue #105: DashboardController.CoverageDayDto.Status used to be a plain "Green"/"Yellow"/
// "Red" string. Same [JsonConverter] reasoning as PainSeverity (see that file) — this keeps
// the exact same string wire format the frontend's statusColors/statusBars lookups already key
// off of, with no global JsonStringEnumConverter configured in Program.cs to fall back on.
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CoverageStatus
{
    Green,
    Yellow,
    Red,
}
