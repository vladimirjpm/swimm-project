---
name: db-migrate
description: Add or apply an EF Core migration for this repo. Use whenever the user wants to create a migration, scaffold a schema change, or apply pending migrations to the local Postgres. Handles the two-DbContext footgun (always passes --context SwimmDbContext) and reminds about swimm_ro grants for new public tables.
---

# db-migrate — EF Core миграции (Swimm)

В репо **два `DbContext`**, поэтому каждая EF-команда обязана нести `--context SwimmDbContext`.
Без флага команда падает с ошибкой «More than one DbContext was found».

## Шаги

### 1. Создать миграцию
```bash
dotnet ef migrations add <Name> \
  --project server/Swimm.Infrastructure --startup-project server/Swimm.API --context SwimmDbContext
```

### 2. Просмотреть сгенерированное (обязательно)
Открой `server/Swimm.Infrastructure/Migrations/<timestamp>_<Name>.cs`. Проверь, что Up/Down
соответствуют намерению.

**Если добавлена новая публичная (бизнес-) таблица**, её должна читать роль `swimm_ro`.
Добавь в миграцию data/grant SQL:
```csharp
migrationBuilder.Sql("GRANT SELECT ON \"<TableName>\" TO swimm_ro;");
```
(`Sys_*`-таблицы роли `swimm_ro` НЕ выдаём — только владелец/`swimm_rw`.)

### 3. Применить
Любой из вариантов:
```bash
# EF tool (через design-time factory = владелец swimm):
dotnet ef database update \
  --project server/Swimm.Infrastructure --startup-project server/Swimm.API --context SwimmDbContext

# или запустить приложение с флагом (применяет и выходит):
dotnet run --project server/Swimm.API -- --migrate
```
Авто-миграции на старте НЕТ — это всегда явный шаг.

## Footguns
- Перед сборкой убедись, что не висит `dotnet run` (см. build-lock в корневом `CLAUDE.md`):
  иначе `Swimm.API.dll is locked`.
- Локальная БД должна быть поднята: `docker compose -f server/docker-compose.yml up -d`.
- Откат: `dotnet ef migrations remove --project server/Swimm.Infrastructure --startup-project server/Swimm.API --context SwimmDbContext` (только если ещё не применена в shared-окружении).
