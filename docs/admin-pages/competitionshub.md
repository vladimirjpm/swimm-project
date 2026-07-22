# /Admin/CompetitionsHub — объединённые соревнования (β)

Файлы: `Pages/Admin/CompetitionsHub/Index.cshtml(.cs)`. Данные — метод
`ICompetitionAdminRepository.GetUnifiedAsync` (реализация `CompetitionAdminRepository`).
DTO — `Dtos/UnifiedCompetitionDtos.cs` (`UnifiedCompetitionRowDto`, `UnifiedSiteInfo`,
`CompetitionStage`).

**Зачем:** объединяет `/Admin/Competitions` (справочник БД `Competitions`) и
`/Admin/Discovery` (входящие isr.org.il `Sys_DiscoveredCompetitions`) в один список.
Соревнование — одна реальная сущность в двух источниках, связка по `OrgCompId`
(+ fallback имя+дата через `DiscoveryCompetitionMatcher`). Заменит обе старые страницы
после проверки (план — память `admin-competitions-discovery-merge-plan`).

**Стадии (`CompetitionStage`)** одной строки:
- `OnSite` — есть на сайте, в БД нет (ждёт «Затянуть»/импорт);
- `Imported` — есть и там, и там (site-оверлей: языки/площадка);
- `DbOnly` — только в БД (PDF-импорт без OrgCompId, напр. Маккабиада);
- `Ignored` — Discovery-строка скрыта.

**Фильтры:** поиск (по имени; для site-строк — по имени тоже), категория (только БД-строки),
сезон, стадия. Сортировка по дате (убыв.), пагинация 20/стр (merge и срез в памяти —
на admin-масштабе ок, при росте вынести в БД).

**Статус:** v1 — только просмотр + переходы (Edit для БД-строк, isr.org.il для site).
Действия (Затянуть/импорт, изменить/удалить, синхр. языки) переносятся инкрементами 2–3;
затем cutover — удаление Competitions и Discovery, переименование в `/Admin/Competitions`.
