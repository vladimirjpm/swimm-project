# Задание (Opus): Часть В, сервер — весь сезон из «Входящих», фон без лишних обращений, postponed/cancelled

## Контекст

План — [`docs/plans/competitions-home-plan.md`](../plans/competitions-home-plan.md), раздел «Часть В»
(решения Влада В9–В12 от 19.10.2026 — все «да») и «Журнал» (Б1–Б9 сделаны). Календарь главной
`/competitions` — `GET /api/competitions/calendar?season=` (Б1): `CompetitionCalendarBuilder`
(Application/Mapping, чистые функции, 48 тестов `CompetitionCalendarBuilderTests`),
`CompetitionCalendarRepository` (уже читает `Sys_DiscoveredCompetitions` через `SwimmDbContext` —
только площадку/даты/дисциплину по compID затянутых и заявок), DTO `CompetitionCalendarDtos.cs`.
Клиент календаря готов (`client/src/projects/competitions-project/calendar/`), его в этом задании
НЕ трогаем (кроме типов — см. п. 5) — куб будущего старта сделает отдельный шаг В5′.

Это шаги **В1′–В4′** плана. Всё — в локальном `master`, коммиты своими путями (не `git add -A`;
в рабочей копии лежат чужие незакоммиченные правки другой сессии: season-best, `SeasonMath`,
`SwimmerPageRepository` и др. — их не трогать и не коммитить). Push/PR/миграция на прод — НЕТ.

## Что известно о коде (разведка 19.10)

- Забор списка: `Swimm.Parsing/Discovery/IsrOrgDiscoveryProvider.FetchListAsync(finished, year)`;
  пустой «прошедший» список бросает, пустой «будущий» — нет. Upsert —
  `CompetitionDiscoveryService.SyncAsync(int? year)` (`Infrastructure/Services`, ~23–78): у
  существующей строки при смене Name/DateStart/DateEnd все три молча перезаписываются; пропавшие
  строки не помечаются; `LastSeenAt` = now у увиденных. Вызывающие: фон
  `Swimm.API/BackgroundServices/CompetitionDiscoveryBackgroundService` (year null), кнопка
  «Проверить сейчас» `DiscoveryAdminController.Sync` (POST `/api/admin/discovery/sync[?year=]`,
  JS `wwwroot/js/admin/competitions-index.js` ~700–719), CLI `Cli/DiscoveryCli.cs`.
- Матч входящей строки с затянутым: в `/Admin/Competitions` — сначала `Competitions.OrgCompId`,
  иначе `DiscoveryCompetitionMatcher.MatchAsync` (`Infrastructure/Services`, ~21–55: дата дня в
  `[DateStart..DateEnd]` + нормализованное имя равно/начинается с). Чемпионат по названию —
  `CompetitionAdminRepository.IsChampionship(name)` (~303: «אליפות»+«ישראל» или
  «championship»+«israel»). Статусы `new|imported|ignored`; `ignored` ставит админ («✕ Скрыть»).
- Фоновые задачи: `Program.cs` ~116 — под `backgroundJobsEnabled` (`HostingMode.BackgroundJobsEnabled`,
  умолчание true; Production — false, ProdAdmin — true, Development — ключа нет) зарегистрированы
  `ImportBackgroundService`, `RecordCountryRunBackgroundService` (это ОЧЕРЕДИ: исполняют то, что
  поставила админка), `CompetitionDiscoveryBackgroundService`, `LogligSuggestionVerificationBackgroundService`,
  `LogligBatchBackgroundService` (это РАСПИСАНИЯ). У всех трёх расписаний «последний запуск» —
  локальная переменная (`DateTime.MinValue`) → после каждого перезапуска API идёт наружу сразу.
  Настройки интервалов — `AdminSettingsService` (реестр ключей; `DiscoveryEnabled` false,
  `DiscoveryIntervalHours` 12, `StartListEnabled` false, `LogligVerify*`, `LogligBatch*`).
- `Sys_DiscoveredCompetitions` УЖЕ в `server/db/seed-tables.txt` и в `DataVersionForSync`
  (синхронизация с прода её возит — шаг В3′ по сути готов). ⚠ `sync-merge.sql` требует совпадения
  версии схемы прода и локальной: после любой новой миграции `/db-sync-from-prod` не пойдёт, пока
  миграция не накатана на прод (это будет вместе с Б10, по команде Влада).
- Наша страница предстоящего — `/competitions/upcoming/{orgCompId}` (без заявок показывает «Start
  list not published yet»); страница федерации — `https://isr.org.il/comp.asp?compID={id}`.

## Решения (зафиксированы)

### 1. Журнал запусков фоновых задач в базе (В2′, В12)

- Новая таблица **`Sys_JobRuns`**: `Name` (varchar 100, PK), `LastAttemptAt` (timestamptz null),
  `LastSuccessAt` (timestamptz null), `LastError` (varchar 1000 null). Sys_, приватная, без гранта
  `swimm_ro`, **не** в seed-tables (у каждой базы свой журнал — у прода свой, его пишет ProdAdmin).
- Порт `IJobRunStore` в `Application/Abstractions`: `GetAsync(name)` → (lastAttempt, lastSuccess),
  `RecordAsync(name, attemptAt, success, error)`. Реализация в Infrastructure на `SwimmDbContext`.
- Правило «пора ли» — чистая функция `JobSchedule.IsDue(now, lastSuccess, lastAttempt, interval,
  retryAfterFailure)` в `Application/Mapping` + тесты: никогда не запускалась → пора; успех был
  меньше `interval` назад → нет; последняя попытка неудачна → не раньше `retryAfterFailure`
  (1 час) после неё. Имена задач — константы (`discovery`, `start-list`, `loglig-verify`, `loglig-batch`).
- Три расписания (discovery + проход start-list внутри него, loglig-verify, loglig-batch) берут
  «последний запуск» из журнала и пишут его (успех/ошибка). Тик (5/15 мин) не меняется.
- Кнопка «Проверить сейчас» без `year` (текущий сезон) тоже пишет успех `discovery` — иначе фон
  тут же повторит. С `year` — не пишет (это другой сезон).
- `DiscoveryIntervalHours` по умолчанию **24** (В12). `DiscoveryEnabled` по умолчанию НЕ меняем —
  Влад включит его в `/Admin/Settings` под ProdAdmin (скажи об этом в отчёте).

### 2. Локальная разработка — расписания выключены (В10), очереди работают

- Разделить регистрацию в `Program.cs`: **очереди** (`ImportBackgroundService`,
  `RecordCountryRunBackgroundService`) — там, где включена админка (`adminEnabled`): без них не
  работает импорт из локальной админки. **Расписания** (discovery, loglig-verify, loglig-batch) —
  под `backgroundJobsEnabled`.
- `HostingMode.BackgroundJobsEnabled(config, env)`: явный ключ `BackgroundJobs:Enabled` важнее;
  без ключа — `false` в Development, `true` иначе. `appsettings.Development.json` не трогать (он не
  в git и содержит секреты). Обновить `HostingModeTests`, XML-док `HostingMode`, лог старта
  (`Program.cs` ~644–660: «админка есть, а джобы выключены» в Development теперь норма — не critical).
- Проверить, что Production (`appsettings.Production.json` false, Admin false) — ничего из пяти не
  стартует; ProdAdmin (true/true) — всё стартует.

### 3. Postponed / Cancelled из синхронизации (В4′, В4)

- Миграция (через `/db-migrate`, `--context SwimmDbContext`) добавляет в `Sys_DiscoveredCompetitions`:
  `PreviousDateStart`, `PreviousDateEnd` (тот же тип, что `DateStart`), `DateChangedAt`
  (timestamptz null), `MissingSince` (timestamptz null). В той же миграции — `Sys_JobRuns`. Триггер
  версии у таблицы уже есть; `02-grants.sql` не менять. Применить локально.
- Правила — чистые функции в `Application/Mapping/DiscoverySyncRules.cs` + тесты, вызываются из
  `SyncAsync`:
  - **Перенос:** у существующей строки сменился `DateStart`, а прежний `DateStart` ≥ сегодня
    (Asia/Jerusalem, `IsraelTime`) — старт ещё не прошёл → если `PreviousDateStart` пуст, записать
    туда прежние даты (храним ПЕРВУЮ объявленную дату), `DateChangedAt` = now. Если новая дата
    равна `PreviousDateStart` — перенос отменили: очистить `Previous*`. Прежняя дата в прошлом —
    это правка опечатки, не перенос: не помечать.
  - **Отмена:** только при `year == null` и успешном заборе обоих списков. Кандидаты — строки (не
    `ignored`), у которых `DateStart` ≥ сегодня и в текущем сезоне (`SeasonMath`), а `OrgCompId` нет
    в полученных списках → `MissingSince` = now (если пусто). Увиденная строка → `MissingSince` =
    null. **Предохранитель от сбоя сайта:** будущий список пуст ИЛИ кандидатов больше
    `max(3, половина будущих строк сезона)` → отмену не помечать, `LogWarning`.
  - `DiscoverySyncResult` += `Postponed`, `Missing`; текст кнопки в `competitions-index.js` их
    показывает («перенесено: N, пропало: M»), если не ноль.

### 4. Календарь = справочник + входящие (В1′, В9)

- Репозиторий дочитывает входящие сезона: строки, у которых `DateStart` ИЛИ `PreviousDateStart` в
  сезоне (`SeasonMath.RangeOf`), плюс уже читаемые по compID. Поля: OrgCompId, Name, DateStart,
  DateEnd, Previous*, MissingSince, Venue, Discipline, Status. Наружу — только то, что ниже.
- Билдер (тесты обязательно): строка входящих **без** затянутого/заявок — новая meet
  `kind: "upcoming"`, `id` = OrgCompId, `key` = `upcoming:{id}` (тот же, что у старта по заявкам).
  «Затянута» — compID входит в compID'ы какого-либо старта (как сейчас) **или** совпала по
  имени+дате с днём справочника по правилу `DiscoveryCompetitionMatcher` — вынести его нормализацию
  и сравнение в чистую функцию Application (например `DiscoveryNameMatch`) и звать из матчера и из
  билдера (тесты матчера должны остаться зелёными). `ignored` — не показывать.
  - дни — промежуток `DateStart..DateEnd` (≤ 7 дней, иначе только старт и конец), `loaded:false`;
  - `pool` ""; `venue`; `type` — `artistic` по `Discipline`, иначе открытая вода по названию;
  - `is_championship` — справка о старте (`CompetitionMeetInfos`, если есть), иначе по названию —
    правило `CompetitionAdminRepository.IsChampionship` вынести в Application (одно на двоих);
  - `group` — `CanonicalFromName`; `status` по датам; `has_start_list` false.
- **Новое поле DTO** `source_url` (string?, snake_case) у `kind: "upcoming"` —
  `https://isr.org.il/comp.asp?compID={id}`; у остальных null. Клиент (В5′) по нему ведёт
  `Details ↗`, если заявок нет.
- **Postponed:** строка с `PreviousDateStart` (не `ignored`), прежняя дата в сезоне → ДОПОЛНИТЕЛЬНЫЙ
  «призрак» на прежних датах: `key` = `postponed:{id}`, `kind: "upcoming"`, `status: "postponed"`,
  `postponed_to` = новый `DateStart` ISO, остальные поля как у старта. Сам старт — на новых датах,
  как обычно.
- **Cancelled:** у meet `kind: "upcoming"`, чей compID (основной) в входящих с `MissingSince` —
  `status: "cancelled"`. Затянутые (event/competition) отменёнными не бывают.
- `is_championship` и прочие флаги у meet по заявкам (как в Б1) не меняются; если у такой meet есть
  строка входящих — `source_url` тоже проставить.

### 5. Клиентские типы

Только `client/src/projects/competitions-project/calendar/calendar-types.ts`: добавить
`source_url: string | null` в `CalendarMeet`. Больше ничего на клиенте.

## Тесты (обязательно, образец — `CompetitionCalendarBuilderTests`)

`JobScheduleTests`; `DiscoverySyncRulesTests` (перенос будущего / не перенос прошлого / возврат даты
очищает / отмена / предохранитель пустого списка и массового пропадания / увиденная строка снимает
`MissingSince`); дополнить `CompetitionCalendarBuilderTests` (входящие без затянутого → upcoming с
`source_url`; затянутые по OrgCompId и по имени+дате — не дублируются; `ignored` нет; призрак
postponed; cancelled; чемпионат по названию); `HostingModeTests`. Если есть тесты
`CompetitionDiscoveryService` — дополнить случаем переноса и отмены. Новые пути записи в
Postgres-таблицу с ограничениями — по правилу памяти «InMemory не видит CK Postgres»: посмотреть `\d`
и при необходимости `*PgTests`.

## Проверка

- ⚠ Debug-сборку держит API Влада на :5078 — НЕ трогать. Сборка и тесты: `dotnet test
  server/Swimm.Tests -c Release` — весь набор зелёный.
- Миграция: `/db-migrate` (в Release, если Debug заблокирован: `dotnet ef ... --configuration Release`),
  применить к локальной базе; проверить `\d "Sys_DiscoveredCompetitions"` и `\d "Sys_JobRuns"`.
- Живьём: API Release на :5079 (`preview_start` имени `swimm-api-5079` или `dotnet run --project
  server/Swimm.API --no-build -c Release --urls http://localhost:5079` в фоне), затем
  `curl /api/competitions/calendar?season=2026` — в сезоне 2026/27 появились будущие старты из
  входящих (в локальной базе их ~46 строк 9.10.2026–30.07.2027), без дублей с затянутыми 09.10;
  остановить API. В Development фоновые расписания не стартуют (лог старта), импорт-очередь
  стартует.
- Перенос/отмену проверить юнит-тестами; на живой базе можно временно руками проставить
  `PreviousDateStart`/`MissingSince` одной строке, посмотреть ответ и ВЕРНУТЬ как было.

## Доки (часть работы)

- План: раздел «Часть В» — отметить В1′–В4′ (В3′: уже было, проверено) и запись в «Журнал»
  (что сделано, решения, ⚠ про блок `/db-sync-from-prod` до миграции прода).
- `docs/admin-pages/` — MD страницы Competitions/Settings, если меняется поведение кнопки или
  настроек; `docs/pre-push-rules.md` правило 14 — фоновые службы: не новые, но гейт изменился —
  поправить формулировку, если она говорит про `BackgroundJobs:Enabled` иначе.
- `docs/data-integrity.md` — если там есть раздел про входящие/автозабор, строка про перенос/отмену.

## Коммиты

Своими путями, по смыслу (например: журнал запусков + гейт; перенос/отмена + миграция; календарь),
сообщения по-русски, в конце `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
Не пушить.

## Вне скоупа

Клиентский куб будущего старта и `Details ↗` (В5′), миграция на прод, Б10, изменение страницы
`/competitions/upcoming/{id}`, забор прошлых сезонов.

## Отчёт

Хэши коммитов, список файлов, результаты тестов, что проверено живьём, **«Отклонения»**, и что
нужно сделать Владу (включить `DiscoveryEnabled` в ProdAdmin; миграция на прод с Б10; до неё
`/db-sync-from-prod` не пойдёт).
