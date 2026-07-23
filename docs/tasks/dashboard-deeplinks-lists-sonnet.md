# Задание (Sonnet 5): дашборд «здоровье данных» — T3b, новые выборки для deep-links

Контекст: T1 (счётчики), T2 (вёрстка 1c) и T3a (проводка дешёвых фильтров) уже в master.
Дашборд `/Admin` ставит ссылки на фильтры, для которых на целевых страницах пока НЕТ данных.
Это T3b — добавить недостающие выборки и секции. Счётчики этих же метрик уже считаются в
`DashboardStatusService` — твои выборки обязаны использовать **те же предикаты** (сверяйся
с ним, чтобы число на дашборде совпадало со списком на странице).

Общий контракт прежний: без query-параметра страницы работают как раньше; неизвестный
`filter` игнорируется.

## Решения (зафиксированы, не пересматривать)

- Все новые списки — **read-only v1** (посмотреть и перейти), без новых мутаций. Существующие
  ссылки на Edit-страницы — можно.
- Списки капятся: **200 строк** + общий count («показаны первые 200 из N»).
- Паттерн для JS-страниц (Swimmers, Clubs): новая сворачиваемая секция под существующими,
  грузится ЛЕНИВО — только когда пришёл соответствующий `?filter=` (или по клику на
  заголовок секции). Не замедлять обычную загрузку страницы.
- Предикаты (копия T1, `DashboardStatusService`):
  - swimmers no-org-id: `Origin == "isr" && SwimmerOrgId == null`;
  - swimmers no-results: не синтетика (`SwimmerOrgId NOT LIKE 'SYNTH-%'`), нет Results и
    нет RelayMembers;
  - clubs no-swimmers: `!IsPseudo && Name NOT LIKE 'SYNTH%'`, нет Swimmer.ClubId и нет
    Results.ClubId;
  - clubs no-country: `CountryId == null && !IsPseudo`;
  - competitions no-results: нет Results.CompetitionId;
  - competitions no-org-comp-id: `OrgCompId == null`;
  - discovery-error: `DiscoveredCompetition.LastError != null`;
  - media unchecked: `LinkCheckedAt == null`;
  - media moderation-pending: `UserMediaPublication.Status == "pending"`;
  - fk-anomaly: Results с несуществующим SwimmerId или ClubId;
  - empty-relay: Relay без RelayMember.

## Шаги

### 1. Swimmers/Index — `?filter=no-org-id|no-results`
- `SwimmersAdminController`: endpoint `GET /api/admin/swimmers/quality?filter=no-org-id|no-results`
  → `{ total: int, items: [{ id, lastName, firstName, birthYear, clubName }] }` (топ-200,
  сортировка по Id). Логика — в сервис/репозиторий по слоям (интерфейс в Application,
  реализация в Infrastructure — как соседние).
- На странице: секция «Качество данных» (заголовок + таблица), рендер при соответствующем
  `?filter=` (скролл к ней) или клике. Ссылка строки → публичная карточка пловца или
  просто текст, если карточки нет — смотри, как соседние списки линкуют.

### 2. Clubs/Index — `?filter=no-swimmers|no-country`
- Аналогично: `GET /api/admin/clubs/quality?filter=...` → `{ total, items: [{ id, name,
  nameEn, countryId }] }`; строка линкует на `/Admin/Clubs/Edit?id=`.

### 3. Competitions/Index — `?filter=discovery-error|no-org-comp-id|no-results`
- Расширить существующий unified-листинг: `ICompetitionAdminRepository.GetUnifiedAsync`
  (+ реализация) — новый необязательный параметр `qualityFilter` с этими тремя значениями,
  применяется как доп-WHERE (для discovery-error — по discovery-строкам, глянь как stage
  OnSite фильтрует). PageModel мапит `?filter=` в параметр (алиасы T3a не ломать: сначала
  существующий маппинг ignored/discovery-new → Stage, потом новые значения → qualityFilter).
- В шапке списка — заметка активного фильтра + ссылка «сбросить».

### 4. Results/Index — `?filter=fk-anomaly|empty-relay`
- Страница остаётся формой «перейти по Id», под ней добавить секцию «Аномалии»:
  `GET /api/admin/results/anomalies` → `{ fkAnomalies: { total, items: [{ resultId,
  swimmerId, clubId, competitionId }] }, emptyRelays: { total, items: [{ relayId,
  competitionId }] } }` (топ-200 каждая). Секция грузится при `?filter=` (подсветить
  нужную таблицу) или по клику. Ссылка строки fk → `/Admin/Results/Edit?id=`.

### 5. Media/Index — `?filter=unchecked|moderation-pending` (`broken-links` = текущий вид)
- PageModel: ветвление по `filter`. `unchecked` → выборка непроверенных (`LinkCheckedAt
  == null`, топ-200 + total) — новый метод рядом с `GetBrokenAsync` в `IUserMediaLinkChecker`
  (или отдельный репо-метод — по месту виднее). `moderation-pending` → read-only список
  pending-публикаций (`UserMediaPublications` + join Media/HubGroup: url, mediaType,
  группа, владелец, createdAt). Решения по публикациям принимают админы групп — кнопок
  approve/reject НЕ добавлять.
- Вкладки/переключатель видов сверху страницы (3 ссылки с query), активный подсвечен.

### 6. HubGroups/Index — `?tab=requests`
- Секция «Заявки на вступление (pending)»: список `HubGroupUserMembers.Status == "pending"`
  (группа, пользователь-email, когда) — read-only, через `IHubGroupAdminService` (новый
  метод) — показывается при `?tab=requests` (иначе скрыта, ссылка-тогл в шапке).

### 7. Тесты (обязательно)
- На каждый новый серверный метод выборки: предикат совпадает с дашборд-счётчиком
  (образец — `DashboardStatusServiceTests`, InMemory, стабы без Moq): позитив, негатив
  (исключения: синтетика/псевдо/approved и т.п.), кап 200 (total больше items.Count).
- Обнови `docs/admin-pages/*.md` затронутых страниц (swimmers, clubs, competitions,
  results, media, hubgroups): раздел про deep-link фильтры.

## Footguns

- Build-lock MSB3027 → Release, убить висящий `Swimm.API.exe`; :5078 может держать VS.
- Clean Architecture: контроллеры/PageModel — только интерфейсы из Swimm.Application;
  реализации в Infrastructure; DTO в Application/Dtos.
- Не трогать: `Pages/Admin/Index.cshtml`, `DashboardStatusService`/DTOs (кроме чтения),
  `Styles/admin.css`. Новые Tailwind-классы не вводить (используй существующие admin-*);
  css:build не запускать; если MSBuild сам перегенерил `admin.min.css` — `git checkout --`
  на него перед завершением.
- Все admin-эндпоинты: `[Authorize(Roles="Admin")]`; GET-выборки antiforgery не требуют.
- `Sys_`-таблицы (публикации, членства) читаются через `SwimmDbContext` — это и так
  дефолт в админке.

## Вне скоупа (не делать)

- Кнопка «Проверить апдейты» рекордов (T4, делается параллельно — не трогай
  RecordsAdminController/import-сервисы рекордов).
- Мутации (approve/reject публикаций, удаление пловцов и т.п.).
- Пагинация дальше топ-200, экспорт, сортировки.
