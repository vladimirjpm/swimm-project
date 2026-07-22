# /Admin/Competitions — справочник соревнований

Файлы: `Pages/Admin/Competitions/Index.cshtml`, `Edit.cshtml`; API
`CompetitionAdminRepository` через `/api/admin/competitions`.

Эталонный CRUD админки (образец для новых CRUD-страниц): список с фильтрами
(категории — из /Admin/Categories), редактирование карточки (Edit), события
(EventId — многодневные соревнования), PoolType, IsMasters/IsAward, страна.

Кросс-линк с /Admin/Discovery по `OrgCompId`: ячейка OrgCompId — ссылка «↗» на
`/Admin/Discovery?org=<id>` (найдёт строку во «Входящих»). Обратно: `?org=<OrgCompId>`
сужает список до этого соревнования (или всего его события) и подсвечивает строку
(серверный фильтр в `GetPagedAsync(orgCompId)` + скролл/подсветка по `data-org`).
