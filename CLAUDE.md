# CLAUDE.md — agent operating guide

Primary instructions for AI coding agents working in this repo (Claude Code is the main one).
Keep this file the single source of agent guidance; other agents are pointed here via `AGENTS.md`.

**Read these too — don't duplicate them here:**
- [`README.md`](README.md) — what the project is + human quick-start.
- [`.github/copilot-instructions.md`](.github/copilot-instructions.md) — architecture (Clean
  Architecture layering) and coding conventions. Follow it for where code goes and naming.

This file covers only what those don't: how to build, run, migrate, and test **in practice here**,
plus repo-specific footguns.

**Nested guides (auto-loaded when you work in those dirs):**
- [`client/CLAUDE.md`](client/CLAUDE.md) — frontend stack, `src/` map, feature pattern, footguns.
- [`server/Swimm.Application/CLAUDE.md`](server/Swimm.Application/CLAUDE.md) — Clean Architecture
  boundary rules for the Application layer.

**Skills for recurring multi-step ops:** `/db-migrate` (EF migrations with the two-context flag),
`/smoke-test-api` (build → run → poll → curl → stop, handles the build-lock).

---

## Golden rules

- **Match the surrounding code.** Clean Architecture layering is enforced: dependencies point
  inward, controllers inject only `Swimm.Application` interfaces — never `Swimm.Infrastructure`
  types directly.
- **Don't commit or push unless asked.** Default branch is `master`; branch first if asked to commit.
- **Comments/identifiers in this codebase are bilingual (RU prose, EN identifiers).** Mirror the
  local style of the file you edit.

## Build & run

```bash
# build the whole server solution
dotnet build server/Swimm.sln

# run the API (Development) → http://localhost:5078
ASPNETCORE_ENVIRONMENT=Development dotnet run --project server/Swimm.API --urls http://localhost:5078
```

⚠️ **Build-lock footgun (happens constantly):** if `dotnet build` fails with
`MSB3027 / MSB3021 … "Swimm.API.dll" is locked by ".NET Host (<pid>)"`, a previous
`dotnet run` is still alive. Kill it, then rebuild:

```powershell
Stop-Process -Id <pid> -Force
# or sweep all: Get-CimInstance Win32_Process -Filter "Name='Swimm.API.exe'" | ForEach-Object { Stop-Process -Id $_.ProcessId -Force }
```

When smoke-testing, run the API in the background, poll `/auth/me` until it returns 200, curl the
endpoints, then **stop the process** so the next build isn't locked.

## Database (local)

Local Postgres runs in Docker (`swimm-postgres`, db `swimm`, owner `swimm`/`swimm_local_dev`):

```bash
docker compose -f server/docker-compose.yml up -d
docker exec -it swimm-postgres psql -U swimm -d swimm          # interactive
docker exec swimm-postgres psql -U swimm -d swimm -c '<SQL>'   # one-off (note the quoted "Identifiers")
```

**Least-privilege roles** (`server/db/setup-roles.sql`, run once): owner `swimm` (DDL/migrations),
`swimm_rw` (runtime DML), `swimm_ro` (public read — `SELECT` on business tables only, no `Sys_*`).
Connection strings in `server/Swimm.API/appsettings.json`; each falls back to `DefaultConnection`
if unset. Read-only public path uses `SwimmReadDbContext`; everything that writes or touches
`Sys_*` uses `SwimmDbContext`.

## Migrations

There are **two `DbContext`s**, so every EF command needs `--context SwimmDbContext`:

```bash
dotnet ef migrations add <Name> \
  --project server/Swimm.Infrastructure --startup-project server/Swimm.API --context SwimmDbContext

# apply: EF tool (uses owner via design-time factory) …
dotnet ef database update \
  --project server/Swimm.Infrastructure --startup-project server/Swimm.API --context SwimmDbContext
# … or run the app with the flag (applies and exits):
dotnet run --project server/Swimm.API -- --migrate
```

There is **no auto-migrate on startup** — applying migrations is an explicit step.
After scaffolding, review the generated migration (and add data-backfill / grant SQL if a new
public table must be readable by `swimm_ro`).

## Auth & security model (already built — extend, don't reinvent)

Google OAuth + local email/password over `Sys_UserLocalCredentials`. Key invariants:
- Cookie carries a `SecurityStamp` re-validated in `CookieSecurityStampValidator`
  (`OnValidatePrincipal`); bump `AppUser.SecurityStamp` to revoke sessions (deactivation, role
  change, password reset, logout-all).
- External logins are matched by `(Provider, ProviderKey)`; email-based merge requires a
  provider-verified email.
- Local passwords use Argon2id (`Argon2PasswordHasher`); tokens are stored as SHA-256 hashes,
  single-use, in `Sys_UserSecurityTokens`. Emails go through `IEmailSender` (dev logs the link).
- Admin mutations require antiforgery; auth endpoints are rate-limited.

Anything touching authentication or access boundaries is security-sensitive — be deliberate.
