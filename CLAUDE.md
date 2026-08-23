# Schichtplaner

Full concept (German, source of truth for architecture/data model/decisions): [readme.md](readme.md).
§23–26 cover security/login, external API keys, Docker+Postgres, and deployment.

Ongoing/planned work lives in [GitHub Issues](https://github.com/mycaravam-crypto/shifty/issues),
not here and not in readme.md — readme.md stays a stable concept doc, this file stays a factual
snapshot of what exists right now.

## Current state

readme.md §22 "Phase 1: Foundation" backend is done (Employee, Team, Contract, ShiftType +
migration + controllers). Frontend UI for it is not. What's built:

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
    "mögliche Schichten" (readme.md §3) — GET/PUT `/api/employees/{id}/eligible-shift-types`.
    Data model only, no eligibility validator yet — that needs `ShiftAssignment` (Phase 2),
    which doesn't exist ([issue #6](https://github.com/mycaravam-crypto/shifty/issues/6)).
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
    (`/employees/{id}/contracts`, `/contracts/{id}`). Teams/ShiftTypes management still has
    no frontend at all — only reachable today via Swagger/API key.
  - `components/AppShell.vue` — sidebar nav (Dienstplan/Mitarbeiter/Einstellungen) + user
    identity + logout, applying CLAUDE.md's "Visual design" tokens (dark glass, Inter,
    blue→indigo accent). `ScheduleView`/`SettingsView` are styled but still minimal placeholders
    — this is a functional cut of [issue #5](https://github.com/mycaravam-crypto/shifty/issues/5),
    not the full pm-tool2/vanspace3d component-level parity pass.
  - Verified end-to-end in a real (headless) browser against the local stack below: login
    success/failure, employee list load, create, 409-conflict surfaced in the UI, logout.
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

`shifty.vi0lins.de`, same VPS as [vanspace3d](https://github.com/mycaravam-crypto/vanspace3d),
behind that host's existing shared Caddy instance (not a containerized one — see the header
comment in [deploy/Caddyfile](deploy/Caddyfile) for the one-time host-side snippet still
needed). Deploy pipeline is [.github/workflows/deploy.yml](.github/workflows/deploy.yml) →
[deploy/deploy.sh](deploy/deploy.sh), triggered on push to `main`. The pipeline isn't runnable
yet — see [issue #4](https://github.com/mycaravam-crypto/shifty/issues/4) for what's missing.

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
`views/Employees/EmployeesView.vue`) — sidebar, glass panels, gradient buttons, Inter. The
`components/ModalShell.vue` + `views/Employees/EmployeeDetailModal.vue` pair brings the
pm-tool2 modal pattern (fixed `bg-black/60 backdrop-blur` overlay, centered glass panel) in
too. `ScheduleView`/`SettingsView` pick up the theme via the shell but have no real content to
style yet; JetBrains Mono is loaded but unused until shift-time data exists. Issue #5 is closed;
any further component-level parity work (e.g. Teams/ShiftTypes UI) would be a new issue.
