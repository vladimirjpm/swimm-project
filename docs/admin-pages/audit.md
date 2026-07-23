# /Admin/Audit — журнал ручных мутаций (аудит)

Файлы: `Pages/Admin/Audit/Index.cshtml(.cs)`; чтение — `IAdminAuditRepository`
(`AdminAuditRepository`); запись — `IAdminAuditService` (`AdminAuditService`).
Таблица: `Sys_AdminAudit` (приватная Sys_-таблица, БЕЗ grant swimm_ro).

## Что делает

Read-only лента «кто / что / когда» по рискованным ручным операциям админки.
Фильтры: поиск (по сводке / актору / id объекта, `ILike`) и выпадашка по коду
действия. Пагинация — параметр `p` (НЕ `page`, зарезервировано Razor Pages),
30 строк на страницу, новые сверху (по `Id desc`). У строк с деталями —
раскрывающийся `<details>` с JSON.

## Модель записи (фаза 7.4)

`AdminAudit`: `ActorUserId` (без FK — переживает удаление юзера) + `ActorName`
(снимок email/имени), `Action` (машинный код, напр. `swimmer.merge`), `EntityType`
/ `EntityId`, `Summary`, `DetailsJson`, `IpAddress`, `CreatedAt` (UTC).

Actor приходит из `ICurrentActor` (реализация `HttpCurrentActor` в API-слое поверх
`IHttpContextAccessor`; вне HTTP — `"cli"`). Запись **best-effort**: сбой аудита
логируется, но не роняет саму мутацию. Вызывать `LogAsync` ПОСЛЕ успешного
`SaveChanges` мутации (сервис делает собственный `SaveChanges` на общем scoped-контексте).

## Кто пишет в журнал (точки врезки)

- `SwimmersAdminController`: `swimmer.merge`, `swimmer.orphans-delete`,
  `swimmer.dedup-ignore` / `-unignore`.
- `ClubsAdminController`: `club.merge`, `club.dedup-ignore` / `-unignore`.
- `Competitions/Index`: `competition.delete`, `competition.delete-event`.
- `AdminController`: `user.role-add` / `-remove`, `user.set-active`,
  `user.force-signout`, `setting.update`.

Добавляя новую мутацию — инжектируй `IAdminAuditService` и логируй по этому образцу.
