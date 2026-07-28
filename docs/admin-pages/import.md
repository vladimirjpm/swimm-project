# /Admin/Import — импорт данных

Файлы: `Pages/Admin/Import.cshtml`; API: `/api/admin/import` (+`/status/{id}`,
`/clear`), `/api/admin/parse-pdf`, `/api/admin/import-parsed`,
`/api/admin/records/apply`, `/api/admin/clearable-tables`.
Сервисы: `JsonImportService`, `ImportJobQueue` (+`ImportBackgroundService`),
PDF — `Swimm.Parsing` (PdfPig).

Импорт соревнований из JSON (наш формат isr.org.il) и PDF-протоколов; привязка к
событию (EventId), очистка таблиц, импорт рекордов через diff. Задания идут через
очередь с фоновым воркером — статус опрашивается.

## Правила начисления очков при импорте (Э5)

Два селекта («Клубные очки» и «High Point») в общем блоке настроек — он один на
JSON- и PDF-ветку, как категории и привязка к событию. Дефолт — **«Авто»**:
правило подберётся по дате и типу соревнования (`CompetitionRuleResolver`), кроме
правил с пометкой `manual` — те работают только по явной привязке. Список правил —
[pointsrules.md](pointsrules.md).

- Выбранные Id едут через `ImportEventOptions.PointRuleClubsId` /
  `PointRuleSwimmersId` (JSON-ветка — поля формы, PDF-ветка — поля
  `ImportParsedRequest`) и проставляются в `JsonImportService` **каждому созданному
  соревнованию**: правило хранится у соревнования, а не у события, поэтому у
  многодневки его получает каждый день.
- **Переимпорт существующего соревнования привязку не меняет** — соревнование уже
  есть в БД, ветка создания не выполняется. Менять привязку осознанно — на
  /Admin/Competitions (в форме дня, кнопкой «всем дням события» или массово на
  `/Admin/Competitions/AssignRules`).
- Выбор не обязателен: «Авто» оставляет поведение, которое было до Э5.
