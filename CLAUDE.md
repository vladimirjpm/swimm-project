# CLAUDE.md — agent operating guide

Primary instructions for AI coding agents working in this repo (Claude Code is the main one).
Keep this file the single source of agent guidance; other agents are pointed here via `AGENTS.md`.

**Read these too — don't duplicate them here:**
- [`README.md`](README.md) — what the project is + human quick-start.
- [`.github/copilot-instructions.md`](.github/copilot-instructions.md) — architecture (Clean
  Architecture layering) and coding conventions. Follow it for where code goes and naming.
- [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) — целевая архитектура: швы (интерфейсы),
  стратегия кэша, бэклог рефакторинга. Сверяйся перед структурными изменениями.
- [`docs/ROADMAP.md`](docs/ROADMAP.md) — план работ по фазам (parsing-lib, рекорды в БД,
  масштаб/кэш, логин, галереи, isr.org.il, админка). Отмечай выполненные этапы.
- [`docs/relays.md`](docs/relays.md) — **эстафеты**: модель `RelayMembers`, импорт/парсинг,
  журнал фиксов, чек-лист отладки. Читай ПЕРВЫМ при любом баге эстафет.
- [`docs/media-page.md`](docs/media-page.md) — **подсистема медиа / My media**: модель
  `Sys_UserMedia`, API, потоки Add link, публикации, footguns. Читай при багах медиа.
- [`docs/competition-overview-cards.md`](docs/competition-overview-cards.md) — **карточки
  Overview соревнования**: что считает каждая (Top clubs, Best swim, Most decorated, High
  Point…), правила медалей/очков и чек-лист «цифра выглядит неправильно».

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

### Admin pages map

`docs/admin-pages/` holds one MD per admin page (naming: route without `/Admin/`,
lowercase, `/` → `-`; index — `README.md`). **Read the page's MD before touching an
admin page; when adding a page, add its MD + a row in the README table** — part of
definition of done.

### Admin/home CSS (Tailwind v4)

The admin panel (`/Admin/*`) and `wwwroot/home.html` share one compiled bundle,
`wwwroot/css/admin.min.css`, built from `Styles/admin.css` (source: `@import "tailwindcss"` +
`@theme` tokens + `@source` scans of `Pages/Admin/**/*.cshtml`/`.cs` and `wwwroot/home.html`).

```bash
cd server/Swimm.API
npm install          # once
npm run css:build    # regenerate wwwroot/css/admin.min.css after changing Tailwind classes
npm run css:watch    # rebuild on save while iterating
```

`dotnet build` auto-runs `css:build` via an MSBuild target (`BeforeTargets="Build"`) **if**
`node_modules` exists; if it doesn't (CI/no-Node machines), the build just uses the already-committed
`admin.min.css`. That's why **`admin.min.css` is committed** — don't gitignore it, and re-run
`npm run css:build` and commit the result whenever you add new Tailwind classes to Admin pages or
`home.html`. There is no other CSS for these pages — the old per-page stylesheets
(`admin-all.css`, `db.css`, `import.css`, `settings.css`, `api.css`, `home.css`, `db-banner.css`)
were removed; anything they styled either became Tailwind utility classes inline or was folded into
`Styles/admin.css`'s `@layer components`.

## Database (local)

Local Postgres runs in Docker (`swimm-postgres`, db `swimm`, owner `swimm`/`swimm_local_dev`).

⚠️ **Хост-порт — `5445`, не дефолтный 5432** (5432/5433 заняты контейнерами других проектов
на машине Влада). Внутри контейнера порт обычный 5432, поэтому `docker exec … psql` работает
как раньше, а вот подключения **с хоста** (DBeaver/pgAdmin, `psql -h localhost`, строки
подключения) должны идти на 5445. Источник правды — `ports:` в `server/docker-compose.yml`;
локальные строки подключения в `server/Swimm.API/appsettings.json` уже на 5445.

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
