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
— issues #8/#9, see below). Phase 4 (Usability: week-copy/filters/search/shortcuts) has
just started — only week-copy exists so far, frontend-only. What's built:

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
    /employees/{id}/eligible-shift-types`), and Contract list/create/delete
    (`/employees/{id}/contracts`, `/contracts/{id}`).
  - `views/Stammdaten/StammdatenView.vue` — Teams (list + create only; the backend has no
    `PUT`/`DELETE` for Teams) and ShiftTypes (list + create + click-to-edit via
    `views/Stammdaten/ShiftTypeDetailModal.vue`, since `ShiftTypesController` does have a
    `PUT`) on one page, mirroring `EmployeesView.vue`'s list/create pattern. Reachable at
    `/stammdaten` in the sidebar nav — previously this data was only reachable via
    Swagger/API key.
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
    chips are native-HTML5-`draggable` (no drag-and-drop library added — none was installed,
    and this is a simple day×employee matrix); dropping a palette chip on a cell `POST`s a new
    assignment from the ShiftType's template times, dropping an existing chip on a different
    cell `PUT`s the same assignment onto the new employee/date (a move). No touch/mobile drag
    support (accepted gap — desktop-only per readme.md §16's own interaction model). Each
    employee row shows a `font-mono` "Xh / Yh ⚠" hour readout + progress bar — sums the
    backend's already-computed `netHours` (never re-derives the subtraction client-side),
    target hours scaled from the active Contract's `WeeklyHours` by the visible month's day
    count ÷ 7 (a Contract still only defines a weekly figure — no separate monthly-target field
    was added). Clicking an assignment chip opens `views/Schedule/ShiftAssignmentModal.vue`
    (`ModalShell`-based, mirrors `EmployeeDetailModal.vue`'s shape) to change ShiftType/times/
    break or delete — content edits only, moving stays drag-only, and it has no create mode.
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
    the palette + month nav already cover that. Shortcuts/optimized drag-and-drop (the rest of
    Phase 4) aren't started.
  - `components/AppShell.vue` — sidebar nav (Dienstplan/Mitarbeiter/Stammdaten/Einstellungen) +
    user identity + logout, applying CLAUDE.md's "Visual design" tokens (dark glass, Inter,
    blue→indigo accent). `SettingsView` is still a styled-but-minimal placeholder — this is a
    functional cut of [issue #5](https://github.com/mycaravam-crypto/shifty/issues/5), not the
    full pm-tool2/vanspace3d component-level parity pass.
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
    none of this has actually been clicked through in a browser yet.
    interactively exercised in a browser (no local Team data exists yet to click through
    either).
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
