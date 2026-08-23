using Microsoft.AspNetCore.Identity;

namespace ShiftPlanner.Infrastructure.Persistence;

// Admin/Manager accounts only — Employee self-service login is deferred (readme.md §23).
public class ApplicationUser : IdentityUser
{
}
