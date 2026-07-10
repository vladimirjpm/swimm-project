# Задание (Sonnet 5): фаза 3, этап 3.1 — HTTP-кэш (ETag + Cache-Control) на публичных GET

Контекст: фаза 3 роадмапа ([docs/ROADMAP.md](../ROADMAP.md)) — «Масштаб и кэш». Образец
уже работает в проде-коде: `RecordsController.CachedJson` (ETag + `Cache-Control` +
кэш сериализованного JSON в `ICacheService`, 304 по `If-None-Match`). Твоя работа —
вынести паттерн в общий хелпер и накрыть им остальные публичные GET. Работа механическая,
формы ответов **не меняются ни на байт**.

## Что уже готово (не переделывать)

- `RecordsController` (`server/Swimm.API/Controllers/RecordsController.cs`) — эталон:
  приватный `CachedJson<T>(cacheKey, load)` + `CachedPayload(Json, ETag)`, max-age=300,
  payload TTL 24ч. Комментарии в нём объясняют компромиссы — прочитай перед началом.
- Инвалидация уже глобальная: админ-мутации и импорт зовут `ICacheService.InvalidateAllAsync()`
  (токен-инвалидация в `MemoryCacheService`) — записи хелпера вылетают вместе со всеми.
  Ничего дополнительно инвалидировать не нужно, но проверь по месту, что импорт результатов
  действительно дергает InvalidateAllAsync — если нет, скажи, не чини молча.
- Репозиторные кэши под этими эндпоинтами (TTL 2–10 мин в `ResultRepository` и др.) —
  не трогать, это второй слой.

## Шаг 1 — общий хелпер

Вынеси `CachedJson` из `RecordsController` в переиспользуемое место, например
`server/Swimm.API/Http/CachedJsonExtensions.cs` — extension-метод на `ControllerBase`:

```csharp
Task<IActionResult> CachedJson<T>(this ControllerBase c, ICacheService cache,
    string cacheKey, Func<Task<T>> load, TimeSpan payloadTtl, string cacheControl)
```

- Логика 1-в-1 из эталона (SHA-256 → ETag, 304 на If-None-Match, `Content(json)`).
- `RecordsController` переводится на хелпер **без изменения поведения**: те же ключи
  (`http:records:…`), тот же max-age=300, тот же TTL 24ч. Это регресс-защита: если после
  рефакторинга curl-ответы Records отличаются — ты что-то сломал.
- Сериализация: `JsonSerializer.Serialize` без опций — snake_case у нас через
  `[JsonPropertyName]` на DTO, глобальной naming policy нет. Не добавляй опций.

## Шаг 2 — накрыть эндпоинты

| Эндпоинт | Где | Ключ (префикс `http:`) | payload TTL | max-age |
|---|---|---|---|---|
| `GET /api/competitions` | `ResultsController.GetSources` | `http:competition-sources` | 5 мин | 60 |
| `GET /api/categories` и `GET /api/categories/{key}` | `CategoriesController` | `http:categories[:key]` | 1 ч | 300 |
| `GET /api/club-points` | `ClubPointsController` | `http:club-points` | 1 ч | 300 |
| `GET /api/athletes/career?name=` | `ResultsController.GetAthleteCareer` | `http:athlete-career:{name.Trim().ToLowerInvariant()}` | 5 мин | 60 |
| `GET /api/results/filter-hints?field=&q=&limit=` | `ResultsController.GetFilterHints` | `http:filter-hints:{field}:{q}:{limit}` | 5 мин | 60 |

- Валидации/BadRequest-ветки остаются ДО хелпера (400 не кэшируем).
- `athletes/career`: пустой `name` → 400 как сейчас; «пловец не найден» — сейчас отдаётся
  нулевой DTO, он тоже кэшируется (это ок).
- TTL-логика: max-age маленький (браузер), payload TTL больше (сервер отдаёт 304 дёшево).
  Числа из таблицы — стартовые, если видишь причину другие — напиши почему в комментарии.
- `GET /api/results` (страницы результатов) в 3.1 НЕ трогаем — у него своя пагинация и
  кэш в репозитории; ETag на него — отдельное решение.

## Шаг 3 — не сломать клиент

Клиентские хелперы (`club-points-helper.ts`, `category-helper.ts`, `records-helper.ts`,
`filter-data-source-ddl.tsx`) используют обычный `fetch` — браузер сам обработает ETag/304,
код менять не надо. Но прогони смоук вживую (см. приёмку), особенно селектор соревнований.

## Приёмка

- `dotnet build` + `dotnet test` зелёные.
- Смоук по каждому эндпоинту из таблицы (API поднят, см. `/smoke-test-api`):
  1. `curl -si <url>` → 200, есть `ETag` и `Cache-Control`;
  2. повторно с `-H 'If-None-Match: <etag>'` → **304 без тела**;
  3. тело первого ответа **байт-в-байт** совпадает с ответом до твоих правок
     (сними эталоны ДО рефакторинга).
- Инвалидация: правка в админке (например, категории) → следующий запрос отдаёт новый
  ETag и свежие данные.
- `RecordsController` после перевода на хелпер: `GET /api/records?region=ISR` — поведение
  идентично (заголовки, 304, тело).

## Правила репо

- RU-комментарии/EN-идентификаторы; не коммитить без просьбы; контроллеры инжектят только
  интерфейсы Application. Формы ответов и маршруты не менять — чего-то не хватает → спроси.
