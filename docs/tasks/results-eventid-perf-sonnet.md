# Задание Sonnet — фикс перф-обрыва `/api/results?eventId=`

## 1. Контекст

Публичный endpoint `GET /api/results?eventId=<N>` (листает страницы результатов
соревнования/события) на боевом объёме данных отдаёт страницу за **6–9 секунд** вместо
миллисекунд. Причина найдена через `EXPLAIN (ANALYZE)` на локальной БД с 3 млн строк
(синтетика нагрузочного теста; у события всего ~915 строк):

Сейчас фильтр по событию в `BuildFilteredQueryAsync` выражен через навигацию:
```csharp
if (filter.EventId.HasValue)
    query = query.Where(r => r.Competition.EventId == filter.EventId.Value);
```
Из-за `ORDER BY CompetitionDate DESC ... OFFSET/LIMIT` планировщик Postgres выбирает
«ловушку»: идёт по индексу `IX_Results_CompetitionDate` (чтобы не сортировать) и
**прочёсывает ВСЮ таблицу** (3 млн строк), для каждой строки через JOIN проверяя
`Competition.EventId`. 915 нужных строк «зарыты» глубоко → сканируется почти вся таблица.

`EXPLAIN` (факт):
- как сейчас (JOIN по `EventId`): **~6–9 с**, `Index Scan Backward ... rows removed 3 012 239`;
- переписать на `WHERE CompetitionId IN (...)` **+ точная статистика** по `CompetitionId`:
  планировщик берёт уже существующий композит
  `IX_Results_CompetitionId_CompetitionDate_Position` → **~1.4 мс**.

Это тот же класс бага, что уже чинили для стилей: **фильтр по навигации/`.Name` через
JOIN убивает индексы — надо резолвить в Id и фильтровать по `IN`.** Готовый образец в
этом же файле — `ResolveStyleIdsAsync` (`StyleName → StyleId[]`).

## 2. Решения (зафиксированы, НЕ пересматривать)

1. **Резолв `EventId → CompetitionId[]`** отдельным кэшируемым запросом к `Competitions`,
   затем `query.Where(r => compIds.Contains(r.CompetitionId))`. Точная калька с
   `ResolveStyleIdsAsync` (тот же стиль: кэш-ключ, TTL, пустой список → заведомо пустой набор).
2. **TTL кэша резолва — 10 минут** (как у `styles:name-to-ids`). Событие↔соревнования
   меняется только при импорте (редко); плюс сам results-кэш уже 2 мин.
3. **Пустой список CompetitionId** (событие без соревнований / несуществующий eventId) →
   `query = query.Where(r => false)` — заведомо пустой набор, как в стилях.
4. **Область фикса — только `filter.EventId`.** Ветку `filter.CompetitionId` (одиночный id)
   и `filter.Latest` НЕ трогаем: там уже `WHERE CompetitionId == x`, JOIN нет.
5. **Миграция `SET STATISTICS`** на `Results.CompetitionId` — включить (см. шаг 3).
   На синтетике именно она заставила планировщик поверить в верную оценку строк.
   В проде без перекоса, возможно, не критично, но безопасна и дёшева — оставляем как
   защиту от регрессии плана.
6. Кэш-ключи (`FilterCacheKey`) и сигнатуры DTO/endpoint — **не меняем**. Поведение
   (какие строки возвращаются) остаётся идентичным — меняется только план исполнения.

## 3. Что уже готово (не переделывать)

- `server/Swimm.Infrastructure/Repositories/ResultRepository.cs`
  - `BuildFilteredQueryAsync(ResultFilter)` — единая точка фильтрации (строка с
    `if (filter.EventId.HasValue)` — её и правим).
  - `ResolveStyleIdsAsync(string)` — **эталон** для нового метода (кэш `Dictionary`,
    TTL 10 мин, `_db.Styles.AsNoTracking()...`).
  - Репозиторий читает из `SwimmReadDbContext _db` (роль `swimm_ro`, read-only) —
    `_db.Competitions` доступен (используется в `GetSourcesAsync`).
- Индекс `IX_Results_CompetitionId_CompetitionDate_Position` **уже существует** — новый
  индекс НЕ создавать.
- Домен: `Competition` имеет `int? EventId`; у `ResultRecord` есть `int CompetitionId`.
- Тесты: `server/Swimm.Tests/ResultRepositoryTests.cs` — есть `SeedResultAsync(...)` и
  `NullCacheService` (всегда miss → репозиторий идёт в БД). Тесты на InMemory-провайдере.

## 4. Шаги

### Шаг 1 — новый метод резолва (ResultRepository.cs)

Рядом с `ResolveStyleIdsAsync` добавить по образцу:
```csharp
/// <summary>EventId → его CompetitionId(ы) из кэша (TTL 10 мин). Пусто — событие без
/// соревнований или несуществующий id. Резолвим в Id, чтобы фильтр по Results шёл по
/// композитному индексу (CompetitionId,...), а не через JOIN на Competition.EventId —
/// последнее на большом объёме заставляет планировщик сканировать всю таблицу.</summary>
private async Task<int[]> ResolveEventCompetitionIdsAsync(int eventId)
{
    var key = $"event-competitions:{eventId}";
    var ids = await _cache.GetAsync<int[]>(key);
    if (ids is null)
    {
        ids = await _db.Competitions.AsNoTracking()
            .Where(c => c.EventId == eventId)
            .Select(c => c.Id)
            .ToArrayAsync();
        await _cache.SetAsync(key, ids, TimeSpan.FromMinutes(10));
    }
    return ids;
}
```
> Проверь фактическую сигнатуру `Competition` (имя PK — `Id`, поле — `EventId`). Если
> `_cache.GetAsync<int[]>` на miss возвращает `null` — оставь `is null`; если `default`
> для массива иначе, сверься с тем, как это делает `ResolveStyleIdsAsync` (там
> `Dictionary`, `is null`). Массив тоже reference-тип → `null` на miss — ок.

### Шаг 2 — переписать фильтр EventId (BuildFilteredQueryAsync)

Заменить:
```csharp
if (filter.EventId.HasValue)
    query = query.Where(r => r.Competition.EventId == filter.EventId.Value);
```
на:
```csharp
if (filter.EventId.HasValue)
{
    var eventCompIds = await ResolveEventCompetitionIdsAsync(filter.EventId.Value);
    query = eventCompIds.Length > 0
        ? query.Where(r => eventCompIds.Contains(r.CompetitionId))
        : query.Where(r => false);
}
```
Метод `BuildFilteredQueryAsync` уже `async` — `await` внутри законен.

### Шаг 3 — миграция SET STATISTICS

Скаффолдить миграцию (footgun: **всегда** `--context SwimmDbContext`, см. корневой
CLAUDE.md → «Migrations»):
```bash
dotnet ef migrations add ResultsCompetitionIdStatistics \
  --project server/Swimm.Infrastructure --startup-project server/Swimm.API --context SwimmDbContext
```
В `Up` оставить ТОЛЬКО raw SQL (без изменений модели):
```csharp
migrationBuilder.Sql("""ALTER TABLE "Results" ALTER COLUMN "CompetitionId" SET STATISTICS 1000;""");
```
В `Down`:
```csharp
migrationBuilder.Sql("""ALTER TABLE "Results" ALTER COLUMN "CompetitionId" SET STATISTICS -1;""");
```
> Не вызывай `ANALYZE` в миграции (она в транзакции; статистику подхватит autovacuum
> при следующем проходе, либо разово вручную — это НЕ часть задания).
> Применять миграцию на боевой БД НЕ надо — это делает Влад отдельно.

## 5. Тесты (обязательно)

В `ResultRepositoryTests.cs` добавить кейс на **корректность** резолва EventId→IN
(перф на InMemory не проверить — только логику «те же строки, что и раньше»):

- Засеять событие: 2 `Competition` с одинаковым `EventId = 42` (по 1–2 результата в каждой)
  + одно «чужое» соревнование с `EventId = null` (или другим) и своим результатом.
- Вызвать `GetPagedAsync(new ResultFilter { EventId = 42 }, page:1, pageSize:100)` с
  `NoCache()`.
- Ассерты: вернулись ТОЛЬКО результаты двух соревнований события 42 (по количеству и по
  тому, что чужой результат отсутствует).
- Второй кейс: `EventId = 999` (нет такого) → пустой набор, `total == 0`.

`SeedResultAsync` создаёт по одному Competition на вызов — для события с двумя
соревнованиями либо расширь helper параметром `eventId`/переданным `Competition`, либо
собери граф в тесте вручную (посмотри, как helper строит `Competition` — там нет `EventId`,
добавишь его в сущность). Держись стиля файла.

## 6. Проверка

```bash
# сборка (footgun build-lock: если VS/предыдущий run держат :5078 — Release + :5079)
dotnet build server/Swimm.sln
dotnet test server/Swimm.sln --filter FullyQualifiedName~ResultRepositoryTests

# живой смоук (по желанию, если БД под рукой): подними API, дёрни
#   GET /api/results?eventId=<реальный> — должно быть быстро и с теми же строками.
```
Клиент/tsc не затрагиваются (правка только серверная).

## 7. Footguns

- **Build-lock от Visual Studio/предыдущего `dotnet run`**: `MSB3027 ... locked by .NET Host`.
  Обход: собирать `--configuration Release`, API поднимать на `:5079`. Не убивай чужой
  VS-процесс без нужды — просто работай в Release.
- **Два DbContext**: EF-команды всегда с `--context SwimmDbContext`. Репозиторий читает из
  `SwimmReadDbContext`, но миграции/DDL идут через `SwimmDbContext` (owner-роль).
- **swimm_ro**: миграция только меняет `STATISTICS` существующей колонки — новых таблиц
  нет, грант-SQL не нужен.
- **Кэш**: `_cache` — in-memory (`IMemoryCache`), хранит объект по ссылке. TTL резолва
  10 мин — при активном импорте событий возможен лаг видимости, это осознанно.

## 8. Вне скоупа (НЕ делать)

- НЕ трогать ветки `filter.CompetitionId`, `filter.Latest`, `filter.Name/Club` и прочие —
  только `filter.EventId`.
- НЕ создавать новые индексы (нужный композит уже есть).
- НЕ применять миграцию на боевую БД и НЕ запускать `ANALYZE` в проде.
- НЕ менять DTO, сигнатуру endpoint, кэш-ключи, клиентский код.
- НЕ рефакторить `ResolveStyleIdsAsync` и остальной репозиторий «заодно».
