# Задание (Sonnet 5): дашборд «здоровье данных» — T3a, проводка deep-link фильтров

Контекст: новый дашборд `/Admin` (план `docs/plans/admin-dashboard-health-2-plan.md`, контракт
ссылок — `!design_handoff/design_handoff_data_health_dashboard/README.md`, раздел «Контракт
ссылок») ссылается на админ-страницы с query-параметрами. Это **T3a — только дешёвая проводка**:
где данные/фильтр уже существуют, научить страницу читать query и применять его до первого
рендера. Новые серверные выборки (no-org-id, no-results, no-swimmers, discovery-error,
страница Results, медиа-модерация) — T3b, НЕ твоё.

Общий контракт: неизвестное/отсутствующее значение `filter` игнорируется — страница работает
как раньше. Никаких изменений поведения без query-параметра.

## Решения (зафиксированы, не пересматривать)

- JS-страницы читают query через `new URLSearchParams(location.search)` при init, до первого
  рендера, и выставляют состояние (селекты/фильтры), затем обычный поток.
- Server-side страницы — `[BindProperty(SupportsGet = true)]`, как соседние параметры.
- **`Pages/Admin/Index.cshtml` НЕ трогать вообще** — его параллельно переверстывает другой
  агент (фильтр `?filter=deactivated` вкладки Users сделает ревьюер).

## Страницы и что сделать

### 1. Swimmers/Index (`Pages/Admin/Swimmers/Index.cshtml`, JS-рендер)
- `?filter=dedup-unsure` / `dedup-sure`: клиентский фильтр списка кандидатов по `c.sure`
  (false/true соответственно). Реализовать как состояние `let dedupFilter = null|'sure'|'unsure'`,
  применяемое в `render()`; при активном фильтре показать сброс (чип/кнопка «показать все»).
- `?filter=orphans`: после загрузки проскроллить к секции сирот (`#orphans`) и подсветить её
  (кратковременный outline admin-accent, ~1.5с).
- Значения `no-org-id`/`no-results` — игнорировать (T3b).

### 2. Swimmers/Loglig (`Pages/Admin/Swimmers/Loglig.cshtml`, JS-рендер)
- `?filter=loglig-unlinked` → выставить `#status = 'unlinked'` до `load()`.
- `?filter=loglig-pending` / `loglig-rejected`: в `<select id="status">` добавить опции
  `suggested` («На проверке») и `rejected` («Отклонённые»). Проверь контроллер
  (`/api/admin/loglig/list`, параметр `status`) — если он такие значения не фильтрует,
  расширь его фильтрацию по `Swimmer.LogligIdStatus` ("Suggested"/"Rejected"); маппинг
  query→значение селекта: `loglig-pending`→`suggested`, `loglig-rejected`→`rejected`.

### 3. Clubs/Index (`Pages/Admin/Clubs/Index.cshtml`, JS-рендер)
- `?filter=dedup-unsure` / `dedup-sure`: как в п.1 (по `c.sure`).
- `no-swimmers`/`no-country` — игнорировать (T3b).

### 4. Competitions/Index (server-side, query уже биндится)
- Принять `?filter=` как алиас: `ignored` → `Stage="Ignored"`, `discovery-new` → `Stage="OnSite"`
  (только если `stage` не задан явно — явный `stage` приоритетнее). Реализация в `OnGetAsync`.
- `discovery-error`/`no-org-comp-id`/`no-results` — игнорировать (T3b).

### 5. Records/Index (server-side, фильтры уже биндятся)
- Принять `?region=` как алиас пары: `world` → `RegionType="world"`, `RegionCode=""`;
  `israel` → `RegionType="country"`, `RegionCode="ISR"` (значения сверь с
  `Record.RegionTypes` в `Swimm.Domain/Entities/Record.cs` — там "world"/"continent"/"country").
  Явные `regionType`/`regionCode` приоритетнее алиаса. Неизвестный `region` игнорируется.

### 6. Audit/Index (server-side)
- Новый параметр `?period=` со значениями `24h`/`7d`/`30d` (иное/пусто = всё время):
  `[BindProperty] string? Period`, маппинг в `DateTime SinceUtc`, поле `DateTime? SinceUtc`
  добавить в `AdminAuditFilter`, условие `CreatedAt >= SinceUtc` — в реализацию
  `IAdminAuditRepository.QueryAsync`. UI: селект периода рядом с существующими фильтрами,
  прокинуть через `PageUrl(...)` (пагинация — параметр `p`).
- `&level=error` из хендоффа игнорировать всегда — уровня ошибок в аудите нет (решение T1).

### 7. HubGroups/Index (server-side)
- `?filter=official`: показать только официальные группы. Проверь `HubGroupAdminRowDto` /
  `IHubGroupAdminService.GetAllAsync` — если признака `IsOfficial` в DTO нет, добавь поле
  (маппится из `HubGroup.IsOfficial`) и отфильтруй в PageModel (фильтрация в памяти по
  уже загруженному списку допустима — списки маленькие). Заодно колонка/бейдж «official»
  в таблице, чтобы фильтр был виден глазами.
- `?tab=requests` — игнорировать (T3b решит, как показывать заявки на вступление).

### 8. Тесты
- Единственная серверная логика с ветвлением — Audit `SinceUtc` и Loglig-статусы:
  - тест(ы) на `AdminAuditRepository.QueryAsync` с `SinceUtc` (запись старше границы не
    попадает) — по образцу существующих тестов аудита в `Swimm.Tests` (найди по
    `AdminAudit`);
  - если расширял Loglig-контроллер/репозиторий — тест на фильтр `status=suggested|rejected`.
- Alias-маппинги (`filter`→`Stage`, `region`→пара) — юнит-тестировать не нужно (тонкие
  PageModel-ветки), но проверь вручную.

## Что уже готово (не переделывать)

- Разведка (2026-07-23): Competitions/Records/Audit уже читают query (`SupportsGet`);
  Swimmers/Clubs/Loglig — чистый JS-рендер без чтения URL; пагинация server-side страниц —
  параметр `p` (у Records — `page`).
- Дашборд уже ставит все эти ссылки (T2) — твоя работа делает их живыми.

## Проверка

- `dotnet build server/Swimm.sln --configuration Release` — 0 ошибок;
  `dotnet test server/Swimm.Tests --configuration Release` — все зелёные.
- Вживую (API Release, :5079 при занятом :5078): открой руками (или curl'ом проверь 200):
  `/Admin/Competitions?filter=ignored` (список = как при stage=Ignored),
  `/Admin/Records?region=israel`, `/Admin/Audit?period=7d`. JS-страницы требуют логина —
  проверь хотя бы отсутствие JS-синтакс-ошибок; живой прогон сделает ревьюер.
- Останови API-процесс после проверки.

## Footguns

- Build-lock MSB3027 → Release-конфигурация, убить висящий `Swimm.API.exe`.
- **Не трогай** `Pages/Admin/Index.cshtml`, `Styles/admin.css`, `wwwroot/css/admin.min.css`,
  `docs/admin-pages/index.md` — их правит параллельный агент (T2). Если менял Tailwind-классы
  на своих страницах — НЕ пересобирай css (иначе конфликт по admin.min.css); новые классы
  не добавляй, используй существующие admin-* компоненты.
- `AdminAuditFilter` — record/класс в Application-слое; расширение — новый необязательный
  параметр в конец, чтобы не ломать существующие вызовы.
- Razor Pages: имя query-параметра `page` занято роутингом — новые параметры не называть так.

## Вне скоупа (не делать)

- Всё из T3b: no-org-id/no-results (Swimmers), no-swimmers/no-country (Clubs),
  discovery-error/no-org-comp-id/no-results (Competitions), списки на Results,
  Media unchecked/moderation-pending, HubGroups tab=requests.
- `?filter=deactivated` вкладки Users (Index.cshtml).
- Обновление docs/admin-pages/*.md целевых страниц — сделает ревьюер одним проходом (T5).
