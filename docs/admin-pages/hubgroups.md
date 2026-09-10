# /Admin/HubGroups — группы SwimHub

Файлы: `Pages/Admin/HubGroups/Index.cshtml`, `Edit.cshtml`; API — HubGroup*-сервисы
(`HubGroupAdminService`, `HubGroupCrudCore`, media/user/permission-сервисы).

Неформальные объединения пловцов (группы/клубы/сборные): CRUD, участники
(members-слой), медиа группы, join policy. Публичная витрина — на клиенте.

## Deep-link фильтры (T3b)

`?filter=official` — как раньше (только официальные группы). `?tab=requests` —
секция «Заявки на вступление (pending)» под тулбаром (тогл-ссылка в шапке или
deep-link с дашборда): `HubGroupUserMembers.Status == "pending"`, через
`IDataQualityService.GetPendingJoinRequestsAsync` (топ-200 + total). Read-only —
группа/email/дата подачи, без approve/reject (это Sys_-таблица, решения принимают
владельцы/админы конкретной группы вне этой страницы).

## Удаление (2026-09-10)

И в списке, и в форме Edit подтверждение показывает **перечень потерь**: пловцы в составе,
аккаунты, админы группы, тренировки с результатами, медиа, публикации участников,
официальный статус, заявка — только ненулевое. Скрипт один на обе страницы —
`_DeleteConfirm.cshtml` (`hubGroupDeleteConfirm(id)` поверх `adminConfirm`), данные — из
`GET /api/me/hub-groups/{id}/delete-impact` (у админа сайта `CanDelete` на любую группу).
Ивритские имена в тексте диалога изолированы символами FSI/PDI — иначе «ёлочки» вокруг них
встают задом наперёд. Удаление пишется в аудит `hubgroup.delete` самим `DeleteAsync`.
Правило целиком — `docs/hubgroups-architecture.md` §3.
