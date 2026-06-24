# Copilot Instructions

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

После изменения сущностей или DbContext — добавить миграцию:

```
dotnet ef migrations add <Name> --project server/Swimm.Infrastructure --startup-project server/Swimm.API
```

Применить вручную (в проде применяется автоматически через `IDbMigrator` при старте):
```
dotnet ef database update --project server/Swimm.Infrastructure --startup-project server/Swimm.API
```

### Conventions

- **Новый сервис** → интерфейс в `Swimm.Application/Abstractions/`, реализация в `Swimm.Infrastructure/Services/`, регистрация в `AddInfrastructure()`.
- **Новый репозиторий** → интерфейс в `Swimm.Application/Abstractions/`, реализация в `Swimm.Infrastructure/Repositories/`, регистрация в `AddInfrastructure()`.
- **Новая сущность или FK** → всегда добавить миграцию командой выше, не дожидаясь отдельной просьбы.
- **Контроллеры** не должны ссылаться на `SwimmDbContext` или любые Infrastructure-типы напрямую.
- **Новый/изменённый API-эндпоинт** → обновить `server/Swimm.API/wwwroot/admin/api.html`.
- **Изменение `GET /api/admin/db-schema`** → обновить `server/Swimm.API/wwwroot/admin/db.html`.
- **Кириллица битая (`?`)** в C#/SQL/XML-summary → переписать по смыслу на корректный русский.

### Dev Workflow

```
# API (Debug — Smart App Control обходится через dotnet dll, не apphost):
dotnet run --project server/Swimm.API

# PostgreSQL:
docker compose up -d   # postgres:16, порт 5432

# Google OAuth:  server/Swimm.API/appsettings.Development.json  (gitignored)
```
