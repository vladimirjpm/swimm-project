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

**Действия (инкремент 2, перенесены из Discovery):** «Проверить сейчас» (sync), на site-строках —
«Затянуть»→превью→импорт (модал), «✓ имп.» (пометить вручную), «Синхр. языки», PDF, скрыть/вернуть;
на строках БД — «Изменить» (Edit). Все дёргают `/api/admin/discovery/*`; после мутаций страница
перезагружается (SSR). Имена discovery-строк — через `window.__DISC` (id→name), не через onclick.

**Осталось (инкремент 3 + cutover):** «Удалить» день/событие, «Создать»; затем удаление старых
Competitions и Discovery, переименование роута в `/Admin/Competitions`.
