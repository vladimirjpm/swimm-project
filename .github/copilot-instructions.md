# Copilot Instructions

> Architecture & coding conventions (the detailed reference). For a human quick-start see
> [`README.md`](../README.md); for the agent operating guide (build/run/migrate/test, footguns)
> see [`CLAUDE.md`](../CLAUDE.md).

## Project Overview

Mono-repo: React + TypeScript client (`client/`) и .NET 10 Web API сервер (`server/`).

---

## Client (`client/`)

**Stack:** React 18 · TypeScript · Vite · Redux Toolkit · Tailwind CSS

### Structure
- `src/pages/` — route-level pages
- `src/projects/` — feature modules
- `src/projects/components/` — shared UI components
- `src/types/`, `src/utils/interfaces/` — shared types
- `src/utils/helpers/` — utility functions
- `src/store/store.ts` — Redux store
- `public/data/` — static JSON data files
- `public/images/` — static images

### Conventions
- Strict TypeScript; prefer interfaces from `src/types/`
- Function components only; co-locate CSS with component
- All components in `src/projects/components/mix/` **must** use the `UI_` prefix (`UI_ClubIcon`, `UI_AgeLabel`, …). Без исключений.
- Kebab-case для папок, camelCase для файлов, PascalCase для компонентов

### Dev Workflow
- `npm run dev` — dev-сервер (http://localhost:5173)
- `npm run build` — production build → `dist/`

---

## Server (`server/`)

### Architecture — Clean Architecture

```
Swimm.Domain         ← POCO-сущности, без EF Core / ASP.NET
Swimm.Application    ← интерфейсы (Abstractions/), DTO (Dtos/), маппинг
Swimm.Infrastructure ← EF Core DbContext, миграции, реализации сервисов и репозиториев
Swimm.API            ← тонкие контроллеры, DI, middleware, Razor Pages admin
```

Зависимости направлены строго внутрь. Контроллеры инжектируют только интерфейсы из Application, никогда не Infrastructure-типы напрямую.

### EF Core Migrations

Есть **два** `DbContext` (`SwimmDbContext` — read-write + миграции; `SwimmReadDbContext` —
read-only публичный путь), поэтому EF-инструментам нужен `--context SwimmDbContext`.

После изменения сущностей или DbContext — добавить миграцию:

```
dotnet ef migrations add <Name> --project server/Swimm.Infrastructure --startup-project server/Swimm.API --context SwimmDbContext
```

Применить миграции (авто-применения при старте больше нет — это отдельный шаг):
```
# вариант A — через EF-инструменты (design-time factory подключается под owner-ролью):
dotnet ef database update --project server/Swimm.Infrastructure --startup-project server/Swimm.API --context SwimmDbContext

# вариант B — запустить приложение с флагом (применит и выйдет):
dotnet run --project server/Swimm.API -- --migrate
```

### DB-роли (least-privilege)

Рантайм работает под ограниченными ролями, миграции — под owner.
См. `server/db/01-roles.sql` (роли, до миграций) и `server/db/02-grants.sql` (гранты, после)
(запустить один раз на БД). Connection strings в appsettings:
- `MigrationConnection` (owner `swimm`) — DDL, только миграции;
- `AdminConnection` (`swimm_rw`) — DML рантайма (auth, admin, импорт), без DDL;
- `ReadConnection` (`swimm_ro`) — SELECT только на публичные бизнес-таблицы (browsing результатов).

Все три при отсутствии откатываются на `DefaultConnection`. **Read-репозитории публичного пути**
инжектируют `SwimmReadDbContext`; всё, что пишет или читает `Sys_*` таблицы — `SwimmDbContext`.

### Публичный read-API (миграция клиента со статики на БД)

Клиент исторически читал статические JSON из `client/public/data/`; идёт перевод на серверный API
(детальный план — в auto-memory агента, `data-source-server-migration`). Готовые read-эндпоинты
(через `SwimmReadDbContext` + кэш `ICacheService`; JSON-форма совпадает с клиентскими интерфейсами):
- `GET /api/results` — результаты, фильтры + пагинация (`{page,pageSize,hasMore,data}`); `data` =
  клиентский `Result`.
- `GET /api/club-points` — правила очков (`{rules:[...]}`), заменяет `club-points-config.json`.
- `GET /api/categories`, `GET /api/categories/{key}` — категории и их соревнования (заменили
  `sources-config*.json`, файл удалён). Клиент читает через `CategoryHelper` только для отображения
  (name/badge); канонические ключи категорий (`all`/`kids8_11`/`young11_14`/`juniors`/`adults`/`masters`) и URL-контракт
  остаются client-only в `results-categories.ts` — в БД их нет (`all` синтетический).

Модель данных под это:
- `Category` + `CategoryCompetition` (M:N) — какие соревнования в какой категории. Membership
  пишется НА ИМПОРТЕ (`ResultWrap.categories` — массив ключей категорий) и правится вручную.
- Флаги `IsMasters` / `IsAward` / `ShowCombineAllResults` — на `Competition` (ставятся импортом).
- `ClubPointsRule` + `ClubPointsRuleEntry` — система очков клубов.

Дев-связка client↔server: Vite-proxy в `client/vite.config.js` (`/api`, `/auth` →
`http://localhost:5078`) — относительные запросы клиента идут на API как same-origin.

### Conventions

- **Новый сервис** → интерфейс в `Swimm.Application/Abstractions/`, реализация в `Swimm.Infrastructure/Services/`, регистрация в `AddInfrastructure()`.
- **Новый репозиторий** → интерфейс в `Swimm.Application/Abstractions/`, реализация в `Swimm.Infrastructure/Repositories/`, регистрация в `AddInfrastructure()`.
- **Новая сущность или FK** → всегда добавить миграцию командой выше, не дожидаясь отдельной просьбы.
- **Контроллеры** не должны ссылаться на `SwimmDbContext` или любые Infrastructure-типы напрямую.
- **Новый/изменённый API-эндпоинт** → обновить страницу-справочник админки
  `server/Swimm.API/Pages/Admin/Api.cshtml` и её описание `docs/admin-pages/api.md`.
  (Старых `wwwroot/admin/*.html` больше нет — админка целиком на Razor Pages.)
- **Изменение `GET /api/admin/db-schema`** → обновить `server/Swimm.API/Pages/Admin/Db.cshtml`
  и `docs/admin-pages/db.md`.
- **Новая страница админки** → завести `docs/admin-pages/<route>.md` и строку в
  `docs/admin-pages/README.md` — это часть definition of done.
- **Кириллица битая (`?`)** в C#/SQL/XML-summary → переписать по смыслу на корректный русский.

### Dev Workflow

```
# API (Debug — Smart App Control обходится через dotnet dll, не apphost):
dotnet run --project server/Swimm.API

# PostgreSQL (compose-файл лежит в server/, не в корне):
docker compose -f server/docker-compose.yml up -d   # postgres:16, хост-порт 5445 (внутри 5432)

# Google OAuth:  server/Swimm.API/appsettings.Development.json  (gitignored)
```
