# /Admin/Styles — справочник стилей плавания

Файлы: `Pages/Admin/Styles/Index.cshtml(.cs)`, `Edit.cshtml(.cs)`;
репозиторий `IStyleAdminRepository` (`StyleAdminRepository`).

## Что делает

CRUD справочника `Styles` (`ResultRecord.StyleId` → сюда). Список показывает id,
имя и число ссылающихся результатов; Edit — создать / переименовать / удалить.

## Ключевые правила

- **Зарезервированные стили** (посевные 7: freestyle, backstroke, breaststroke,
  butterfly, individual_medley, medley_relay, free_relay —
  `Style.ReservedNames`): имена зашиты в парсер, импорт (`NormalizeStyleName`),
  рекорды и подсчёт клубных очков → **rename/delete запрещены** (бейдж `system`).
- **Удаление** нерезервного стиля — только если на него нет результатов
  (иначе FK `Restrict` всё равно оборвёт; отдаём понятную ошибку заранее).
- Имя уникально (unique-индекс) — дубликат отклоняется.
- Имена стилей денормализованы в публичных выдачах → после каждой мутации
  `ICacheService.InvalidateAllAsync()`.
- Мутации пишутся в аудит (7.4): `style.create` / `style.update` / `style.delete`.

Импорт умеет заводить новые стили сам (`NormalizeStyleName` + вставка), так что
здесь обычно только чистка/переименование нестандартных, попавших из протоколов.
