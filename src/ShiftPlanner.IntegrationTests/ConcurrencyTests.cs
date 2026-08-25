using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace ShiftPlanner.IntegrationTests;

// issue #75's 4th ask: "two concurrent writers on the same Schedule". Optimistic concurrency
// control (a Version/xmin check) does not exist in this codebase — issue #68's own session
// explicitly deferred it, since it would need coordinated frontend changes (every mutation
// would need to carry and check a version token) that nothing in the frontend does today. So
// there is nothing here for a test to catch as a *bug* yet; what follows instead documents the
// CURRENT last-write-wins behavior explicitly, as a known/accepted gap, so a future
// optimistic-concurrency change (tracked separately) has a test that will start failing the day
// this behavior is deliberately replaced with a real conflict check.
[Collection(IntegrationTestCollection.Name)]
public class ConcurrencyTests(IntegrationTestFixture fixture)
{
    private record EmployeeDto(Guid Id);
    private record ShiftTypeDto(Guid Id);
    private record ScheduleDto(Guid Id);
    private record ShiftAssignmentDto(Guid Id);
    private record AssignmentDetail(Guid Id, TimeOnly StartTime, TimeOnly EndTime);

    [Fact]
    public async Task TwoConcurrentUpdates_ToTheSameAssignment_BothSucceed_LastWriteWinsWithNoConflict()
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

        // Two managers moving the same shift to two different time ranges at once — with no
        // version/If-Match check, both requests read the pre-write row, both write, and
        // whichever SaveChangesAsync commits last on the DB simply overwrites the other. Neither
        // request is expected to fail.
        var updateA = admin.PutAsJsonAsync($"/api/assignments/{assignment.Id}",
            new { EmployeeId = employee.Id, ShiftTypeId = shiftType.Id, Date = "2026-12-05", StartTime = "09:00:00", EndTime = "17:00:00", BreakMinutes = 30 },
            TestJson.Options);
        var updateB = admin.PutAsJsonAsync($"/api/assignments/{assignment.Id}",
            new { EmployeeId = employee.Id, ShiftTypeId = shiftType.Id, Date = "2026-12-05", StartTime = "10:00:00", EndTime = "18:00:00", BreakMinutes = 30 },
            TestJson.Options);
        var responses = await Task.WhenAll(updateA, updateB);

        // Documenting the gap: today this is ALWAYS both-succeed. A real optimistic-concurrency
        // fix would make one of these responses a 409/412 instead — if that ever happens, this
        // assertion (not a random flake) is what will tell you issue #97's deferred scope landed
        // and this test needs updating alongside it.
        Assert.All(responses, r => Assert.Equal(HttpStatusCode.NoContent, r.StatusCode));

        var finalResponse = await admin.GetAsync($"/api/schedules/{schedule.Id}");
        finalResponse.EnsureSuccessStatusCode();
        var detail = await finalResponse.Content.ReadFromJsonAsync<JsonElement>(TestJson.Options);
        var finalAssignment = detail.GetProperty("assignments").EnumerateArray()
            .First(a => a.GetProperty("id").GetGuid() == assignment.Id);
        var finalStart = TimeOnly.Parse(finalAssignment.GetProperty("startTime").GetString()!);

        // The row landed on exactly one of the two full payloads — a clean overwrite, not a
        // corrupted mix of both writers' fields (which would indicate something worse than plain
        // last-write-wins, e.g. a partial-update bug).
        Assert.True(finalStart == new TimeOnly(9, 0) || finalStart == new TimeOnly(10, 0),
            $"Expected the assignment to reflect exactly one full writer's update, got {finalStart}.");
    }
}
