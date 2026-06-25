# Swimm

Swimming competition results & statistics platform — a public results browser plus an admin
panel for importing and managing data.

Mono-repo: a **React + TypeScript** client and a **.NET 10 Web API** server (Clean Architecture)
backed by **PostgreSQL**.

> Detailed architecture and coding conventions live in
> [`.github/copilot-instructions.md`](.github/copilot-instructions.md). This README is the
> practical entry point: what the project is and how to run it.

---

## Stack

| Layer    | Tech |
|----------|------|
| Client   | React 18 · TypeScript · Vite · Redux Toolkit · Tailwind CSS |
| Server   | .NET 10 · ASP.NET Core Web API + Razor Pages (admin) · EF Core |
| Database | PostgreSQL 16 |
| Auth     | Cookie sessions · Google OAuth · local email + password |

## Repository layout

```
client/                     React + TypeScript SPA (public results browser)
server/
  Swimm.Domain/             POCO entities (no EF/ASP.NET)
  Swimm.Application/        Interfaces (Abstractions/), DTOs, mapping
  Swimm.Infrastructure/     EF Core DbContext, migrations, services, repositories
  Swimm.API/                Controllers, DI, middleware, Razor Pages admin
  Swimm.Parser/             Standalone parsing utility
  db/setup-roles.sql        Least-privilege DB roles (run once per database)
  docker-compose.yml        Local PostgreSQL
```

---

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/)
- [Node.js 18+](https://nodejs.org/)
- [Docker](https://www.docker.com/) (for local PostgreSQL)
- EF Core tools: `dotnet tool install --global dotnet-ef`

## Quick start

```bash
# 1. Start PostgreSQL
docker compose -f server/docker-compose.yml up -d

# 2. Create least-privilege DB roles (once per database)
docker exec -i swimm-postgres psql -U swimm -d swimm < server/db/setup-roles.sql

# 3. Apply migrations (owner role; applies and exits)
dotnet run --project server/Swimm.API -- --migrate

# 4. Run the API  → http://localhost:5078
dotnet run --project server/Swimm.API

# 5. Run the client → http://localhost:5173
cd client && npm install && npm run dev
```

The admin panel is served by the API at **`/admin`** (requires the `Admin` role).

---

## Database & migrations

Two `DbContext`s exist (`SwimmDbContext` = read-write + migrations; `SwimmReadDbContext` =
read-only public path), so EF tooling needs `--context SwimmDbContext`:

```bash
# add a migration
dotnet ef migrations add <Name> \
  --project server/Swimm.Infrastructure --startup-project server/Swimm.API \
  --context SwimmDbContext

# apply (either the EF tool, or `dotnet run ... -- --migrate`)
dotnet ef database update \
  --project server/Swimm.Infrastructure --startup-project server/Swimm.API \
  --context SwimmDbContext
```

### Connection strings (least privilege)

Configured in `server/Swimm.API/appsettings.json`; each falls back to `DefaultConnection` if unset:

| Name                  | Role        | Used by |
|-----------------------|-------------|---------|
| `MigrationConnection` | owner       | DDL / migrations only |
| `AdminConnection`     | `swimm_rw`  | runtime writes (auth, admin, import) — no DDL |
| `ReadConnection`      | `swimm_ro`  | public results browsing — `SELECT` on business tables only |

Adding a **new public table** read by anonymous users? Add a `GRANT SELECT` line for `swimm_ro`
in [`server/db/setup-roles.sql`](server/db/setup-roles.sql) (the read role is fail-closed by design).

---

## Authentication & security

- **Sign-in:** Google OAuth (`/auth/login/google`) and local email + password
  (`/auth/register`, `/auth/login/local`, `/auth/verify-email`, `/auth/forgot-password`,
  `/auth/reset-password`).
- **Passwords:** Argon2id (OWASP parameters), stored with a self-describing hash + algorithm tag.
- **Session revocation:** a `SecurityStamp` on each user is written into the cookie and
  re-validated (`OnValidatePrincipal`); it's bumped on deactivation, role change, password reset,
  and "sign out everywhere" (`POST /auth/logout-all`) — invalidating existing sessions.
- **Account linking:** external logins are matched by `(provider, providerKey)`; merging into an
  existing email account requires a provider-verified email (prevents account takeover).
- **Hardening:** least-privilege DB roles (above), antiforgery on admin mutations, rate limiting
  on auth endpoints, account lockout, large imports processed by a background worker.

Email (verification / password reset) is sent via `IEmailSender`. The dev implementation
(`LoggingEmailSender`) logs the link to the console; wire a real SMTP/provider implementation for
production.

---

## Configuration notes

- Google OAuth needs `Authentication:Google:ClientId` + `ClientSecret` (secret kept out of source —
  use user-secrets / environment variables). Without them, Google sign-in is disabled and the app
  still runs.
- Committed `*_local_dev` passwords are for the local Docker database only.
