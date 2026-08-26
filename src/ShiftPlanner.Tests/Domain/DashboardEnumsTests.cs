using System.Text.Json;
using ShiftPlanner.Domain.Scheduling;
using Xunit;

namespace ShiftPlanner.Tests.Domain;

// issue #105: PainSeverity/CoverageStatus replaced DashboardController's plain
// "Error"/"Warning"/"Green"/"Yellow"/"Red" strings with real enums, using
// [JsonConverter(typeof(JsonStringEnumConverter))] to keep the exact same wire format the
// frontend already expects. These tests pin both halves of that: the enum values compare
// correctly (the whole point of moving off magic strings), and serialization still produces
// the original string literals, not the ordinal numbers System.Text.Json would emit by default
// (see AbsenceType/PreferenceLevel's ordinal-serialization notes elsewhere in this codebase).
public class DashboardEnumsTests
{
    [Theory]
    [InlineData(PainSeverity.Warning, "\"Warning\"")]
    [InlineData(PainSeverity.Error, "\"Error\"")]
    public void PainSeverity_SerializesAsOriginalStringLiteral(PainSeverity severity, string expectedJson)
    {
        Assert.Equal(expectedJson, JsonSerializer.Serialize(severity));
    }

    [Theory]
    [InlineData(CoverageStatus.Green, "\"Green\"")]
    [InlineData(CoverageStatus.Yellow, "\"Yellow\"")]
    [InlineData(CoverageStatus.Red, "\"Red\"")]
    public void CoverageStatus_SerializesAsOriginalStringLiteral(CoverageStatus status, string expectedJson)
    {
        Assert.Equal(expectedJson, JsonSerializer.Serialize(status));
    }

    [Fact]
    public void PainSeverity_ComparesByValue_NotByStringTypo()
    {
        // The bug this issue closes: a typo'd string literal ("eror" vs "Error") compiled fine
        // and silently never matched. An enum value can't be mistyped past the compiler.
        PainSeverity severity = PainSeverity.Error;
        Assert.True(severity == PainSeverity.Error);
        Assert.False(severity == PainSeverity.Warning);
    }

    [Fact]
    public void CoverageStatus_ComparesByValue_NotByStringTypo()
    {
        CoverageStatus status = CoverageStatus.Yellow;
        Assert.True(status == CoverageStatus.Yellow);
        Assert.False(status == CoverageStatus.Green || status == CoverageStatus.Red);
    }
}
