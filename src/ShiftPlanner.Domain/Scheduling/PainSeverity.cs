using System.Text.Json.Serialization;

namespace ShiftPlanner.Domain.Scheduling;

// issue #105: DashboardController.PainPointDto.Severity used to be a plain "Error"/"Warning"
// string compared with ==, so a typo in a literal wouldn't be caught by the compiler. The
// [JsonConverter] keeps the wire format exactly as it was — System.Text.Json serializes enums
// as their ordinal number by default (see AbsenceType/PreferenceLevel's ordinal-mapping notes
// in CLAUDE.md), and Program.cs has no global JsonStringEnumConverter configured, so without
// this attribute switching to an enum would silently break the frontend's existing
// severity === 'Error' string comparisons.
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PainSeverity
{
    Warning,
    Error,
}
