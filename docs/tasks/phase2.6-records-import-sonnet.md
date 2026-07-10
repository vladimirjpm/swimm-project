# Задание (Sonnet 5): этап 2.6 — обновление рекордов из источников + редизайн экрана Import

Контекст: [docs/ROADMAP.md](../ROADMAP.md), этап 2.6 (был «опциональным», теперь в работе).
Историческая справка: в снесённом `Swimm.Parser` был `POST /pdf/fetch-world-records`,
качавший XLSX **напрямую с api.worldaquatics.com** — при переносе парсинга в библиотеку
(фаза 1) эта возможность потерялась. Восстанавливаем её правильно: через шов
`IRecordSourceProvider`, с превью-диффом перед применением, и заодно перепланируем экран
Import — форматы из безликого `<select>` превращаются в понятные карточки-источники.

## Что уже есть (не переделывать)

- Парсеры в `server/Swimm.Parsing/Parsers/`: `WorldRecords` (XLSX, ClosedXML),
  `IsrOrgAgeRecords` / `IsrOrgMastersRecords` (HTML isr.org.il), `IsrOrg` (PDF протоколов).
  Парсеры едят потоки (`ParseRequest`) — их НЕ менять.
- Таблица `Records` (3-осевая схема, см. `Swimm.Domain/Entities/Record.cs`), админ-CRUD
  (`RecordsAdminController`), публичный `/api/records` с ETag.
- Экран `Pages/Admin/Import.cshtml`: табы JSON/PDF, формат — `<select>`, preview, выбор
  события/дня. Логика импорта соревнований рабочая — переезжает в новый layout без изменений.

## Часть 1 — сервер: `IRecordSourceProvider` + дифф

1. **Порт в Application** (`Abstractions/IRecordSourceProvider.cs`):
   ```csharp
   Task<IReadOnlyList<ParsedRecordDto>> FetchAsync(RecordSourceRequest req, CancellationToken ct);
   ```
   `RecordSourceRequest`: `Source` (worldrecords | isrorg-age | isrorg-masters),
   опционально загруженные файлы (стримы) — если файлов нет, провайдер качает сам по URL.
2. **Реализация** (Infrastructure или Swimm.Parsing — где HttpClient уместнее; парсеры
   переиспользовать как есть). URL-ы для worldrecords (восстановлены из истории,
   коммит `61af61b^`, старый `PdfController.FetchWorldRecordsAsync`):
   - `https://api.worldaquatics.com/fina/records/report?pool=SCM&recordCode=WR` → WR_SCM.xlsx
   - `https://api.worldaquatics.com/fina/records/report?pool=LCM&recordCode=WR` → WR_LCM.xlsx
   - `…?recordCode=NR&pool=SCM&countryId={id}` и `…&pool=LCM…` → национальные рекорды;
     id Израиля по умолчанию: `962f77d6-d9c0-49ad-ba93-adc831c9ec9f`.
   Для isr.org-источников URL страниц рекордов вынести в настройки (`ISettingsService`
   или appsettings) — они могут меняться; плюс всегда оставить fallback «загрузить файл руками».
3. **⚠ SSRF-защита обязательна**: провайдер качает ТОЛЬКО с whitelist-доменов
   (`api.worldaquatics.com`, `isr.org.il`) — URL от пользователя не принимаем вообще,
   только выбор источника кнопкой. Таймаут HttpClient ≤ 30с, лимит размера ответа.
4. **Дифф-сервис**: спарсенное сопоставляется с `Records` по уникальным осям
   (RegionType+RegionCode+Category+AgeKey+Gender+PoolType+Style+Distance) →
   `{ added[], changed[] (old→new time/holder/date), unchanged: N, missingInSource: M }`.
   `missingInSource` — только информационно, НИЧЕГО не удаляем.
5. **Endpoints** (админ, antiforgery как у соседей):
   - `POST /api/admin/records/fetch` `{ source }` (+файлы multipart опционально) → дифф
     (id сессии диффа в кэше 10 мин + payload для UI);
   - `POST /api/admin/records/apply` `{ diffId, applyAdded, applyChanged }` → транзакция,
     upsert, `ICacheService.InvalidateAllAsync()`.

## Часть 2 — сервер: `Record.UpdatedAt` (просил Влад — не потерять!)

1. Поле `UpdatedAt` (DateTime, UTC) в `Record` + миграция `AddRecordUpdatedAt`
   (`/db-migrate`; для существующих строк — время миграции). **Не путать с `RecordDate`** —
   это дата УСТАНОВЛЕНИЯ рекорда спортсменом; `UpdatedAt` — когда МЫ обновили запись.
2. Проставлять: в apply-диффа (только реально изменённым/новым), в админ-CRUD мутациях,
   в сидере.
3. `RecordDto` → `updated_at` (ISO-дата, snake_case как остальные поля).
4. **Клиент — показывать дату**: `records-helper.ts` прокидывает `updated_at`;
   в карточках age/masters-рекордов и попапе нормативов/рекордов — приглушённая строка
   «updated dd/MM/yyyy» (сводно по карточке: max updated_at её рекордов; на строку —
   в title/tooltip). Место — по образцу существующих muted-подписей, не спорить с датой
   самого рекорда, которая уже выводится.

## Часть 3 — редизайн экрана Import

Принцип: **сначала выбираешь ЧТО импортируешь, потом видишь ровно то, что парсишь**.
Никаких `<select>` с форматами — источники это карточки-кнопки с описанием.

```
IMPORT
[ Соревнования ]  [ Рекорды ]              ← верхние табы (что импортируем)

── Таб «Соревнования» ─────────────────────────────────────────
  Источник (карточки-кнопки, выбранная подсвечена):
  ┌───────────────────────────┐ ┌───────────────────────────┐
  │ 📄 PDF протокол (IsrOrg)  │ │ 🗂 JSON (legacy формат)    │
  │ Протокол с isr.org.il,    │ │ Файл выгрузки старого     │
  │ PDF, HE/EN                │ │ сайта                     │
  └───────────────────────────┘ └───────────────────────────┘
  → зона загрузки с подписью, какой файл ожидается
  → ПРЕВЬЮ «что я парсю»: шапка (название, дата, бассейн, N строк,
    язык протокола) + первые строки таблицей — как сейчас, но заметнее
  → выбор события/дня (существующая логика) → [Импортировать]

── Таб «Рекорды» ──────────────────────────────────────────────
  Три карточки-источника в ряд:
  ┌──────────────────────┬──────────────────────┬──────────────────────┐
  │ 🌍 World Records     │ 🇮🇱 Age Records       │ 🇮🇱 Masters Records   │
  │ api.worldaquatics.com│ isr.org.il           │ isr.org.il           │
  │ XLSX, 4 файла, авто  │ HTML страница        │ HTML страница        │
  │ Обновлено: 09/07/26  │ Обновлено: —         │ Обновлено: —         │
  │ [⬇ Fetch from URL]   │ [⬇ Fetch] [📁 Файл]  │ [⬇ Fetch] [📁 Файл]  │
  └──────────────────────┴──────────────────────┴──────────────────────┘
  «Обновлено» = max(UpdatedAt) рекордов источника — сразу видно, что протухло.
  → после Fetch: ПРЕВЬЮ-ДИФФ (не сырой дамп!):
     «+12 новых · ~5 изменившихся · 1702 без изменений · 3 нет в источнике»
     таблица изменений: Дисциплина | Было (время, держатель) | Станет | ✓
  → [Применить выбранное] → toast + обновление даты на карточке
```

- Существующие JSON/PDF-механики (upload, статус фоновой джобы, danger-zone очистки)
  сохраняются — меняется группировка и подача, не логика.
- Tailwind: после правок классов `npm run css:build` в `server/Swimm.API`, коммитить
  `admin.min.css` вместе со страницей.
- Danger-zone («полная очистка») остаётся внизу таба «Соревнования», как сейчас.

## Приёмка

- `dotnet build/test` зелёные; `npx tsc --noEmit` чистый; миграция применяется.
- World Records: нажать Fetch (нужен интернет) → дифф показывает вменяемые числа →
  Apply → `/api/records?region=world` отдаёт новые данные с новым ETag; повторный Fetch
  сразу после — «всё без изменений». UpdatedAt изменился только у затронутых строк.
- Age/Masters: загрузка сохранённого HTML-файла руками даёт дифф (URL-fetch может
  зависеть от доступности isr.org.il — файл-fallback обязан работать всегда).
- Импорт соревнования PDF: полный прежний сценарий через новый UI (превью → событие →
  импорт → строки в БД).
- Клиент: в карточке age-рекордов видна «updated …»; после Apply в админке дата
  на клиенте меняется (кэш инвалидирован).
- Регресс: JSON-импорт и очистка данных работают как раньше.

## Правила репо

- RU-комментарии/EN-идентификаторы; не коммитить без просьбы; контроллеры — только
  интерфейсы Application; `/db-migrate` для миграций (два DbContext!). Парсеры не менять;
  чего-то не хватает в их выводе — стоп и вопрос.
