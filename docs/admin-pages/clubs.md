# /Admin/Clubs — дубли клубов

Файлы: `Pages/Admin/Clubs/Index.cshtml`; API `ClubsAdminController`
(`/api/admin/clubs`): dedup-candidates, merge, dedup-ignore(+remove/список),
{id}/status. Сервисы: `ClubDedupService`, `ClubMergeService`.

Аналог страницы Swimmers для клубов: кандидаты на склейку, dry-run → apply,
скрытие «не дублей». Плюс переключение статуса клуба (псевдо-клубы `IsPseudo`).

## Правка клуба (фаза 7.3 op#2)

`Pages/Admin/Clubs/Edit.cshtml(.cs)`; репозиторий `IClubAdminRepository`
(`ClubAdminRepository`). Переименование (Name/NameEn) + флаг IsPseudo. Имя клуба
нигде не денормализовано (результаты ссылаются по ClubId), поэтому «каскад» — это
инвалидация кэша агрегатов после сохранения. Мутация пишется в аудит (`club.update`
со снимком before/after). Вход: поле «Клуб по Id» на тулбаре Index + клик по имени
клуба в таблице кандидатов (открывается в новой вкладке). Удаление/дедуп — через merge.
