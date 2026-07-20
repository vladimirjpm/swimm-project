# /Admin/Import — импорт данных

Файлы: `Pages/Admin/Import.cshtml`; API: `/api/admin/import` (+`/status/{id}`,
`/clear`), `/api/admin/parse-pdf`, `/api/admin/import-parsed`,
`/api/admin/records/apply`, `/api/admin/clearable-tables`.
Сервисы: `JsonImportService`, `ImportJobQueue` (+`ImportBackgroundService`),
PDF — `Swimm.Parsing` (PdfPig).

Импорт соревнований из JSON (наш формат isr.org.il) и PDF-протоколов; привязка к
событию (EventId), очистка таблиц, импорт рекордов через diff. Задания идут через
очередь с фоновым воркером — статус опрашивается.
