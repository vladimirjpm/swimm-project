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
