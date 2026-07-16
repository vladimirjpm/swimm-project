# Задание Sonnet: фиксы по итогам ревью (A1–A3)

Три независимых мелких фикса в серверном коде. Источник — `docs/tasks/next-front-plan-2.md`,
поток A (замечания code-review к уже смерженной работе по Discovery и дедупу пловцов).

Задание самодостаточное: всё нужное ниже, контекст прошлых сессий не требуется.

**Не запускай других агентов — делай всё сам.**

## Решения (зафиксированы, не пересматривать)

- **A1**: статус `imported` метит **фоновый обработчик очереди** после успешного
  завершения job. `discoveredId` протаскиваем через job как nullable-поле. Вариант
  «фронт поллит статус» — отклонён (страница уходит в Import History, поллинга нет).
  Если job упал (`Failed`) — статус НЕ трогаем (запись остаётся `new`/прежней,
  админ повторит).
- **A2**: пересекающиеся пары — это когда один и тот же `Id` встречается более чем в
  одной паре в любой роли (canonical или duplicate). Проверка — **до** любых обращений
  к БД и до любых изменений, отклоняется **весь вызов** через
  `throw new ArgumentException(...)` с понятным русским текстом. Не per-pair status:
  это ошибка ввода, а не конфликт данных. Дубликат одной и той же пары (A←B дважды)
  тоже пересечение → та же ошибка.
- **A3**: `GET {id}/pdf` больше не вызывает `RefreshDetailsAsync`. Он читает только
  уже сохранённый `LogligId`; если его нет — `BadRequest` с текстом
  «Детали не загружены — нажмите «Затянуть» (нет loglig-id).». Обновление деталей
  остаётся в `POST {id}/preview` и `POST {id}/details`.

## Что уже готово (не переделывать)

- `server/Swimm.API/Controllers/DiscoveryAdminController.cs` — `POST import` (~124–146),
  `GET {id}/pdf` (~75–83), `POST {id}/preview` (~85–122), приватный
  `FetchPdfAsync` (~148–176), в котором и сидит побочный эффект `RefreshDetailsAsync`.
- Очередь: порт `server/Swimm.Application/Abstractions/IImportJobQueue.cs`,
  реализация `server/Swimm.Infrastructure/Services/ImportJobQueue.cs` (in-memory singleton,
  `Channel.CreateBounded<ImportJobItem>(20)`), потребитель
  `server/Swimm.API/BackgroundServices/ImportBackgroundService.cs`
  (`ConsumeAsync` + `SetRunning/SetCompleted/SetFailed`).
  Внутренний элемент — `ImportJobQueue.ImportJobItem(Guid JobId, byte[] Data, string FileName,
  IReadOnlyList<string> CategoryKeys, ImportEventOptions? EventOptions)`.
- DTO статусов: `server/Swimm.Application/Dtos/ImportJobDtos.cs`.
- Статус discovery-записи: `SetStatusAsync(int discoveredId, string status, CancellationToken)`
  на сервисе discovery (см. использование в контроллере, интерфейс в
  `server/Swimm.Application/Abstractions/`).
- Merge: интерфейс `ISwimmerMergeService.MergeAsync(IReadOnlyList<SwimmerMergePair> pairs,
  bool dryRun = true, CancellationToken ct = default)`, реализация
  `server/Swimm.Infrastructure/Services/SwimmerMergeService.cs` (валидация — внутри цикла:
  self-merge, не найден, synthetic, конфликт заплывов). DTO —
  `server/Swimm.Application/Dtos/SwimmerMergeDtos.cs`
  (`record SwimmerMergePair(int CanonicalId, int DuplicateId)`).
- Тесты merge: `server/Swimm.Tests/SwimmerMergeServiceTests.cs` (xUnit, EF InMemory,
  имя БД = `nameof(TestMethod)`, сервис создаётся напрямую `new SwimmerMergeService(db)`, AAA).

## Шаги

### A1 — статус imported после завершения job

1. В `IImportJobQueue.Enqueue(...)` добавь параметр `int? discoveredId = null`
   (в конец, с дефолтом — остальные вызовы не ломаются). Протащи его в
   `ImportJobItem` (новое поле `int? DiscoveredId`).
2. `ImportBackgroundService`: после успешного импорта (там, где сейчас `SetCompleted`)
   — если `item.DiscoveredId is int id`, вызвать `SetStatusAsync(id, "imported", ct)`
   через **scoped**-резолв сервиса из `IServiceScopeFactory` (background service —
   singleton, scoped сервисы напрямую инжектить нельзя; посмотри, как там уже
   резолвится импорт-сервис, и сделай так же). Ошибка простановки статуса не должна
   ронять job — залогируй и продолжай (`_logger.LogWarning`).
3. `DiscoveryAdminController.Import`: убрать `await _discovery.SetStatusAsync(...)`,
   передать `entry.DiscoveredId` в `Enqueue`.
4. Если при `Failed` — статус не трогаем (ничего не добавляй).

### A2 — защита от пересекающихся пар

5. В начале `SwimmerMergeService.MergeAsync`, **до** любых запросов к БД: собери все
   Id из пар (оба поля каждой пары) и найди те, что встречаются больше одного раза.
   Если такие есть — `throw new ArgumentException($"Пересекающиеся пары: один и тот же
   пловец участвует в нескольких парах (Id: {список через запятую}). Разбейте на
   отдельные вызовы.")`. Пары с `CanonicalId == DuplicateId` этой проверкой не
   затрагиваются — для них остаётся существующий per-pair error (не ломай его тест).
   Учти это: сначала отсей self-пары из подсчёта пересечений либо считай так, чтобы
   существующий тест на self-merge остался зелёным.
6. Проверь, как контроллер, вызывающий merge (найди по `MergeAsync`), обрабатывает
   исключения: `ArgumentException` должен превращаться в `BadRequest { error = ex.Message }`,
   а не в 500. Если такой обработки нет — добавь try/catch в экшене.

### A3 — убрать побочный эффект из GET

7. `FetchPdfAsync`: вынеси решение «обновлять детали или нет» в параметр
   `bool refreshIfMissing`. `GET {id}/pdf` вызывает с `false` (без `RefreshDetailsAsync`;
   при отсутствующем `LogligId` — текст ошибки из «Решений» выше),
   `POST {id}/preview` — с `true` (как сейчас). Комментарий `// GET-скачивание файла;
   мутаций нет` теперь правдив — оставь.

## Тесты (обязательно)

В `server/Swimm.Tests/SwimmerMergeServiceTests.cs`, по образцу соседних тестов:
- `Merge_ThrowsOnOverlappingPairs` — пары `(A←B)` и `(C←B)` → `ArgumentException`,
  и (важно!) **ни одна** запись в БД не изменилась: после `Assert.Throws`
  проверь, что оба дубликата на месте (`db.Swimmers.Count()` не изменился).
- `Merge_ThrowsWhenSameCanonicalIdUsedTwice` — `(A←B)` и `(A←C)` → `ArgumentException`.
- Существующие тесты merge должны остаться зелёными без правок.

Тестов на `DiscoveryAdminController`/`ImportJobQueue` в проекте нет и заводить
инфраструктуру под них не надо — A1/A3 проверяются сборкой и живой проверкой.

## Проверка

```bash
dotnet build server/Swimm.sln --configuration Release
dotnet test server/Swimm.sln --configuration Release
```
Все тесты должны быть зелёными (было 272/272 — станет больше на число новых).

Живая проверка Discovery не обязательна (ходит в чужой прод) — достаточно сборки и тестов.

## Footguns

- **Build-lock**: если сборка падает с `MSB3027/MSB3021 … "Swimm.API.dll" is locked` —
  жив прошлый `dotnet run`. Собирай `--configuration Release`; если нужен запуск API,
  бери порт `:5079`, а осиротевшие процессы гаси:
  `Get-CimInstance Win32_Process -Filter "Name='Swimm.API.exe'" | ForEach-Object { Stop-Process -Id $_.ProcessId -Force }`
- Clean Architecture: контроллеры инжектят только интерфейсы из `Swimm.Application`,
  никаких типов `Swimm.Infrastructure` напрямую. `ImportJobItem` — внутренний тип
  Infrastructure, наружу его не тащи.
- Два DbContext — если вдруг понадобится EF-команда, всегда `--context SwimmDbContext`
  (в этом задании миграции не нужны — схема не меняется).
- Синтетика в БД (`SwimmerOrgId LIKE 'SYNTH-%'`) — не трогать, merge её уже отсекает.
- Комментарии в коде — русские, идентификаторы английские (как в соседних файлах).

## Вне скоупа (не делать)

- Не чинить `TryWrite`-баг переполнения канала в `ImportJobQueue` (известен, отдельная задача).
- Не добавлять поллинг статуса job на страницу Discovery.
- Не трогать merge-логику конфликтов/synthetic/self-merge — только новая проверка пересечений.
- Не менять UI-страницы, не запускать `npm run css:build`.
- Не коммитить в `master` без просьбы: сделай **один коммит** с этими изменениями
  в текущей ветке и отчитайся.
