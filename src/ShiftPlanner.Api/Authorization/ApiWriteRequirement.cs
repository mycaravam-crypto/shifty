using Microsoft.AspNetCore.Authorization;
using ShiftPlanner.Domain.Common;

namespace ShiftPlanner.Api.Authorization;

// JWT-authenticated staff (no ApiKeyScope claim) may always write.
// API-key callers need ReadWrite scope (readme.md §24).
public class ApiWriteRequirement : IAuthorizationRequirement;

public class ApiWriteRequirementHandler : AuthorizationHandler<ApiWriteRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, ApiWriteRequirement requirement)
    {
        var scope = context.User.FindFirst("ApiKeyScope")?.Value;
        if (scope is null || scope == ApiKeyScope.ReadWrite.ToString())
            context.Succeed(requirement);

        return Task.CompletedTask;
    }
}
