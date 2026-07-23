# /Admin/Competitions — соревнования (справочник БД + входящие isr.org.il)

Файлы: `Pages/Admin/Competitions/Index.cshtml(.cs)`, `Edit.cshtml`. Данные списка —
`ICompetitionAdminRepository.GetUnifiedAsync` (реализация `CompetitionAdminRepository`);
CRUD — тот же репозиторий; каскадное удаление — `IImportService.DeleteCompetition(Event)Async`.
Действия входящих — API `DiscoveryAdminController` (`/api/admin/discovery/*`) + сервисы
`CompetitionDiscoveryService` / `CompetitionDiscoveryBackgroundService`; матч —
`DiscoveryCompetitionMatcher`. DTO — `Dtos/UnifiedCompetitionDtos.cs`, `CompetitionCrudDtos.cs`.

**Объединённый список:** одно соревнование — одна строка со стадией жизненного цикла
(`CompetitionStage`): `OnSite` (на сайте, в БД нет) / `Imported` (есть и там, и там) /
`DbOnly` (только в БД, PDF-импорт без OrgCompId) / `Ignored` (скрыто). Склейка справочника
`Competitions` и входящих `Sys_DiscoveredCompetitions` по `OrgCompId` (+ fallback имя+дата).
Раньше это были две отдельные страницы (Competitions + Discovery) — объединены 2026-07.

**Фильтры:** поиск (имя/подзаголовок), категория (из /Admin/Categories), сезон, стадия,
чекбокс «показать синтетику» (SYNTH Meet…, по умолчанию скрыта). Сортировка по дате (убыв.),
пагинация 20/стр. Merge в памяти (admin-масштаб ~1.5k; при росте — вынести в БД-пагинацию).

**Действия входящих (site-строки):** «Проверить сейчас» (sync списка), «Затянуть»→превью-модал→
импорт (через очередь, см. Import History), «✓ имп.» (пометить вручную), «Синхр. языки»
(дозаполнить EN-имена из EN-протокола без переимпорта), PDF, скрыть/вернуть. Все дёргают
`/api/admin/discovery/*`; после мутаций страница перезагружается. Имена discovery-строк для
JS — через `window.__DISC` (id→name), не onclick (экранирование иврита/кавычек).

**CRUD справочника:** «Изменить» (Edit-страница — многодневные события/EventId, PoolType,
IsMasters/IsAward, страна, категории, URL результатов), «Удалить» день/событие (каскад с
подтверждением), «+ Новое соревнование». Эталон CRUD для новых админ-страниц.

## Deep-link фильтры «здоровье данных» (T3b)

`?filter=ignored|discovery-new` — алиасы на `Stage` (как раньше). `?filter=discovery-error|
no-org-comp-id|no-results` — новый доп-параметр `qualityFilter` в
`GetUnifiedAsync` (те же предикаты, что в `DashboardStatusService`): применяется
поверх стадии/поиска/сезона, PageModel кладёт его в `QualityFilter` для шапки списка
(«активный фильтр… сбросить») и пробрасывает во все ссылки пагинации/месяцев.
Для свёрнутого многодневного события «нет OrgCompId»/«нет результатов» проверяются
по любому дню/сумме дней, а не только по «голове» строки.
