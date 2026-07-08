# План редизайна серверной админки (Razor Pages)

> Рабочий документ — обновляется по мере выполнения. Исходное ТЗ: `admin-redesign-prompt.md`.
> Статусы: ⬜ не начато · 🔄 в работе · ✅ готово

## Принятые решения

- **API Reference** — типизированный каталог эндпоинтов в C# + partial-карточка (не Swagger, не порт 1:1).
- **Удаление Competition** — каскадное (вместе с Results) с подтверждением вводом названия; в диалоге показывать число удаляемых результатов; удаление логировать.
- **home.html** — переделать в едином dark-стиле с админкой, на общем Tailwind-бандле.
- **Tailwind-сборка** — v4 через `@tailwindcss/cli`, npm-пайплайн внутри `server/Swimm.API`;
  MSBuild-target `BeforeTargets="Build"` (инкрементальный, условие «node_modules существует»);
  собранный `wwwroot/css/admin.min.css` **коммитится**, чтобы билд без Node работал.
- **Clean Architecture** — новый порт `ICompetitionAdminRepository` в `Swimm.Application/Abstractions`
  + DTOs; реализация в `Swimm.Infrastructure`; PageModel'ы ходят только через интерфейс.
  Миграция БД не нужна (таблицы `Competitions`, `CompetitionResultUrls` уже есть).
- **Авторизация/роли не трогаем**; URL-ы админки можно менять свободно.
- **Dev-обход логина** (по просьбе владельца): `"DevAdminBypass": true` в
  `appsettings.Development.json` (файл не коммитится) — синтетический админ без Google OAuth.
  Работает только в Development + при явном флаге; код в `Program.cs` между
  `UseAuthentication`/`UseAuthorization`.
- **Partials — только по абсолютным путям** (`/Pages/Admin/Shared/_X.cshtml`): относительные
  имена резолвятся от папки страницы и ломаются во вложенных папках (`Competitions/`).

## Целевая структура

```
server/Swimm.API/
├── package.json                  # npm-пайплайн Tailwind (только CSS)
├── Styles/admin.css              # исходник: @import "tailwindcss" + @theme токены
├── wwwroot/css/admin.min.css     # собранный бандл (в репо)
├── Pages/Admin/
│   ├── _Layout.cshtml            # сайдбар-layout
│   ├── Shared/
│   │   ├── _Sidebar.cshtml       # рендер по C#-навмодели
│   │   ├── _PageHeader.cshtml
│   │   ├── _Card.cshtml, _StatusBadge.cshtml, _Toast.cshtml
│   │   ├── _ConfirmDialog.cshtml # ввод названия для каскадного удаления
│   │   ├── _Icon.cshtml          # inline-SVG (Lucide) вместо эмодзи
│   │   └── _ApiEndpointCard.cshtml
│   ├── Index / Db / Import / ImportHistory / Settings   # переписываются
│   ├── Api.cshtml + ApiCatalog.cs
│   └── Competitions/             # эталонный CRUD
│       ├── Index.cshtml(.cs)     # список + поиск + пагинация
│       └── Edit.cshtml(.cs)      # create/edit + OrgCompId + CompetitionResultUrls
└── wwwroot/home.html             # единый стиль с админкой
```

## Фазы

| # | Статус | Фаза | Сложность | Модель |
|---|--------|------|-----------|--------|
| 0 | ✅ | Tailwind-инфраструктура | Низкая-средняя | Sonnet 5 |
| 1 | ✅ | Каркас: layout + сайдбар + компоненты | **Высокая** | **Fable** |
| 2 | ✅ | Миграция страниц: Index, Db, Settings, ImportHistory | Средняя | Sonnet 5 |
| 3 | ✅ | Import.cshtml (без регрессии импорта) | Средняя-высокая | Opus 4.8 |
| 4 | ✅ | API Reference → каталог | Средняя | Sonnet 5 |
| 5 | ✅ | CRUD Competitions (эталон) | **Высокая** | **Fable** / Opus 4.8 |
| 6 | ✅ | home.html + чистка старых CSS + CLAUDE.md | Низкая | Sonnet 5 |
| 7 | ✅ | Верификация | Низкая | Sonnet 5 |

Зависимости: 0 → 1 → (2, 3, 4 — независимы, можно параллельно) → 5 → 6 → 7.

### Фаза 0 — Tailwind-инфраструктура ✅
- [x] `server/Swimm.API/package.json` + `@tailwindcss/cli` (v4, та же мажорная, что в `client/`)
- [x] `Styles/admin.css`: `@import "tailwindcss"` + `@theme` токены dark-темы
- [x] npm-скрипты `css:build` / `css:watch`; выход → `wwwroot/css/admin.min.css`
- [x] MSBuild-target в `Swimm.API.csproj` (инкрементальный, skip без node_modules)
- [x] Убрать `<script src="https://cdn.tailwindcss.com">` из `_Layout.cshtml`

### Фаза 1 — Каркас ✅
- [x] Новый `_Layout.cshtml`: левый сайдбар, контентная область, тост, DB-banner как partial
- [x] `_Sidebar.cshtml` + C#-навмодель (`Shared/AdminUi.cs`) — новая страница = одна строка
- [x] Мобильная адаптивность: сайдбар за гамбургером на узких экранах (топбар + бэкдроп)
- [x] Библиотека partials: `_PageHeader`, `_StatusBadge`, `_Toast`, `_ConfirmDialog`, `_Icon`, `_DbBanner`
      (вместо `_Card` — CSS-класс `.admin-card`: partial не может оборачивать произвольный контент)
- [x] Дизайн-токены dark-first темы + компонентные классы (`.admin-btn*`, `.admin-input`,
      `.admin-table`, `.admin-badge`) в `Styles/admin.css` `@layer components`
- [x] Страница-витрина `/Admin/UiPreview` — стайлгайд компонентов для фаз 2–5

### Фаза 2 — Миграция страниц ✅
- [x] `Index.cshtml` (главная админки: users/roles/stats) — новый `ViewData["PageSubtitle"]`,
      статичная разметка на `.admin-card`/`.admin-input`/`.admin-table`; JS-логика не тронута
- [x] `Db.cshtml` (schema viewer) — то же для чрома страницы; JS-рендер таблиц/сайдбара
      оставлен на классах из `admin-all.css` (`.sidebar`, `.col-table`, …) — рефактор этих
      классов не входит в фазу 2, слишком много точек генерации разметки в JS
- [x] `Settings.cshtml` — банер и таблица на компонентных классах; toggle/scope-tab поведение
      (JS + `admin-all.css`) не тронуто
- [x] `ImportHistory.cshtml` — статы на `.admin-card`, таблица на `.admin-table`
- [ ] Удалить старые CSS — отложено: `admin-all.css` всё ещё нужен (tabs/toggle-switch/scope-tab/
      db sidebar+table классы, генерируемые в JS); чистка — фаза 6, как и по плану

### Фаза 3 — Import ✅
- [x] Переписать UI на компоненты; JS-логика импорта и `JsonImportService` работают 1:1
      (весь `<script>`-блок оставлен байт-в-байт; тронута только статичная разметка)
- [x] Регресс-тест: реальный импорт JSON проходит как раньше

### Фаза 4 — API Reference ✅
- [x] `Shared/ApiCatalog.cs`: типизированный список (`ApiParam`/`ApiEndpoint`/`ApiGroup`,
      21 эндпоинт, метод/путь/параметры/описания RU/права/highlighted-варианты)
- [x] `Shared/_ApiEndpointCard.cshtml` + переиспользован общий Run-JS (`runApi` в
      `Api.cshtml` не менялся) — Public/Admin вкладки теперь `@foreach` по каталогу
      + один partial вместо ~700 строк копипаст-разметки. Results/Competitions вкладки
      (не документация, а рабочие инструменты) и вся их JS-логика не тронуты.

### Фаза 5 — CRUD Competitions ✅
- [x] `ICompetitionAdminRepository` + DTOs (`CompetitionCrudDtos.cs`: `PagedResult<T>`,
      `CompetitionListItemDto`/`EditDto`/`ResultUrlDto`/`InputDto`/`SaveResult`) в `Swimm.Application`,
      реализация `CompetitionAdminRepository` в `Swimm.Infrastructure` + DI-регистрация
- [x] `Competitions/Index`: список (server-rendered), поиск по Name/SubName (ILIKE), пагинация по 20
- [x] `Competitions/Edit`: create/edit всех полей (Name, SubName, Date, PoolType, Country,
      `OrgCompId`, флаги Masters/Award/Combine); Event/DayNumber — read-only (управляет импорт)
- [x] Блок `CompetitionResultUrls`: добавление/удаление URL по culture (связь по `OrgCompId`),
      с проверкой уникальности (OrgCompId, Culture) и существования OrgCompId
- [x] Каскадное удаление: серверная проверка вводом названия + показ числа Results + лог
      (`ILogger`, пишет user/id/name/counts); переиспользует `IImportService.DeleteCompetitionAsync`
- [x] Antiforgery на всех мутациях (Razor-формы + auto-validate); структура — образец для будущих CRUD

### Фаза 6 — home.html + чистка ✅
- [x] `home.html` в едином стиле на общем бандле
- [x] Удалить оставшиеся старые CSS (`admin-all`, `admin`, `db`, `import`, `settings`, `api`, `home`, `db-banner`)
- [x] Обновить `CLAUDE.md` (шаг сборки CSS)

### Фаза 7 — Верификация ✅
```bash
cd server/Swimm.API && npm run css:build     # бандл собирается
dotnet build server/Swimm.sln                # билд ок (и с Node, и без)
ASPNETCORE_ENVIRONMENT=Development dotnet run --project server/Swimm.API --urls http://localhost:5078
```
Обход: `/`, `/Admin`, `/Admin/Api` (Run-кнопки), `/Admin/Db`, `/Admin/Settings`,
`/Admin/Import` (**реальный импорт JSON**), `/Admin/ImportHistory`,
`/Admin/Competitions` (создать → добавить URL → отредактировать → каскадно удалить).
Мобильный вид: гамбургер. В конце `/smoke-test-api` и убить процесс (build-lock footgun).

## Риски
- **Import** — единственная страница с рабочей бизнес-логикой на JS → отдельная фаза, сильная модель.
- **Каскадное удаление** — деструктивно; подтверждение вводом названия + лог обязательны.
- MSBuild+npm на Windows капризен → CSS-артефакт коммитится, билд без Node всегда работает.

## Журнал прогресса

_(заполняется по мере выполнения: дата, фаза, что сделано, отклонения от плана)_

- **2026-07-07, Фаза 0**: добавлены `package.json` (`@tailwindcss/cli` + `tailwindcss` v4.1.17),
  `Styles/admin.css` (`@import "tailwindcss"` + `@source` на `Pages/Admin/**/*.cshtml` + `@theme`
  с токенами `--color-admin-*`), MSBuild-target `BuildAdminCss` (`BeforeTargets="Build"`,
  `Condition="Exists('node_modules')"`), убран CDN-скрипт из `_Layout.cshtml` — вместо него
  подключён `/css/admin.min.css`. `npm install` + `npm run css:build` + `dotnet build server/Swimm.sln`
  проверены локально: бандл собирается, MSBuild-target срабатывает, билд зелёный (10 warnings,
  не связаны с этой фазой). Классы Tailwind в самих `.cshtml` пока старые (CDN-стиль, миграция —
  фазы 1–6), но инфраструктура сборки готова. Визуально не проверялось — нет запущенного
  `dotnet run`/preview для Razor Pages в этой сессии.
- **2026-07-07, Фаза 2**: `Index`, `Db`, `Settings`, `ImportHistory` переведены на новую
  `_Layout` (убраны старые `ViewData["HeaderTitle"/"TitleColor"/"BodyClass"]`, добавлен
  `PageSubtitle`; убраны дублирующие `max-w/mx-auto/p-6`-обёртки — их даёт `<main>` в `_Layout`).
  Статичная разметка переведена на компонентные классы (`.admin-card`, `.admin-input`,
  `.admin-table`, токены `admin-accent/success/danger/warning`). JS-логика всех четырёх страниц
  не менялась. `Db.cshtml` и часть `Index`/`Settings` по-прежнему используют legacy-классы из
  `admin-all.css` (`.sidebar`, `.col-table`, `.tab-btn`, `.toggle-switch`, `.scope-tab` и т.п.) —
  они генерируются в JS-шаблонах, полный перевод в компоненты не входил в объём этой фазы;
  файл остаётся до фазы 6. Проверено: `npm run css:build`, `dotnet build server/Swimm.sln` —
  зелёные; локально поднят `dotnet run`, curl `/Admin`, `/Admin/Db`, `/Admin/Settings`,
  `/Admin/ImportHistory` → 200, в HTML — новый `<title>` и `admin.min.css`; процесс остановлен
  после проверки.
- **2026-07-07, Фаза 3 (Opus 4.8)**: `Import.cshtml` — статичная разметка переведена на
  `_Layout`/токены/компоненты (`.admin-card`, `.admin-input`, `.admin-btn admin-btn-ghost`,
  `admin-accent/success/danger`), убраны `ViewData["HeaderTitle"/"TitleColor"]` + внешняя
  `p-6`-обёртка (форма осталась `max-w-[800px] mx-auto`). **Весь `<script>`-блок (drag&drop,
  очередь+поллинг `/api/admin/import` → `status/{jobId}`, clear-data, модалка) оставлен
  байт-в-байт** — он тоглит произвольные классы (`border-l-[#66bb6a/#ef5350/#ffa726]`,
  `bg-white/[.07]`), которые Tailwind видит через `@source`-скан этого же `.cshtml`; сохранены
  все `id` и JS-referenced класс `drop-zone`. Сохранены строгие красные кнопки удаления
  (`bg-[#c62828]`) как акцент опасной зоны. Проверки: `npm run css:build` (все JS-тоглимые
  классы попали в бандл — grep по `border-left-color:#66bb6a` и `bg-white/[.07]`),
  `dotnet build` зелёный; `/Admin/Import` → 200 с новым чромом и всеми `id`;
  **реальный сквозной импорт**: POST `/api/admin/import` с antiforgery-токеном (Development
  пускает admin-эндпоинты) файла `competition-2025-hapoel-young-isr-championship.json` →
  job `completed`: **totalRows 63, created 63, skipped 0, errors 0** (fallback-путь Array, как и
  раньше). Регрессии нет. Процесс остановлен (build-lock).
- **2026-07-07, Фаза 4**: `Api.cshtml` (Public/Admin вкладки, ~700 строк копипаст-карточек)
  переведён на данные: `Shared/ApiCatalog.cs` содержит все 21 эндпоинт (`ApiParam`/`ApiEndpoint`/
  `ApiGroup`), `Shared/_ApiEndpointCard.cshtml` рендерит карточку (метод-бейдж, path, auth-бейдж,
  таблица параметров, Run-форма с `data-param`/`data-path`/`data-body` инпутами, response-area) —
  включая три highlighted-карточки (import/clear/enrich) с их Body/Response-документацией и
  import-заглушку без Run (только Note-ссылка на /Admin/Import). Добавлен `@source
  "../Pages/Admin/**/*.cs"` в `Styles/admin.css`, чтобы Tailwind видел arbitrary-классы
  (`border-l-[#66bb6a]` и т.п.), написанные в C#, а не только в `.cshtml`. Results/Competitions
  вкладки (не документация — рабочие инструменты с фильтрами/поиском/CRUD-флагами) и общий
  `runApi()`/весь остальной `<script>` не тронуты. Мелкая шероховатость: null-тернарный трюк для
  необязательных `data-path`/`data-body`/`style` атрибутов в partial не убирает их из разметки
  (рендерится `data-path=""` вместо отсутствия атрибута) — не влияет на поведение, т.к. JS сравнивает
  строго с `'true'`, но чище было бы вынести в `@if`; оставлено как есть (не блокирует фазу).
  Проверено: `npm run css:build` (highlighted-цвета в бандле), `dotnet build` зелёный; поднят
  `dotnet run`, `/Admin/Api` → 200, в HTML ровно 21 `.api-card`, 20 Run-кнопок (у import её нет —
  верно), счётчики method-badge (13 GET/3 POST/2 DELETE/2 PATCH/1 PUT) и auth-badge (14 Admin/1 Auth)
  совпадают с оригиналом 1:1; проверены живые вызовы `/api/admin/stats`, `/api/admin/clearable-tables`,
  `/api/admin/competition-events` → 200. Процесс остановлен.
- **2026-07-07, Фаза 5 (Opus 4.8)**: эталонный CRUD Competitions.
  - **Application**: `Abstractions/ICompetitionAdminRepository.cs` (пагинация/поиск, GetById,
    Create/Update, Add/RemoveResultUrl), `Dtos/CompetitionCrudDtos.cs`. В `DeleteCompetitionResult`
    добавлено поле `ResultUrls`.
  - **Infrastructure**: `Repositories/CompetitionAdminRepository.cs` (через `SwimmDbContext`,
    ILIKE-поиск, серверная проверка уникальности Name+Date+PoolType и OrgCompId с дружелюбным
    текстом вместо DbUpdateException, инвалидация кэша после мутаций) + DI-регистрация.
    `JsonImportService.DeleteCompetitionAsync` расширен: чистит `CompetitionResultUrls` по OrgCompId
    **с guard'ом** (только если OrgCompId не используется другим соревнованием) — фикс общего
    пробела (польза и для Api-таба), в одной транзакции.
  - **API (Razor Pages)**: `Pages/Admin/Competitions/Index.cshtml(.cs)` (список+поиск+пагинация),
    `Edit.cshtml(.cs)` (handlers Save/AddUrl/DeleteUrl/Delete, PRG + TempData-toast, hidden
    antiforgery в каждой форме). Каскадное удаление — **серверная** проверка вводом точного
    названия (не только клиентская), логирование `ILogger` (user/id/name/counts). Пункт
    Competitions раскомментирован в `AdminNav`.
  - **Проверено вживую** (`dotnet run`, сессия через Python cookiejar — curl криво тянул
    antiforgery-токен): `dotnet build` зелёный; Index → 200 (13 соревнований, поиск, nav);
    полный цикл create #79 → GET edit (OrgCompId виден) → AddUrl (he-IL) → Save (Country ISR→USA) →
    Delete c неверным именем = **отклонено** (баннер-ошибка рендерится, соревнование на месте) →
    Delete c верным именем = **каскад** (redirect на Index; лог: «Admin dev-admin каскадно удалил
    #79 … 1 URL» — подтверждает очистку CompetitionResultUrl); дубль Name+Date+Pool и дубль
    OrgCompId **отклоняются** без создания строк. Antiforgery-формы валидируются (Razor auto).
    Тестовые соревнования подчищены. Процесс остановлен (build-lock).
  - Примечание: ошибки валидации приходят HTML-entity-энкоднутыми (Razor кодирует кириллицу в
    `&#x…;`) — это норма для `@Model.Error`, влияет только на строковый поиск в тестах, не на UI.

- **2026-07-07, Фаза 1 (Fable)**: каркас готов и проверен вживую.
  - Новые файлы: `Pages/Admin/Shared/` — `AdminUi.cs` (навмодель + вью-модели),
    `_Sidebar`, `_Icon` (inline Lucide SVG вместо эмодзи), `_PageHeader`, `_StatusBadge`,
    `_Toast`, `_ConfirmDialog` (JS `adminConfirm()` → Promise, danger-режим,
    подтверждение вводом текста), `_DbBanner`; страница-витрина `Pages/Admin/UiPreview.cshtml`.
  - `_Layout.cshtml` переписан: сайдбар + мобильный топбар с гамбургером; глобальные JS
    `toast()/esc()/api()` и antiforgery сохранены со старыми сигнатурами — существующие
    страницы работают без правок. Страницы задают `ViewData["Title"]` (+ опц. `PageSubtitle`);
    старые `HeaderTitle/TitleColor/BodyClass` игнорируются.
  - `Styles/admin.css`: добавлен токен `--color-admin-warning`, `@layer components`
    (`.admin-card/.admin-btn*/.admin-input/.admin-table/.admin-badge`).
  - `admin-all.css`: удалены правила `body` (градиент/моно-шрифт) и `.toast.show`;
    остальное живёт до фаз 2–4. Ссылка на `db-banner.css` из layout убрана
    (файл ещё нужен `home.html` — фаза 6).
  - **Dev-обход логина**: `Program.cs` + `"DevAdminBypass": true` в `appsettings.Development.json`.
  - Проверено против запущенного сервера (preview): все 7 страниц `/Admin*` — 200 с сайдбаром,
    активный пункт подсвечивается, тема применяется, toast/confirm работают, мобильный режим
    (гамбургер/бэкдроп) работает. `dotnet build` — 0 warnings.
  - Уроки: (1) partial-имена резолвятся от папки страницы → только абсолютные пути;
    (2) НЕ редактировать русскоязычные файлы через PowerShell `Get-Content`/`-replace`
    (PS 5.1 читает UTF-8 как ANSI и портит кодировку) — только Read/Edit/Write-инструменты;
    (3) событие `close` у `<dialog>` не срабатывает в headless-движке — `adminConfirm`
    резолвится напрямую с кнопок; (4) `.claude/launch.json` получил конфиг `swimm-api`.
  - Отклонение от плана: `_Card`-partial заменён CSS-классом `.admin-card` (Razor-partial
    не оборачивает произвольный контент); добавлена витрина `/Admin/UiPreview` сверх плана.

- **2026-07-07, Фаза 6**: `home.html` + чистка старых CSS + `CLAUDE.md`.
  - `wwwroot/home.html`: убран CDN-скрипт Tailwind и линки на `home.css`/`db-banner.css`, вместо
    них — `/css/admin.min.css` (тот же бандл, что у `/Admin/*`). Фон/токены переведены на
    `bg-admin-bg`/`text-admin-text`/`text-admin-accent`/`text-admin-text-muted`; профиль-карточка
    теперь `.admin-card`, кнопки API/Admin — `.admin-btn.admin-btn-ghost`; DB-banner переведён с
    отдельного CSS-класса на Tailwind-утилиты (`border-admin-warning/30 bg-admin-warning/10` +
    `hidden`/`classList.toggle`, вместо `style.display`). Кнопка Google Sign-in оставлена с
    брендовым синим (`bg-[#4285f4]`) — не токен темы, узнаваемость логотипа важнее унификации.
    В `Styles/admin.css` добавлен `@source "../wwwroot/home.html"`, чтобы Tailwind сканировал
    классы статического HTML вне `Pages/Admin`.
  - Удалены как файлы (не просто отвязаны): `wwwroot/css/admin-all.css`, `admin.css`, `db.css`,
    `import.css`, `settings.css`, `api.css`, `home.css`, `db-banner.css`. Первые пять были уже
    полностью не подключены нигде (мёртвые с более ранних фаз); `admin-all.css` ещё
    использовался (`<link>` в `_Layout.cshtml`) для классов, которые JS-рендер генерирует в
    Index/Settings/Import/Db/Api (`.tab-*`, `.toggle-switch`/`.toggle-slider`, `.scope-tab.active`,
    `.saved-icon.show`, `.drop-zone.drag-over`, `.sidebar h3/a`, `.col-table`, `.dep-table`,
    `.obj-block`, `.params-table`, `.param-inputs`, `.response-*`, `.method-badge`+варианты,
    `.path`, `.desc`, `.auth-badge`, `.admin-note`) — перенесены дословно в `Styles/admin.css`.
  - **Баг и фикс во время переноса**: первая попытка обернула перенесённые классы в
    `@layer components` — это сломало `.scope-tab.active`/`.tab-btn.active` (Tailwind-утилиты типа
    `bg-transparent`/`text-admin-text-muted` на тех же элементах стали перебивать их несмотря на
    более высокую специфичность правил, потому что **cascade layers в CSS игнорируют
    специфичность**: unlayered-правила и более поздние layers всегда бьют более ранние —
    `utilities` объявлен Tailwind позже `components`). Исходный `admin-all.css` работал только
    потому, что был отдельным unlayered-стилшитом. Фикс: перенесённый блок оставлен **вне
    `@layer`** (plain CSS в конце `Styles/admin.css`), с комментарием-предупреждением, почему.
    Проверено `preview_inspect` до/после (`.scope-tab.active` background-color: `rgba(0,0,0,0)` →
    `rgb(79,195,247)`).
  - `CLAUDE.md` (корень): добавлена секция «Admin/home CSS (Tailwind v4)» в `## Build & run` —
    `npm install`/`npm run css:build`/`css:watch`, автозапуск через MSBuild-target при наличии
    `node_modules`, почему `admin.min.css` коммитится, список удалённых legacy-файлов.
  - Проверено вживую (`dotnet run`, DevAdminBypass): `/home.html` (тёмная тема, без CDN/legacy CSS
    в network-логе после ребилда), `/Admin/Db` (сайдбар + col-table/dep-table/obj-block),
    `/Admin/Settings` (toggle ON/OFF, scope-tab клик меняет активную вкладку и фильтр),
    `/Admin/Import` (drag-over подсветка drop-zone), `/Admin/Api` (method-бейджи, tab-переключение
    Public/Results), `/Admin` (Dashboard, tab-btn.active), `/Admin/Competitions` — все без console
    errors. `npm run css:build` + `dotnet build server/Swimm.sln` зелёные (0 warnings). Процесс
    остановлен.
  - Осталось: Фаза 7 (верификация полного обхода + мобильный вид + `/smoke-test-api`).

- **2026-07-07, Фаза 7**: полная верификация редизайна.
  - **Билд**: `npm run css:build` зелёный; `dotnet build server/Swimm.sln` зелёный **и с
    `node_modules`, и без** (временно переименовал `node_modules` → билд всё равно прошёл на
    закоммиченном `admin.min.css`, MSBuild-таргет корректно скипнулся по `Condition="Exists(...)"`;
    `node_modules` восстановлен).
  - **Обход всех страниц** (`dotnet run`, DevAdminBypass, preview-инструменты): `/` (home.html,
    тёмная тема, ни одного legacy-запроса в network-логе), `/Admin` (Dashboard, tab-btn.active),
    `/Admin/Db` (26 col-table, 1 dep-table, сайдбар), `/Admin/Settings` (toggle ON/OFF, scope-tab
    фильтрация), `/Admin/Import`, `/Admin/ImportHistory`, `/Admin/Api` — **Run-кнопки реально
    проверены** (`/auth/me` → status-ok зелёный, `/api/admin/stats` → status-ok), `/Admin/Competitions`.
    Мобильный вид (375×812): гамбургер открывает сайдбар (`-translate-x-full` → `translate-x-0`),
    backdrop показывается; `/` тоже не ломается на mobile. Console errors — ни разу.
  - **Реальный импорт JSON**: скопировал `client/public/data/json/competition-2024-summer-isr-championship.json`
    (28 results, ранее не импортировался — свежий тест create-пути, не переиспользование фазы 3)
    во временный `wwwroot/tmp-verify-import.json`, прогнал через реальный UI-флоу `/Admin/Import`
    (синтетический `File`/`DataTransfer` → `change` на `#file-input` → `setFile()` → клик
    `#btn-upload` → `FormData` POST) — **28 total, 28 created, 0 skipped, 0 errors**, запись
    появилась в `/Admin/ImportHistory` (id 26). Временный файл удалён после теста.
  - **Полный CRUD-цикл Competitions**: (1) отредактировал импортированное соревнование #83 —
    задал `OrgCompId=99001`, `Country=ISR`, Save → toast «Изменения сохранены»; (2) добавил URL
    (he-IL) → toast «URL добавлен»; (3) каскадное удаление с **неверным** именем → отклонено
    («Название введено неверно — удаление отменено», данные на месте); (4) с **верным** именем →
    каскад прошёл, toast «…удалено (28 результатов)», redirect на Index (13→13, тестовая запись
    ушла), `Sys_ImportHistory` тоже почистился (9→8 записей), лог `ILogger` подтвердил
    («…каскадно удалил #83… 28 результатов… 1 URL… 1 записей истории»). (5) Отдельно проверил
    **создание** через форму `+ Новое соревнование` (не было протестировано выше) — здесь всплыл
    **баг** (см. ниже), после фикса — создание/каскадное удаление прошли чисто (id 84, 0 результатов).
  - **Найден и исправлен баг** (не привнесён фазой 6 — существовал с фазы 5, но проявился только
    сейчас при реальном сквозном тесте создания): `CompetitionAdminRepository.Apply()`
    (`server/Swimm.Infrastructure/Repositories/CompetitionAdminRepository.cs:187-189`) и
    `ValidateAsync()` (строки 203-204) звали `.Trim()` на `input.Date`/`input.PoolType`/
    `input.Country` без null-проверки. Эти поля **не обязательны** (звёздочка `*` в форме только у
    Name) — многие реальные соревнования имеют пустой `Country` (видно как «—» в списке
    Competitions). ASP.NET Core биндит **пустое** текстовое поле формы в `null`, а не `""`, для
    `string`-свойств — воспроизвёл создавая соревнование с пустым «Страна» → `NullReferenceException`,
    500 Internal Server Error. Фикс: `(input.Date ?? "").Trim()` / `(input.PoolType ?? "").Trim()`
    / `(input.Country ?? "").Trim()` в обоих местах. Пересобрано, перезапущено, воспроизведено ещё
    раз с тем же пустым Country — создание теперь проходит без ошибки.
  - **`/smoke-test-api`**: `dotnet build` зелёный → `dotnet run` в фоне → poll `/auth/me` (200) →
    curl `/auth/me`, `/api/admin/stats` (13 competitions — чистое состояние после теста), `/api/results`,
    `/Admin/Competitions` (200), `/` (200) → процесс остановлен через `Get-NetTCPConnection -LocalPort 5078`
    (по имени `Swimm.API.exe` процесс не нашёлся — хостится под `dotnet.exe`) → повторный
    `dotnet build` подтвердил отсутствие build-lock.
  - Итог: редизайн админки (фазы 0–7) завершён. Единственное расхождение с планом — по ходу
    верификации найден и исправлен независимый баг создания Competition с пустыми
    необязательными полями (вне первоначального объёма фазы 6/7, но блокировал «эталонный CRUD»).
