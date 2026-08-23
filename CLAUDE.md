# Schichtplaner

Full concept (German, source of truth for architecture/data model/decisions): [readme.md](readme.md).
§23–26 cover security/login, external API keys, Docker+Postgres, and deployment.

## Current state

Scaffold only — nothing from readme.md §22 "Phase 1: Foundation" (Employee, Team,
Contract, ShiftType) exists yet. What's built:

- **Backend** (`src/`): 4-project skeleton (Domain → Application → Infrastructure → Api)
  matching readme.md §19/§20. Builds clean (`dotnet build ShiftPlanner.sln`, verified via
  the .NET 10 SDK — **no dotnet SDK installed on this machine**, use the Docker container
  shown below). Program.cs wires Postgres/EF Core, ASP.NET Identity, JWT + API-key auth
  schemes, rate limiting, CORS, Swagger, `/health`, and `--migrate`. No domain entities,
  no controllers yet — only the `ApiKey`/`AuditLog` support types and Identity's own tables.
- **Frontend** (`frontend/`): Vite + Vue 3 + TS + Tailwind v4 + Pinia + Vue Router + Axios.
  Three routes (`/`, `/employees`, `/settings`) each render a bare `<h1>` — no real UI yet.
  `services/api.ts` has JWT-attach + 401-refresh wired, but nothing calls it.
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

## Next step

readme.md §22 Phase 1: Employee, Team, Contract, ShiftType domain entities + EF Core
migrations + the first real controllers — the scaffold has nowhere to apply a migration
against yet.
