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
- [`docs/plans/README.md`](docs/plans/README.md) — **рабочие планы** незапущенных фаз
  (Azure-деплой, страницы спортсмена/клуба, рекорды всех стран) + список открытых решений,
  ждущих ответа Влада. Читай перед стартом любой из этих работ, чтобы не планировать заново.
- [`docs/data-integrity.md`](docs/data-integrity.md) — **целостность данных**: инварианты,
  все слои проверок, журнал решений и инцидентов, чек-лист «вижу кривые данные», план
  единого реестра проверок (фазы Д1–Д5). Читай ПЕРВЫМ, когда «в протоколе одно, а в базе
  другое», и записывай туда решения по данным — это их единственное место.
- [`docs/relays.md`](docs/relays.md) — **эстафеты**: модель `RelayMembers`, импорт/парсинг,
  журнал фиксов, чек-лист отладки. Читай ПЕРВЫМ при любом баге эстафет.
- [`docs/media-page.md`](docs/media-page.md) — **подсистема медиа / My media**: модель
  `Sys_UserMedia`, API, потоки Add link, публикации, footguns. Читай при багах медиа.
- [`docs/competition-overview-cards.md`](docs/competition-overview-cards.md) — **карточки
  Overview соревнования**: что считает каждая (Top clubs, Best swim, Most decorated, High
  Point…), правила медалей/очков и чек-лист «цифра выглядит неправильно». Там же —
  **«Почему у одного соревнования ДВА набора мест»**: протокольные места официальные
  (медали дают за каждый день), Combine All Results — сравнительный вид. Читай перед
  любым расчётом мест, медалей или очков.

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
- **Имена пловцов и названия клубов на витрине — ИВРИТСКИЕ по умолчанию, всегда** (решение
  Влада, 28.08.2026).
  Английское имя показывается только как фоллбек (ивритского в базе нет) или по явному
  запросу языка; порядок в коде `FirstName/LastName` → `FirstNameEn/LastNameEn`, не наоборот.
  Правилу «видимый UI только на английском» это не противоречит: то правило про строки
  интерфейса, а имя человека — данные.

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

The admin panel (`/Admin/*`) and `wwwroot/admin-home.html` share one compiled bundle,
`wwwroot/css/admin.min.css`, built from `Styles/admin.css` (source: `@import "tailwindcss"` +
`@theme` tokens + `@source` scans of `Pages/Admin/**/*.cshtml`/`.cs` and `wwwroot/admin-home.html`).

```bash
cd server/Swimm.API
npm install          # once
npm run css:build    # regenerate wwwroot/css/admin.min.css after changing Tailwind classes
npm run css:watch    # rebuild on save while iterating
```

⚠️ **Не пиши в Admin-разметке `[#hex]/opacity`** (`border-[#66bb6a]/30`, `bg-[#4fc3f7]/10`).
Tailwind гонит такой цвет через oklab и печатает результат ЧИСЛАМИ
(`oklab(71.8488% -.115896 .0814871/.3)`), а последний разряд у него разный на Windows и на
Linux-раннере. Бандл коммитится и сверяется в CI **побайтово** (job «Бандл админки
пересобран»), поэтому такая строка роняет CI навсегда: пересборка на Windows возвращает
«свою» цифру. Пиши форму `[rgba(...)]` — `border-[rgba(102,187,106,0.3)]`: она компилируется
в обычный hex с альфой (`#66bb6a4d`), одинаковый везде. Пойман 31.08.2026, чинилось заменой
двух классов в `Api.cshtml` и `Index.cshtml`. (Токеновые `color-mix(in oklab, …)`, которые
Tailwind печатает текстом, безопасны — их считает браузер.)

`dotnet build` auto-runs `css:build` via an MSBuild target (`BeforeTargets="Build"`) **if**
`node_modules` exists; if it doesn't (CI/no-Node machines), the build just uses the already-committed
`admin.min.css`. That's why **`admin.min.css` is committed** — don't gitignore it, and re-run
`npm run css:build` and commit the result whenever you add new Tailwind classes to Admin pages or
`admin-home.html`. There is no other CSS for these pages — the old per-page stylesheets
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

**Least-privilege roles** (`server/db/01-roles.sql` → миграции → `server/db/02-grants.sql`;
порядок обязателен, см. ниже): owner `swimm` (DDL/migrations),
`swimm_rw` (runtime DML), `swimm_ro` (public read — `SELECT` on business tables only, no `Sys_*`).
Connection strings in `server/Swimm.API/appsettings.json`; each falls back to `DefaultConnection`
if unset. ⚠ **Порядок на чистой БД жёсткий и не переставляется:** `01-roles.sql` → `--migrate`
→ `02-grants.sql`. Гранты ссылаются на таблицы, которых до миграций нет, а миграции — на роль
`swimm_ro`, которой нет до первого шага. Список публичных таблиц живёт только в `02-grants.sql`.
Наполнение прод-БД — `dump-seed.sh` / `restore-seed.sh` (что переносится и что нет — в
`server/db/seed-tables.txt`). Read-only public path uses `SwimmReadDbContext`; everything that writes or touches
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
