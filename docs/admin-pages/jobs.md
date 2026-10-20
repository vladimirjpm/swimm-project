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
| **Расписания** — сами ходят наружу по таймеру | `discovery`, `start-list` (`CompetitionDiscoveryBackgroundService`), `loglig-verify`, `loglig-batch` | журнал `Sys_JobRuns` (пора ли) + история `Sys_JobRunLog` (что сделали) |
| **Прод-расписание** — идёт с ПРОДА круглосуточно | `training-reminders` (`TrainingReminderBackgroundService`, напоминания о тренировках через Web Push, [web-push-reminders-plan.md](../plans/web-push-reminders-plan.md)) | тот же журнал; флаг свой — `PushReminders:Enabled` (true только на проде). Тик 5 мин: каждый сдвигает «последний тик» (`IJobRunStore.RecordStateAsync`), строку истории пишет только тик, который что-то отправил или упал. В ProdAdmin карточка «следит» за прод-джобом: тик старше 35 мин — «молчит» (жёлтая) |
| **Очереди** — исполняют то, что поставила админка | импорт (`ImportBackgroundService`), рекорды по странам (`RecordCountryRunBackgroundService`) | снимок очереди в памяти + их собственные таблицы: `Sys_ImportHistory`, журнал свежести рекордов `Sys_RecordSourceChecks` (`worldrecords-countries`) |
| **Служебные** | пинг БД (`DbPingBackgroundService`), отставание от прода (`DataSyncCheckBackgroundService`, только Development), мост кэша (`CacheBridgeListener`) | память процесса: `DbStatusService.LastPing`, `IDataSyncStatus.Current`, `ICacheBridgeStatus.State()` |

У расписания: запущено ли в ЭТОМ процессе, настройка «включено» и интервал (Admin/Settings;
ключи и умолчания — `JobNames.All` в `Swimm.Application/Mapping/JobSchedule.cs`), последний
запуск и успех, ошибка, когда следующий (`JobSchedule.NextDueAt` — то же правило, что
`IsDue`), и последние 15 прогонов: начало, кто запустил (фон / кнопка / CLI), сколько длился,
итог одной строкой или ошибка.

## Сводка вверху

Десять плиток (`JobsOverviewBuilder.Build`, тип `OverviewTile`), клик — якорь к карточке или группе
(`#schedules`, `#queues`, `#job-<ключ>`). Проблема — рамка и число warning/danger.

**Плитка «Службы» открывает попап** — список всех служб (`<dialog id="services-list">`, нативный
диалог, как `_ConfirmDialog`): точка-цвет, имя, вид (расписание / очередь / служебная) и короткий
статус одной строкой (`JobCard.Status`): «выключена флагом процесса (BackgroundJobs:Enabled)»,
«выключена настройкой (DiscoveryEnabled)», «ок 3 ч назад», «ошибка 20 мин назад: 502…»,
«просрочена на 2 ч», «ждёт задания», «база доступна, пинг 10 с назад». Строка — ссылка на карточку.
Закрывается Esc, кликом по фону, кнопкой ✕ и переходом по ссылке; при открытии фокус на ✕, после
закрытия — обратно на плитку (кроме перехода к карточке).

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
| Очереди | в очереди · идёт сейчас (импорт + рекорды по странам, память процесса) | — |
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
  `/Admin/Swimmers/Loglig` (`AppendLogAsync`: батч другого размера ночной не откладывает).
- Итог — одна строка из `JobRunSummaries` (один текст на задачу, кто бы ни запустил).
- Рост ограничен: после каждой записи у задачи остаются последние `JobRunLog.KeepPerJob` = 50.
- `Trigger` ограничен CHECK `CK_Sys_JobRunLog_Trigger`: `schedule` / `manual` / `cli`.
- Sys_, приватная: без гранта `swimm_ro`, **не** в `server/db/seed-tables.txt` — у каждой базы
  свой журнал (миграция `AddJobRunLog`).

## Грабли

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
