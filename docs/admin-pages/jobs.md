# /Admin/Jobs — фоновые службы и что они делали

Файлы: `Pages/Admin/Jobs.cshtml` (только каркас: шапка, места под виды, панель деталей, окно
прогона), `Pages/Admin/Jobs.cshtml.cs` (пустой `JobsModel`, только `[Authorize]`), скрипты
`wwwroot/js/admin/jobs/*.js`. Пункт сайдбара «Jobs» в разделе «Система». С 20.11.2026 страница
рисуется **на клиенте** из `GET /api/admin/jobs/monitor` и сама обновляется раз в 30 с — см.
«Как устроена страница» ниже.

Откуда данные — один источник и для страницы, и для блока «Jobs» дашборда ([index.md](index.md), п. 9):

| Что | Где |
|---|---|
| Порт | `IBackgroundJobsStatus` (`Swimm.Application/Abstractions`): `GetReportAsync` — полный отчёт для страницы, `GetSummaryAsync` — только сводка для дашборда (без истории и журналов очередей) |
| Реализация | `Swimm.API/Services/BackgroundJobsStatusService.cs` — каталог служб, карточки, короткий статус. В API-слое, как `HttpCurrentActor`: нужны флаги процесса (`HostingMode`) и `DbStatusService` |
| Подсчёт | `JobsOverviewBuilder` (`Swimm.Application/Mapping`) — чистые функции: статус расписания (`Schedule`), просрочка, сводка-плитки и проблемные службы (`Build`). Тесты — `JobsOverviewBuilderTests` |
| DTO | `Swimm.Application/Dtos/BackgroundJobsDtos.cs`: `JobCard`, `JobsSummary`, `OverviewTile`, `BackgroundJobsReport` |
| API дашборда | с 20.11.2026 блок «Jobs» берёт всё из `GET /api/admin/jobs/monitor` (`summary` внутри — та же сводка); `GET /api/admin/jobs/summary` (`JobsAdminController`) остаётся |
| API мониторинга (20.11.2026) | `GET /api/admin/jobs/monitor` — всё для новой страницы одним JSON; `GET /api/admin/jobs/runs/{id}` — один прогон с подробностями. Контракт — раздел [«Контракт мониторинга»](#контракт-мониторинга-get-apiadminjobsmonitor) ниже |

Не путать с `/Admin/Services` — там внешние сайты и кабинеты.

## Что показывает

Все `AddHostedService` процесса (`Program.cs`, `Swimm.Infrastructure/DependencyInjection.cs`) в
трёх группах. Вверху — окружение и два флага процесса: `Admin:Enabled` (админка и очереди) и
`BackgroundJobs:Enabled` (расписания, `HostingMode`).

| Вид | Службы | Откуда состояние |
|---|---|---|
| **Расписания** — сами ходят наружу по таймеру | `discovery`, `start-list`, `results-auto` (П5, режим `ResultsAutoMode`, см. ниже) (`CompetitionDiscoveryBackgroundService`), `loglig-verify`, `loglig-batch`, `records-check` (`RecordsCheckBackgroundService`, 16.11.2026: проверка источников рекордов — дифф и журнал `Sys_RecordSourceChecks`, Apply НЕ делает), `swimmer-club-check` (`SwimmerClubCheckBackgroundService`, 18.11.2026, Р98: сверка кандидатов на смену клуба пловца с loglig — см. ниже) | журнал `Sys_JobRuns` (пора ли) + история `Sys_JobRunLog` (что сделали) |
| **Прод-расписание забора** (П2, 20.11.2026) — `discovery`, `start-list` и `results-auto` (П5) под `AutoPull:Enabled` | те же карточки, что в строке «Расписания» | флаг ВЫКЛ, пока не пройдена проба сети П0 — задачи идут из ProdAdmin, как раньше. Включён в `appsettings.Production.json` → служба на проде, а ProdAdmin (читает тот же файл) свою не заводит и карточка «следит» за прод-журналом: факт «работает на ПРОДЕ», статус «на проде (AutoPull:Enabled): …», просрочка и сбой видны. См. «Прод-расписание забора» ниже |
| **Прод-расписание** — идёт с ПРОДА круглосуточно | `training-reminders` (`TrainingReminderBackgroundService`, напоминания о тренировках через Web Push, [web-push-reminders-plan.md](../plans/web-push-reminders-plan.md)) | тот же журнал; флаг свой — `PushReminders:Enabled` (true только на проде). Тик 5 мин: каждый сдвигает «последний тик» (`IJobRunStore.RecordStateAsync`), строку истории пишет только тик, который что-то отправил или упал. В ProdAdmin карточка «следит» за прод-джобом: тик старше 35 мин — «молчит» (жёлтая) |
| **Очереди** — исполняют то, что поставила админка | импорт (`ImportBackgroundService`), рекорды по странам (`RecordCountryRunBackgroundService`), проверка пар клубов в loglig (`ClubPairLogligCheckBackgroundService`, 19.11.2026, Р112 — см. ниже) | снимок очереди в памяти + их собственные таблицы: `Sys_ImportHistory`, журнал свежести рекордов `Sys_RecordSourceChecks` (`worldrecords-countries`); у проверки пар — только память процесса (ответы loglig — в `Sys_SwimmerLogligClubs`) |
| **Служебные** | пинг БД (`DbPingBackgroundService`), отставание от прода (`DataSyncCheckBackgroundService`, только Development), мост кэша (`CacheBridgeListener`) | память процесса: `DbStatusService.LastPing`, `IDataSyncStatus.Current`, `ICacheBridgeStatus.State()` |

Вне карточек намеренно — **прогрев после старта** (`StartupWarmupService`, 19.11.2026): разовый шаг
старта только на проде (`Warmup:Enabled` в `appsettings.Production.json`), а прод админки не имеет.
Итог — строка журнала «Прогрев после старта…» (Warning, если адрес не прошёл — тогда видно в
App Insights); его запросы в телеметрии помечены `swimm.source=warmup` ([appinsights.md](appinsights.md)).

У расписания: запущено ли в ЭТОМ процессе, настройка «включено» и интервал (Admin/Settings;
ключи и умолчания — `JobNames.All` в `Swimm.Application/Mapping/JobSchedule.cs`), последний
запуск и успех, ошибка, когда следующий (`JobSchedule.NextDueAt` — то же правило, что
`IsDue`), и последние 15 прогонов: начало, кто запустил (фон / кнопка / CLI), сколько длился,
итог одной строкой или ошибка.

## Как устроена страница (с 20.11.2026)

Редизайн по передаче дизайнера (`!design_handoff/design_handoff_admin_jobs_monitor/`, не в git) и
решениям Влада 20.11.2026. Разметка (`Jobs.cshtml`) — только каркас; всё рисуют скрипты из
`GET /api/admin/jobs/monitor` (контракт — раздел «Контракт мониторинга» ниже).

| Скрипт (`wwwroot/js/admin/jobs/`) | Что делает |
|---|---|
| `jobs-ui.js` | общее: `window.JobsMon`, формат времени, статусы (цвет + иконка + слово), зоны, тексты о службе (главная строка, итог, «следующий») |
| `jobs-panels.js` | «Требует внимания», «Идёт сейчас», «Ближайшие запуски», «Журнал» |
| `jobs-timeline.js` | вид «Таймлайн» и легенда меток |
| `jobs-wall.js` | вид «Стена статусов» |
| `jobs-detail.js` | панель деталей службы и окно прогона («Что изменилось») |
| `jobs-page.js` | загрузка, опрос, шапка, счётчики, переключатель вида, клавиатура, `#job-<ключ>` |

Классические скрипты без сборщика, как `dashboard.js`; Tailwind-классы в них — целыми строками (их
собирает `@source` в `Styles/admin.css`).

- **Ширина.** Страница ставит `ViewData["WideMain"]` и `ViewData["OwnHeader"]` — `_Layout` снимает
  потолок 1200 px и не рисует общий `_PageHeader` (у страницы своя шапка). Больше никто эти флаги не
  ставит. Сайдбар как был.
- **Шапка:** «Jobs», чип среды (`environment`), подпись по `journal` («журнал прод-базы — общий с
  продом» / «журнал локальной базы»), переключатель **«Таймлайн | Стена»** (`<button aria-pressed>`),
  «обновлено HH:MM:SS · авто 30 с», «Обновить».
- **Опрос** раз в 30 с; при скрытой вкладке не опрашивает, при возвращении — сразу, если данные
  старше 30 с. Ошибка запроса — жёлтая плашка `role="alert"` над счётчиками, последние данные
  остаются на экране.
- **Вид:** по умолчанию «Таймлайн»; выбор — `localStorage` `admin:jobs:view`, `?view=timeline|wall`
  в адресе главнее (переключение пишет и туда, и туда). Переключение — без запроса и перезагрузки.
- **Шесть счётчиков** (`counters`): упала · просрочена · идут · ждёт тебя · в норме · выключены —
  пилюли в «Таймлайне», плитки в «Стене». Заменили 11 плиток, плашку процесса и окно «Службы».
- **Зоны** — рамки из `zones[]` (цвет `admin-env-prod` / `admin-env-local` + иконка облако/монитор;
  неизвестная третья зона — рамка цвета акцента). Справа в шапке зоны — пульс (`pulseKind`: `tick` —
  «жив, тик N назад», `started` — «запущен в HH:MM»). Подгруппы «Расписания / Очереди · по кнопке /
  Служебные» — по `kind`, подписи только если в зоне больше одного вида. В Development зона одна —
  «LOCAL · Development».
- **«Таймлайн»:** ось окна (`windowFrom..windowTo`, 36 ч скользящее) — подписи каждые 3 ч на
  экране ≥ 1536 px (уже — каждые 6 ч), полночь — «сб 21.11», «сейчас HH:MM». Дорожка: статус +
  название (на широком — и слово статуса) | полоса | «следующий запуск» (на широком второй строкой —
  итог). На полосе: прошлое светлее, будущее темнее, метки прогонов (`windowRuns`: зелёная/красная),
  идущий — голубой отрезок до «сейчас», просрочка — оранжевый от `overdueSince`, план (`planned`) —
  серые метки, у `nextApprox` (start-list) — короче и бледнее («ориентир»). `timelineMode = ticker`
  (напоминания): ленту за всё окно не рисуем — только от последнего тика до «сейчас» и подпись
  «пустые тики не хранятся»; метки — непустые тики. Служебные — строкой плашек. Внизу три панели.
- **«Стена»:** плитки расписаний (главная строка крупно цветом статуса, итог с многоточием, полоска
  15 исходов `recentOutcomes`, «был / след.»), очереди и служебные — короче. При ширине окна ≥ 1700 px
  — «Журнал» под зонами (последние 8 прогонов всех задач из `history[]` + идущие по заявке, с меткой
  зоны) и колонка панелей справа; уже — журнала нет, панели рядом под зонами.
- **Панели:** «Требует внимания» (`attention[]`; брошенная заявка — жёлтая; «ждёт тебя» — ссылка во
  «Входящие»), «Идёт сейчас» (`running[]`; прогресс — процент, без оценки — полосатая), «Ближайшие
  запуски» (`upcoming[]`; повтор после ошибки — красным, `approx` — «≈»). Пусто — одна строка текста.
- **Панель деталей службы** — клик (или Enter) по дорожке, плитке, плашке, строке панели или журнала;
  справа поверх страницы 560 px, на узком — во всю ширину. Внутри: статус и `statusText`, «что
  делает», брошенная/живая заявка, прогресс, **полный текст ошибки** (`lastError`), факты (`facts`),
  у `records-check` — «Рекорды · источники» (`recordsSources`), параметры и настройки (`toggle`,
  `settings`), действия и ссылки (`actions`: `post` — кнопка с `X-XSRF-TOKEN`, после ответа данные
  перечитываются; «Проверить сейчас» у рекордов), у очередей — прогоны с запуска процесса, у импорта —
  «Последние импорты» (`recentImports`), у расписаний — история (Начало / Кто / Откуда / Длилось /
  Что сделала). Дата прогона с `hasDetails` открывает окно `GET runs/{id}`: у discovery — «Что
  изменилось» (`discoveryChanges`), у остальных — итог, ошибка и `details` как есть. Закрывается Esc и
  ✕; фокус возвращается на элемент, с которого открыли. Адрес получает `#job-<ключ>` — ссылки
  дашборда `/Admin/Jobs#job-<ключ>` открывают панель сразу. При опросе панель перерисовывается
  только если что-то изменилось, прокрутка и фокус сохраняются.
- **Справка «ⓘ» у службы** (20.11.2026, [admin-notes.md](admin-notes.md)): есть файл
  `docs/admin-notes/job.<ключ службы>.md` — в шапке панели деталей рядом с названием кнопка «ⓘ» с
  попапом; нет файла — значка нет, код не правится. На дорожке, плашке и плитке — пассивная пометка
  «ⓘ» (`data-note-mark`: они сами `<button>`, кнопка в кнопке невалидна); клик по пометке открывает
  панель и сразу попап справки. Первая — [job.loglig-batch.md](../admin-notes/job.loglig-batch.md)
  («почему выключена»). Поменял поведение службы — обнови её справку и дату «Проверено».
- **Доступность:** статус — всегда цвет + иконка + слово (в плитке/дорожке слово в `aria-label`);
  дорожки и плитки — `<button>`, стрелки ↑↓←→ ходят между ними. Время — местное время браузера
  (раньше — местное время сервера; у Влада оба — Израиль), «N назад» — от серверного `now`.
  Ивритские куски в русских строках изолируются `<bdi>` (`JobsMon.bidi`).

Убрано вместе со старой страницей: `Shared/_JobCard.cshtml`, `Shared/_JobRunChangesDialog.cshtml`,
окно «Службы» и плитки сводки на странице (`summary.tiles` в ответе `monitor` и `summary` остались,
страница их больше не рисует). Переключатели «По месту / По типу / По статусу» и «12 ч / 36 ч / 7 дн»
из макетов намеренно не нарисованы (решение Влада 20.11.2026).

**Проверка двухзонного вида без ProdAdmin.** В Development зона одна; вид ProdAdmin проверяется на
подставных данных: в консоли страницы `JobsMon.page.stop()` (остановить опрос), затем
`JobsMon.page.show(d)` с переписанным ответом `monitor` (зона `prod`, все шесть статусов). Фикстуры
в репозитории нет.

**Проблема службы** (`JobCard.Alert`, из неё — главная цифра блока дашборда): danger — у работающего
расписания упала последняя попытка, база не отвечает, мост кэша включён и не слушает; warning —
расписание просрочено, локальная база отстаёт от прода. У выключенной службы проблемы нет, даже если
её последний прогон упал. Статус мониторинга (`status`) шире — см. контракт.

Сводка `JobsOverviewBuilder.Build` (`summary`, плитки `OverviewTile`, статистика 24 ч / 7 дн) по-прежнему
кормит блок «Jobs» дашборда; «Рекорды · источники» — `RecordsCheckTileBuilder` (тесты
`RecordsCheckTileBuilderTests`): давность последней успешной проверки, «ждут Apply …», «сбой: …»;
тон danger — упала попытка источника, warning — есть что применить или старше 7 дней.

Светлой темы у админки нет — проверена тёмная на 1920, 1280 и 375 px (без горизонтального скролла).

## История прогонов (`Sys_JobRunLog`)

- Пишет `IJobRunStore` (`JobRunStore`): `RecordAsync` — состояние в `Sys_JobRuns` **и** строка
  истории; `AppendLogAsync` — только строка истории, расписание не сдвигается.
- Кто пишет: `ScheduledJob.RunIfDueAsync` (фон, итог возвращает сама работа); кнопка «Проверить
  сейчас» в `/Admin/Competitions` (`DiscoveryAdminController.Sync`, текущий сезон — `RecordAsync`,
  прошлый сезон и неудача — `AppendLogAsync`); `--discovery-sync` (CLI); ручной батч в
  `/Admin/Swimmers/Loglig` (`AppendLogAsync`: батч другого размера ночной не откладывает);
  кнопка «🔎 Проверить все источники» на `/Admin/Import` (`RecordsImportController.CheckAll`,
  `RecordAsync` задачи `records-check`: после ручной проверки ночная сутки не повторяет).
  CLI `--records-check` в историю Jobs не пишет — только журнал `Sys_RecordSourceChecks`.
  Проход `swimmer-club-check` **после импорта** (`AppendLogAsync`, триггер `import` — «после импорта»
  в истории): суточное расписание не сдвигает.
- Итог — одна строка из `JobRunSummaries` (один текст на задачу, кто бы ни запустил).
- **Подробности — `Details` (jsonb, миграция `AddJobRunLogDetails`, 16.11.2026).** Пока пишет только
  discovery (фон, кнопка, CLI): список самих изменений прогона — `DiscoverySyncResult.Changes`
  (`DiscoveryChange`, виды — `DiscoveryChangeKinds`: new / postponed / restored / updated / missing),
  JSON собирает и разбирает `JobRunDetails` (потолок `MaxChanges` = 300, обрезка помечается).
  Прогон без изменений подробностей не пишет. Фон отдаёт их через `JobRunOutcome` в
  `ScheduledJob.RunIfDueAsync` (перегрузка; старая, со строкой, осталась у остальных задач).

## Попап «что изменилось» (discovery)

В истории панели деталей «Входящие isr.org.il» дата прогона с подробностями (`hasDetails`) — кнопка
(пунктирное подчёркивание): открывает окно `<dialog id="run-changes">` — его наполняет
`jobs-detail.js` из `GET /api/admin/jobs/runs/{id}` (`discoveryChanges`). У прогонов без подробностей и у
старых строк (до миграции) дата обычным текстом.

- Группы по порядку: **Новые**, **Перенесены** (старая дата зачёркнута → новая), **Обновлены**
  (имя или даты без переноса; «было: …» у переименованных; отменённый перенос — с пометкой),
  **Пропали**. Перенос входит и в счётчик «обновлено» итога — так и подписано.
- Ссылки строки: `compID ↗` на isr.org.il и запись в /Admin/Competitions — «в базе #id» (правка
  соревнования), если старт уже есть в базе, иначе «во Входящих» (поиск `?q=<имя>&season=<cYear>&discipline=all`
  — ровно его строка). Есть ли в базе — `ICompetitionDiscoveryService.GetAllAsync` (штамп
  OrgCompId, иначе матч имя+дата — как сама /Admin/Competitions); зовётся только при открытии окна (`GetRunDetailsAsync`).
- Вид собирает чистая `DiscoveryRunChangesBuilder` (`Swimm.Application/Mapping`) в
  `BackgroundJobsStatusService.GetRunDetailsAsync`. Закрытие: Esc, ✕, клик по фону; фокус — обратно
  на дату (Esc закрывает сначала окно, вторым нажатием — панель деталей).
- Тесты: `JobRunDetailsTests` (JSON, группы, ссылки, даты), `CompetitionDiscoveryServiceTests.Sync_RecordsChangeList_MatchingCounters`,
  `JobRunStorePgTests.Details_jsonb_round_trip` (настоящий Postgres под swimm_rw).
- Рост ограничен: после каждой записи у задачи остаются последние `JobRunLog.KeepPerJob` = 50.
- `Trigger` ограничен CHECK `CK_Sys_JobRunLog_Trigger`: `schedule` / `manual` / `cli`.
- **`RunBy` — откуда шёл прогон** (миграция `AddJobRunLogRunBy`, 20.11.2026): подпись процесса
  «среда@машина#pid» (`JobRunOrigin`, singleton в `Program.cs`; та же строка, что владелец заявки
  П1). Ставит само хранилище в `AppendLogAsync` — поэтому есть при любом триггере, вызывающие её не
  передают. В панели деталей — колонка «Откуда» (`history[].originText`): `Production` → «прод», `ProdAdmin` → «ProdAdmin»,
  `Development` → «локально» (`JobTimeText.Origin`), полная подпись — в подсказке. У строк старше
  20.11.2026 — «—». Зачем: с `AutoPull:Enabled` забор идёт с прода, а журнал у прода и ProdAdmin
  общий — без подписи не отличить, кто выполнил прогон.
  ⚠ Порядок выката: миграция на прод ДО деплоя (код пишет и читает колонку).
- Sys_, приватная: без гранта `swimm_ro`, **не** в `server/db/seed-tables.txt` — у каждой базы
  свой журнал (миграция `AddJobRunLog`).

## Частота стартовых протоколов (`start-list`, П3, 20.11.2026)

План — [prod-auto-pull-plan.md](../plans/prod-auto-pull-plan.md) §4.3 (решение Р-4). Посев выкладывают за
12–36 ч до старта; суточный шаг на всё окно опаздывал на сутки.

- **Шаг тика** — своя настройка `StartListIntervalHours` (3 ч; раньше общий `DiscoveryIntervalHours`,
  24 ч). `ScheduledJob` по-прежнему решает только «пора ли тик» и держит заявку П1 на весь тик.
- **Кого смотреть в тик** — чистая `StartListSweepRules.Decide` (`Swimm.Application/Mapping`, тесты
  `StartListSweepRulesTests`) по журналу заборов `Sys_StartListPulls` и дате старта (по Израилю):

  | До старта | Шаг (минус час допуска на длину тика) |
  |---|---|
  | 3 дня … `StartListDaysAhead` | раз в сутки |
  | 1–2 дня, посева нет | 3 ч |
  | 1–2 дня, посев уже забран (ok/partial с заявками) | 6 ч |
  | день старта | 3 ч до первого заплыва (`CompetitionEntries.HeatStartAt` этого дня), потом не трогает |

  Последний забор упал — повтор не позже 3 ч и у дальних. Дальний старт без loglig-id не тянется
  (ждёт догрузки деталей); ближайший без loglig-id — тянется: забор сам дочитывает comp.asp (И-54).
- **loglig-id** (`RefreshUpcomingDetailsAsync`, окно + прошедшие за 30 дней) — раз в сутки: первый тик
  нового дня по Израилю (`StartListSweepRules.DetailsDue` по прошлому успеху задачи).
- **Итог строкой:** «окно 14 дн.: посмотрели 3 из 20 (рано 16, уже начались 1), забрано 3 из 3; loglig-id
  добыт у 1 из 4» — в тик без догрузки вместо хвоста «loglig-id — в суточном тике».
- Паузы между запросами к loglig — прежние (внутри `IStartListPullService`). Шаг больше 3 ч просто делает
  ближайшие старты реже; меньше — частоту стартов не меняет (правило), но чаще читает журнал.
- Тесты: `StartListSweepRulesTests` (границы 2 дня / день старта / посев / старт прошёл / день по Израилю),
  `StartListScheduleServiceTests` (InMemory), `StartListSchedulePgTests` (настоящий Postgres, только чтение).

## Прод-расписание забора (`AutoPull:Enabled`, П2, 20.11.2026)

План — [prod-auto-pull-plan.md](../plans/prod-auto-pull-plan.md) §4.2, решения Р-1 и Р-6.

- **Где идёт `CompetitionDiscoveryBackgroundService`** (`discovery` + `start-list`) — правило
  `HostingMode.DiscoveryScheduleEnabled` (тесты `HostingModeTests`): свой `AutoPull:Enabled` — да; иначе
  по `BackgroundJobs:Enabled`, но в ProdAdmin — только пока прод сам не забирает. «Прод забирает» ProdAdmin
  узнаёт из закоммиченного `appsettings.Production.json` (`HostingMode.ProdAutoPullEnabled`): поэтому
  включать надо **файлом**, а не переменной App Service — её ProdAdmin не видит.
- **Сейчас** флаг `true` (П0 пройдена 20.11.2026): `discovery`, `start-list`, `results-auto` ведёт прод,
  у ProdAdmin этой службы нет. Аварийно вернуть в ProdAdmin — `false` той же строкой файла + деплой.
- **Остальные расписания ProdAdmin** (`loglig-verify`, `loglig-batch`, `records-check`,
  `swimmer-club-check`) — в своих службах под одним `BackgroundJobs:Enabled`, флаг их не трогает.
- **Выключатели в базе главные:** `DiscoveryEnabled` / `StartListEnabled` гасятся в `/Admin/Settings` из
  ProdAdmin, прод подхватывает на следующем тике (5 мин) без деплоя.
- **Двойной запуск:** тики прода и ProdAdmin (старый код до перезапуска) разводит замок П1. Кнопка
  «Проверить сейчас» (`DiscoveryAdminController.Sync`) и CLI (`--discovery-sync`, `--pull-start-list`)
  заявку **не берут** (решение П2): это человек, которому нужен результат сейчас, а совпадение с прогоном
  прода безвредно — «Входящие» держит UNIQUE `OrgCompId`, заявки — UNIQUE
  (`OrgDisciplineId`, `Heat`, `Lane`, `SwimmerId`): второй писатель упадёт на ключе, а не задвоит строки,
  и повторит на следующем тике/нажатии.
- **Телеметрия:** исходящие запросы и SQL внутри прогона любого `ScheduledJob` помечены
  `swimm.source=job` и `swimm.job=<задача>` (`Telemetry/JobTelemetry.cs`: имя задачи в `AsyncLocal`,
  процессор ставит теги на старте спана; заголовков наружу не добавляет).
- В логе старта — строка «Забор «Входящих» и стартовых протоколов по расписанию: ВКЛ/выкл в этом
  процессе; AutoPull:Enabled …», у ProdAdmin при снятом расписании — пояснение, что идёт с прода.

## Результаты прошедших стартов (`results-auto`, П5, 20.11.2026)

План — [prod-auto-pull-plan.md](../plans/prod-auto-pull-plan.md) §4.4–4.5, «П5 — ход» (решение Р-2).
**Ничего не импортирует** — только вердикт и причина у строки «Входящих».

- **Где идёт:** третий проход `CompetitionDiscoveryBackgroundService` после `discovery` и `start-list` —
  то же правило `HostingMode.DiscoveryScheduleEnabled` (сейчас ProdAdmin, после включения
  `AutoPull:Enabled` — прод). Своей службы нет: так результаты строго последовательны с забором
  старт-листов (loglig не получает два потока, процесс сайта — два разбора PDF сразу).
- **Режим** `ResultsAutoMode` (Admin/Settings): `off` (умолчание) — тик даже не заводится, в журнал
  ничего не пишется; `report` — проверяет; `import` — пока то же, что report, с пометкой в итоге
  «импорт ещё не реализован — П6». `ResultsAutoEnabled` (умолчание true) — аварийный выключатель;
  `ResultsAutoIntervalHours` (1 ч) — шаг тика. Карточка при `off` — «выключена» с причиной в факте
  «Настройка».
- **Кого за тик** — чистая `ResultsAutoRules.Pick` (тесты `ResultsAutoRulesTests`): одна строка `new`,
  плавание, с loglig-id, не связанная с базой, без ручной пометки «протокола нет», с полудня дня
  окончания по Израилю и не старше 7 дней. Сначала ни разу не смотренные (свежий старт первым), потом
  дольше всех не перепроверявшиеся. Перепроверка: «ещё идёт» — 2 ч, сбой — 3 ч, «протокола нет» —
  6 ч, «ждёт тебя» и «взяла бы сама» — сутки.
- **Что со строкой:** чемпионат по имени и многодневка по датам — сразу «ждёт тебя», без сети. Иначе —
  `IBulkPullService.PullOneAsync` (превью «Затянуть» + регламент + `BulkPullClassifier`, ровно путь
  пакетной кнопки) → гейты `ResultsAutoRules.Decide`: 🟢 классификатора (🟠 «без регламента» — тоже
  «ждёт»), не чемпионат, один день в файле, закончено (`CompetitionProgress`), бассейн из регламента или
  справочника (`IVenuePoolService.LoadAsync` → `Resolve`). Превью снимается из кэша сразу.
- **Вердикт** — в строку: `AutoPullVerdict` (`would-take` / `blocked` / `not-finished` / `failed` /
  `empty`, CHECK `CK_Sys_DiscoveredCompetitions_AutoPullVerdict`), `AutoPullBlockedReason`,
  `AutoPullCheckedAt`, `AutoPullPoolType` (у would-take). Чип и фильтр — `/Admin/Competitions`
  ([competitions.md](competitions.md)), блок — дашборд.
- **Итог строкой:** «report: в окне 3, пора 1 — «ליגה …» #1234: взяла бы сама, 25m (venue)» / «…: ждёт
  тебя — бассейн не задан: …». Подробности (`Details`, JSON `ResultsAutoTickReport`) — только у тика,
  который смотрел строку; попапа у карточки нет (читает SQL отчёта П5).
- **CLI:** `--results-auto-tick [--id <discoveredId>]` — один тик в report (всегда, без импорта),
  `--id` минует окно и шаг перепроверки; в истории — триггер «CLI», расписание не сдвигается.
- **Безопасность публичного процесса (§6):** потолок строки 10 мин (`ResultsAutoService.RowTimeout`,
  связан с `stoppingToken`), предел ответа 32 МБ на клиентах забора (`OutboundLimits.MaxResponseBytes`:
  `IsrOrgDiscoveryProvider`, именованные `loglig`), таймауты 60 с / 100 с; сбой строки — вердикт
  `failed`, сбой тика — запись в журнал `ScheduledJob`, сайт не падает.

## Смена клуба пловца ↔ loglig (`swimmer-club-check`, Р98)

С 18.11.2026 импорт клуб в карточке пловца сам не меняет — заводит кандидата
(`Sys_SwimmerClubCandidates`), а эта задача сверяет его с карточкой loglig.com и меняет клуб, только
если loglig показывает новый клуб (решение — [data-integrity.md](../data-integrity.md) Р98).

- **Суточный прогон** — по расписанию (`SwimmerClubCheckEnabled`, дефолт **вкл**;
  `SwimmerClubCheckIntervalHours`, 24): все ждущие кандидаты; ответ loglig моложе суток берётся из
  кэша кандидата (`LogligClubName`, `CheckedAt`) без сети.
- **После импорта** — импорт дёргает сигнал (`ISwimmerClubCheckSignal`), служба просыпается (после
  30 с тишины — дни многодневки собираются в один проход) и сверяет только **новых** кандидатов (без
  вердикта). В истории — триггер «после импорта», расписание не сдвигается. Нечего сверять — строки нет.
- Сеть последовательно, ~1 запрос в секунду (`SwimmerClubLogligCheckService.DefaultRequestGap`):
  ~330 кандидатов ≈ 6 минут. Бесплатно — карточка loglig публичная.
- Итог строкой: «сверено 12: confirmed 9 · archive 2 · old 1; запросов 10, из кэша 2, применено 9».
- Работает там же, где остальные расписания (ProdAdmin) — там же идёт и импорт. В разработке
  кандидаты ждут: сверить — CLI `--refresh-swimmer-clubs` или «Проверить сейчас» на `/Admin/Swimmers`.

## Пары клубов ↔ loglig (`club-pair-loglig`, Р112)

Очередь кнопок «Проверить в loglig» на /Admin/Clubs ([clubs.md](clubs.md)): у пары «Общие пловцы» или
у всех пар сразу открывает карточки loglig общих пловцов с ответом старше суток, последовательно ~1 в
секунду, общего для нескольких пар — один раз, и пишет клуб в `Sys_SwimmerLogligClubs`. Очередь в
памяти (`ClubPairLogligCheckQueue`, singleton), служба `ClubPairLogligCheckBackgroundService`, живёт под
`adminEnabled`, как рекорды по странам. **Активный прогон один**: вторая кнопка, пока идёт первый,
получает 409 со статусом идущего, и страница просто показывает его ход. Статус —
`GET /api/admin/clubs/dedup-candidates/loglig/runs/{id}` (и `runs/current` — страница после перезагрузки
подхватывает идущий). Перезапуск процесса прогон обрывает; сделанное уже сохранено, повтор откроет только
оставшееся. Карточка показывает идущий и последний прогон с запуска процесса.

## Замок прогона (`ClaimedAt` / `ClaimedBy`, П1, 19.11.2026)

Зачем: в одну базу могут смотреть два процесса (прод и ProdAdmin, план
[prod-auto-pull-plan.md](../plans/prod-auto-pull-plan.md) §4.1). «Пора ли» оба читают до работы, попытку
пишут после — без замка оба начали бы один и тот же забор.

- **Заявка** — две колонки в `Sys_JobRuns` (миграция `AddJobRunClaim`): `ClaimedAt` (часы БАЗЫ, `now()`)
  и `ClaimedBy` («среда@машина#pid», `JobClaimOwner`).
- **Берёт её `ScheduledJob.RunIfDueAsync`** — и только когда задаче пора (пустой тик в базу не пишет):
  `IJobRunStore.TryClaimAsync` — один запрос `INSERT … ON CONFLICT DO UPDATE … WHERE заявки нет или она
  протухла`. Не досталась — в лог «занята: её выполняет X с …», тик пропускается, в историю ничего не
  пишется. Досталась — «пора ли» читается ещё раз (другой процесс мог закончить, пока шли к заявке).
- **Снимается** после записи итога (`ReleaseClaimAsync`, только своя — сравнение по `ClaimedBy`), в том
  числе при ошибке работы и при остановке приложения.
- **Протухает через `JobSchedule.ClaimStaleAfter` (2 ч)** — процесс убит посреди прогона (деплой).
  Следующий прогон перехватывает заявку сам; до тех пор задача ждёт.
- **На странице:** бейдж и факт «Идёт сейчас — с …, запустил …» (видно и из процесса, где расписания
  выключены: заявка в базе); протухшая — факт «Брошенная заявка» жёлтым.
- **Чего замок НЕ закрывает:** кнопки («Проверить сейчас», «Проверить все источники»), CLI и проход
  `swimmer-club-check` после импорта идут мимо `ScheduledJob` и заявку не берут. Напоминания о
  тренировках — тоже (у них свой UNIQUE журнала доставки).
- Тесты: `JobRunClaimPgTests` (настоящий Postgres: 12 одновременных заявок — один победитель; два
  «процесса» `ScheduledJob` — работа один раз), `JobScheduleTests`, `JobsOverviewBuilderTests`.

## Контракт мониторинга (`GET /api/admin/jobs/monitor`)

Слой данных редизайна страницы (передача дизайнера `!design_handoff/design_handoff_admin_jobs_monitor/`,
решения Влада 20.11.2026). Страница (виды «Таймлайн» и «Стена», панель деталей службы) и блок Jobs
дашборда рисуются **на клиенте** из этого JSON; готовые строки фактов разбирать не нужно. Нынешняя
страница и `GET /api/admin/jobs/summary` работают как раньше.

| Что | Где |
|---|---|
| Ручки | `JobsAdminController`: `GET monitor`, `GET runs/{id}` (404 — строки нет: вытеснена обрезкой). Та же защита, что у `summary`: `[Authorize(Roles = "Admin")]`, antiforgery |
| Порт | `IBackgroundJobsStatus.GetMonitorAsync` / `GetRunDetailsAsync` |
| Сборка | `BackgroundJobsStatusService` кладёт в карточку структурные поля (`JobCard.Monitor`, тип `JobMonitorExtras`), чистый `JobsMonitorBuilder` (`Swimm.Application/Mapping`) собирает ответ |
| DTO | `Swimm.Application/Dtos/JobsMonitorDtos.cs` (`JobsMonitor`, `JobMonitorItem`, …) |
| Тесты | `JobsMonitorBuilderTests` (статус, зона, окно, план, исходы, счётчики, панели, «ждёт тебя»), `JobsAdminControllerContractTests` (имена полей JSON), `JobRunStorePgTests.Light_log_has_details_flag_without_details` |

JSON — camelCase, время — UTC ISO 8601 (`2026-11-20T14:20:00Z`); форматирует клиент (у Влада — Израиль).

**Клиенты контракта:** страница `/Admin/Jobs` и блок «Jobs» дашборда ([index.md](index.md), п. 9; `wwwroot/js/admin/dashboard-jobs.js`). Строка службы в панели блока ведёт на `/Admin/Jobs#job-<key>` (ключ — `jobs[].key`) — страница по этому якорю открывает панель деталей службы.

### Корень ответа

| Поле | Что |
|---|---|
| `now` | серверное «сейчас» (UTC) — от него считать «N мин назад», а не от часов браузера |
| `environment` | `ProdAdmin` \| `Development` (чип среды) |
| `journal` | чей журнал показан: `prod` (прод-база, ProdAdmin — «журнал прод-базы, общий с продом») \| `local` |
| `windowFrom`, `windowTo`, `windowHours` | окно таймлайна: **36 ч, скользящее** — от часа «сейчас» минус 18 ч; «сейчас» посередине, граница суток всегда внутри |
| `zones[]` | зоны в порядке прод → local, только непустые (см. ниже) |
| `counters` | шесть счётчиков: `failed`, `overdue`, `running`, `waiting` («ждёт тебя»), `ok` (вместе с `idle`), `off` |
| `jobs[]` | все службы в порядке нынешней страницы: расписания, очереди, служебные |
| `attention[]` | «Требует внимания» |
| `running[]` | «Идёт сейчас» |
| `upcoming[]` | «Ближайшие запуски» (до 8) |
| `waiting[]` | строки results-auto «ждёт тебя»: `id`, `orgCompId`, `name` (иврит — `dir="auto"`), `dateStart`, `dateEnd`, `reason`, `checkedAt` |
| `summary` | **та же** сводка, что `GET /summary` (`JobsOverviewBuilder.Build`), с теми же `counters` |
| `recordsSources` | плитка «Рекорды · источники» (`RecordsCheckTileBuilder`): `value`, `sub`, `tone`, `lastCheckedAt` |
| `recentImports[]` | 5 последних импортов (`ImportHistoryDto`, как на нынешней странице) |

### Зона (`zone`, `zones[]`)

| Значение | Когда |
|---|---|
| `prod` | задача **не запущена здесь**, а этот процесс следит за её прод-журналом — ровно условие факта «не запущена — работает на ПРОДЕ…» (`Placement.Watching` у discovery/start-list/results-auto при `AutoPull:Enabled` прода; `training-reminders` в ProdAdmin). Не список ключей |
| `local` | всё остальное: выполняется в этом процессе или выключено здесь. **В Development — всегда `local`**, зоны `prod` нет |

`zones[]`: `key`, `process`, `pulseAt`, `pulseKind`, `jobKeys` (порядок служб в зоне). Пульс прода —
`process` = `RunBy` последней строки журнала с подписью `Production@…`, `pulseAt` = последний тик
`training-reminders` (`Sys_JobRuns.LastAttemptAt`), `pulseKind` = `tick`. Пульс local — подпись этого
процесса (`JobRunOrigin`) и время его запуска, `pulseKind` = `started`. Подгруппы внутри зоны — по
`kind` службы.

### Статус (`status`) — шесть значений

Получается из бейджа нынешней карточки (`JobsMonitorBuilder.Status`; бейдж уже выбран по приоритету
идёт → выключена → упала → просрочена): исходный бейдж — в `badge` (`text`, `variant`).

| `status` | `badge.variant` | Бейджи сейчас |
|---|---|---|
| `failed` | danger | «последний запуск упал», «последний тик упал», «база недоступна», «не слушает» |
| `overdue` | warning | «просрочена», «молчит», «ещё не запускалась», «ещё не сверяла», «локальная отстаёт», «прод недоступен», «ждёт первого пинга» |
| `running` | info | «идёт сейчас», «импортирует», «идёт прогон», «идёт проверка» |
| `ok` | success (не очередь) | «работает», «база доступна», «слушает», «в синхроне» |
| `idle` | success (очередь) | «ждёт задания» |
| `off` | neutral | «выключена», «выключен», «не в этом процессе» |

⚠ `status` шире, чем `alert` (проблема для главной цифры дашборда, `summary.problems`): «ещё не
запускалась», «ждёт первого пинга», «прод недоступен», «ещё не сверяла» — `overdue`, но `alert = null`.
Счётчик «просрочена» и число проблем дашборда поэтому могут не совпадать — это разные вопросы.

### Служба (`jobs[]`)

| Поле | Что |
|---|---|
| `key`, `title`, `kind` | ключ (`discovery`, `import`, `db-ping`…), название, вид `schedule` \| `queue` \| `service` |
| `zone`, `status`, `badge`, `statusText` | см. выше; `statusText` — короткий статус одной строкой (как в списке служб нынешней страницы) |
| `alert`, `active` | проблема `danger`/`warning`/null (как в `summary.problems`); работает (здесь или наблюдается) и не выключена |
| `what`, `service` | «что делает» и имя класса службы |
| `intervalSeconds`, `intervalKey` | интервал (расписания — из Admin/Settings; напоминания 300, пинг 30, сверка с продом 900; очереди и мост — null) |
| `toggle` | выключатель расписания: `key`, `value`, `isDefault` (ключа в Admin/Settings нет); у очередей/служебных null — их включают флаги процесса |
| `settings[]` | параметры: `key`, `value` (как хранится; null — умолчание), `isDefault` — интервал, `ResultsAutoMode` у results-auto, `StartListDaysAhead`, `LogligBatchPerRun` |
| `lastRunAt`, `lastSuccessAt` | последняя попытка/тик/пинг/сверка и последний успех |
| `lastRun`, `lastDurationSeconds` | последняя строка истории (форма — как `history[]`) и её длительность. У напоминаний это последний **непустой** тик |
| `nextAt`, `nextIsRetry`, `nextApprox` | когда станет пора (то же правило, что `JobSchedule.IsDue`); повтор после ошибки (через 1 ч); план приблизителен (`start-list`: шаг — тик, частоту стартов решает правило близости) |
| `dueNow` | пора уже сейчас — запустится при ближайшей проверке журнала |
| `overdueSince`, `overdueSeconds` | только при `status = overdue` и известном моменте «пора»: оранжевый отрезок от `overdueSince` до `now` |
| `claim` | заявка `Sys_JobRuns`: `since`, `by` («среда@машина#pid»), `live` (false — **брошенная**, старше `ClaimStaleAfter` = 2 ч), `origin` |
| `lastError` | полный текст последней ошибки (как хранится, до 1000 символов); null, если последняя попытка удалась |
| `lastSummary` | итог последней работы одной строкой (у расписаний — из истории: итог или ошибка) |
| `stateSince` | с какого момента в текущем состоянии: идёт с…, мост слушает с… |
| `recentOutcomes[]` | до 15 исходов, **свежие первыми**: `success` \| `failed` \| `running` (идущий — первым) |
| `timelineMode` | `runs` — метки прогонов + план; `ticker` — напоминания: сплошная лента «тикает» + метки непустых тиков, плана нет; `none` — служебные (плашка, без дорожки) |
| `windowRuns[]` | прогоны в окне по возрастанию: `id` (null у идущего и у очередей), `startedAt`, `finishedAt` (null — идёт), `outcome`, `trigger`, `origin`, `runBy`. Идущий по заявке — открытый отрезок до «сейчас» |
| `planned[]` | плановые моменты после `now` до `windowTo`: первый — `nextAt`, дальше шагом интервала (до 48); у выключенных и `ticker` пусто |
| `progress` | `done`, `total`, `current`, `unit`, `note` — у прогона по странам («120 из 470 стран, сейчас FRA, строк 3400») и проверки пар клубов; null — «без оценки» |
| `queue` | очередь в памяти процесса: `queued`, `running`, `completed`, `failed` (с запуска процесса) |
| `facts[]` | пары «подпись–значение» (`label`, `value`, `variant`) для панели деталей — те же факты, что у нынешней карточки (у `records-check` плюс факт «Источники») |
| `actions[]` | `kind` (`link` — переход; `post` — кнопка: POST с antiforgery), `text`, `url`, `hint`. Сейчас: «Проверить сейчас» у `records-check` (`POST /api/admin/records/check-all`, до 5 минут) и ссылки (Входящие, ждут тебя, `/Admin/Import`, `/Admin/ImportHistory`, `/Admin/Clubs`, `/Admin/Cache`, `/Admin/Settings`) |
| `history[]` | до 15 последних прогонов (свежие первыми): `id`, `startedAt`, `finishedAt`, `durationSeconds`, `trigger` + `triggerText` (фон/кнопка/CLI/после импорта), `origin` + `originText` (прод/ProdAdmin/локально; null у строк старше 20.11.2026), `runBy`, `success`, `summary`, `error`, `hasDetails` |

`origin` — код по подписи `RunBy`: `prod` \| `prodadmin` \| `local` \| `other` \| null.

У **очередей** прогоны и исходы — из памяти процесса (после перезапуска с нуля), `history[]` пустая
(постоянная история импорта — `/Admin/ImportHistory` и `recentImports`). У **служебных** нет ни
прогонов, ни плана.

### Панели

- `attention[]` — `key`, `title`, `zone`, `kind`, `status`, `text` (объяснение по-русски), `since`, `href`.
  Порядок: `failed` (текст — «Упала: <ошибка>»), `abandoned-claim` (брошенная заявка, в том числе у
  службы в норме), `overdue` («Пора с HH:MM, прогона нет уже N. Последний успех — …»), `waiting`
  (по строке results-auto «ждёт тебя»: «Старт «…» #compId не взят автоматически: <причина>»,
  `href` — `/Admin/Competitions?filter=autopull-blocked&q=<имя>`, `status` = `waiting`). У служб `href`
  = null: клиент открывает панель деталей по `key`. «Ждут Apply» у рекордов сюда и в «ждёт тебя»
  **не** входят (решение 20.11.2026) — они видны фактом «Источники» и в `recordsSources`.
- `running[]` — `key`, `title`, `zone`, `since`, `by` (кто взял заявку; у очередей null), `progress`.
- `upcoming[]` — работающие службы с `nextAt` в будущем, по возрастанию: `key`, `title`, `zone`, `at`,
  `isRetry` (повтор после ошибки — красным), `approx`.

### «Ждёт тебя»

Правило одно — `ResultsAutoWaiting` (`Swimm.Application/Mapping`): вердикт `blocked`, проверена за
последние 24 ч, строка `new` и не связана с соревнованием в базе. Его же зовёт блок «Соревнования»
дашборда (`competitions.autoPullBlocked24h`) — цифры совпадают. Строки — `IResultsAutoWaitingQuery`
(`ResultsAutoWaitingQuery`): вердикт/окно/статус — в SQL, матч с соревнованиями только при кандидатах.

### Честность напоминаний

`training-reminders` пишет в историю только непустые тики (что-то отправил или упал), пустые сдвигают
лишь `Sys_JobRuns`. Поэтому: `lastRunAt` — последний тик (любой), `windowRuns`/`history` — только
непустые, `timelineMode = ticker`. Сплошную ленту клиент рисует как «тикает» от последнего известного
тика, а не как набор прогонов: истории пустых тиков нет.

### Подробности прогона (`GET /api/admin/jobs/runs/{id}`)

`id`, `name`, `trigger`, `startedAt`, `finishedAt`, `success`, `runBy`, `origin`, `summary`, `error`,
`details` (JSON как записан: discovery — `DiscoveryRunDetails`, results-auto — `ResultsAutoTickReport`;
null — нет), `discoveryChanges` (только discovery: готовый вид окна «Что изменилось» — `DiscoveryRunChangesView`,
группы new/postponed/updated/missing со ссылками; тот же `DiscoveryRunChangesBuilder`, что попап нынешней
страницы). Звать по `history[].hasDetails = true`.

### Цена опроса (раз в 30 с, в ProdAdmin — против прод-базы)

Один `GET monitor` = **4–5 запросов**: `Sys_JobRuns` (все строки, ~8), `Sys_JobRunLog` **без колонки
`Details`** (только `Details IS NOT NULL`; ≤ 50 строк на задачу, на проде 20.11 — ~70 строк), «ждёт
тебя» по `Sys_DiscoveredCompetitions` (фильтр в SQL) + соревнования (id, имя, дата) **только если
кандидаты есть**, 5 последних `Sys_ImportHistory` (LIMIT в SQL). Статистика 24 ч / 7 дн считается по уже
прочитанному журналу (`JobRunStats.From` — та же функция, что `GetStatsAsync`). Свежесть рекордов
(`GetFreshnessAsync`, ~25 запросов) запоминается в памяти сервиса на 10 мин; ключ — последняя попытка
`records-check` и последний прогон по странам, поэтому после проверки она перечитывается сразу, а Apply
и CLI `--records-check` видны не позже 10 мин. Остальное (очереди, пинг, мост, настройки) — память
процесса. Локально: первый вызов ~0,4 с (прогрев), дальше ~20 мс.

## Грабли

- **Колонки `ClaimedAt` / `ClaimedBy` должны быть в базе раньше нового кода** — то же правило, что у
  `Details` ниже: код читает строку `Sys_JobRuns` целиком, без миграции `AddJobRunClaim` падает и
  расписание, и страница. Миграция только добавляет nullable-колонки — старый код их не замечает.

- **Колонка `Details` должна быть в базе раньше нового кода.** ProdAdmin с этим кодом на прод-базе
  без миграции `AddJobRunLogDetails` падает на чтении и записи журнала (нет колонки). Порядок — по
  docs/deploy.md «Выкатить код»: влить в master → остановить ProdAdmin → из master
  `--launch-profile ProdAdmin -- --migrate` → деплой. Накатить миграцию на прод ДО вливания нельзя:
  ProdAdminGuard сверяет миграции в обе стороны, и ProdAdmin со старого master не стартует.

- **В ProdAdmin страница показывает журнал прод-базы** — расписания работают только там (В17; кроме
  `discovery`/`start-list` после включения `AutoPull:Enabled` — они тогда идут с прода, см. выше).
  Локально (Development) расписания выключены: карточки «не в этом процессе», история там
  появляется только от кнопки и CLI.
- Очереди и служебные живут в памяти: после перезапуска счётчики с нуля. Постоянная история
  импорта — `/Admin/ImportHistory`.
- `LogligBatch*` нет в реестре Admin/Settings — карточка так и пишет («ключа нет — значение по
  умолчанию»): из админки пакетную привязку пока не включить.
- «Следующий» — момент, когда задаче станет пора; реальный запуск — ближайшая проверка журнала
  (раз в 5 мин у discovery, раз в 15 мин у loglig). У `start-list` «следующий» — следующий ТИК (3 ч), а не
  забор конкретного старта: тот решает правило частоты (раздел выше).
- **Напоминания (`training-reminders`) в ProdAdmin НЕ запущены, и это норма** — их шлёт прод;
  карточка в ProdAdmin показывает прод-журнал. «Ключи VAPID НЕ заданы» в ProdAdmin — тоже норма
  (ключи живут только в App Service). Дубль, если флаг всё же включат в двух местах, отсекает
  UNIQUE `Sys_ReminderDeliveries`.
- **Новая фоновая служба** → карточка в `BackgroundJobsStatusService` (с `Status` и `Alert`), правило №14 в
  [pre-push-rules.md](../pre-push-rules.md).
