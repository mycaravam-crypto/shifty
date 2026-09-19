using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace ShiftPlanner.IntegrationTests;

// Full CRUD round trip against the real EmployeesController + Postgres, plus the
// role-gated write checks (ApiWriteRequirement/ManagerWrite) that ShiftPlanner.Tests has no way
// to exercise since that project has no ASP.NET Core hosting/authorization pipeline at all.
[Collection(IntegrationTestCollection.Name)]
public class EmployeeCrudTests(IntegrationTestFixture fixture)
{
    private record EmployeeDto(Guid Id, string PersonnelNumber, string FirstName, string LastName, string? Email, string? PhoneNumber, bool Active, Guid? TeamId);
    private record CreateEmployeeRequest(string PersonnelNumber, string FirstName, string LastName, string? Email, string? PhoneNumber, Guid? TeamId);
    private record UpdateEmployeeRequest(string PersonnelNumber, string FirstName, string LastName, string? Email, string? PhoneNumber, bool Active, Guid? TeamId);

    [Fact]
    public async Task Create_Get_Update_Delete_RoundTrips()
    {
        using var manager = fixture.CreateManagerClient();
        var personnelNumber = $"IT-{Guid.NewGuid():N}"[..12];

        var createResponse = await manager.PostAsJsonAsync("/api/employees",
            new CreateEmployeeRequest(personnelNumber, "Integration", "Tester", "it@example.test", null, null), TestJson.Options);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<EmployeeDto>(TestJson.Options);
        Assert.NotNull(created);
        Assert.Equal(personnelNumber, created!.PersonnelNumber);

        var getResponse = await manager.GetAsync($"/api/employees/{created.Id}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var fetched = await getResponse.Content.ReadFromJsonAsync<EmployeeDto>(TestJson.Options);
        Assert.Equal("Tester", fetched!.LastName);

        var updateResponse = await manager.PutAsJsonAsync($"/api/employees/{created.Id}",
            new UpdateEmployeeRequest(personnelNumber, "Integration", "Updated", "it@example.test", "+49 555 1234", true, null), TestJson.Options);
        Assert.Equal(HttpStatusCode.NoContent, updateResponse.StatusCode);

        var afterUpdate = await (await manager.GetAsync($"/api/employees/{created.Id}")).Content.ReadFromJsonAsync<EmployeeDto>(TestJson.Options);
        Assert.Equal("Updated", afterUpdate!.LastName);
        Assert.Equal("+49 555 1234", afterUpdate.PhoneNumber);

        var deleteResponse = await manager.DeleteAsync($"/api/employees/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var afterDelete = await manager.GetAsync($"/api/employees/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, afterDelete.StatusCode);
    }

    [Fact]
    public async Task Create_WithDuplicatePersonnelNumber_ReturnsConflict()
    {
        using var manager = fixture.CreateManagerClient();
        var personnelNumber = $"IT-{Guid.NewGuid():N}"[..12];
        var request = new CreateEmployeeRequest(personnelNumber, "Dup", "One", null, null, null);

        var first = await manager.PostAsJsonAsync("/api/employees", request, TestJson.Options);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var second = await manager.PostAsJsonAsync("/api/employees",
            request with { FirstName = "Dup2" }, TestJson.Options);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Create_WithoutAuthentication_ReturnsUnauthorized()
    {
        using var anonymous = fixture.CreateClient();

        var response = await anonymous.PostAsJsonAsync("/api/employees",
            new CreateEmployeeRequest($"IT-{Guid.NewGuid():N}"[..12], "No", "Auth", null, null, null), TestJson.Options);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // readme.md §23: EmployeesController writes are ManagerWrite (Admin or Manager) — a
    // correctly-authenticated Staff user with only the Employee role must be forbidden, not
    // just an anonymous caller.
    [Fact]
    public async Task Create_AsEmployeeRole_ReturnsForbidden()
    {
        using var employee = fixture.CreateEmployeeClient();

        var response = await employee.PostAsJsonAsync("/api/employees",
            new CreateEmployeeRequest($"IT-{Guid.NewGuid():N}"[..12], "Wrong", "Role", null, null, null), TestJson.Options);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_AsEmployeeRole_IsAllowed()
    {
        // ApiRead only requires an authenticated Staff/API-key caller, not a specific role —
        // reads are open to every role, only writes are gated.
        using var employee = fixture.CreateEmployeeClient();

        var response = await employee.GetAsync("/api/employees");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // The bug this session's clarity pass fixed: ShiftAssignment.EmployeeId is a Restrict FK
    // (ApplicationDbContext), so deleting an employee who still has a shift used to fail with
    // an opaque foreign-key-violation 500 and no usable message. Now it should 409 up front with
    // a specific, actionable reason, and the employee should still be there afterward.
    [Fact]
    public async Task Delete_WithAssignedShift_ReturnsConflictWithActionableMessage()
    {
        using var admin = fixture.CreateAdminClient();
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var employeeResponse = await admin.PostAsJsonAsync("/api/employees",
            new { PersonnelNumber = $"DEL-{suffix}", FirstName = "Delete", LastName = "Blocked" }, TestJson.Options);
        employeeResponse.EnsureSuccessStatusCode();
        var employee = await employeeResponse.Content.ReadFromJsonAsync<EmployeeDto>(TestJson.Options);

        var shiftTypeResponse = await admin.PostAsJsonAsync("/api/shift-types",
            new { Name = $"DelTest-{suffix}", StartTime = "08:00:00", EndTime = "16:00:00", BreakMinutes = 30, Color = "#4f46e5" },
            TestJson.Options);
        shiftTypeResponse.EnsureSuccessStatusCode();
        var shiftTypeId = (await shiftTypeResponse.Content.ReadFromJsonAsync<ShiftTypeIdDto>(TestJson.Options))!.Id;

        var scheduleResponse = await admin.PostAsJsonAsync("/api/schedules",
            new { Name = $"DelTest {suffix}", StartDate = "2026-09-01", EndDate = "2026-09-30" }, TestJson.Options);
        scheduleResponse.EnsureSuccessStatusCode();
        var scheduleId = (await scheduleResponse.Content.ReadFromJsonAsync<ScheduleIdDto>(TestJson.Options))!.Id;

        var assignmentResponse = await admin.PostAsJsonAsync($"/api/schedules/{scheduleId}/assignments",
            new { EmployeeId = employee!.Id, ShiftTypeId = shiftTypeId, Date = "2026-09-02", StartTime = "08:00:00", EndTime = "16:00:00", BreakMinutes = 30 },
            TestJson.Options);
        assignmentResponse.EnsureSuccessStatusCode();

        var deleteResponse = await admin.DeleteAsync($"/api/employees/{employee.Id}");
        Assert.Equal(HttpStatusCode.Conflict, deleteResponse.StatusCode);
        // ControllerBase.Conflict(string) is written out via StringOutputFormatter as plain
        // text/plain, not JSON — a bare string body, not a quoted JSON string — so this reads
        // it as text rather than ReadFromJsonAsync<string>.
        var message = await deleteResponse.Content.ReadAsStringAsync();
        Assert.Contains("Schicht", message);
        Assert.Contains("Dienstplan", message);

        // The employee must still exist — a blocked delete must not partially apply.
        var stillThere = await admin.GetAsync($"/api/employees/{employee.Id}");
        Assert.Equal(HttpStatusCode.OK, stillThere.StatusCode);
    }

    private record ShiftTypeIdDto(Guid Id);
    private record ScheduleIdDto(Guid Id);
}
