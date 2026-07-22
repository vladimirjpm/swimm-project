# Задание (Sonnet 5): CLI-бэкфилл Competition.OrgCompId по Discovery-строкам (dry-run + apply)

Контекст: импорт теперь штампует `Competition.OrgCompId` (compID сайта) — но соревнования,
импортированные РАНЬШЕ, `OrgCompId` не имеют, поэтому кросс-линк /Admin/Competitions ↔
/Admin/Discovery по OrgCompId для них пуст. В админке уже есть построчная кнопка «Обновить»
(`POST /api/admin/discovery/{id}/link-competition` → `CompetitionDiscoveryService.LinkImportedAsync`),
но прогонять её по всем строкам через HTTP неудобно (нужна admin-авторизация). Твоя задача —
сделать **разовый CLI-бэкфилл всех импортированных строк** с dry-run по умолчанию, чтобы Влад
глазами проверил таблицу «discovered → сматченное соревнование» перед применением.

## Prerequisite (проверь до старта)

Фича штампа OrgCompId должна быть в дереве (может быть уже закоммичена). Убедись, что существуют:
- `CompetitionDiscoveryService.LinkImportedAsync(int id, ...)` и `RelinkResult` (в `Dtos/DiscoveryDtos.cs`).
- `DiscoveryCompetitionMatcher.MatchAsync` возвращает `Dictionary<int, CompetitionMatch?>` (Id+Name).
Если их нет — СТОП, сообщи (значит незакоммиченный кусок не подтянулся).

## Решения (зафиксированы, не пересматривать)

- **Не** ходить через HTTP/эндпоинт. Делать CLI-флагом, как `--backfill-relay-members`
  (образец: `server/Swimm.Infrastructure/Services/RelayMemberBackfillService.cs` +
  ветка в `server/Swimm.API/Program.cs`, ищи `--backfill-relay-members`).
- **Dry-run по умолчанию**, запись только с `--apply` (тот же паттерн, что `--merge-swimmers`).
- Матчинг «discovered → competition» — ТОЛЬКО через существующий `DiscoveryCompetitionMatcher`
  (имя+дата). Не изобретать свой. Уникальность `OrgCompId` уважать: если compID уже занят
  ДРУГИМ соревнованием — пропуск с пометкой (как в `LinkImportedAsync`).
- Обрабатывать строки со статусом `imported` ИЛИ имеющие матч (matched по имени+дате).
  Идемпотентно: если у сматченного соревнования уже стоит этот OrgCompId — «уже привязано».
- Никаких миграций (поле `Competition.OrgCompId` уже есть).

## Шаги

### 1. Метод сервиса — батч-бэкфилл с отчётом

В `server/Swimm.Infrastructure/Services/CompetitionDiscoveryService.cs` добавь
`Task<IReadOnlyList<DiscoveryBackfillRow>> BackfillImportedOrgCompIdsAsync(bool apply, CancellationToken ct = default)`:
- Загрузи все `DiscoveredCompetitions`; прогони через `matcher.MatchAsync`.
- Для каждой строки с матчем сформируй `DiscoveryBackfillRow` (discovered: OrgCompId, Name,
  DateStart..DateEnd; matched: CompetitionId, CompetitionName; Action). Логику проставления/
  проверки уникальности переиспользуй из `LinkImportedAsync` (вынеси общий кусок в приватный
  метод, чтобы не дублировать: «взять matched comp → если OrgCompId уже == → AlreadyLinked;
  иначе если занят другим → TakenByOther/skip; иначе — Set (только при apply)»).
- Строки без матча в отчёт НЕ включай (или включай с Action=NoMatch — на твой выбор, но
  тогда пометь явно). Проще — не включать.
- При `apply=false` — ничего не писать в БД; при `apply=true` — `SaveChangesAsync` один раз
  в конце (или по мере, но одним контекстом).
- Возможные Action (enum или строка): `WouldLink` / `Linked` / `AlreadyLinked` / `TakenByOther`.

Добавь DTO `DiscoveryBackfillRow` в `server/Swimm.Application/Dtos/DiscoveryDtos.cs`
(int OrgCompId, string DiscoveredName, int CompetitionId, string CompetitionName, string Action).
Добавь метод в интерфейс `ICompetitionDiscoveryService`
(`server/Swimm.Application/Abstractions/ICompetitionDiscoveryService.cs`).

### 2. CLI-флаг в Program.cs

`server/Swimm.API/Program.cs` — рядом с веткой `--backfill-relay-members`, добавь:
`dotnet run -- --backfill-discovery-orgcompid [--apply]`
- Создай scope, возьми `ICompetitionDiscoveryService`, вызови
  `BackfillImportedOrgCompIdsAsync(apply: args.Contains("--apply"))`.
- Печатай **таблицу** для глазной проверки (одна строка на mapping), напр.:
  `[WouldLink] compID 16745 → comp #1486 «...»  (discovered: «...», 24-30/07/2026)`
  Заголовок «=== DRY-RUN ... (добавь --apply) ===» / «=== ПРИМЕНЕНО ===».
- Итог: сколько would-link/linked, already, taken-by-other.
- `return;` в конце (как другие CLI-ветки — до `app.Run()`).

### 3. Тест

`server/Swimm.Tests/CompetitionDiscoveryServiceTests.cs` (InMemory, есть хелперы
`CreateDb`/`CreateService`/`FakeProvider`; см. рядом тест `LinkImported_StampsOrgCompId...`):
- Сид: 2 соревнования + 2 discovered-строки (одна матчится, одна нет; можно и «taken-by-other»).
- `BackfillImportedOrgCompIdsAsync(apply:false)` — БД не изменилась, в отчёте `WouldLink`.
- `BackfillImportedOrgCompIdsAsync(apply:true)` — OrgCompId проставлен сматченному; повторный
  вызов даёт `AlreadyLinked`.

## Проверка (обязательно)

- `dotnet build server/Swimm.sln --configuration Release` — **в Release** (в Debug билд-лок,
  если открыта Visual Studio; см. корневой CLAUDE.md, footgun MSB3027).
- `dotnet test server/Swimm.Tests --configuration Release` — все зелёные (сейчас 530).
- Смоук CLI (локальный Postgres поднят): `dotnet run --project server/Swimm.API
  --configuration Release -- --backfill-discovery-orgcompid` → печатает таблицу, БД не тронута.
  НЕ запускай `--apply` сам — это делает Влад после проверки таблицы.

## Definition of done

- CLI `--backfill-discovery-orgcompid [--apply]` печатает mapping-таблицу (dry-run) и
  проставляет OrgCompId с `--apply`, уважая уникальность.
- Тест добавлен, вся сюита зелёная, Release-сборка чистая.
- НЕ коммить (Влад закоммитит сам). В конце — короткий отчёт: какие файлы тронул, вывод
  dry-run на реальной БД (таблица mapping'ов), число would-link.
