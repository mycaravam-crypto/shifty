using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace ShiftPlanner.IntegrationTests;

// The core Schedule -> ShiftAssignment -> ScheduleValidator -> Publish/Archive lifecycle
// (issues #8/#9/#10/#17/#63/#68) through the real HTTP + Postgres pipeline. ShiftPlanner.Tests
// already covers ScheduleValidator's rules in isolation over plain POCOs; this instead exercises
// the whole path a manager actually drives through the API, including the 409 gating that only
// exists at the controller level (issue #68) and so isn't unit-testable there.
[Collection(IntegrationTestCollection.Name)]
public class ScheduleFlowTests(IntegrationTestFixture fixture)
{
    private record EmployeeDto(Guid Id);
    private record ShiftTypeDto(Guid Id);
    private record ScheduleDto(Guid Id, string Name, DateOnly StartDate, DateOnly EndDate, int Status);
    private record ShiftAssignmentDto(Guid Id, Guid ScheduleId);
    private record ValidationIssueDto(string Type, string Message, Guid? EmployeeId, Guid? ShiftAssignmentId);
    private record ValidationResultDto(List<ValidationIssueDto> Errors, List<ValidationIssueDto> Warnings, bool IsValid);

    [Fact]
    public async Task CreateAssignEditPublishArchive_FullLifecycle_Succeeds()
    {
        using var admin = fixture.CreateAdminClient();
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var employee = await CreateAsync<EmployeeDto>(admin, "/api/employees",
            new { PersonnelNumber = $"SF-{suffix}", FirstName = "Flow", LastName = "Test" });

        // A 12h shift (08:00-20:00) needs >=45min break (ArbZG §4, BreakMinutesValidator) —
        // created with BreakMinutes=0 on purpose so the first assignment below is deliberately
        // invalid, then fixed further down.
        var shiftType = await CreateAsync<ShiftTypeDto>(admin, "/api/shift-types",
            new { Name = $"Tag-{suffix}", StartTime = "08:00:00", EndTime = "20:00:00", BreakMinutes = 0, Color = "#4f46e5" });

        var schedule = await CreateAsync<ScheduleDto>(admin, "/api/schedules",
            new { Name = $"Integration {suffix}", StartDate = "2026-09-01", EndDate = "2026-09-30" });
        Assert.Equal(0 /* Draft */, schedule.Status);

        var assignment = await CreateAsync<ShiftAssignmentDto>(admin, $"/api/schedules/{schedule.Id}/assignments",
            new { EmployeeId = employee.Id, ShiftTypeId = shiftType.Id, Date = "2026-09-02", StartTime = "08:00:00", EndTime = "20:00:00", BreakMinutes = 0 });

        var invalidResult = await GetJsonAsync<ValidationResultDto>(admin, $"/api/schedules/{schedule.Id}/validate");
        Assert.False(invalidResult.IsValid);
        Assert.Contains(invalidResult.Errors, e => e.Type == "InsufficientBreak" && e.ShiftAssignmentId == assignment.Id);

        // issue #68: publishing must be blocked while a blocking Error exists, and the 409 body
        // must carry the same ValidationResult /validate already showed.
        var blockedPublish = await admin.PostAsync($"/api/schedules/{schedule.Id}/publish", content: null);
        Assert.Equal(HttpStatusCode.Conflict, blockedPublish.StatusCode);
        var blockedBody = await blockedPublish.Content.ReadFromJsonAsync<ValidationResultDto>(TestJson.Options);
        Assert.Contains(blockedBody!.Errors, e => e.Type == "InsufficientBreak");

        // Fix the break, matching ArbZG's 45-minute minimum for a >9h shift.
        var updateResponse = await admin.PutAsJsonAsync($"/api/assignments/{assignment.Id}",
            new { EmployeeId = employee.Id, ShiftTypeId = shiftType.Id, Date = "2026-09-02", StartTime = "08:00:00", EndTime = "20:00:00", BreakMinutes = 45 },
            TestJson.Options);
        Assert.Equal(HttpStatusCode.NoContent, updateResponse.StatusCode);

        var validResult = await GetJsonAsync<ValidationResultDto>(admin, $"/api/schedules/{schedule.Id}/validate");
        Assert.True(validResult.IsValid);

        var publishResponse = await admin.PostAsync($"/api/schedules/{schedule.Id}/publish", content: null);
        Assert.Equal(HttpStatusCode.OK, publishResponse.StatusCode);
        var published = await publishResponse.Content.ReadFromJsonAsync<ScheduleDto>(TestJson.Options);
        Assert.Equal(1 /* Published */, published!.Status);

        // issue #68: a Published schedule's assignments are frozen.
        var blockedCreate = await admin.PostAsJsonAsync($"/api/schedules/{schedule.Id}/assignments",
            new { EmployeeId = employee.Id, ShiftTypeId = shiftType.Id, Date = "2026-09-03", StartTime = "08:00:00", EndTime = "16:00:00", BreakMinutes = 30 },
            TestJson.Options);
        Assert.Equal(HttpStatusCode.Conflict, blockedCreate.StatusCode);

        var republish = await admin.PostAsync($"/api/schedules/{schedule.Id}/publish", content: null);
        Assert.Equal(HttpStatusCode.Conflict, republish.StatusCode);

        var archiveResponse = await admin.PostAsync($"/api/schedules/{schedule.Id}/archive", content: null);
        Assert.Equal(HttpStatusCode.OK, archiveResponse.StatusCode);
        var archived = await archiveResponse.Content.ReadFromJsonAsync<ScheduleDto>(TestJson.Options);
        Assert.Equal(2 /* Archived */, archived!.Status);
    }

    [Fact]
    public async Task CreateAssignment_OutsideScheduleDateRange_ReturnsBadRequest()
    {
        using var admin = fixture.CreateAdminClient();
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var employee = await CreateAsync<EmployeeDto>(admin, "/api/employees",
            new { PersonnelNumber = $"SF2-{suffix}", FirstName = "Range", LastName = "Test" });
        var shiftType = await CreateAsync<ShiftTypeDto>(admin, "/api/shift-types",
            new { Name = $"Kurz-{suffix}", StartTime = "09:00:00", EndTime = "13:00:00", BreakMinutes = 0, Color = "#22c55e" });
        var schedule = await CreateAsync<ScheduleDto>(admin, "/api/schedules",
            new { Name = $"Range {suffix}", StartDate = "2026-10-01", EndDate = "2026-10-31" });

        var response = await admin.PostAsJsonAsync($"/api/schedules/{schedule.Id}/assignments",
            new { EmployeeId = employee.Id, ShiftTypeId = shiftType.Id, Date = "2026-11-01", StartTime = "09:00:00", EndTime = "13:00:00", BreakMinutes = 0 },
            TestJson.Options);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ScheduleWrites_AsEmployeeRole_ReturnForbidden()
    {
        using var employee = fixture.CreateEmployeeClient();

        var response = await employee.PostAsJsonAsync("/api/schedules",
            new { Name = "Should not be creatable", StartDate = "2026-01-01", EndDate = "2026-01-31" }, TestJson.Options);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private static async Task<T> CreateAsync<T>(HttpClient client, string url, object payload)
    {
        var response = await client.PostAsJsonAsync(url, payload, TestJson.Options);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<T>(TestJson.Options);
        return body!;
    }

    private static async Task<T> GetJsonAsync<T>(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<T>(TestJson.Options);
        return body!;
    }
}
