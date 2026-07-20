# /Admin/Clubs — дубли клубов

Файлы: `Pages/Admin/Clubs/Index.cshtml`; API `ClubsAdminController`
(`/api/admin/clubs`): dedup-candidates, merge, dedup-ignore(+remove/список),
{id}/status. Сервисы: `ClubDedupService`, `ClubMergeService`.

Аналог страницы Swimmers для клубов: кандидаты на склейку, dry-run → apply,
скрытие «не дублей». Плюс переключение статуса клуба (псевдо-клубы `IsPseudo`).
