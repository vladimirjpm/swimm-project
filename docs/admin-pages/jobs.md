# /Admin/Jobs — фоновые службы и что они делали

Файлы: `Pages/Admin/Jobs.cshtml` (+ короткий inline-скрипт попапа), `Pages/Admin/Jobs.cshtml.cs`
(`JobsModel` — тонкий, берёт отчёт), карточка — `Pages/Admin/Shared/_JobCard.cshtml`. Пункт
сайдбара «Jobs» в разделе «Система». Страница собирается на сервере; обновить — перезагрузить.

Откуда данные — один источник и для страницы, и для блока «Jobs» дашборда ([index.md](index.md), п. 9):

| Что | Где |
|---|---|
| Порт | `IBackgroundJobsStatus` (`Swimm.Application/Abstractions`): `GetReportAsync` — полный отчёт для страницы, `GetSummaryAsync` — только сводка для дашборда (без истории и журналов очередей) |
| Реализация | `Swimm.API/Services/BackgroundJobsStatusService.cs` — каталог служб, карточки, короткий статус. В API-слое, как `HttpCurrentActor`: нужны флаги процесса (`HostingMode`) и `DbStatusService` |
| Подсчёт | `JobsOverviewBuilder` (`Swimm.Application/Mapping`) — чистые функции: статус расписания (`Schedule`), просрочка, сводка-плитки и проблемные службы (`Build`). Тесты — `JobsOverviewBuilderTests` |
| DTO | `Swimm.Application/Dtos/BackgroundJobsDtos.cs`: `JobCard`, `JobsSummary`, `OverviewTile`, `BackgroundJobsReport` |
| API дашборда | `GET /api/admin/jobs/summary` (`JobsAdminController`) |

Не путать с `/Admin/Services` — там внешние сайты и кабинеты.

## Что показывает

Все `AddHostedService` процесса (`Program.cs`, `Swimm.Infrastructure/DependencyInjection.cs`) в
трёх группах. Вверху — окружение и два флага процесса: `Admin:Enabled` (админка и очереди) и
`BackgroundJobs:Enabled` (расписания, `HostingMode`).

| Вид | Службы | Откуда состояние |
|---|---|---|
| **Расписания** — сами ходят наружу по таймеру | `discovery`, `start-list` (`CompetitionDiscoveryBackgroundService`), `loglig-verify`, `loglig-batch`, `records-check` (`RecordsCheckBackgroundService`, 16.11.2026: проверка источников рекордов — дифф и журнал `Sys_RecordSourceChecks`, Apply НЕ делает), `swimmer-club-check` (`SwimmerClubCheckBackgroundService`, 18.11.2026, Р98: сверка кандидатов на смену клуба пловца с loglig — см. ниже) | журнал `Sys_JobRuns` (пора ли) + история `Sys_JobRunLog` (что сделали) |
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

## Сводка вверху

Десять плиток (`JobsOverviewBuilder.Build`, тип `OverviewTile`) плюс «Рекорды · источники» (ниже), клик — якорь к карточке или группе
(`#schedules`, `#queues`, `#job-<ключ>`). Проблема — рамка и число warning/danger.

**Плитка «Службы» открывает попап** — список всех служб (`<dialog id="services-list">`, нативный
диалог, как `_ConfirmDialog`): точка-цвет, имя, вид (расписание / очередь / служебная) и короткий
статус одной строкой (`JobCard.Status`): «выключена флагом процесса (BackgroundJobs:Enabled)»,
«выключена настройкой (DiscoveryEnabled)», «ок 3 ч назад», «ошибка 20 мин назад: 502…»,
«просрочена на 2 ч», «ждёт задания», «база доступна, пинг 10 с назад». Строка — ссылка на карточку.
Закрывается Esc, кликом по фону, кнопкой ✕ и переходом по ссылке; при открытии фокус на ✕, после
закрытия — обратно на плитку (кроме перехода к карточке).

**Плитка «Рекорды · источники» (одиннадцатая, 16.11.2026)** — не из `JobsOverviewBuilder`, а своя:
`RecordsCheckTileBuilder` (`Swimm.Application/Mapping`, тесты `RecordsCheckTileBuilderTests`) по
`IRecordSourceCheckService.GetFreshnessAsync`. Крупно — давность последней УСПЕШНОЙ проверки
(«2 ч назад», полная дата в `title`), строкой ниже — «ждут Apply: wa-masters +2 ~1», «сбой:
wa-junior» или «изменений нет». Тон: danger — последняя попытка какого-то источника упала;
warning — есть что применить или пора проверить (старше 7 дней); иначе ok. Только пять источников
с провайдером — прогон по странам плитку не желтит. Внизу кнопка **«Проверить сейчас»** (тот же
`POST /api/admin/records/check-all`, до 5 минут; прогон ложится в журнал `records-check`, после
ответа страница перезагружается) и ссылка «Import ›» на таб рекордов (`/Admin/Import?diff=records`).
Плитка — `<div>`, а не ссылка: внутри кнопка.

**Проблема службы** (`JobCard.Alert`, из неё — главная цифра блока дашборда): danger — у работающего
расписания упала последняя попытка, база не отвечает, мост кэша включён и не слушает; warning —
расписание просрочено, локальная база отстаёт от прода. У выключенной службы проблемы нет, даже если
её последний прогон упал.

| Плитка | Что считает | Когда красная / жёлтая |
|---|---|---|
| Службы | работают в процессе / всего (`JobCard.Active`: флаги процесса и настройка) | — |
| Расписания с ошибкой | у скольких последняя попытка упала (`JobCard.Failed`) | красная при > 0 |
| Просрочены | работающие расписания, которым пора уже дольше `JobsOverviewBuilder.OverdueAfter` (30 мин; фон смотрит журнал раз в 5–15 мин) | жёлтая при > 0 |
| Следующий запуск | ближайший `NextDueAt` среди работающих | — |
| Прогонов за 24 ч / 7 дней | `IJobRunStore.GetStatsAsync` — один запрос по `Sys_JobRunLog` за неделю, все задачи и все запускавшие | 24 ч — красная, 7 дн — жёлтая при ошибках |
| Очереди | в очереди · идёт сейчас (импорт + рекорды по странам + проверка пар клубов в loglig, память процесса) | — |
| База | последний пинг | красная, если не отвечает |
| Локальная vs прод | `IDataSyncStatus.Current` (только Development) | жёлтая, если отстаёт или прод недоступен |
| Мост кэша | слушает ли канал | красная, если включён и не слушает |

Светлой темы у админки нет — проверена тёмная и ширина 375 px (плитки в 2 колонки, без
горизонтального скролла).

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

В журнале карточки «Входящие isr.org.il» дата прогона, в котором что-то изменилось, — кнопка
(пунктирное подчёркивание): открывает `<dialog id="run-changes-<Id строки>">`
(`Pages/Admin/Shared/_JobRunChangesDialog.cshtml`). У прогонов без изменений и у старых строк
(до миграции подробностей нет) дата обычным текстом.

- Группы по порядку: **Новые**, **Перенесены** (старая дата зачёркнута → новая), **Обновлены**
  (имя или даты без переноса; «было: …» у переименованных; отменённый перенос — с пометкой),
  **Пропали**. Перенос входит и в счётчик «обновлено» итога — так и подписано.
- Ссылки строки: `compID ↗` на isr.org.il и запись в /Admin/Competitions — «в базе #id» (правка
  соревнования), если старт уже есть в базе, иначе «во Входящих» (поиск `?q=<имя>&season=<cYear>&discipline=all`
  — ровно его строка). Есть ли в базе — `ICompetitionDiscoveryService.GetAllAsync` (штамп
  OrgCompId, иначе матч имя+дата — как сама /Admin/Competitions); зовётся, только если попапы есть.
- Вид собирает чистая `DiscoveryRunChangesBuilder` (`Swimm.Application/Mapping`), страница —
  `JobsModel.RunChanges` (по Id строки журнала), карточка узнаёт кликабельные прогоны через
  `ViewData[JobsModel.RunChangesKey]`. Закрытие: Esc, ✕, клик по фону; фокус — обратно на дату.
- Тесты: `JobRunDetailsTests` (JSON, группы, ссылки, даты), `CompetitionDiscoveryServiceTests.Sync_RecordsChangeList_MatchingCounters`,
  `JobRunStorePgTests.Details_jsonb_round_trip` (настоящий Postgres под swimm_rw).
- Рост ограничен: после каждой записи у задачи остаются последние `JobRunLog.KeepPerJob` = 50.
- `Trigger` ограничен CHECK `CK_Sys_JobRunLog_Trigger`: `schedule` / `manual` / `cli`.
- Sys_, приватная: без гранта `swimm_ro`, **не** в `server/db/seed-tables.txt` — у каждой базы
  свой журнал (миграция `AddJobRunLog`).

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

## Грабли

- **Колонка `Details` должна быть в базе раньше нового кода.** ProdAdmin с этим кодом на прод-базе
  без миграции `AddJobRunLogDetails` падает на чтении и записи журнала (нет колонки). Порядок — по
  docs/deploy.md «Выкатить код»: влить в master → остановить ProdAdmin → из master
  `--launch-profile ProdAdmin -- --migrate` → деплой. Накатить миграцию на прод ДО вливания нельзя:
  ProdAdminGuard сверяет миграции в обе стороны, и ProdAdmin со старого master не стартует.

- **В ProdAdmin страница показывает журнал прод-базы** — расписания работают только там (В17).
  Локально (Development) расписания выключены: карточки «не в этом процессе», история там
  появляется только от кнопки и CLI.
- Очереди и служебные живут в памяти: после перезапуска счётчики с нуля. Постоянная история
  импорта — `/Admin/ImportHistory`.
- `LogligBatch*` нет в реестре Admin/Settings — карточка так и пишет («ключа нет — значение по
  умолчанию»): из админки пакетную привязку пока не включить.
- «Следующий» — момент, когда задаче станет пора; реальный запуск — ближайшая проверка журнала
  (раз в 5 мин у discovery, раз в 15 мин у loglig).
- **Напоминания (`training-reminders`) в ProdAdmin НЕ запущены, и это норма** — их шлёт прод;
  карточка в ProdAdmin показывает прод-журнал. «Ключи VAPID НЕ заданы» в ProdAdmin — тоже норма
  (ключи живут только в App Service). Дубль, если флаг всё же включат в двух местах, отсекает
  UNIQUE `Sys_ReminderDeliveries`.
- **Новая фоновая служба** → карточка в `BackgroundJobsStatusService` (с `Status` и `Alert`), правило №14 в
  [pre-push-rules.md](../pre-push-rules.md).
