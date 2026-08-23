# Schichtplaner

Full concept (German, source of truth for architecture/data model/decisions): [readme.md](readme.md).
§23–26 cover security/login, external API keys, Docker+Postgres, and deployment.

## Current state

readme.md §22 "Phase 1: Foundation" backend is done (Employee, Team, Contract, ShiftType +
migration + controllers). Frontend UI for it is not — that's next. What's built:

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
    `OnModelCreating`, not just app-level checks. Not yet applied to any real database — no
    Postgres has been stood up to run `--migrate` against.
  - `Api/Controllers/`: `EmployeesController` (full CRUD), `TeamsController` (GET/POST — matches
    §18's minimal cut exactly, no PUT/DELETE), `ShiftTypesController` (GET/POST/PUT, no DELETE,
    matching §18), `ContractsController` (full CRUD, nested under
    `/api/employees/{id}/contracts` for list/create — §18 doesn't spell this one out but Contract
    is a named Phase 1 entity so it needs *some* API surface). Plain CRUD, no
    Application-layer services yet — there's no business logic to put there until Phase 3
    validation exists, so controllers talk to `ApplicationDbContext` directly.
  - `Api/Authorization/ApiWriteRequirement.cs` + two new policies in Program.cs (`ApiRead`,
    `ApiWrite`): both JWT and `X-Api-Key` can hit these endpoints per readme.md §24, but an
    API key needs `Scope: ReadWrite` to pass `ApiWrite` — previously the `ApiKeyScope` enum
    existed but nothing enforced it. No Admin/Manager role split yet (§23's three roles aren't
    seeded or checked anywhere) — that's still open.
- **Frontend** (`frontend/`): Vite + Vue 3 + TS + Tailwind v4 + Pinia + Vue Router + Axios.
  Three routes (`/`, `/employees`, `/settings`) each render a bare `<h1>` — no real UI yet.
  `services/api.ts` has JWT-attach + 401-refresh wired, but nothing calls it. Nothing calls the
  new Phase 1 endpoints yet either.
- **Docker/deploy**: `docker-compose.yml` (db/api/web) validated with `docker compose config`,
  never actually deployed. No `.env` exists anywhere yet (only `.env.example`).

## Run it locally

Frontend only (this is what there is to look at right now):
```bash
cd frontend && npm install && npm run dev   # http://localhost:5173
```

Backend build/test (no local SDK — use the container):
```bash
docker run --rm -v "$PWD":/src -w /src mcr.microsoft.com/dotnet/sdk:10.0 dotnet build ShiftPlanner.sln
```

Full stack: `docker compose up` needs a `.env` first (copy `.env.example`, fill in real
values — see docker-compose.yml for which vars).

## Deploy target

`shifty.vi0lins.de`, same VPS as [vanspace3d](https://github.com/mycaravam-crypto/vanspace3d),
behind that host's existing shared Caddy instance (not a containerized one — see the header
comment in [deploy/Caddyfile](deploy/Caddyfile) for the one-time host-side snippet still
needed). Deploy pipeline is [.github/workflows/deploy.yml](.github/workflows/deploy.yml) →
[deploy/deploy.sh](deploy/deploy.sh), triggered on push to `main`.

**Not yet done, needed before the pipeline can run:**
- GitHub repo secrets: `SHIFTPLANNER_DEPLOY_SSH_KEY`, `SHIFTPLANNER_DEPLOY_HOST`, `SHIFTPLANNER_DEPLOY_PATH`
- `.env` created on the server at `$SHIFTPLANNER_DEPLOY_PATH/.env` (from `.env.example`)
- The one-time host Caddy block from `deploy/Caddyfile`'s header comment
- DNS: `shifty.vi0lins.de` → the server

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
- **Icons**: `lucide-vue-next` (pm-tool2's choice) — reuse it rather than adding a second
  icon set.
- **Chrome**: thin custom scrollbars (`rgba(255,255,255,.14)` thumb), `focus-visible` rings in
  the accent color on every interactive element, `transition-colors` on hover states, modals as
  a fixed `bg-black/60 backdrop-blur` overlay + centered panel (see pm-tool2's `ModalShell.vue`).
- **App shell**: fixed-width sidebar (pm-tool2: `w-72`, dark, own border) + flex-1 content
  area — maps well onto this app's Mitarbeiter-list-as-sidebar + Wochenplanung-as-main-content
  shape from readme.md §15.

Not yet applied to the scaffold — the three placeholder views (`ScheduleView.vue` etc.) are
still unstyled `<h1>`s. Bring in `lucide-vue-next` and the Inter/JetBrains Mono font links
(see vanspace3d's `index.html` head) when building the real UI, not before.

## Next step

Phase 1 backend is done but unverified against a real database — no Postgres has been stood
up yet to run `dotnet run -- --migrate` against and smoke-test the controllers. After that:
wire the Employees view (frontend) to the new `/api/employees` + `/api/teams` endpoints, per
readme.md §17 and the visual design direction below. Role-based authorization (Admin vs
Manager, readme.md §23) is still unimplemented — every write today just needs *any*
authenticated JWT or a ReadWrite API key.
