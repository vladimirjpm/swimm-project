# Задание Sonnet: админ-панель склейки клубов (/Admin/Clubs)

Фаза C плана `docs/tasks/club-merge-plan.md` (прочитай его разделы «Исходные данные»
и «Фаза C» для контекста). Серверная часть (фаза B) уже готова: сервисы, ручки,
тесты — тебе только Razor-страница + JS + навигация.

## Решения (зафиксированы, не пересматривать)

- Одна страница `Pages/Admin/Clubs/Index.cshtml` (+ `Index.cshtml.cs` — пустая
  PageModel по образцу соседей). Никакого CRUD клубов — только склейка дублей.
- Данные тянутся клиентским JS из готовых ручек (как на /Admin/Swimmers):
  - `GET /api/admin/clubs/dedup-candidates` → `ClubDedupReport`:
    `{ candidates: [...], realClubs: int }`, кандидат:
    `{ canonicalId, canonicalName, canonicalNameEn, canonicalResults, duplicateId,
       duplicateName, duplicateNameEn, duplicateResults, heuristic, sharedSwimmers, sure }`,
    `heuristic`: `suffix` | `swimmers` | `levenshtein`.
  - `POST /api/admin/clubs/merge` c телом `{ pairs: [{canonicalId, duplicateId}], apply: bool }`
    → `ClubMergeReport { dryRun, pairs: [{canonicalId, duplicateId, status, actions[], conflicts[]}] }`,
    `status`: `merged` | `dry-run` | `conflict` | `error`. Antiforgery-заголовок обязателен
    (ручка под `[AutoValidateAntiforgeryToken]`) — скопируй механику с /Admin/Swimmers.
- UI: таблица кандидатов (канон: имя+NameEn+результаты | дубль: то же | эвристика |
  общих пловцов | sure-бейдж), чекбокс на строку + «выбрать все sure», кнопки
  **Dry-run** и **Merge выбранные** (Merge — с confirm-диалогом `_ConfirmDialog`).
  Результат прогона: тост (`_Toast`) + стойкая строка статуса с разбивкой
  merged/conflict/error и разворачиваемыми actions/conflicts по парам.
- Эвристики показывать человекочитаемо: `suffix` → «Мусорный хвост», `swimmers` →
  «Общие пловцы (N)», `levenshtein` → «Опечатка». Sure-пары визуально выделить
  (бейдж `_StatusBadge` variant=success).
- Пункт навигации: `new("Clubs", "shield", "/Admin/Clubs", "Data")` в
  `Pages/Admin/Shared/AdminUi.cs` (список `AdminNav.Items`), после «Swimmers».
  Если иконки `shield` нет в `_Icon.cshtml` — возьми любую существующую подходящую.

## Что уже готово (не переделывать)

- Контроллер `server/Swimm.API/Controllers/ClubsAdminController.cs` — обе ручки.
- Сервисы `ClubDedupService`/`ClubMergeService` + DTO — не трогать.
- Образец страницы с той же механикой (fetch + antiforgery + чекбоксы + dry-run/merge):
  `server/Swimm.API/Pages/Admin/Swimmers/Index.cshtml` (+ `.cs`). Бери её за шаблон
  вёрстки и JS; Discovery/Index.cshtml — второй референс.
- Общие partials: `_PageHeader`, `_StatusBadge`, `_Toast`, `_ConfirmDialog`, `_Icon`
  в `Pages/Admin/Shared/`.

## Шаги

1. `Pages/Admin/Clubs/Index.cshtml.cs` — PageModel по образцу Swimmers (авторизация
   как у соседей, ничего лишнего).
2. `Pages/Admin/Clubs/Index.cshtml` — вёрстка + JS по решению выше.
3. Строка в `AdminNav.Items`.
4. `cd server/Swimm.API && npm run css:build` — пересобрать `wwwroot/css/admin.min.css`
   (новые Tailwind-классы иначе не попадут в бандл). Результат закоммитить вместе с кодом.

## Тесты

Логика вся на сервере и уже покрыта (`ClubDedupServiceTests`, `ClubMergeServiceTests`) —
новых юнит-тестов не требуется. Обязательно: `dotnet build server/Swimm.sln` зелёный и
`dotnet test server/Swimm.Tests/Swimm.Tests.csproj` — все существующие проходят.

## Проверка вживую

1. Запусти API: `ASPNETCORE_ENVIRONMENT=Development dotnet run --project server/Swimm.API --urls http://localhost:5079`
   (:5079 — база может быть занята другим процессом на :5078).
2. Залогинься админом, открой `/Admin/Clubs`: таблица кандидатов рендерится
   (в живой БД должны быть suffix-кандидаты из ~85 мусорных клубов).
3. Выбери 1-2 sure-пары → Dry-run → строка статуса показывает план, БД не изменилась
   (повторный GET кандидатов тот же). **Merge с apply НЕ прогонять** — боевой прогон
   по живым данным делает Влад отдельно.
4. Останови процесс API (иначе build-lock).

## Footguns

- **Build-lock**: если `dotnet build` падает с MSB3027/MSB3021 — жив старый
  `dotnet run`; убей `Swimm.API.exe` (см. корневой CLAUDE.md).
- `admin.min.css` собирается из `Styles/admin.css` сканом `@source` по
  `Pages/Admin/**` — новые классы требуют `npm run css:build`, файл коммитится.
- Комментарии — RU-проза, идентификаторы EN, весь видимый UI-текст — **английский**
  (правило проекта), кроме уже принятых русских строк в админке — смотри, как сделано
  на соседних страницах, и повторяй их язык.
- Контроллер требует роль Admin + antiforgery: JS должен слать `RequestVerificationToken`
  как на /Admin/Swimmers.

## Вне скоупа (не делать)

- CRUD клубов (создание/переименование/удаление руками) — бэклог 7.2.
- Изменения сервисов/контроллера/DTO фазы B.
- Merge псевдоклубов (USA/Israel/M25…) и любые решения по club-summary.
- Прогон merge с `apply: true` по живой БД.

## Отчёт

В конце: список изменённых файлов, что проверено вживую (со скринами не надо, текстом),
раздел «Отклонения от задания» (если пусто — так и напиши).
