using ShiftPlanner.Domain.Scheduling;

namespace ShiftPlanner.Domain.Employees;

public class Team
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public bool Active { get; set; } = true;

    // issue #57: which German state this team operates in, for per-Bundesland public
    // holidays (Fronleichnam, Reformationstag, ...) on top of the 9 nationwide ones — null
    // means nationwide-only, matching every Team's behavior before this field existed.
    // Keyed on Team (not Employee) per the issue's own framing ("wherever a team operates"):
    // Employee already carries TeamId, so this is resolved via that relationship.
    public Bundesland? Bundesland { get; set; }
}
