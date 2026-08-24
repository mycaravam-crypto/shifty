# Schichtplaner

Full concept (German, source of truth for architecture/data model/decisions): [readme.md](readme.md).
§23–26 cover security/login, external API keys, Docker+Postgres, and deployment.

Ongoing/planned work lives in [GitHub Issues](https://github.com/mycaravam-crypto/shifty/issues),
not here and not in readme.md — readme.md stays a stable concept doc, this file stays a factual
snapshot of what exists right now.

## Current state

readme.md §22 "Phase 1: Foundation" is done (Employee, Team, Contract, ShiftType, backend +
basic frontend). "Phase 2: Planung" (Schedule, ShiftAssignment, Wochenansicht, Drag & Drop,
Stundenberechnung) is also now built, backend + frontend. Phase 3 (Validierung) is now fully
built, including the two rules needing cross-assignment history (Ruhezeit, max-consecutive-days
— issues #8/#9, see below). Phase 4 (Usability: month-copy/filters/search/shortcuts/optimized
drag-and-drop) is now fully built too, frontend-only. Phase 5 (Erweiterungen) has also
just started — hourly wage rates (issue #14), touch/mobile drag-and-drop for the
Wochenansicht (issue #19), absence tracking (issue #17), the overtime ledger built on top of
it (issue #18), a public holiday calendar (issue #15), and shift-type wage surcharges
(issue #16) — see below. All Phase 5 issues filed so far are now built; issue #16 was the
last one open. An operational dashboard (issue #27) is now also underway, scoped into three
sub-issues per the parent issue's own suggestion: the backend read-model endpoint (issue #29),
the frontend view (issue #30), and its Action Required feed (issue #31) are all now built —
issue #27 is fully closed out. Two structural gaps were then closed: there was no automated
test coverage anywhere (readme.md §19 named a `ShiftPlanner.Tests` project that never got
built) and no CI gate before `deploy.yml`'s push-to-`main` (issues #50/#51, see below). A
backlog of eight frontend UX-polish issues (#36–#43) had also accumulated with no toast/
confirm-dialog system, loading skeletons, filter persistence, a keyboard-shortcuts hint, or
mobile-responsive tables on the two main list views — all closed now, including #43 (a real
`SettingsView`, see below), which was the last one open. Two more features landed with no
issue filed for either (same as the earlier PDF export): `Employee.PhoneNumber` rounds out
contact info alongside the pre-existing (and already-editable) Email field, and a first cut of
readme.md §17's "später können hier Arbeitszeitpräferenzen ergänzt werden" — per-employee
shift-type/weekday preferences plus a ShiftSuggestionEngine that ranks candidates for an open
(date, ShiftType) slot, surfaced as a "Vorschlagen" action in the Wochenansicht. See below.
What's built:

- **Backend** (`src/`): 4-project skeleton (Domain → Application → Infrastructure → Api)
  matching readme.md §19/§20. Builds clean (`dotnet build ShiftPlanner.sln`, verified via
  the .NET 10 SDK — **no dotnet SDK installed on this machine**, use the Docker container
  shown below). Program.cs wires Postgres/EF Core, ASP.NET Identity, JWT + API-key auth
  schemes, rate limiting, CORS, Swagger, `/health`, and `--migrate`.
  - Domain entities (`Domain/Employees/Team.cs`, `Employee.cs`, `Domain/Contracts/Contract.cs`,
    `Domain/Scheduling/ShiftType.cs`) plus the pre-existing `ApiKey`/`AuditLog` support types.
  - `Infrastructure/Persistence/Migrations/Phase1Foundation` — first migration, covers Identity's
    tables too since nothing had been applied before. Unique constraints per readme.md §11
    (`Employee.PersonnelNumber`, `Team.Name`, `ShiftType.Name`, `Contract.EmployeeId+ValidFrom`)
    and FKs (`Employee.TeamId` restrict, `Contract.EmployeeId` cascade) are in the DbContext's
    `OnModelCreating`, not just app-level checks. Verified end-to-end against a real local
    Postgres via `docker compose` — both migrations apply cleanly, CRUD + the eligibility
    endpoint work, unique-constraint conflicts return 409, and `ApiRead`/`ApiWrite` scope
    checks behave (401 unauthenticated, 403 for a ReadOnly key on write)
    ([issue #1](https://github.com/mycaravam-crypto/shifty/issues/1)).
  - `Api/Controllers/`: `EmployeesController` (full CRUD), `TeamsController` (GET/POST — matches
    §18's minimal cut exactly, no PUT/DELETE), `ShiftTypesController` (GET/POST/PUT, no DELETE,
    matching §18), `ContractsController` (full CRUD, nested under
    `/api/employees/{id}/contracts` for list/create — §18 doesn't spell this one out but Contract
    is a named Phase 1 entity so it needs *some* API surface). Plain CRUD, no
    Application-layer services yet — there's no business logic to put there until Phase 3
    validation exists, so controllers talk to `ApplicationDbContext` directly.
  - `Api/Authorization/ApiWriteRequirement.cs` + policies in Program.cs (`ApiRead`,
    `AdminWrite`, `ManagerWrite`): both JWT and `X-Api-Key` can hit these endpoints per
    readme.md §24 — an API key just needs `Scope: ReadWrite`; a JWT/Staff user additionally
    needs the right role per §23's split (`AdminWrite` on `TeamsController`/
    `ShiftTypesController` — Stammdaten; `ManagerWrite` on `EmployeesController`/
    `ContractsController` — Mitarbeiter; Admin can do both, roles aren't hierarchical data
    but Admin is still "manages everything"). `Api/Controllers/AuthController.cs` +
    `Api/Authentication/JwtTokenFactory.cs` add the login/refresh JWT flow itself — didn't
    exist before, so `AdminWrite`/`ManagerWrite` would've been untestable dead code
    otherwise. Three roles (Admin/Manager/Employee) are seeded by `--migrate`; since there's
    no Benutzer-management endpoint yet, `--seed-user` (env vars `SeedUser:Email/Password/Role`)
    bootstraps the first Staff accounts. Refresh tokens are self-contained JWTs (`token_use`
    claim distinguishes them from access tokens), not DB-backed — no server-side revocation
    yet (issue #3's original ask; login/refresh + seeding were an unplanned but necessary
    add-on — [issue #3](https://github.com/mycaravam-crypto/shifty/issues/3)). Verified
    end-to-end (login, role-gated writes, refresh, tampered/wrong-purpose-token rejection)
    against a real local Postgres.
  - `Employee.EligibleShiftTypes` (EF many-to-many, join table `EmployeeShiftType`) models
    "mögliche Schichten" (readme.md §3) — GET/PUT `/api/employees/{id}/eligible-shift-types`,
    enforced by `EligibilityValidator` (below) — closes
    [issue #6](https://github.com/mycaravam-crypto/shifty/issues/6).
  - `Domain/Scheduling/Schedule.cs` + `ShiftAssignment.cs` + `WorkingTimeCalculator.cs` (Phase
    2, readme.md §6/§7/§14) — a `Schedule` (Name/StartDate/EndDate/Status: Draft/Published/
    Archived) owns `ShiftAssignment`s (EmployeeId/ShiftTypeId/Date/StartTime/EndTime/
    BreakMinutes; no `Status` field — readme.md never defines what values it'd take, and
    nothing needs one yet). `WorkingTimeCalculator.NetHours` is a static Domain method (no
    Application-layer services exist yet, same as everywhere else) — single source of truth
    for `End − Start − BreakMinutes`, called from the controller so the frontend never
    re-derives the arithmetic. `SchedulesController`: `GET/POST /api/schedules`,
    `GET/PUT /api/schedules/{id}` (nests assignments with `netHours` precomputed; `PUT` is
    also how `Status` transitions — no dedicated `/publish` action),
    `POST /api/schedules/{id}/assignments`, `PUT/DELETE /api/assignments/{id}`, and now
    `GET /api/schedules/{id}/validate`. No unique/overlap DB constraint on assignments —
    `ShiftOverlapValidator` (below) flags it as a Warning at read time instead, matching the
    readme's own §13 example where overlap is a Warning, not a hard Error. `ManagerWrite` on
    writes, matching Program.cs's existing "Manager covers Planung/Mitarbeiter" comment.
    Verified end-to-end against a real local Postgres: create schedule → add assignment →
    `netHours` computed correctly, move via `PUT`, dangling-FK `BadRequest`s, 404s,
    unauthenticated 401.
  - **Phase 3 "Validierung"** (readme.md §12/§13): a new `ShiftPlanner.Application`
    layer — `PlanningDomain`'s actual first tenant, no controller talks to the DB for this.
    `Application/Validation/ValidationResult.cs` mirrors the readme's exact shape
    (`IsValid`/`Errors[]`/`Warnings[]`, not a bool). `ScheduleValidator.Validate(...)` runs
    seven rules (each its own static class, matching the readme's `PlanningDomain` diagram
    naming where it named one): `ShiftOverlapValidator` (Warning), `ContractValidator`
    (planned hours vs. the Contract active at the schedule's start, Error — the only two
    outcomes the readme's own JSON example spells out; scales `Contract.WeeklyHours` by the
    Schedule's actual span in days/7 rather than assuming a 7-day Schedule, since Schedules
    are month-long in practice — see the Wochenansicht note below), `EligibilityValidator` (issue #6,
    Error — an employee with an empty `EligibleShiftTypes` list is treated as unrestricted,
    since that's every employee's default today), `BreakMinutesValidator` (issue #10, ArbZG
    §4 minimums — 30min over 6h worked, 45min over 9h — Error), `StaffingValidator` (issue
    #7, needs the new nullable `ShiftType.MinStaffing`/`MaxStaffing` fields + migration
    `ShiftTypeStaffing`; only checks (ShiftType, Date) pairs that actually have an
    assignment — a day nobody scheduled a shift for isn't flagged; both directions are
    Warnings, it's a target, not a legal minimum), `RestTimeValidator` (issue #8, ArbZG §5 —
    11h min rest between an employee's shifts, Error), `ConsecutiveDaysValidator` (issue #9,
    ArbZG — max 6 consecutive workdays before a rest day is required, Error). The last two
    need history beyond the Schedule being validated (an employee's shift the evening before
    the visible week starts, say), so `SchedulesController`'s `validate` endpoint fetches a
    second, wider `historyAssignments` list (the employees' assignments across every Schedule
    within ±6 days of the one being validated, not just this Schedule's own) and passes it
    into `ScheduleValidator` alongside the normal schedule-scoped `assignments` — the other
    five rules still only see the latter. Cross-midnight shifts (`EndTime < StartTime`) are
    rejected outright — not modeled/supported in v1, closing
    [issue #11](https://github.com/mycaravam-crypto/shifty/issues/11): `ShiftTypesController`
    and `SchedulesController`'s `Create`/`UpdateAssignment` return 400 when `EndTime <=
    StartTime`, so the validators (`RestTimeValidator`'s own `EndTime > StartTime` filter is
    now just defense-in-depth for any pre-existing rows) never see negative-duration data.
    `SchedulesController`
    loads the needed data (assignments, employees with `EligibleShiftTypes` included, shift
    types, contracts) and calls the static validator — still no DI/service layer, matching
    every other controller. Verified end-to-end against a real local Postgres: each rule
    triggered individually via the API (staffing, eligibility, break minutes, overlap,
    contract hours, rest time, consecutive days — the last two via two adjacent Schedules for
    the same employee) and confirmed in the `ValidationResult` JSON; 401/404 on the new
    endpoint. Closes issues #6/#7/#8/#9/#10.
  - `AuditLog` (readme.md §23) previously existed only as an entity + `DbSet` — nothing ever
    wrote to it. Now wired up for real, and extended with `OldValues`/`NewValues` JSON
    snapshot columns (migration `AuditLogValueSnapshots`), closing
    [issue #12](https://github.com/mycaravam-crypto/shifty/issues/12) properly rather than
    just adding columns nothing populates. `ApplicationDbContext.SaveChangesAsync` auto-logs
    every Create/Update/Delete on the six write-controller entities (Team, Employee, Contract,
    ShiftType, Schedule, ShiftAssignment) by walking the EF Core change tracker — no
    per-controller code needed, matching the "no service layer" pattern everywhere else.
    Actor resolution: JWT `NameIdentifier` (the Staff user id) → API key `Name` → `"system"`
    (CLI-driven writes like `--seed-user`, which run outside the HTTP pipeline). `OldValues`
    is a full property snapshot for Delete, `NewValues` likewise for Create; for Update both
    are diffs of only the actually-changed properties. Requires `IHttpContextAccessor` in
    Infrastructure (not an `Sdk.Web` project, so added via an explicit `FrameworkReference` to
    `Microsoft.AspNetCore.App`) to resolve the current actor. The migration file was
    hand-written (not generated via `dotnet ef migrations add`) since no dotnet SDK is
    installed on this machine — `dotnet build ShiftPlanner.sln` (via the Docker SDK container)
    now passes clean, `dotnet ef migrations list` recognizes it in the right order, and
    `dotnet ef migrations script` (no live DB needed) generates exactly the expected
    `ALTER TABLE "AuditLogs" ADD "NewValues"/"OldValues" text;`. **Not runtime-verified against
    a live Postgres** — this session's Docker daemon could pull `mcr.microsoft.com` images
    (used for the build/script checks above) but Docker Hub pulls (`postgres:16-alpine`) were
    blocked by the sandbox's egress policy, so the actual `SaveChangesAsync` audit-writing
    behavior hasn't been exercised end-to-end yet the way earlier phases were.
  - `Contract.HourlyRate` (nullable decimal, issue #14) — Phase 5's first cut, scoped as labor
    *cost estimation*, not payroll processing (readme.md §21 excludes actual Payroll): no
    tax/social-security calc, no payslips. `Domain/Scheduling/WageCalculator.cs` is the single
    source of truth for `NetHours × HourlyRate` (`null` when the rate is unset), mirroring how
    `WorkingTimeCalculator` owns the hours math. `SchedulesController` resolves the *contract
    active on the assignment's own date* (not the schedule's start — a schedule can span a
    month, long enough for a mid-month rate change) and returns `LaborCost` per assignment in
    `ShiftAssignmentDto`; per-employee/schedule-wide totals are summed client-side from that,
    same pattern as the existing hour totals. `dotnet build` clean, and the migration
    (`ContractHourlyRate`) was this time generated via a real `dotnet ef migrations add` (Docker
    was reachable this session) rather than hand-written — `dotnet ef migrations script`
    confirms the exact expected `ALTER TABLE "Contracts" ADD "HourlyRate" numeric;`. Same
    live-Postgres caveat as AuditLog above: Docker Hub pulls are blocked in this sandbox, so
    `SaveChangesAsync`/the API surface hasn't been exercised against a real database — only
    build + migration-script level.
  - **Wage surcharges** (issue #16) — night/Sunday/holiday premiums on top of `HourlyRate ×
    NetHours`. Per the issue's own framing, rates are driven by *when* a shift falls, not
    *what kind* of shift it is, so this is a second `WageCalculator.LaborCost` overload taking
    the raw `StartTime`/`EndTime`/`DayOfWeek`/`isHoliday` rather than a new `ShiftType` field
    or a settings entity — three global percentage constants (night 25%, Sunday 50%, holiday
    125%, common German Tarifvertrag baseline figures — not legally mandated beyond the
    tax-free-allowance thresholds, so these are a starting point, not a compliance claim),
    night window 20:00–06:00. Night stacks additively with Sunday or holiday (works a
    different axis — time-of-day vs. day-type); Sunday and holiday don't stack with each
    other, holiday wins when a holiday lands on a Sunday. Night hours are the raw shift-time
    overlap with the night window, not break-adjusted — `BreakMinutes` has no specific time
    slot to subtract from, so a large break inside a shift's night-window overlap will
    over-count slightly (`ponytail:` comment in the code names this). `isHoliday` comes from
    the existing `GermanPublicHolidays` (issue #15) — `SchedulesController` builds one
    `HashSet<DateOnly>` per schedule load and reuses it across all assignments; `CreateAssignment`
    checks the single date directly. No frontend change needed — the Wochenansicht's existing
    "Lohnkosten" readouts already sum whatever `laborCost` the backend sends. Verified against
    a real local Postgres (this machine's existing `docker compose` stack, rebuilt with
    `docker compose build api`): `dotnet build` clean, then all four cases round-tripped via
    curl on both the create-assignment and schedule-detail endpoints — a plain Tuesday shift
    (no surcharge), an 18:00–22:00 shift (2h night overlap → correct partial surcharge), a
    Sunday shift (+50%), and 1. Weihnachtstag (+125%) — each matched hand-computed expected
    `laborCost` exactly.
  - **Absence tracking** (issue #17, readme.md §8) — the `Absence` entity readme.md always
    defined but nothing had ever built: `Domain/Employees/Absence.cs` (EmployeeId/From/To/
    `AbsenceType` enum Vacation/Sick/Training/Other/Comment), same "doesn't live on Employee
    directly" shape as Contract, cascade-deletes with the Employee. `AbsencesController` is a
    line-for-line mirror of `ContractsController` (`GET`/`POST /employees/{id}/absences`,
    `GET`/`PUT`/`DELETE /absences/{id}`, `ManagerWrite` — Absence is employee data, same policy
    split). A new `AbsenceValidator` (readme.md §8's own framing, "Darf dieser Mitarbeiter an
    diesem Zeitpunkt eingeplant werden?") flags a `ShiftAssignment` falling inside an
    employee's Absence range as an Error; `SchedulesController`'s `validate` endpoint now also
    fetches Absences overlapping the Schedule's span (no lookback needed, unlike the rest-time/
    consecutive-days history window) and passes them into `ScheduleValidator`.
    `ContractValidator` also now takes the same Absence list and subtracts absence days
    overlapping the Schedule's span from the day-count it scales `Contract.WeeklyHours` by
    (`OverlapDays` helper), so a week of vacation doesn't get counted as under- or (once made
    up elsewhere) over-planned hours — the Wochenansicht's own "Xh / Yh" target-hours
    calculation was given the equivalent client-side treatment (`overlapDays` in
    `ScheduleView.vue`, mirroring the backend helper) for the same reason. This session's
    sandbox could reach both `mcr.microsoft.com` (SDK image) and, for the first time, `nuget.org`
    itself — Docker Hub's CDN is still blocked (`api.nuget.org`/`mcr.microsoft.com` aren't behind
    it, `docker.io`'s cloudfront-backed blob storage is), but installing the proxy's CA into the
    SDK container's trust store and running with `--network host` got `dotnet ef migrations add`
    working directly instead of hand-writing the migration. It also turned out this machine
    already has a local PostgreSQL 16 install (previous sessions hadn't found/used it, only ever
    tried Docker Hub's `postgres` image) — starting that directly gave a real database without
    Docker Hub at all. Verified properly end-to-end this time, not just build/script level:
    `dotnet build`/`dotnet ef migrations add` clean, migrated + seeded against the local
    Postgres, then exercised via curl — absence CRUD (including the `To < From` 400 and 401/404
    cases), an assignment placed during an Absence correctly triggers `AssignedDuringAbsence`
    on `/validate` (and a same-employee assignment outside the Absence range correctly doesn't),
    and a contract/absence combination chosen so the *unscaled* day-count would pass but the
    *absence-scaled* one correctly trips `ContractHoursExceeded` (confirms the day-subtraction
    logic is actually being applied, not just present in the code). Frontend: `vue-tsc -b` and
    `vite build` clean, and — Playwright's browser install issue from earlier sessions no longer
    reproduces on this machine — actually clicked through in real headless Chromium against the
    live local stack above: login, create-employee, open `EmployeeDetailModal`, add an Absence
    (row appears with the right German type label), and the Dienstplan/Wochenansicht loading
    with no console errors with an employee that has both a Contract and an Absence (exercises
    `targetHoursFor`'s new absence-day subtraction without throwing). Delete was verified at the
    API level (curl) rather than through the browser, since the second Playwright pass reused
    state from the first and produced a duplicate row that made the delete-button locator
    ambiguous — a test-script artifact, not a product issue.
  - **Dashboard read model** (issue #29, sub-issue of #27) — `GET /api/dashboard?from=&to=
    &teamId=&shiftTypeId=` (new `DashboardController`, mirrors `SchedulesController`'s pattern:
    DTO records at file scope, static aggregation helpers, `ApiRead` policy, no service layer).
    `from`/`to` default server-side to the current week (Mon–Sun) when omitted. Every number is
    derived from existing calculators/validators, not re-derived: `ScheduleValidator` run once
    per Schedule overlapping the period is the source for the flat `PainPoints` list (also feeds
    `PlanningStatus.ConflictCount`/`AffectedSchedules` — a schedule "conflicts" if it has Errors);
    `StaffingValidator`'s `(ShiftTypeId, Date)` grouping is reused (new code) to turn its
    pass/fail Warnings into a `Coverage` percentage/Green-Yellow-Red list (95%/85% thresholds,
    computed server-side so there's one source of truth); `WorkingTimeCalculator`/
    `WageCalculator`/`GermanPublicHolidays` back the cost and hours sums exactly as
    `SchedulesController` already uses them; `ContractValidator`'s `WeeklyHours × day-span −
    absence-days` formula is reused for both `Utilization.ContractCapacityHours` and the
    `OvertimeHours` KPI (`Max(0, actual − expected)` summed, vs. `ContractValidator`'s
    boolean-ish flag). Scope was deliberately trimmed from the parent issue's mockup — no
    Location filter (no such entity exists), `PlanningStatus` redefined onto
    Draft/Published/Conflict counts instead of the mockup's Draft/Published/Incomplete/Conflicts
    (`Schedule.Status` has no "Incomplete" concept), Cost Overview is total + delta vs previous
    period only (no regular/overtime/premium/weekend breakdown or budget comparison — no such
    concepts exist in `WageCalculator` or anywhere else), no per-employee utilization table.
    `historyAssignments` is omitted when calling `ScheduleValidator` here (cross-schedule-
    boundary rest-time/consecutive-day precision is lost at the edges of the period) — acceptable
    for an overview; exact enforcement still lives on the pre-existing
    `GET /schedules/{id}/validate`, unchanged by this work. Team/ShiftType filters narrow the
    KPI/coverage/cost/utilization numbers; Pain Points are only filtered by team (via each
    issue's `EmployeeId`, when set — issues like `Understaffed` that aren't employee-specific are
    always included) since `ValidationIssue` has no structured `ShiftTypeId` to filter by.
    Along the way, `OverlapDays` (the absence-overlap day-count helper) — already duplicated
    verbatim as a *private* method in both `ContractValidator` (Application layer) and
    `HoursBalanceCalculator` (Domain layer), split apart originally only because Domain can't
    depend on Application — got extracted to a new public `WorkingTimeCalculator.OverlapDays`
    (Domain layer, sits below both) now that this endpoint became a 3rd caller needing the same
    math; both private copies were deleted in favor of it. Verified against a real local Postgres
    (this machine's existing `docker compose` stack, rebuilt with `docker compose build api`):
    `dotnet build` clean, then a hand-built scenario (one employee, a 20h/week €15/h contract, a
    ShiftType with `MinStaffing=2`, three assignments in one week — two weekdays, one Sunday)
    round-tripped via curl and checked against hand-computed values — coverage 50% (1/2 staffed)
    on all three dates, labor cost €393.75 (two €112.50 weekday shifts + one €168.75 Sunday
    shift with the existing 50% surcharge), `ContractHoursExceeded` correctly trips at 22.5h
    planned vs 20h expected, overtime 2.5h, utilization 112.5%, conflict count 1 with the right
    `AffectedSchedules` entry, and a second overlapping Draft schedule with no assignments
    correctly adds to `DraftCount` without adding issues. Also checked: `teamId`/`shiftTypeId`
    filters correctly zero out non-matching KPIs while leaving team-unfilterable Pain Points
    (Understaffed) in place, and unauthenticated requests 401. Test data was deleted again
    afterward to leave the dev DB clean. Not yet clicked through in a browser — there's no
    frontend for this endpoint yet (issue #30).
  - **`src/ShiftPlanner.Tests`** (issue #50) — an xUnit project that didn't exist before despite
    readme.md §19 naming one; every phase up to now was only verified by hand (curl against a
    real Postgres, or build/`vue-tsc` cleanliness). Covers every Domain calculator
    (`WorkingTimeCalculator`, `WageCalculator`, `GermanPublicHolidays`, `HoursBalanceCalculator`)
    and every Application validator including `ScheduleValidator` integration cases — 74 tests,
    all pure logic over plain POCOs, no EF Core/Postgres dependency, so no test-container/DB
    setup was needed. `.github/workflows/build.yml` (issue #51) now runs `dotnet build`+
    `dotnet test` and the frontend's `lint`/`build` scripts on every push and PR — `deploy.yml`
    was untouched, so push-to-`main` still deploys the same way, just with a build/test gate
    that didn't exist before it. Verified via the `mcr.microsoft.com/dotnet/sdk:10.0` Docker
    image in both Debug and the Release configuration CI actually uses: solution builds clean,
    all 74 tests pass.
  - **Contact info + Arbeitszeitpräferenzen / shift suggestions** (no issue filed) —
    `Employee.PhoneNumber` (nullable string) sits alongside the pre-existing Email field, same
    optional-contact-field shape, migration `EmployeePreferences`. Two new small entities cover
    readme.md §17's "später können hier Arbeitszeitpräferenzen ergänzt werden":
    `ShiftTypePreference` (EmployeeId+ShiftTypeId, unique) and `WeekdayPreference`
    (EmployeeId+DayOfWeek, unique), both just a `PreferenceLevel` enum (`Preferred`/`Avoid` —
    no stored `Neutral`; the absence of a row already means neutral, so the enum only needs the
    two poles). `EmployeesController` gets `GET`/`PUT /employees/{id}/shift-type-preferences`
    and `/weekday-preferences`, both full-replace PUTs mirroring the existing
    `eligible-shift-types` endpoint's shape exactly (distinct concept, though — eligibility is
    "allowed", preference is "wanted").
    `Application/Suggestions/ShiftSuggestionEngine.cs` (new `PlanningDomain`-adjacent static
    class, same stateless-over-POCOs pattern as every validator) ranks active employees for one
    open (date, ShiftType) slot: `Eligible` mirrors exactly the four rules ScheduleValidator
    treats as Errors for that employee/date/shiftType (`EligibilityValidator`,
    `AbsenceValidator`, `RestTimeValidator`'s 11h-rest check against the shift immediately
    before/after, `ConsecutiveDaysValidator`'s 6-day-streak check) — a suggestion shouldn't
    recommend something the validator would immediately flag red. A second same-day shift
    (which `ShiftOverlapValidator` only Warns about, not Errors) stays eligible but scores -3,
    same Error-vs-Warning severity split as the validators themselves. Everything else is a
    scored nudge, not a filter: ShiftType preference (±2), weekday preference (±1), and a +1
    "under contract target" bonus reusing `ContractValidator`'s exact expected-vs-actual-hours
    formula (scaled to the Schedule's span, Absence days excluded) to favor whoever's furthest
    under their contracted hours for this Schedule. `SchedulesController`'s new
    `GET /schedules/{id}/suggestions?date=&shiftTypeId=` loads the same shape of data the
    existing `/validate` endpoint already does (±6-day history window for the two cross-schedule
    rules, absences overlapping the one date, this Schedule's own assignments, contracts,
    both preference tables) and returns employees ranked eligible-first then score-descending.
    `ShiftPlanner.Tests`: 11 new tests for the engine (one per hard-exclude rule, the
    Warning-not-Error same-day case, each scoring dimension, and the final ordering) — 85 tests
    total now, all passing.
    Verified end-to-end against a real local Postgres (this session's Docker daemon wasn't
    running by default but `dockerd` itself was installable and reachable, and the .NET SDK
    came from the same `mcr.microsoft.com/dotnet/sdk:10.0` image the rest of this file already
    uses — a local `postgresql-16` package turned out to already be installed too, same
    resourcefulness prior sessions used when Docker Hub was blocked): `dotnet build`/`dotnet
    test` clean, migration applied, then curl round-tripped every rule individually — phone
    number persists, both preference PUT/GETs round-trip, a preferred ShiftType scores +2 vs. an
    avoided one at -2, restricting `EligibleShiftTypes` correctly flips `Eligible` to false,
    an Absence on the target date does too, an adjacent shift with <11h gap correctly trips
    `InsufficientRest`, and six consecutive prior days correctly trips `TooManyConsecutiveDays`
    on the would-be 7th. Test data deleted again afterward to leave the dev DB clean.
- **Frontend** (`frontend/`): Vite + Vue 3 + TS + Tailwind v4 + Pinia + Vue Router + Axios +
  ESLint/Prettier + `@lucide/vue` (the `lucide-vue-next` package readme.md/issue #5 named is
  deprecated upstream in favor of this). `services/api.ts`'s JWT-attach + 401-refresh now
  actually gets used — fixed its `baseURL` (`/api/v1` didn't match any controller route; only
  `AuthController` lives under `v1`, everything else is `/api/{controller}`). `stores/auth.ts`
  decodes the JWT's role/email claims for display and does a silent refresh on boot (via the
  httpOnly cookie) so a page reload doesn't force a re-login. `router/index.ts` gates every
  route but `/login` behind having an access token.
  - `views/Login/LoginView.vue` — email/password against `POST /v1/auth/login`; didn't exist
    before, nothing else works without it.
  - `views/Employees/EmployeesView.vue` — list (`GET /employees`, `GET /teams` for the team
    column) + create form + delete, wired to the real API
    ([issue #2](https://github.com/mycaravam-crypto/shifty/issues/2)). Clicking a row opens
    `EmployeeDetailModal.vue` (in a reusable `components/ModalShell.vue`, pm-tool2-style):
    edit/team-assignment, eligible-shift-types checkboxes (`GET`/`PUT
    /employees/{id}/eligible-shift-types`), and Contract list/create/edit/delete
    (`/employees/{id}/contracts`, `/contracts/{id}`) — the Contract form/table now also
    carries the optional `HourlyRate` (issue #14, "€/Std", blank means untracked). Editing an
    existing Contract (issue #62) reuses that same create form rather than a second one: a
    Pencil icon per row (mirroring `ShiftTypeDetailModal.vue`'s click-to-edit for the other
    PUT-only-no-PATCH entity) pre-fills it and an `editingContractId` ref switches the one
    submit handler from `POST` to `PUT /contracts/{id}`, with an "Abbrechen" button back to
    create mode — previously the only way to fix a mistyped field was delete+recreate, which
    lost the id and showed up in `AuditLog` as Delete+Create instead of a single Update.
    Frontend-only (`ContractsController`'s `PUT` already existed, unchanged); verified via
    `npm run lint`/`npm run build` (`vue-tsc -b` + `vite build`, both clean) — not clicked
    through in an actual browser (same Playwright-install gap noted elsewhere in this file). A
    new Abwesenheiten section (issue #17) below Verträge, same list/create/delete table pattern —
    `AbsenceType`'s numeric enum values are mapped to German labels client-side
    (Urlaub/Krankheit/Fortbildung/Sonstiges) since the backend serializes enums as their
    ordinal, not a string.
  - `views/Stammdaten/StammdatenView.vue` — Teams (list + create only; the backend has no
    `PUT`/`DELETE` for Teams) and ShiftTypes (list + create + click-to-edit via
    `views/Stammdaten/ShiftTypeDetailModal.vue`, since `ShiftTypesController` does have a
    `PUT`) on one page, mirroring `EmployeesView.vue`'s list/create pattern. Reachable at
    `/stammdaten` in the sidebar nav — previously this data was only reachable via
    Swagger/API key. **Component-level parity pass** ([issue #60](https://github.com/mycaravam-crypto/shifty/issues/60))
    — the Teams table was the one concrete gap found against `EmployeesView.vue`: the
    ShiftTypes table already got issue #40's `md:hidden` stacked-card / `hidden md:block`
    table split, but the Teams table next to it in the same file never did (still an
    unscrollable table below `md`), so it now gets the identical split. The color `<input>`
    in both `StammdatenView.vue`'s create form and `ShiftTypeDetailModal.vue` was also missing
    the `outline-none focus-visible:ring-2 focus-visible:ring-indigo-500` every other input in
    the app carries — added to both for consistency. Everything else audited against
    `EmployeesView.vue`/`EmployeeDetailModal.vue` (skeleton loading, toast/`ConfirmDialog`
    usage, gradient/`bg-white/10` button split, `inputClass`, glass panels, chip styling,
    hover/`cursor-pointer` on clickable rows) was already at parity from earlier work, so
    nothing else changed. `npm run lint`/`npm run build` (`vue-tsc -b` + `vite build`) clean;
    not clicked through in an actual browser (same Playwright-install gap noted elsewhere in
    this file).
  - `views/Schedule/ScheduleView.vue` — the Wochenansicht (readme.md §15/§16), no longer a
    placeholder. In practice a Schichtplan is always created for a full calendar month (not a
    week — an earlier cut used Mon–Sun `Schedule`s, but that didn't match how the user actually
    plans), so the grid is Employees (rows) × the current month's days (columns, ~28–31 of
    them, generated from the Schedule's actual `StartDate..EndDate` — the column-generation
    logic itself didn't need to change, only what date range gets passed to it); the table's
    existing `overflow-x-auto` wrapper handles the wider column count with horizontal scroll,
    no layout rework needed. Prev/next nav moves by calendar month (plain `Date` math, no
    dependency); an empty-state "Diesen Monat anlegen" button (`POST /schedules`, 1st–last of
    month, named e.g. "August 2026") when no Schedule exists yet for the visible month. A
    palette of active ShiftTypes above the grid, and both palette chips and placed assignment
    chips drive drag-and-drop off raw Pointer Events (`pointerdown`/`pointermove`/`pointerup`
    on `window`, `touch-action: none` on the chips) rather than native HTML5 `draggable` —
    the native API has no touch support, and Pointer Events give mouse and touch the same code
    path instead of two parallel implementations ([issue #19](https://github.com/mycaravam-crypto/shifty/issues/19),
    closing the touch/mobile drag gap readme.md §1 calls out as a goal distinct from a native
    Mobile App). A small movement threshold (6px) gates when a pointer-down actually becomes a
    drag, so a plain tap/click still opens the assignment modal instead of misfiring as a
    zero-distance drag; dragging shows a small floating chip following the pointer and
    highlights the day cell currently under it (`dragOverKey`, via `elementFromPoint` +
    `data-employee-id`/`data-date` attributes on each `<td>`, since native `dragover`/`drop`
    targeting doesn't apply here either). Dropping a palette chip on a cell `POST`s a new
    assignment from the ShiftType's template times, dropping an existing chip on a different
    cell `PUT`s the same assignment onto the new employee/date (a move) — both funnel through
    one `performDrop` used by both create and move, same as the old native-DnD code did.
    `components/ModalShell.vue`'s click-outside-to-close also needed a fix alongside this: a
    touch tap that opens a modal (e.g. tapping an assignment chip) is followed by the browser's
    synthetic compatibility `click` event at the same coordinates, which — once the backdrop
    exists — lands on it and would immediately close what the tap just opened; the backdrop
    now ignores its own `click.self` for 500ms after mount to absorb that one ghost click
    without weakening real click-outside-to-close (verified both still work, mouse and touch).
    Each
    employee row shows a `font-mono` "Xh / Yh ⚠" hour readout + progress bar — sums the
    backend's already-computed `netHours` (never re-derives the subtraction client-side),
    target hours scaled from the active Contract's `WeeklyHours` by the visible month's day
    count ÷ 7 (a Contract still only defines a weekly figure — no separate monthly-target field
    was added), now also reduced by any Absence days (issue #17) overlapping the visible month
    per employee, matching `ContractValidator`'s equivalent backend scaling. Below that, a
    `€`-formatted labor-cost line (issue #14) shown only when at
    least one of the employee's assignments has a non-null backend-computed `laborCost`; a
    schedule-wide "Lohnkosten" total sits in the toolbar row, same client-side-sum-of-backend-
    values pattern as the hour totals. A first cut of the overtime ledger (issue #18,
    "Übertrag: +Xh"/"-Xh" beneath the Xh/Yh line, green/rose, hidden when zero) landed
    alongside this: `GET /api/employees/{id}/hours-balance?before=` (new, `EmployeesController`)
    returns a cumulative over/under-hours balance — every `Schedule` that's fully elapsed before
    the given date (`EndDate < before`), same expected-vs-actual math `ContractValidator` already
    does per-schedule, just summed across all of them instead of checked against one. Backend
    is `Domain/Scheduling/HoursBalanceCalculator.cs`, a stateless static method next to
    `WorkingTimeCalculator` — derived at read time from existing `Schedule`/`ShiftAssignment`/
    `Contract`/`Absence` rows, no stored running-total column, matching the "no persisted
    derived state" design note the issue itself called for. `Absence` days (issue #17, which
    landed after this was first cut) are excluded from each elapsed Schedule's expected hours
    the same way `ContractValidator` already does per-schedule, so an approved absence doesn't
    show as under-hours. The frontend fetches it per visible employee with `before` = the
    visible month's start (so the figure shown is the balance carried *into* this month, not
    double-counting the in-progress month the Xh/Yh bar already covers) — on initial load and
    again on month nav. Verified against a real local Postgres (`dotnet build` clean, `vue-tsc
    -b` clean): balance is 0 for a not-yet-elapsed schedule, correctly sums to the expected
    over/under figure once a schedule's `EndDate` is in the past, excludes Absence-overlapping
    days from that figure, and 404s for an unknown employee id — not yet clicked through in an
    actual browser (same Playwright-install gap as the rest of the Wochenansicht work). Clicking
    an assignment chip opens `views/Schedule/ShiftAssignmentModal.vue`
    (`ModalShell`-based, mirrors `EmployeeDetailModal.vue`'s shape) to change ShiftType/times/
    break or delete — content edits only, moving stays drag-only, and it has no create mode.
    Each day column header now gets a small amber dot (+ `title` tooltip with the holiday's
    name) when it's a gesetzlicher Feiertag (issue #15) — `GET /api/public-holidays?start=&end=`
    (new `PublicHolidaysController`) is *computed*, not a stored table: `Domain/Scheduling/
    GermanPublicHolidays.cs` derives the 9 nationwide holidays for any year from Gauss's Easter
    algorithm plus fixed calendar dates, so there's no yearly seed job and no migration, matching
    the "no persisted derived state" pattern the codebase already uses for
    `HoursBalanceCalculator`/`WorkingTimeCalculator`. First cut is nationwide-only — no
    per-Bundesland holidays (e.g. Fronleichnam, Reformationstag) — since nothing consuming this
    yet needs that precision; readme.md has no §-reference for holidays at all, this is a new
    Phase 5 feature. Verified: the Easter algorithm checked against four known Easter Sundays
    (2024–2027) by hand, `dotnet build` clean, `vue-tsc -b` clean, and round-tripped against a
    real local Postgres/API (August 2026 correctly returns no nationwide holiday, a
    Dec-2026→Jan-2027 range correctly crosses the year boundary and returns Christmas + New
    Year's, `end < start` 400s, unauthenticated 401s) — not yet clicked through in an actual
    browser (same Playwright-install gap as the rest of the Wochenansicht work). Issue #16
    (wage surcharges, backend-only, see above) builds on this and is now done.
    A glass panel above the palette lists every issue from `GET
    /schedules/{id}/validate` (❌ red for Errors, ⚠ amber for Warnings), refetched alongside
    the assignments on every load/move/create — the existing per-employee "Xh / Yh ⚠" bar is
    unchanged (still a client-side glance, not fed by `ValidationResult`) since it already
    covers the same ground `ContractValidator` does for the common case (`ContractValidator`
    itself was updated alongside this — see above — to scale by the Schedule's actual span
    rather than assume 7 days, since it would otherwise flag almost every month-long Schedule
    as over-hours). **Phase 4 "Usability"** (readme.md §22) has started: a "Monat kopieren"
    button (visible once the visible month has assignments; was "Woche kopieren" before the
    week→month switch) copies every assignment to the same day-of-month one month later
    (clamped into shorter months, e.g. the 31st → the 28th/29th/30th), creating that month's
    `Schedule` first if it doesn't exist yet — pure client-side orchestration of the existing
    `/schedules`/`assignments` endpoints, no backend change. Guards against clobbering: aborts
    with an inline error if the target month's `Schedule` already has assignments. A search
    box + team `<select>` (reusing `Employee.TeamId` and `GET /teams`, same as
    `EmployeesView.vue`'s existing pattern) filters the employee rows client-side — covers both
    "Filter" and "Suche" from readme.md's Phase 4 list in one toolbar since they're the same
    filter-the-row-set operation here; a dedicated shift-type/date filter wasn't added since
    the palette + month nav already cover that. **Shortcuts and optimized drag-and-drop**
    (the rest of Phase 4) are now also built: `components/ModalShell.vue` closes on `Escape`
    (one fix, so it covers every modal built on it — `EmployeeDetailModal`,
    `ShiftTypeDetailModal`, `ShiftAssignmentModal` — not a per-modal change). In
    `ScheduleView.vue`, `ArrowLeft`/`ArrowRight` move a month (mirroring the existing
    chevron buttons) and `/` focuses the search box — both are no-ops while an input/select
    is focused (so typing itself, and arrow-key cursor movement inside the search box, isn't
    hijacked) or while the assignment modal is open. The drag-and-drop "optimization" is edge
    auto-scroll for the table's own `overflow-x-auto` wrapper: a month can have 28–31 day
    columns, wider than the viewport, and previously a drag had no way to reach an off-screen
    day except releasing and re-dragging after scrolling manually. Scrolling runs off a
    `setInterval` (16ms) reading the drag's last-known pointer position rather than off
    `pointermove` directly — a pointer parked at the edge stops firing move events, but the
    scroll needs to keep going while it's held there. Verified in a real headless Chromium
    against the live local stack (Docker `api`/`db`, `npm run dev`): logged in, confirmed `/`
    focuses search, `ArrowRight`/`ArrowLeft` navigate months and are correctly inert while
    typing in the search box, dragged a palette chip to the table's right edge and confirmed
    the wrapper's `scrollLeft` kept advancing while held there (not just on movement), dropped
    it on a day that had been off-screen before the auto-scroll (assignment created via a real
    `POST`, confirmed via the API and then deleted again to leave the dev DB clean), and
    confirmed `Escape` closes the resulting `ShiftAssignmentModal`.
  - **PDF export** (new Phase 5 feature, no issue filed) — "PDF exportieren" in
    `ScheduleView.vue`'s header (all employees) and a small printer icon per employee row (that
    employee only) both just call the browser's native `window.print()`, scoped with a
    `print:hidden`/`print:` Tailwind media-print stylesheet rather than a PDF-generation
    dependency — no library added. `printEmployeeId` (unset for "all", set for a single row)
    hides non-matching `<tr>`s via `print:hidden`; the toolbar, palette, search/filter row,
    validation panel, and sidebar (`components/AppShell.vue`) are all `print:hidden` too, and a
    scoped `@page { size: landscape }` block fits a month's ~30 day columns on the page. Users
    save the resulting print dialog as PDF themselves (every major browser's print dialog offers
    "Save as PDF" natively) — there's no server-side PDF generation. Verified via
    `vue-tsc -b`/`vite build` clean; not clicked through in an actual browser print preview (same
    Playwright-install gap as the rest of this app's frontend work).
  - `views/Dashboard/DashboardView.vue` (issues #30/#31, frontend-only — the backend read model
    from issue #29 needed no changes) — new `/dashboard` route, reachable via a new "Übersicht"
    nav entry (placed first, as the landing overview). One `GET /dashboard` fetch per
    filter/period change (native `<input type="date">` × 2 + Team/ShiftType `<select>`s, no new
    filter-option endpoints needed), no client-side KPI computation — every number rendered is
    read straight off the backend DTO. Six KPI cards (Besetzung/Auslastung/Lohnkosten/Planung/
    Offene Probleme/Überstunden), a Besetzungsgrad coverage list and a Planungsstatus panel
    (both `.glass rounded-xl`, matching `ScheduleView.vue`'s panel style), a Pain Points panel
    reusing that same file's ❌/⚠ row pattern, and — issue #31 — a capped (top 8),
    severity-sorted "Handlungsbedarf" action feed built from the same already-fetched
    `painPoints[]`, one sub-issue on top of the other's data with no second backend call.
    `PainPointDto` carries no per-issue date, only `ScheduleId`/`ScheduleName`, so the feed
    sorts by severity only (ties keep the backend's own order) — noted inline rather than
    adding a backend field for it. Each pain-point/affected-schedule row links back into
    `ScheduleView` via a new `?scheduleId=` query param — `ScheduleView.vue`'s existing `load()`
    now looks it up against the `schedules` list it already fetches and jumps `anchorDate` to
    that schedule's month (then strips the query param via `router.replace`), rather than
    adding a second lookup endpoint. Verified end-to-end in a real headless Chromium against
    the local stack (Docker `db`/`api` + `npm run dev`): `vue-tsc -b`/`vite build` clean, the
    empty-state dashboard for the current week loads with no console errors, then a real
    break-minutes-violation assignment was posted via the API to exercise a non-empty state —
    the Pain Points panel, Planungsstatus conflict count, and Handlungsbedarf feed all rendered
    the live issue, and clicking "Öffnen" navigated to Dienstplan and landed on the correct
    month (August 2026) for the affected schedule. Test assignment deleted again afterward to
    leave the dev DB clean.
  - `components/AppShell.vue` — sidebar nav (Übersicht/Dienstplan/Mitarbeiter/Stammdaten/
    Einstellungen) + user identity + logout, applying CLAUDE.md's "Visual design" tokens (dark
    glass, Inter, blue→indigo accent) — a functional cut of
    [issue #5](https://github.com/mycaravam-crypto/shifty/issues/5), not the full pm-tool2/
    vanspace3d component-level parity pass.
  - `views/Settings/SettingsView.vue` ([issue #43](https://github.com/mycaravam-crypto/shifty/issues/43))
    — no longer just the account-info placeholder: a first real, scoped-down cut per the
    issue's own "pick what's actually useful" framing. Only one setting landed — a default
    team filter for the Dienstplan, picked from `GET /teams` in a new `stores/settings.ts`
    Pinia store, persisted to `localStorage` (there's no backend concept of per-user settings
    to persist it server-side, and none of Phase 5 needed one badly enough to add it here).
    The other two candidates the issue named were both dead ends right now: notification
    preferences need something non-transient to configure first (the toast system, issue #36,
    is still purely live/session-only — no digest concept exists anywhere), and a light/dark
    toggle is explicitly out of scope per this file's own "Visual design" section (dark-only by
    design). `ScheduleView.vue`'s `load()` now applies the stored default as the initial
    `teamFilter` — but only when the URL has no `?team=` of its own, so issue #41's existing
    URL-query-string filter persistence (a bookmarked/shared link, or the dashboard's
    `?scheduleId=` deep link) always wins over the user's own default; the two coexist rather
    than one replacing the other. Verified via `vue-tsc -b`/`vite build` clean, and — Docker
    wasn't reachable at all this session, so no live backend/Postgres existed to hit — a
    scratch Playwright script drove the real dev server with `/api/*` mocked at the network
    layer (same technique issue #19's session used for the same reason): the Settings select
    renders the mocked teams, picking one persists to `localStorage` and shows a toast, a fresh
    Dienstplan load with no `?team=` in the URL picks up the stored default (URL gains
    `?team=`), an explicit `?team=` in the URL is left untouched (overrides the default), and
    resetting to "Alle Teams" clears `localStorage` and a subsequent fresh load carries no team
    param — no console/page errors throughout.
  - **Contact info + Arbeitszeitpräferenzen / shift suggestions** (backend above, no issue
    filed) — `EmployeeDetailModal.vue` gets a "Telefon" input next to the existing E-Mail one
    (`EmployeesView.vue`'s create form too), and a new "Präferenzen" section below "Mögliche
    Schichten": one chip row per ShiftType and one per Wochentag, each chip a 3-state
    click-to-cycle toggle (neutral → 👍 bevorzugt → 👎 vermeiden → neutral again, emerald/rose/
    neutral styling) rather than a `<select>` — matches the app's existing chip aesthetic
    (palette chips, eligibility chips) more than the form-select pattern Absence/Contract use.
    One shared "Speichern" button PUTs both preference endpoints in parallel. In
    `ScheduleView.vue`, each palette ShiftType chip gets a small `Sparkles` icon button
    ("Vorschlagen") that opens the new `ShiftSuggestionModal.vue`: a date picker (defaulting to
    and clamped within the visible month) plus the ranked suggestion list from
    `GET /schedules/{id}/suggestions`, each row showing a ✓/✗ eligibility icon, the score, and
    every reason with a 👍/👎 icon (ineligible rows stay visible and clickable — same
    flag-don't-block philosophy `ScheduleValidator`'s Errors already use elsewhere in this
    view — just styled rose/dimmed). Clicking "Zuweisen" `POST`s the assignment (same payload
    shape `performDrop`'s ShiftType-drop branch already sends) and re-fetches the suggestion
    list in place, so assigning one employee immediately re-scores the rest (e.g. the
    just-assigned employee now shows the same-day/rest-time reasons for a second slot) without
    closing the modal — a manager filling several open shifts doesn't have to reopen it each
    time. The `@pointerdown.stop`/`@click.stop` on that icon button keep it from being swallowed
    by the chip's own drag-start handler. Verified end-to-end in real headless Chromium against
    the local stack this session actually had running (Docker daemon + `dockerd`, the local
    `postgresql-16` install, the API run via `dotnet run` in the SDK container, `npm run dev`) —
    not the usual "not clicked through, no Docker" caveat seen elsewhere in this file: logged
    in, opened Anna Schmidt, changed and saved the phone number (toast confirmed, modal closes
    on Stammdaten-save same as before — pre-existing behavior, not new), reopened the modal and
    cycled/saved a ShiftType preference and a Wochentag preference, reopened a third time and
    confirmed both persisted (phone value intact, the cycled chip still emerald). On
    Dienstplan, clicked the Sparkles icon on a ShiftType chip, confirmed the modal opened with
    the right title/date, saw the preferred-ShiftType employee ranked with the correct score and
    reason text, clicked "Zuweisen", confirmed the success toast and that the suggestion list
    live-updated to show the same employee now ineligible for a second same-day slot (rest-time
    + already-assigned reasons) — no console/page errors throughout, aside from the pre-existing
    benign 401 every page load already produces from `stores/auth.ts`'s silent-refresh-on-boot
    attempt when no session cookie exists yet.
  - Verified end-to-end in a real headless browser against the local stack below: login
    success/failure, employee list load, create, 409-conflict surfaced in the UI, logout,
    Dienstplan empty-state schedule creation, drag-to-place, drag-to-move, hour-bar update,
    modal edit, and delete. Re-verified after the validation panel landed: an assignment
    violating `BreakMinutesValidator` renders the ❌ banner live in the browser. "Woche
    kopieren" is only verified against the real API directly (curl, mirroring the exact
    request sequence the button makes) — no browser tooling was available in that session, so
    it hasn't been clicked in an actual browser yet. Same for the search/team-filter toolbar:
    confirmed `GET /teams` returns 200 and `Employee` already carries `teamId` matching the
    new frontend types, and the dev server boots with no console/runtime errors, but not
    interactively exercised in a browser (no local Team data exists yet to click through
    either). The week→month switch (creation, nav, "Monat kopieren", the scaled hour target,
    and the matching `ContractValidator` fix) is verified the same indirect way — `vue-tsc -b`
    clean, the backend rebuilt and `dotnet build` clean, and the exact request sequence each
    button makes round-tripped against a real local Postgres via curl (including a
    31-assignment month that correctly triggers `ContractHoursExceeded` at the scaled monthly
    limit, where the old unscaled check would've false-flagged nearly every month). Playwright's
    browser install is broken in this environment (`TypeError: onExit is not a function`), so
    none of this has actually been clicked through in a browser yet. The hourly-wage-rate UI
    (issue #14 — Contract form's `€/Std` field, the per-employee/schedule-wide "Lohnkosten"
    readouts) is verified the same indirect way as the week→month switch: `vue-tsc -b`/
    `vite build` clean, `dotnet build` clean — but not round-tripped against a real Postgres
    (this session's Docker daemon could reach `mcr.microsoft.com` but Docker Hub pulls were
    blocked by the sandbox's egress policy, so no local Postgres was available at all this
    time, not even via curl) and not clicked through in a browser. The touch/mobile
    drag-and-drop rework (issue #19) **is** verified in a real headless Chromium (this session's
    Docker daemon wasn't reachable at all — not even for a container to build against, so no
    live backend/Postgres existed to hit): a scratch Playwright script drove the actual dev
    server with the `/api/*` calls mocked at the network layer, using CDP
    `Input.dispatchTouchEvent` for genuine touch-sourced Pointer Events (not just JS-dispatched
    synthetic ones) — palette-chip-to-cell (create), assignment-chip-to-different-cell (move),
    and a stationary tap (opens the edit modal) all confirmed on a touch-capable context, plus
    the pre-existing mouse-drag path re-confirmed unbroken on a mouse-only context. That pass
    caught a real bug, not a test artifact: a touch tap that opens `ShiftAssignmentModal` is
    followed by the browser's synthetic compatibility `click` at the same coordinates, which
    landed on the freshly-mounted `ModalShell` backdrop and closed it again immediately via
    `@click.self` — fixed in `ModalShell.vue` (see above) and re-verified, including that
    mouse click-to-open and click-outside-to-close still both work.
  - **UX-polish batch** (issues #36/#37/#38/#39/#40/#41/#42) — a backlog filed but not yet
    built: `stores/toast.ts` + `components/ToastContainer.vue` (mounted once in `App.vue`) is a
    small global success/error toast system, wired into every create/update/delete flow across
    Employees, Stammdaten, Contract/Absence CRUD, and Schedule assignment CRUD (drag-drop
    create/move, the assignment modal, month-copy) — closes #36. `components/ConfirmDialog.vue`,
    built on the existing `ModalShell` pattern, replaces all four native `confirm()` dialogs
    (employee/contract/absence/shift-assignment delete) — closes #37. `ScheduleView.vue`,
    `EmployeesView.vue`, `StammdatenView.vue`, and `DashboardView.vue`'s loading states swap the
    old plain "Lädt…" text for `animate-pulse` skeleton blocks shaped like the eventual content
    — closes #38. `ScheduleView.vue`'s search/team filter now round-trips through the route
    query string (`?q=&team=`), surviving navigation away and back, coexisting with the existing
    `?scheduleId=` dashboard deep link — closes #41. A "?" toolbar button (and `?` keyboard
    shortcut) opens a small `ModalShell` listing the Dienstplan's keyboard shortcuts, which
    previously had no in-UI hint beyond the search box's placeholder text — closes #42.
    Validation panel issues are now clickable when they carry an `employeeId`/
    `shiftAssignmentId`: clicking scrolls to and briefly highlights the relevant employee row or
    day cell, reusing the existing drag-over highlight style — closes #39. `EmployeesView.vue`
    and `StammdatenView.vue`'s tables get a stacked-card layout below the `md` breakpoint
    (matching `ScheduleView.vue`'s existing mobile-first treatment) instead of an unscrollable
    overflowing table, plus responsive form grids (the Contract form's tightest 3-column grid
    also collapses to 2 columns on narrow viewports) — closes #40. Verified via `npm run lint`
    (0 errors) and `npm run build` (`vue-tsc -b` + `vite build`, clean) — not clicked through in
    an actual browser (same Playwright-install gap noted elsewhere in this file). Issue #43 (a
    real `SettingsView`) is the one issue from this batch left open.
- **Reject a refresh token used as a Bearer access token** (issue #71) — a genuine security gap
    from a fresh batch of 25 issues an external code review filed against the whole codebase
    (issues #55–#82): access and refresh JWTs (`JwtTokenFactory`) are both self-contained tokens
    differing only by a `token_use` claim, and the JWT bearer scheme in `Program.cs` validated
    signature/issuer/audience/lifetime but never checked that claim — so a refresh token, valid
    for 7 days vs. the access token's 15 minutes, worked as a Bearer token against every
    protected endpoint if obtained by an attacker. Fixed with an `OnTokenValidated` handler on
    the bearer scheme's `JwtBearerEvents` that fails authentication unless `token_use == access`
    — matches the issue's own suggested approach without needing a schema change (no separate
    `aud` values). No test added to `ShiftPlanner.Tests` — that project is pure Domain/
    Application unit tests over POCOs with no ASP.NET Core hosting/HTTP layer at all (adding one
    is exactly issue #75's "integration test coverage" ask, not a one-off addition here).
    Verified end-to-end instead, the same way this file's earlier auth work was: `dotnet build`/
    `dotnet test` clean (85 tests, unaffected), then a real local Postgres (this machine's
    `postgresql-16` install) + the API run via `dotnet run` in the SDK container — logged in via
    `POST /v1/auth/login`, confirmed the access token still returns `200` from `GET /employees`,
    confirmed the refresh-cookie token used as `Authorization: Bearer` on that same endpoint now
    returns `401` (previously `200`), and confirmed `POST /v1/auth/refresh` (which reads the
    refresh token from its httpOnly cookie, not as a Bearer header) still works normally.
- **Dienstplan grid: sticky employee column and sticky date header** (issue #76, frontend-only)
    — `ScheduleView.vue`'s month grid previously had no sticky positioning at all, so scrolling
    right past a month's ~28–31 day columns lost the employee-name column, and scrolling down
    past a long employee roster lost the date header. `sticky left-0` on the employee-name
    `<th>`/`<td>` cells, `sticky top-0` on the header `<tr>`, and both (`sticky left-0` inside
    the already-`sticky top-0` row) on the corner cell — each with its own opaque `#11141c`
    background (else the underlying cells show through, since sticky cells paint over whatever
    scrolls beneath them) and a subtle drop shadow on the pinned edges. The employee-column cell
    also keeps the existing row-highlight tint (`highlightKey === e.id`, issue #39's click-to-
    scroll) via an inline `:style` background swap rather than a second Tailwind `bg-*` class,
    since two classes both setting `background-color` race on cascade order rather than actually
    layering. Getting this working exposed (and required fixing) two real CSS bugs the sticky
    positioning would otherwise silently no-op against, neither previously visible because
    nothing in the app used `position: sticky` before this:
    - The grid's `overflow-x-auto` wrapper looked horizontal-only, but CSS's own overflow rules
      force `overflow-y` to compute as `auto` too whenever the two axes disagree on
      visible-vs-not (there's no way to specify one axis as `auto` and the other `visible` and
      have the browser respect it) — so the wrapper was already an unconditional scroll
      container on *both* axes, just one that (with no height constraint) never actually
      needed to scroll internally, since it grew to fit its content and the whole page scrolled
      past it instead. `position: sticky` binds to the *nearest* such container regardless of
      whether that container ever actually scrolls — so the header/column were binding to this
      always-static wrapper and just riding along with the page scroll, never appearing to
      stick. Fix: given the ~30-employee-row grids this is meant for, embrace a bounded,
      genuinely-scrolling panel instead of fighting the CSS rule — `max-h-[70vh]` +
      `overflow-auto` (both axes explicitly, matching what the browser was already forcing) on
      the wrapper, so it becomes the real 2D scrollport the sticky cells correctly bind to
      (`print:max-h-none` alongside the pre-existing `print:overflow-visible` keeps printing
      unclipped). A short employee list just never grows tall enough to trigger the internal
      scrollbar, same visual result as before.
    - Confirming the above also surfaced that `AppShell.vue`'s `<main class="overflow-y-auto">`
      has been dead CSS since it was written: `<div class="flex min-h-screen">`'s `min-h-screen`
      (a floor, not a cap) lets the flex row grow past 100vh to fit tall content, so `<main>`
      (a `flex-1` child) never ends up shorter than its own content and its `overflow-y-auto`
      never actually clips anything — confirmed empirically (`main.scrollHeight ===
      main.clientHeight` even with far more content than the viewport) rather than just reasoned
      from the CSS. The page has always scrolled at the document/`<body>` level, on every view,
      not inside `<main>` — harmless before now since nothing needed a real scroll boundary to
      bind sticky positioning to, but left as-is here (not touched) since the Dienstplan fix
      above no longer depends on it and changing shared shell behavior for every other view is
      outside this issue's scope.
    Verified with a scratch Playwright script (same technique prior sessions used for this app
    when no live backend was available) driving the real dev server with `/api/*` mocked at the
    network layer and a synthetic 20-employee/31-day dataset sized to force both scroll axes:
    screenshots confirm the header row and employee column both stay visually pinned through
    independent horizontal-only, vertical-only, and combined scrolling, with the corner cell
    correctly layered above both (z-index), and a separate empty-state load produced no console
    errors or warnings. `npm run lint` (0 errors) and `npm run build` (`vue-tsc -b` + `vite
    build`) both clean.
- **Redesign the Dienstplan validation panel as a grouped summary** (issue #78, frontend-only,
    `ScheduleValidator`'s output shape untouched) — the panel used to render every
    error/warning as one flat list of `<p>` rows, which didn't scale past a handful of issues.
    Now: a compact header ("● 4 Fehler ▲ 3 Warnungen", per the issue's own example) followed by
    one collapsible row per rule *type* (`ValidationIssue.type`, e.g. `ContractHoursExceeded`,
    `InsufficientRest`, `Understaffed`), each showing a German label + count and expanding to the
    individual messages underneath — still clickable per issue #39's existing click-to-scroll
    (that handler, `focusIssue`, was untouched, just relocated into the new nested template).
    Groups sort errors-before-warnings then by size. `ISSUE_TYPE_LABELS` is a small hardcoded
    map from the 9 `type` strings the 7 backend validators actually emit (grepped from
    `Application/Validation/*.cs` rather than guessed) to German labels — falls back to the raw
    type string for anything unmapped, so a future validator doesn't silently disappear from the
    panel. Verified with a scratch Playwright script (mocked `/api/*`, a hand-built
    `ValidationResult` covering 5 of the 9 rule types across both severities): screenshots
    confirm the header counts, the five grouped rows with correct counts/labels, and that
    expanding two of them reveals the right individual messages with the chevron rotated — no
    console errors. `npm run lint` (0 errors) and `npm run build` clean.
- **Docker/deploy**: `docker-compose.yml` (db/api/web) validated with `docker compose config`,
  never actually deployed. No `.env` exists anywhere yet (only `.env.example`).
- **Versioning**: same scheme as vanspace3d. `frontend/package.json`'s `version` is shown
  subtly in the app UI (`App.vue`, bottom-right corner) and gets bumped (patch) + committed
  by [deploy/deploy.sh](deploy/deploy.sh) on every deploy — not yet exercised since the
  pipeline hasn't run (issue #4).

## Run it locally

Full stack — db + api via Docker (no local SDK needed), frontend via Vite:

```bash
cp .env.example .env   # fill in a real POSTGRES_PASSWORD / JWT_SIGNING_KEY for local use
docker compose up -d db
docker compose run --rm api dotnet ShiftPlanner.Api.dll --migrate
docker compose run --rm -e SeedUser__Email=admin@shifty.local \
  -e SeedUser__Password=DevAdmin123! -e SeedUser__Role=Admin \
  api dotnet ShiftPlanner.Api.dll --seed-user
docker compose up -d api

cd frontend && npm install && npm run dev   # http://localhost:5173
```

The `api` service in `docker-compose.yml` has no host port mapping (only the prod `web`
container talks to it internally) — add an untracked `docker-compose.override.yml` (see
`.gitignore`) exposing `api`'s 8080 to a free host port, and point `frontend/vite.config.ts`'s
proxy at that same port. Both currently assume **8081** on this machine (8080 was already
taken by an unrelated container) — adjust if that's not your situation.

Backend build/test only:
```bash
docker run --rm -v "$PWD":/src -w /src mcr.microsoft.com/dotnet/sdk:10.0 dotnet build ShiftPlanner.sln
```

## Deploy target

Live at `shifty.vi0lins.de`, same VPS as
[vanspace3d](https://github.com/mycaravam-crypto/vanspace3d), behind that host's existing
shared Caddy instance (not a containerized one). Deploy pipeline is
[.github/workflows/deploy.yml](.github/workflows/deploy.yml) →
[deploy/deploy.sh](deploy/deploy.sh), triggered on push to `main` — it bumps
`frontend/package.json`'s patch version, rsyncs the repo to the server, then
`docker compose build && up -d` + runs pending migrations there ([issue #4](https://github.com/mycaravam-crypto/shifty/issues/4),
closed).

First-time setup on a fresh target server (already done for `shifty.vi0lins.de`, needed again
only for a new host):

1. GitHub repo secrets: `SHIFTPLANNER_DEPLOY_SSH_KEY`, `SHIFTPLANNER_DEPLOY_HOST`,
   `SHIFTPLANNER_DEPLOY_PATH` (read by `deploy.yml`).
2. `.env` created on the server at `$SHIFTPLANNER_DEPLOY_PATH/.env` (from `.env.example` —
   real `POSTGRES_PASSWORD`/`JWT_SIGNING_KEY`, no local dotnet SDK needed since everything
   runs in the `api` container).
3. One-time host-side Caddy block from [deploy/Caddyfile](deploy/Caddyfile)'s header comment
   (`shifty.vi0lins.de { reverse_proxy 127.0.0.1:8090 }`, then `systemctl reload caddy`) — the
   in-repo Caddyfile only runs *inside* the `web` container and has no TLS/public exposure of
   its own.
4. DNS: `shifty.vi0lins.de` → the server.
5. Push to `main` (or run `deploy/deploy.sh` manually with `SHIFTPLANNER_DEPLOY_HOST`/`_PATH`
   set) — first run needs `docker compose run --rm api dotnet ShiftPlanner.Api.dll --migrate`
   and `--seed-user` on the server once, same as the local dev steps above, since a fresh DB
   has no schema or Staff accounts yet.

## Visual design

Frontend should look and feel like the other two apps in this account —
[pm-tool2](https://github.com/mycaravam-crypto/pm-tool2) ("ChronosPM", Vue) and
[vanspace3d](https://github.com/mycaravam-crypto/vanspace3d) (vanilla+Tailwind). Both share
one dark, glassy design system; pull up their source directly for real component code
(`client/src/components/*.vue` in pm-tool2, `prototype/index.html` in vanspace3d) rather than
re-deriving patterns from scratch. Tokens pulled from both:

- **Theme**: dark-only, near-black base (pm-tool2 `#080a0f`, vanspace3d `#0f172a`), body
  background is a subtle radial gradient toward the accent color, not a flat fill.
- **Panels**: `bg-[#11141c]`/`#121620`-ish solids or a `.glass` gradient-+-blur variant,
  `border border-white/8` (10% on inputs), `rounded-lg`/`rounded-xl`/`rounded-2xl` by size
  (button → panel → modal).
- **Accent**: blue→indigo/violet gradient for primary actions (pm-tool2 leans violet,
  vanspace3d leans blue — either reads as "this family," pick one and stay consistent).
  Semantic colors on top: emerald = success/positive, amber = warning, red/rose = destructive.
- **Typography**: Inter (body/UI), JetBrains Mono for numeric readouts — vanspace3d uses it
  for dimensions, this app's natural fit is hour totals/shift times (`08:00–16:30`, `32h/36h`).
  Section eyebrows are `text-[10px] uppercase tracking-wider font-bold text-slate-500`.
- **Icons**: `@lucide/vue` (pm-tool2 used `lucide-vue-next`, now deprecated upstream in favor
  of this) — reuse it rather than adding a second icon set.
- **Chrome**: thin custom scrollbars (`rgba(255,255,255,.14)` thumb), `focus-visible` rings in
  the accent color on every interactive element, `transition-colors` on hover states, modals as
  a fixed `bg-black/60 backdrop-blur` overlay + centered panel (see pm-tool2's `ModalShell.vue`).
- **App shell**: fixed-width sidebar (pm-tool2: `w-72`, dark, own border) + flex-1 content
  area — maps well onto this app's Mitarbeiter-list-as-sidebar + Wochenplanung-as-main-content
  shape from readme.md §15.

Applied at the shell level (`components/AppShell.vue`, `views/Login/LoginView.vue`,
`views/Employees/EmployeesView.vue`) and now `views/Schedule/ScheduleView.vue` — sidebar, glass
panels, gradient buttons, Inter. The `components/ModalShell.vue` pattern (fixed `bg-black/60
backdrop-blur` overlay, centered glass panel) is reused by both
`views/Employees/EmployeeDetailModal.vue` and `views/Schedule/ShiftAssignmentModal.vue`.
JetBrains Mono (`font-mono`) is now in real use for shift times and the Wochenansicht's hour
totals. `SettingsView` still picks up the theme via the shell but has no real content to style
yet. Issue #5 is closed; any further component-level parity work (e.g. Teams/ShiftTypes UI)
would be a new issue.
