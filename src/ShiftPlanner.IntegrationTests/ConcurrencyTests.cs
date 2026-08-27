using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace ShiftPlanner.IntegrationTests;

// issue #75's 4th ask: "two concurrent writers on the same Schedule". issue #156 closed the gap
// this test used to document (both-succeed, last-write-wins, no conflict signal): ShiftAssignment
// now carries a RowVersion (Postgres's own xmin, see ApplicationDbContext), and
// Update/DeleteAssignment 409 instead of silently overwriting when the caller's RowVersion is
// stale. This test now asserts that real behavior instead of documenting the old gap.
[Collection(IntegrationTestCollection.Name)]
public class ConcurrencyTests(IntegrationTestFixture fixture)
{
    private record EmployeeDto(Guid Id);
    private record ShiftTypeDto(Guid Id);
    private record ScheduleDto(Guid Id);
    private record ShiftAssignmentDto(Guid Id, uint RowVersion);

    [Fact]
    public async Task TwoConcurrentUpdates_ToTheSameAssignment_OneSucceeds_OneConflicts()
    {
        using var admin = fixture.CreateAdminClient();
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var employeeResponse = await admin.PostAsJsonAsync("/api/employees",
            new { PersonnelNumber = $"CC-{suffix}", FirstName = "Concurrent", LastName = "Writer" }, TestJson.Options);
        employeeResponse.EnsureSuccessStatusCode();
        var employee = (await employeeResponse.Content.ReadFromJsonAsync<EmployeeDto>(TestJson.Options))!;

        var shiftTypeResponse = await admin.PostAsJsonAsync("/api/shift-types",
            new { Name = $"Konflikt-{suffix}", StartTime = "08:00:00", EndTime = "16:00:00", BreakMinutes = 30, Color = "#f59e0b" }, TestJson.Options);
        shiftTypeResponse.EnsureSuccessStatusCode();
        var shiftType = (await shiftTypeResponse.Content.ReadFromJsonAsync<ShiftTypeDto>(TestJson.Options))!;

        var scheduleResponse = await admin.PostAsJsonAsync("/api/schedules",
            new { Name = $"Konflikt {suffix}", StartDate = "2026-12-01", EndDate = "2026-12-31" }, TestJson.Options);
        scheduleResponse.EnsureSuccessStatusCode();
        var schedule = (await scheduleResponse.Content.ReadFromJsonAsync<ScheduleDto>(TestJson.Options))!;

        var assignmentResponse = await admin.PostAsJsonAsync($"/api/schedules/{schedule.Id}/assignments",
            new { EmployeeId = employee.Id, ShiftTypeId = shiftType.Id, Date = "2026-12-05", StartTime = "08:00:00", EndTime = "16:00:00", BreakMinutes = 30 },
            TestJson.Options);
        assignmentResponse.EnsureSuccessStatusCode();
        var assignment = (await assignmentResponse.Content.ReadFromJsonAsync<ShiftAssignmentDto>(TestJson.Options))!;

        // Two managers who both read the same pre-write row (same RowVersion) moving the same
        // shift to two different time ranges at once.
        var updateA = admin.PutAsJsonAsync($"/api/assignments/{assignment.Id}",
            new { EmployeeId = employee.Id, ShiftTypeId = shiftType.Id, Date = "2026-12-05", StartTime = "09:00:00", EndTime = "17:00:00", BreakMinutes = 30, assignment.RowVersion },
            TestJson.Options);
        var updateB = admin.PutAsJsonAsync($"/api/assignments/{assignment.Id}",
            new { EmployeeId = employee.Id, ShiftTypeId = shiftType.Id, Date = "2026-12-05", StartTime = "10:00:00", EndTime = "18:00:00", BreakMinutes = 30, assignment.RowVersion },
            TestJson.Options);
        var responses = await Task.WhenAll(updateA, updateB);

        // Exactly one writer wins (200), the other loses the race against a RowVersion that's
        // already stale by the time its own SaveChangesAsync runs (409) — not both succeeding
        // (the old last-write-wins gap) and not both failing (would indicate the check is broken
        // in the other direction, rejecting a legitimate first write too).
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.NoContent);
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);

        var finalResponse = await admin.GetAsync($"/api/schedules/{schedule.Id}");
        finalResponse.EnsureSuccessStatusCode();
        var detail = await finalResponse.Content.ReadFromJsonAsync<JsonElement>(TestJson.Options);
        var finalAssignment = detail.GetProperty("assignments").EnumerateArray()
            .First(a => a.GetProperty("id").GetGuid() == assignment.Id);
        var finalStart = TimeOnly.Parse(finalAssignment.GetProperty("startTime").GetString()!);

        // The row reflects exactly the winning writer's full update, not a corrupted mix of both.
        Assert.True(finalStart == new TimeOnly(9, 0) || finalStart == new TimeOnly(10, 0),
            $"Expected the assignment to reflect exactly one full writer's update, got {finalStart}.");
    }

    [Fact]
    public async Task DeleteWithStaleRowVersion_Conflicts_AndDoesNotDelete()
    {
        using var admin = fixture.CreateAdminClient();
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var employeeResponse = await admin.PostAsJsonAsync("/api/employees",
            new { PersonnelNumber = $"CD-{suffix}", FirstName = "Concurrent", LastName = "Deleter" }, TestJson.Options);
        employeeResponse.EnsureSuccessStatusCode();
        var employee = (await employeeResponse.Content.ReadFromJsonAsync<EmployeeDto>(TestJson.Options))!;

        var shiftTypeResponse = await admin.PostAsJsonAsync("/api/shift-types",
            new { Name = $"Konflikt2-{suffix}", StartTime = "08:00:00", EndTime = "16:00:00", BreakMinutes = 30, Color = "#f59e0b" }, TestJson.Options);
        shiftTypeResponse.EnsureSuccessStatusCode();
        var shiftType = (await shiftTypeResponse.Content.ReadFromJsonAsync<ShiftTypeDto>(TestJson.Options))!;

        var scheduleResponse = await admin.PostAsJsonAsync("/api/schedules",
            new { Name = $"Konflikt2 {suffix}", StartDate = "2026-12-01", EndDate = "2026-12-31" }, TestJson.Options);
        scheduleResponse.EnsureSuccessStatusCode();
        var schedule = (await scheduleResponse.Content.ReadFromJsonAsync<ScheduleDto>(TestJson.Options))!;

        var assignmentResponse = await admin.PostAsJsonAsync($"/api/schedules/{schedule.Id}/assignments",
            new { EmployeeId = employee.Id, ShiftTypeId = shiftType.Id, Date = "2026-12-06", StartTime = "08:00:00", EndTime = "16:00:00", BreakMinutes = 30 },
            TestJson.Options);
        assignmentResponse.EnsureSuccessStatusCode();
        var assignment = (await assignmentResponse.Content.ReadFromJsonAsync<ShiftAssignmentDto>(TestJson.Options))!;

        // An actual field change (not a no-op) to bump the row's RowVersion (Postgres's xmin only
        // changes on a real UPDATE — EF Core skips issuing one at all when nothing changed), so
        // the delete below is provably stale.
        var updateResponse = await admin.PutAsJsonAsync($"/api/assignments/{assignment.Id}",
            new { EmployeeId = employee.Id, ShiftTypeId = shiftType.Id, Date = "2026-12-06", StartTime = "08:00:00", EndTime = "16:00:00", BreakMinutes = 45, assignment.RowVersion },
            TestJson.Options);
        updateResponse.EnsureSuccessStatusCode();

        var deleteResponse = await admin.DeleteAsync($"/api/assignments/{assignment.Id}?rowVersion={assignment.RowVersion}");
        Assert.Equal(HttpStatusCode.Conflict, deleteResponse.StatusCode);

        var finalResponse = await admin.GetAsync($"/api/schedules/{schedule.Id}");
        finalResponse.EnsureSuccessStatusCode();
        var detail = await finalResponse.Content.ReadFromJsonAsync<JsonElement>(TestJson.Options);
        Assert.Contains(detail.GetProperty("assignments").EnumerateArray(), a => a.GetProperty("id").GetGuid() == assignment.Id);
    }
}
