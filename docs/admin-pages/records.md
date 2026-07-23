# /Admin/Records — рекорды и нормативы

Файлы: `Pages/Admin/Records/Index.cshtml`; API: `/api/admin/records`,
`/api/admin/normative-standards` (`RecordsAdminController`), diff/apply —
`RecordDiffService`, сиды — `RecordsSeeder`.

Две вкладки: рекорды (ISR Masters и др., импорт с diff-подтверждением) и
нормативные стандарты (система разрядов; ось Country — alpha-3, RUS/ISR,
"" = универсальный набор). Данные питают normative-попап и карточки рекордов
на публичном сайте.
