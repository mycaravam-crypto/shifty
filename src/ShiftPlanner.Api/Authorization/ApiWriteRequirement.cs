using Microsoft.AspNetCore.Authorization;
using ShiftPlanner.Domain.Common;

namespace ShiftPlanner.Api.Authorization;

// API-key callers (external programs, readme.md §24) just need ReadWrite scope — roles
// don't apply to them, they're not a Staff role at all. JWT-authenticated staff instead
// need one of allowedRoles (readme.md §23's Admin/Manager write-scope split).
public class ApiWriteRequirement(params string[] allowedRoles) : IAuthorizationRequirement
{
    public string[] AllowedRoles { get; } = allowedRoles;
}

public class ApiWriteRequirementHandler : AuthorizationHandler<ApiWriteRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, ApiWriteRequirement requirement)
    {
        var scope = context.User.FindFirst("ApiKeyScope")?.Value;
        if (scope is not null)
        {
            if (scope == ApiKeyScope.ReadWrite.ToString())
                context.Succeed(requirement);
            return Task.CompletedTask;
        }

        if (requirement.AllowedRoles.Any(context.User.IsInRole))
            context.Succeed(requirement);

        return Task.CompletedTask;
    }
}
