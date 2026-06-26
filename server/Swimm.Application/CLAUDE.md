# CLAUDE.md — Swimm.Application (граница Clean Architecture)

Мини-страж, подгружается при работе в слое Application. Полная архитектура и конвенции —
в [`../../.github/copilot-instructions.md`](../../.github/copilot-instructions.md); не дублируй
их сюда. Здесь только правила границы, которые легко нарушить.

## Правило зависимостей (внутрь)

```
Domain  ←  Application  ←  Infrastructure
                       ←  API
```

- `Swimm.Application.csproj` ссылается **только** на `Swimm.Domain` (+ DI.Abstractions).
  Не добавляй сюда ссылку на `Swimm.Infrastructure`, EF Core, Npgsql, ASP.NET — это разворачивает
  стрелку зависимостей. Если нужен I/O — объяви **интерфейс здесь**, реализуй в Infrastructure.
- Контроллеры в `Swimm.API` инжектят **только интерфейсы из этого слоя** (`Abstractions/`),
  никогда конкретные типы Infrastructure.

## Что куда класть

- `Abstractions/` — порты (`I*Repository`, `I*Service`, `ICacheService`, `IEmailSender`, …).
  Реализации живут в `Swimm.Infrastructure/{Repositories,Services}`.
- `Dtos/` — контракты на вход/выход use-case'ов. Доменные entity (`Swimm.Domain/Entities`)
  наружу из API не отдаём — мапим в DTO.
- `Mapping/` — ручной маппинг entity ↔ DTO (см. `ResultMapping.cs`).

## Footguns

- `DependencyInjection.AddApplication()` сейчас пустой (заглушка) — это нормально; регистрация
  реализаций идёт в `Swimm.Infrastructure/DependencyInjection.cs`. Добавляй сюда только то, что
  реально живёт в Application (валидаторы, pipeline-поведения), не реализации портов.
- Два `DbContext` — деталь Infrastructure; Application про них знать не должен (работает через
  интерфейсы репозиториев). Команды миграций — в корневом [`../../CLAUDE.md`](../../CLAUDE.md).
