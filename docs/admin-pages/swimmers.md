# /Admin/Swimmers — дубли пловцов

Файлы: `Pages/Admin/Swimmers/Index.cshtml`; API `SwimmersAdminController`
(`/api/admin/swimmers`): dedup-candidates, merge (dry-run/apply), dedup-ignore
(+remove/список), orphans/delete, attention-summary.
Сервисы: `SwimmerDedupService`, `SwimmerMergeService`, `DedupIgnoreService`.

Логика: кандидаты по нормализованному имени (иврит: финальные буквы, гереш-апостроф),
Левенштейн ≤2, одинаковый год; «уверенная/спорная». Merge необратим: сначала dry-run
(обязателен — Apply разблокируется только после чистого dry-run), пары с общим
заплывом сервис не мержит. «Не дубли» → `Sys_DedupIgnoredPairs`. Блок сирот
(0 результатов/связей) с массовым удалением. После merge — полная инвалидация кэша.

## Deep-link фильтры «здоровье данных» (T3b)

`?filter=dedup-sure|dedup-unsure|orphans` — как раньше (фильтруют уже загруженный
список кандидатов/скроллят к сиротам). `?filter=no-org-id|no-results` — новая
сворачиваемая секция «Качество данных» под сиротами, грузится лениво через
`GET /api/admin/swimmers/quality?filter=…` (`IDataQualityService`, топ-200 + total),
предикаты те же, что в `DashboardStatusService` (карточки дашборда). Read-only.
