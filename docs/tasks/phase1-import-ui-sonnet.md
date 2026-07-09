# Задание (Sonnet 5): фаза 1, этапы 1.3–1.5 — PDF-импорт в админке

Контекст: фаза 1 роадмапа ([docs/ROADMAP.md](../ROADMAP.md)) — «Parsing как библиотека».
Этапы 1.1–1.2 **уже сделаны** (Fable, 2026-07-09): логика парсинга живёт в class library
`server/Swimm.Parsing`, шов спроектирован и зарегистрирован в DI. Твоя работа — UI-пайплайн
в админке, регрессионная проверка и снос легаси-проекта. Интерфейсы **не менять** — только
потреблять.

## Что уже готово (не переделывать)

- `server/Swimm.Parsing/` — библиотека: `Parsers/` (IsrOrg, IsrOrgAgeRecords,
  IsrOrgMastersRecords, WorldRecords), `Helpers/`, `Models/`, `ParserConstants.cs`.
  Namespace — `Swimm.Parsing.*`.
- `Swimm.Application/Abstractions/IResultSourceProvider.cs` + DTO в
  `Swimm.Application/Dtos/ParsedCompetition.cs` (`ResultSourceRequest`, `ParsedCompetition`,
  `ParsedCompetitionSummary`, `SourceFilePart`).
- `Swimm.Parsing/PdfResultSourceProvider.cs` — реализация; `AddParsing()` уже вызван в
  `Program.cs`, т.е. `IResultSourceProvider` **доступен из DI в любом контроллере/странице**.
- `Swimm.Tests` ссылается на Swimm.Parsing; 62 теста зелёные.
- `Swimm.Parser` (старый веб-сервис) пока в solution как тонкая обёртка — его контроллеры
  работают через библиотеку. Ты его удалишь на шаге 1.5.

## Ключевой контракт

`ParsedCompetition.ResultsJson` — это **готовый вход** для существующего импорта:
JSON-массив в том же формате, что легаси JSON-загрузка. Путь в очередь:

```csharp
var parsed = await provider.ParseAsync(new ResultSourceRequest(stream, fileName, format, isAward, poolType, ...));
var jobId = _jobs.Enqueue(Encoding.UTF8.GetBytes(parsed.ResultsJson), fileName, categoryKeys, eventOptions);
// дальше — существующий поллинг GET /api/admin/import/status/{jobId}
```

Никакого нового пути записи в БД. Провайдер только парсит; пишет только очередь импорта.

## Этап 1.3 — режим «PDF» на странице Admin/Import

Файлы: `server/Swimm.API/Pages/Admin/Import.cshtml(.cs)`, `Controllers/AdminController.cs`.

1. Новый endpoint в `AdminController` (антифорджери уже включён на контроллере —
   `[AutoValidateAntiforgeryToken]`, заголовок `X-XSRF-TOKEN` шлёт хелпер `api()` из
   `_Layout.cshtml`):
   - `POST /api/admin/parse-pdf` — multipart: `file` (обязателен), `secondaryFile?`,
     `thirdFile?`, `fourthFile?`, `format` (default `IsrOrg`), `isAward`, `poolType?`.
     Зеркалит параметры старого `PdfController.UploadAsync` (см. историю git —
     `server/Swimm.Parser/Controllers/PdfController.cs`). Вызывает
     `IResultSourceProvider.ParseAsync`, возвращает **превью без импорта**:
     `{ format, resultCount, competitions: [{competition, date, resultCount}], warnings, debugLog }`
     плюс сам `resultsJson` НЕ гонять на клиент и обратно (большой) — см. п.2.
   - `InvalidOperationException` от провайдера → `400 { error }` (человекочитаемое сообщение).
2. Хранение между «превью» и «импортом»: результат парсинга держать на сервере
   (IMemoryCache, ключ = Guid превью, TTL ~15 мин), клиенту отдавать `previewId`.
   `POST /api/admin/import-parsed { previewId, categoryKeys, eventId?/newEventName? }` →
   достать JSON из кэша → `IImportJobQueue.Enqueue` → `202 { jobId }` (как у ImportJson).
3. UI Import.cshtml: таб/переключатель «JSON | PDF». В PDF-режиме: file inputs (1 обязательный
   + до 3 доп.), select формата (данные — `IResultSourceProvider.AvailableFormats`, можно
   отдать в модели страницы), чекбокс isAward, select poolType (25m/50m/пусто). Кнопка
   «Распарсить» → превью-карточка: счётчик результатов, таблица соревнований (имя+дата+кол-во),
   warnings (жёлтым), сворачиваемый debug-лог. Кнопка «Импортировать» активна после парса;
   выбор категорий и привязка к событию — те же контролы, что у JSON-режима (переиспользуй).
   После enqueue — существующий поллинг статуса (уже есть на странице).
4. Tailwind: классы по образцу существующих Admin-страниц; после правок обязательно
   `cd server/Swimm.API && npm run css:build` и закоммитить `wwwroot/css/admin.min.css`
   вместе с изменениями.

## Этап 1.4 — регрессия

1. `dotnet test server/Swimm.Tests/Swimm.Tests.csproj` — должно быть зелёно.
2. Смоук end-to-end: собрать, поднять API (`http://localhost:5078`), в админке распарсить
   любой реальный PDF-протокол isr.org.il (спросить у пользователя файл, если в репо нет),
   сверить счётчики превью с импортом, довести до completed в ImportHistory.
   ⚠️ Build-lock: перед пересборкой убить висящий процесс (`MSB3027` → см. CLAUDE.md).
3. Если пользователь даст PDF, который уже импортирован через старый путь — сравнить
   количество результатов нового парса с состоянием БД по этому соревнованию.

## Этап 1.5 — снос Swimm.Parser

Только после того, как 1.3 работает и проверен:

1. `dotnet sln server/Swimm.sln remove server/Swimm.Parser/Swimm.Parser.csproj`
2. `git rm -r server/Swimm.Parser` (Controllers, Program.cs, Properties, appsettings, wwwroot).
3. Проверить, что нигде не осталось ссылок: `grep -rn "Swimm.Parser" server client docs --include=* | grep -v Parsing | grep -v obj` — вычистить упоминания в доках
   (`CLAUDE.md`, `.github/copilot-instructions.md`, `swimm-architecture-plan.html`), кроме
   исторических записей в ROADMAP.
4. `dotnet build server/Swimm.sln` + `dotnet test` — зелёные.
5. Отметить этапы 1.3–1.5 в `docs/ROADMAP.md` (☐ → ✅) и обновить статус фазы 1.

## Правила репо (важно)

- Комментарии — RU-проза, идентификаторы — EN; стиль соседнего кода.
- Не коммитить без просьбы пользователя.
- Контроллеры инжектят только интерфейсы `Swimm.Application` — не Infrastructure-типы.
- Не трогать `SwimmReadDbContext`/роли БД — импорт идёт по существующему rw-пути.
- Интерфейс `IResultSourceProvider` и DTO не менять; если чего-то не хватает для UI —
  остановись и спроси, это осознанный дизайн шва.
