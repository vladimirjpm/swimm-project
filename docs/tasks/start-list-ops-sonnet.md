# Задание (Sonnet 5): стартовый протокол — операционка (С2, С5, С10)

## Контекст

Делаем фичу «когда и на какой дорожке плывёт мой ребёнок»: перед соревнованием тянем с
loglig стартовый протокол и показываем родителю. Полный план — [`docs/plans/start-list-plan.md`](../plans/start-list-plan.md),
прочитай §1 (источник) и §6 (этапы) перед началом.

Ядро уже написано и проверено вживую. Осталась операционка: **как это запускается** —
руками из админки и по расписанию. Твои шаги — **С2, С5, С10**.

## Решения (зафиксированы, не пересматривать)

1. **Идентичность соревнования — `OrgCompId`** (compID на isr.org.il), а не наш
   `Competitions.Id`. Справочник `Competitions` до старта НЕ трогаем ничем и никак.
2. **`RefreshDetailsAsync` — единственный способ узнать `logligId`.** Ничего нового для
   его добычи не изобретаем: С2 = автоматически звать существующий метод.
3. **Автозабор деталей идёт ТОЛЬКО для будущих стартов** (`DateStart >= сегодня`) и только
   для тех, у кого `LogligId is null`. Прошлые не трогаем: их 372 штуки, и дёргать сайт
   ради архива незачем.
4. **Два новых параметра настроек**, рядом с существующими `DiscoveryEnabled` /
   `DiscoveryIntervalHours`: `StartListEnabled` (bool, по умолчанию `false`) и
   `StartListDaysAhead` (int, по умолчанию `14`). Выключено по умолчанию — как discovery.
5. **Отдельного фонового сервиса НЕ заводим** — второй проход внутри существующего
   `CompetitionDiscoveryBackgroundService`. Один тик, одна настройка «включено», меньше
   движущихся частей.
6. **Кнопка в админке живёт на объединённой `/Admin/Competitions`**, новой страницы не
   создаём. Раздел в `docs/admin-pages/competitions.md` — часть definition of done.

## Что уже готово (не переделывать)

| Что | Где |
|---|---|
| Разбор сетки заплывов и стартового протокола | `server/Swimm.Parsing/Parsers/Loglig/LogligStartListParser.cs` |
| Забор + upsert + журнал | `server/Swimm.Infrastructure/Services/StartListPullService.cs`, интерфейс `IStartListPullService` |
| Сверка при перезаборе | `server/Swimm.Infrastructure/Services/StartListMatcher.cs` |
| Сшивка с результатами | `StartListStitchService`, зовётся из конца `JsonImportService` |
| Таблицы | `CompetitionEntries` (публичная), `Sys_StartListPulls` (журнал заборов) |
| Публичный API | `server/Swimm.API/Controllers/StartListController.cs` |
| Разовые команды | `dotnet run -- --pull-start-list <orgCompId>`, `--stitch-start-list <orgCompId>` |

`IStartListPullService.PullAsync(orgCompId, ct)` возвращает `StartListPullReport`
(`Status` = `ok | partial | empty | error`, счётчики `Events/EventsFetched/Entries/
Added/Moved/Removed/Unchanged/SwimmersCreated/SwimmersStamped/ClubsUnmatched`).
**Он не бросает на ожидаемых состояниях источника** — «посев не сделан» и «нет loglig-id»
приходят статусом `empty`. Не оборачивай его в try/catch «на всякий случай»: проглотишь
настоящие сбои.

## Шаги

### С2 — автодобыча `logligId` для предстоящих стартов

Сейчас `CompetitionDiscoveryService.SyncAsync` тянет только СПИСОК соревнований
(`:23-78`), а `LogligId` заполняется исключительно ручным `RefreshDetailsAsync(id)`
(`:105-118`) — то есть у будущего старта его нет, и весь конвейер стартового протокола
начать нечем.

1. В `ICompetitionDiscoveryService` добавь:
   ```csharp
   /// <summary>
   /// Дочитать детальные страницы будущих стартов, у которых ещё нет loglig-id.
   /// Возвращает (проверено, добыто).
   /// </summary>
   Task<(int Checked, int Resolved)> RefreshUpcomingDetailsAsync(
       int daysAhead, CancellationToken ct = default);
   ```
2. Реализация в `CompetitionDiscoveryService`: выбрать строки
   `Sys_DiscoveredCompetitions`, у которых `LogligId == null`,
   `Status != "ignored"`, `DateStart >= DateTime.UtcNow.Date` и
   `DateStart <= DateTime.UtcNow.Date.AddDays(daysAhead)`; для каждой позвать
   существующий `RefreshDetailsAsync(row.Id, ct)`. Ошибку по одной строке логировать
   и идти дальше (она уже пишется в `LastError` внутри метода) — прогон не должен падать
   целиком из-за одной страницы.
3. Разовая команда в `Program.cs` рядом с `--pull-start-list`:
   `dotnet run -- --refresh-upcoming [--days 14]`. Печатает «проверено N, добыто M».

⚠ **Про риск №1 из плана (§1.6).** Неизвестно, появляется ли iframe loglig на `comp.asp`
ДО соревнования. Если у всех будущих стартов `logligId` так и не добудется — это не баг
твоей реализации, а ответ на открытый вопрос. Напиши это в отчёте, не «чини».

### С5 — кнопка «Стартовый протокол» в админке

1. **Админ-эндпоинт.** В `server/Swimm.API/Controllers/DiscoveryAdminController.cs`
   (там же, где `Preview`, `RefreshDetails`) добавь:
   ```csharp
   [HttpPost("{id:int}/start-list")]
   public async Task<IActionResult> PullStartList(int id, CancellationToken ct)
   ```
   `id` — это `Sys_DiscoveredCompetitions.Id` (как у соседних методов), внутри достань
   `OrgCompId` и позови `IStartListPullService.PullAsync`. Верни `StartListPullReport` как есть.
   Антифоржери и авторизация — как у соседей по контроллеру, ничего своего не выдумывай.

2. **Кнопка.** `server/Swimm.API/Pages/Admin/Competitions/Index.cshtml`, метод
   `SiteActions` (~строка 122). Там уже есть переменная
   `var finished = s.DateStart <= DateTime.UtcNow;`, и сейчас **у будущих стартов не
   рисуется ни одной кнопки** — это и надо исправить: кнопка «Стартовый протокол» нужна
   в первую очередь именно им.

   Рисуй её всегда, когда `s.Status != "ignored"` (и для прошедших тоже — на них удобно
   проверять). Обработчик — по образцу соседних `discPreview`/`discSyncLangs`: POST на
   `/api/admin/discovery/{id}/start-list`, потом показать сводку из отчёта в том же виде,
   что показывают соседние действия. Статус `empty` показывай спокойным сообщением
   (`Error` из отчёта уже содержит человеческий текст вроде «Посев ещё не сделан»),
   а не как ошибку.

3. **Колонка/бейдж «заявок N»** — если это дёшево вписать в существующую таблицу, покажи
   рядом со строкой число заявок из `CompetitionEntries` по `OrgCompId`. Если вписывается
   плохо (таблица собирается в одном месте из двух источников) — **пропусти и напиши в
   отчёте**, это не обязательный пункт.

4. **`docs/admin-pages/competitions.md`** — раздел про новую кнопку: что делает, что
   значат статусы `ok/partial/empty/error`, куда смотреть при пустом результате.

### С10 — расписание

В `server/Swimm.API/BackgroundServices/CompetitionDiscoveryBackgroundService.cs` добавь
второй проход в том же цикле, после существующего `discovery.SyncAsync(...)`:

```
если settings.GetValue("StartListEnabled", false):
    daysAhead = settings.GetValue("StartListDaysAhead", 14)
    1) discovery.RefreshUpcomingDetailsAsync(daysAhead)      // С2
    2) для каждого будущего старта с LogligId != null внутри окна:
           startList.PullAsync(orgCompId)
    залогировать сводку одной строкой
```

⚠ Забор одного чемпионата — под две сотни HTTP-запросов с вежливой паузой 2 с (~6 минут).
Это фон, торопиться некуда, но: обязательно уважай `stoppingToken` (передавай его во все
вызовы) и не держи `DbContext` дольше одного соревнования — бери скоуп на итерацию, как
это уже сделано в существующем цикле.

Настройки добавь в `/Admin/Settings` рядом с `DiscoveryEnabled` / `DiscoveryIntervalHours`,
тем же способом (посмотри, как те заведены, и повтори).

## Тесты (обязательно, xUnit в `server/Swimm.Tests`)

Образцы: `CompetitionDiscoveryServiceTests.cs` (там уже есть `FakeProvider` — расширь его
или сделай свой), `StartListPullServiceTests.cs` (фейковый провайдер + in-memory БД).

1. `RefreshUpcomingDetailsAsync` берёт **только** будущие строки без `LogligId` и только
   внутри окна `daysAhead`; прошедшие и `ignored` не трогает.
2. Ошибка на одной строке не роняет прогон — остальные обрабатываются.
3. Админ-эндпоинт: неизвестный `id` → 404; известный → отчёт с ожидаемым статусом.
4. Фоновый проход не делает ничего при `StartListEnabled = false`.

Не пиши тест на «сколько HTTP-запросов ушло» — это не поведение, а реализация.

## Проверка

```bash
dotnet build server/Swimm.sln
dotnet test server/Swimm.Tests/Swimm.Tests.csproj
```

Живьём (обязательно, хотя бы на архивном старте — будущих на сайте сейчас НЕТ):

```bash
dotnet run --project server/Swimm.API -- --refresh-upcoming --days 14
dotnet run --project server/Swimm.API -- --pull-start-list 16786
```

Затем открой `/Admin/Competitions`, найди соревнование `compID 16786` и нажми новую кнопку —
она должна отработать и показать сводку. Повторное нажатие обязано быть идемпотентным
(`новых 0, снялись 0`).

Тестовые данные в локальной БД уже есть: `compID 16786` (loglig 14208, 989 заявок) и
`compID 16835` (loglig 14914, 137 заявок, уже сшито с результатами).

## Footguns

- **Build-lock.** Если `dotnet build` падает с `MSB3027/MSB3021 … locked by ".NET Host (pid)"` —
  это живой `dotnet run`. Убей его: процесс называется `dotnet.exe`, **не** `Swimm.API.exe`:
  `Get-CimInstance Win32_Process -Filter "Name='dotnet.exe'" | Where-Object { $_.CommandLine -like "*Swimm.API*" } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force }`.
  Если порт :5078 занят Visual Studio — работай в `--configuration Release` и на :5079.
- **Два DbContext.** Любая команда EF требует `--context SwimmDbContext`. В этой задаче
  миграции не нужны вовсе.
- **`SwimmReadDbContext` (роль `swimm_ro`) НЕ видит `Sys_*`** — журнал заборов ей недоступен
  по гранту. Админские выборки идут через `SwimmDbContext`.
- **Не зови `PullAsync` из UI-потока синхронно на большом чемпионате** — это минуты.
  В админке это нажимает человек и ждёт; для фона — только фоновый сервис.
- **Кэш** после забора сбрасывать не надо: у `/api/start-list` TTL 60 секунд.

## Вне скоупа (не делать)

- Клиент: таб, страницы, компоненты — это отдельное задание (`start-list-ui-sonnet.md`).
- Тянуть `נרשמים` (`RegisteredCompetitionAthletes`) — решение В4: в первой версии не тянем.
- Трогать `StartListPullService`, `StartListMatcher`, `StartListStitchService`,
  `LogligStartListParser`, публичный `StartListController` — они готовы и проверены вживую.
- Заводить `Competition` для будущего старта. Никогда. Это ломает `BulkPullService`,
  «последнее соревнование» на главной и проверку правила очков (§3.1 плана).
- Уведомления (почта/пуши) — решение В7, не в первой версии.
