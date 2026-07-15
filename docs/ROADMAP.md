# Роадмап Swimm — фазы и этапы

Целевая архитектура и принятые решения — в [ARCHITECTURE.md](ARCHITECTURE.md).
Порядок фаз выбран по зависимостям: сначала фундамент (парсинг как библиотека),
затем данные (рекорды в БД), затем масштаб/кэш, затем пользовательские фичи.
Каждая фаза самодостаточна — после неё продукт в рабочем состоянии.

Статусы: ☐ не начато · ◐ в работе · ✅ готово

---

## Фаза 1 — Parsing как библиотека (диалог Parser ↔ API)

**Цель:** упразднить отдельный веб-сервис Swimm.Parser; парсинг вызывается из API напрямую.
Пайплайн админки: *загрузил PDF → распарсил → превью → импорт* — одним потоком, без ручного
скачивания JSON.

- ✅ 1.1. (2026-07-09) Class library `server/Swimm.Parsing` создана; перенесены `Parsers/*`
  (IsrOrg, IsrOrgAgeRecords, IsrOrgMastersRecords, WorldRecords), `Helpers`, `Models`,
  `ParserConstants` (git mv, namespace → `Swimm.Parsing.*`). Парсеры зарегистрированы в DI
  (`AddParsing()`); статический `ParserFactory` остался только для легаси Swimm.Parser.
- ✅ 1.2. (2026-07-09) `IResultSourceProvider` + DTO `ParsedCompetition`/`ResultSourceRequest`
  в Application; `PdfResultSourceProvider` в Swimm.Parsing. Совместимость с импортом — по
  построению: `ResultsJson` = тот же snake_case-массив, что принимает
  `IImportService.ImportAsync` / `IImportJobQueue.Enqueue`. Swimm.Parser стал тонкой
  обёрткой над библиотекой (живёт до 1.5); тесты переведены на Swimm.Parsing (62 зелёных).
- ✅ 1.3. (2026-07-09) Админка: страница Import получила режим «PDF» (переключатель
  JSON/PDF): upload файлов (primary + до 3 доп.) → `POST /api/admin/parse-pdf` → превью
  результата (счётчики, таблица соревнований, warnings, свёрнутый debug-лог) →
  `POST /api/admin/import-parsed` (превью в `IMemoryCache` по `previewId`, TTL 15 мин) →
  существующая фоновая очередь `IImportJobQueue`. Категории/привязка к событию переиспользуют
  контролы JSON-режима.
- ✅ 1.4. (2026-07-09) Регрессия: `dotnet test` — 62 зелёных. Смоук end-to-end: поднят API,
  распарсен реальный протокол (Maccabiah, IsrOrg) через новый UI-пайплайн — 851 результат
  (198+380+273 по трём дням), сверено с уже импортированным состоянием БД — совпадает точно.
- ✅ 1.5. (2026-07-09) Проект `Swimm.Parser` удалён из solution и репозитория целиком
  (Program.cs, контроллеры, wwwroot); заодно снесён осиротевший статический `ParserFactory`
  (последний потребитель ушёл вместе с Swimm.Parser). JSON-импорт в админке остался как
  легаси-режим.

**Критерий приёмки:** новый протокол попадает в БД целиком из админки, руками файл не
переносится; solution собирается без Swimm.Parser. **Фаза 1 завершена.**

## Фаза 2 — Рекорды и нормативы в БД

**Цель:** убрать `client/public/data/normative*.js` (~17k строк) — рекорды и нормативы живут
в БД, редактируются в админке, приходят с сервера.

- ✅ 2.1. (2026-07-09) Схема — **три независимые оси** вместо плоского scope (решение
  пользователя: система должна расширяться на любые страны/континенты/категории):
  территория (`RegionType`: world/continent/country + `RegionCode`: ''/EU/ISR/…) →
  категория (`Category`: open/age/junior/masters + `AgeKey`) → дисциплина
  (Gender/PoolType/Style/Distance). `NormativeStandards` — kind (regular/masters) +
  `Country` (система нормативов, сейчас RUS). Unique-индексы по всем осям (NOT NULL
  пустые строки вместо nullable). Миграция `AddRecordsAndNormativeStandards` применена,
  гранты swimm_ro/rw в setup-roles.sql и на живой БД.
- ✅ 2.2. (2026-07-09) Сидер `IRecordsSeeder` / `dotnet run -- --seed-records <dir> [--force]`
  (без --force отказывается перезаписывать непустые таблицы). Залито и сверено с
  исходниками потрое: 166 open (WR+ISR) + 827 age + 726 masters рекордов; 630 regular +
  5880 masters нормативов. Иврит не искажён.
- ✅ 2.3. (2026-07-09) API: `GET /api/records?region=&category=` (region обязателен —
  кэш и выборки по регионам) и `GET /api/normative-standards?kind=` через
  `IRecordRepository` (SwimmReadDbContext + ICacheService, TTL 24ч, ключи
  `records:{region}:{category}`). Смоук: world=83, ISR/age=827, 400 без region.
- ✅ 2.4. (2026-07-09, Sonnet) `records-helper.ts`: warmUp при старте страницы, синхронные
  геттеры отдают легаси-формы window.normative_* (fallback до/при недоступном API);
  `helper-normative.ts`, попап нормативов и age/masters-карточки переведены с прямого
  чтения `window.*`. Проверено в браузере, вкл. сценарий с выключенным API.
  Попутная находка: client — **multi-page Vite-сборка**, у results_main.html своя точка
  входа `src/pages/results-main-page.tsx` (client/CLAUDE.md обновлён).
- ✅ 2.5. (2026-07-09, Sonnet) Админ-CRUD: `IRecordAdminRepository` → `RecordsAdminController`
  → `Pages/Admin/Records` (вкладки Records/Standards, фильтры, пагинация, inline-правка,
  валидация осей, конфликт unique-индекса → 400, каждая мутация → InvalidateAllAsync).
  Смоук сквозь стек: правка в админке → /api/records → попап клиента.
- ✅ 2.7. (2026-07-09) Статика снесена: 5 файлов `normative*.js` (~17k строк) и их
  script-теги удалены (`filter-data.js` остался — другие данные). Геттеры
  `records-helper.ts` получили третий уровень fallback (пустое дерево вместо undefined);
  холодный сценарий «API выключен + статики нет» проверен в браузере — потребители
  деградируют мягко (—/null/false/[]), без исключений.
  Бонус (кусок 3.1 вытащен вперёд): `/api/records` и `/api/normative-standards` отдают
  **ETag + Cache-Control: public, max-age=300**; сериализованный JSON кэшируется в
  ICacheService (та же токен-инвалидация из админ-CRUD), If-None-Match → 304.
  Проверено curl-ом; побочный плюс — при падении API браузер до 5 мин живёт на
  HTTP-кэше.
- ✅ 2.6. (2026-07-09, Sonnet; принято Fable 2026-07-10) `IRecordSourceProvider` + 3 провайдера
  в `Swimm.Parsing/RecordSources/` (WorldRecords — авто-фетч 4 XLSX с api.worldaquatics.com;
  IsrOrgAge/IsrOrgMasters — PDF isr.org.il по URL из настроек `RecordsImport:*` с обязательным
  файл-fallback; SSRF-whitelist доменов, URL от пользователя не принимается).
  `IRecordDiffService`/`RecordDiffService`: дифф по 8 осям, превью-сессия 10 мин, Apply
  в транзакции + `InvalidateAllAsync`; `RecordsImportController`
  (`/api/admin/records/fetch|apply|source-status`, Admin + antiforgery). Экран Import
  перепланирован: верхние табы «Соревнования»/«Рекорды», источники — карточки-кнопки вместо
  `<select>`, на карточках рекордов «Обновлено: max(UpdatedAt)», превью-дифф перед Apply.
  **`Record.UpdatedAt`** + миграция `AddRecordUpdatedAt` (бэкофилл через `EXTRACT(YEAR…)` —
  точное сравнение timestamptz с DateTime-дефолтом не срабатывает из-за сдвига таймзоны),
  `updated_at` в API, на клиенте «updated dd/MM/yyyy» в карточках age/masters и попапе
  нормативов — проверено вживую. Уточнение против задания: парсеры IsrOrg*Records едят PDF
  (PdfPig), а не HTML. Доводка на приёмке (Fable): HttpClient в провайдерах получил
  User-Agent — без него api.worldaquatics.com не отвечает вовсе (висит до таймаута);
  живость URL и формат XLSX проверены curl-ом. Интерактивный Fetch→дифф→Apply из админки —
  за Владом (нужен админ-логин).

**Критерий приёмки:** `client/public/data/normative*.js` удалены; рекорд можно поправить в
админке, и он виден на сайте без деплоя клиента.

## Фаза 3 — Масштаб и кэш (готовность к миллионам записей)

**Цель:** сервер отдаёт быстро и дёшево; клиент не выкачивает датасеты целиком.

- ✅ 3.1. (2026-07-09) HTTP-кэш (ETag + Cache-Control) на публичных GET. Паттерн `CachedJson`
  из `RecordsController` (2.7) вынесен в `CachedJsonExtensions` (extension-метод на
  `ControllerBase`, `server/Swimm.API/Http/`) и переиспользован в `RecordsController` (без
  изменения поведения — тот же ключи/TTL) и на новых эндпоинтах: `/api/competitions`
  (5 мин / max-age=60), `/api/categories[/{key}]` и `/api/club-points` (1ч / max-age=300),
  `/api/athletes/career` и `/api/results/filter-hints` (5 мин / max-age=60). 400/404-ветки
  остаются до хелпера (не кэшируются). Инвалидация — существующая глобальная
  `ICacheService.InvalidateAllAsync()` из админ-CRUD/импорта, отдельно ничего не добавлял.
  `/api/results` (paged) осознанно не тронут — своя пагинация/кэш в репозитории.
  Проверено curl-ом: заголовки, byte-for-byte тело, 304 на повторный If-None-Match.
- ✅ 3.2. (2026-07-09) Серверная фильтрация на клиенте. Контракт —
  `docs/tasks/phase3-paged-results-contract.md`.
  **Сервер:** `/api/results` дополнен `birthYearFrom/To`, `ageGroup`, `position` (top/podium),
  `eventDate`, `total` в ответе (отдельный кэш-ключ на COUNT), лимит глубины
  `page*pageSize ≤ 10000` (иначе 400); публичный `GET /api/results/filter-hints`. Смоук на
  живом API + 67 зелёных тестов; предикаты на синтетике 3 млн едут на индексе 3.3.
  **Клиент:** `loadFromApi` в `filter-data-source-ddl.tsx` — paged-ветка делает один fetch
  страницы вместо `while hasMore`; `buildResultsFilterParams` (`utils/helpers/results-api.ts`)
  маппит `state.filterSelected` → query по контракту §2. Redux: `resultsPaging` (`store.ts`) —
  `{page, pageSize, total, hasMore}`. Смена фильтра/источника → рефетч страницы 1
  (`lastPagedFetchKeyRef` в DataSourceDDL защищает от задвоения с собственным
  дозагрузка-эффектом). «Show more» в `results-table.tsx` аппендит страницу+1, счётчик
  «showing N of total» — total из `resultsPaging`, не из длины массива. `level_filter`,
  `is_recalculated`, `activity_type` скрыты в paged (§5 контракта) — их серверные аналоги в 3.4.
  Опции style/distance/club/name в paged — из `/api/results/filter-hints`
  (`useFilterHints`, debounce 300мс на текстовых полях; `enabled` гейтит фетч, чтобы full-режим
  не дёргал hints вхолостую). Побочный фикс: `hasSource` (`results-main-project.tsx`,
  `results-table.tsx`) считался по `results.length` — в paged первая страница с дефолтными
  фильтрами законно может быть пустой, что прятало весь UI фильтров; переведено на `title`.
  Проверено вживую на SYNTH-соревновании (5000 строк): один запрос страницы, «showing 100 of
  995», Show more догружает и меняет счётчик, level/recalculate/activity_type не рендерятся;
  full-режим (`?loadMode=full` и без параметра) — прежние N запросов по 500 без query-фильтров,
  regression не найден.
  **Приёмка (Fable, 2026-07-09): принято с доводками.** (1) Серверный `position=top` теперь
  оставляет строки без места (DSQ/DNS) — зеркало клиентского фильтра, иначе ломался паритет
  full/paged; (2) `/api/competitions` отдаёт `day_dates`, `filter-event-date` в paged берёт
  опции дней оттуда (страница, сортированная по дате DESC, покрывает один день события);
  (3) смена источника сбрасывает `event_date` — устранён баг «день Maccabiah фильтрует SYNTH
  в ноль» (в full-режиме тот же сток-фильтр молча опустошал таблицу); (4) починен генератор
  синтетики: некоррелированный `LATERAL (SELECT random())` вычислялся один раз на INSERT и
  все 3 млн строк получали Distance='100' — рандомы перенесены во вложенный FROM, данные
  перегенерированы, EXPLAIN-профиль перепрогнан (paged-запросы ≤ 4 мс). Живая приёмка в
  браузере: 1 запрос страницы, Show more 100→200→300 of 328, паритет full/paged (78/78 и
  328/328), день с 0 результатов не ломает UI. Известное ограничение: опции Age-фильтра
  в paged — из загруженной страницы (неполные), кандидат на hints/3.4.
- ✅ 3.3. (2026-07-09) Индексы под реальные запросы. Синтетика: `server/db/synthetic-results.sql`
  (3 млн строк: 600 соревнований × 5000, маркировка `SYNTH`, откат `synthetic-results-cleanup.sql`);
  EXPLAIN-профиль 10 запросов — `server/db/explain-top-queries.sql`. Найден и закрыт один провал:
  фильтр по `CompetitionId`/`EventId` с сортировкой выдачи шёл по индексу даты с фильтрацией всей
  таблицы (335 мс) → композитный индекс `(CompetitionId, CompetitionDate DESC, Position)`
  миграцией `AddResultsCompetitionPagingIndex` (0.5 мс; заодно снят избыточный одиночный
  `IX_Results_CompetitionId`). Остальные запросы ≤ 40 мс. Известные тяжёлые, но закрытые кэшем:
  счётчики `GetSources` (~250 мс, TTL 5 мин) и `DISTINCT Distance` (~190 мс, TTL 10 мин) —
  кандидаты на серверные агрегаты в 3.4. Глубокий OFFSET растёт линейно (~37 мс на 10k) —
  в контракте 3.2 ограничена глубина страниц.
- ✅ 3.4. (2026-07-13) Агрегаты с клиента → на сервер + кэш. Была дыра: `HelperClub.getClubsSummary`
  считал очки/медали/пловцов по клубам на клиенте по полному датасету, поэтому в paged-режиме
  сводка по клубам просто отключалась (`filter-club.tsx`: `if (mode === 'paged') return`). Вынес
  на сервер `GET /api/club-summary?competitionId=|eventId=|country=` — переиспользует серверный
  `ClubPointsScoring` (из сезонного зачёта 8.5), считает per-club {points, gold/silver/bronze,
  swimmerCount, successfulCount} за `CachedJson` (TTL 2 мин, ETag). Паритет с клиентом: ключ клуба
  club→relay_team_name→club_en, эстафета ×2 очка, timeFail не гейтит. Клиент: хук `useClubSummary`
  + paged-ветка `filter-club` рендерит богатый `UI_ClubDetails` (поиск фильтрует локально). Фильтр
  результатов вынесен в переиспользуемый `BuildFilteredQueryAsync` (единая точка для paged и
  агрегата). Тесты 181 (было 178, +3). Проверено вживую на SYNTH Maccabiah: 40 клубов с медалями/
  очками, совпало с API. `GetSources`/`DISTINCT Distance` (~250/190 мс) оставлены за кэшем —
  внутри бюджета, отдельная оптимизация без повода не нужна.
- ✅ 3.5. Нагрузочный smoke: k6-профиль «список + 3 фильтра + карточка спортсмена»
  (`server/loadtest/paged-smoke.js`, детали — `server/loadtest/README.md`). Прогон 2026-07-13
  на Release-сборке, локальная БД с синтетикой ~3.01 млн строк: p95 list=7.65 мс,
  filtered=6.6 мс, athlete=6.41 мс — все ниже бюджета 300 мс с большим запасом,
  `http_req_failed`=0%. Механика порогов проверена вручную (временный p(95)<1 → падение,
  код выхода 99).
  - **Строгий бенчмарк по всей таблице + фикс обрыва** (`server/loadtest/full-scan-smoke.js`,
    2026-07-14): профиль вскрыл обрыв — несоскоупленные запросы (произвольные фильтры) под 20 VU
    давали **p95 ~14 с** на `global-filtered`. Корень: фильтр стиля по `Style.Name` через JOIN
    не давал планировщику взять уже существующие композитные индексы (LIMIT 34→4мс, COUNT 86→4мс
    по EXPLAIN). **Фикс (код, без миграции):** `ResultRepository` резолвит `styleName→StyleId`
    (кэш, Styles=8 строк) и фильтрует по `StyleId`. p95 14с→**5.5мс** (~2300×), пропускная
    4.4→451 итер/с (~100×). Теперь full-scan-smoke — green-gate по 300мс на всей 3М.
    Остаток (не блокер): `/api/athletes/career` — скан по имени без индекса, холодный ~5с/p99,
    тёплый мс; кандидат на индекс, если понадобится.

**Критерий приёмки:** страница результатов открывается без деградации на 3М строк — и
соскоуплено по источнику (6мс), и на несоскоупленном пути с фильтрами под конкуренцией (p95 5.5мс
после фикса StyleId); повторные запросы идут из кэша. **Фаза 3 завершена.**

## Фаза 4 — Логин на клиенте (Google + email/пароль)

**Цель:** пользователь логинится из React-приложения. Бэкенд полностью готов
(auth-фазы 0–4 закрыты) — работа только клиентская + SMTP.

- ✅ 4.1. `useAuth`-хук (обобщение логики `/auth/me` из `useFavorites`) + user-меню в шапке:
  аватар/имя, Sign out, Sign out everywhere (`POST /auth/logout-all`).
- ✅ 4.2. Экран/модал логина: кнопка Google (`/auth/login/google?returnUrl=`) + форма
  email/пароль (`POST /auth/login/local`), обработка 403 pre-verify и 429 lockout.
- ✅ 4.3. Регистрация + «забыл пароль» на клиенте (`/auth/register`, `/auth/forgot-password`);
  verify/reset остаются серверными HTML-страницами (уже есть).
- ✅ 4.4. Прод-`IEmailSender`: `SmtpEmailSender` (встроенный SmtpClient, STARTTLS) —
  включается конфигом `Email:Smtp:*` (env/user-secrets), без него остаётся `LoggingEmailSender`.
- ☐ 4.5. Favorites-UI показывает CTA «залогинься» вместо скрытия функций.

План/детали шагов: `docs/tasks/client-login-plan.md`.

**Критерий приёмки:** полный цикл register → verify → login → favorites → logout-all
проходит из UI клиента без curl.

## Фаза 5 — Пользовательские галереи (внешние ссылки)

> Паттерн «галерея = только ссылки» уже обкатан на группах (фаза 8.10, `Sys_HubGroupMedia`):
> лайтбокс `UI_SwimmerGallery` с контролируемым режимом, `HelperMedia` (youtube/vimeo/превью),
> тип `HubGroupMediaItem`, source_type `album` = внешняя ссылка. Здесь переиспользовать.

**Цель:** залогиненный юзер ведёт свои фото/видео (только ссылки YouTube/Vimeo/URL-картинки,
без аплоада) на трёх уровнях: пловец / соревнование / заплыв. Таблица `Sys_UserMedia` уже
создана (favorites Phase 1), эндпоинты закрыты — открываем осознанно.

- ☐ 5.1. CRUD `/api/user-media` (SwimmDbContext, antiforgery, rate-limit на мутации,
  scoped по userId — IDOR-тесты как в favorites). Валидация URL: allowlist хостов
  (youtube/youtu.be/vimeo + прямые https-картинки), максимум N элементов на пользователя.
- ☐ 5.2. UI «моя галерея» в карточке пловца/заплыва: добавление ссылки, приватность
  (private по умолчанию), удаление. Рендер видео — только sandbox-iframe embed по allowlist.
- ☐ 5.3. Публичный показ public-медиа у заплыва/пловца (отдельный агрегированный запрос,
  не в горячем пути результатов; кэш не применять — персональное/модерируемое).
- ☐ 5.4. Модерация: админ-страница «UserMedia» — просмотр/скрытие/удаление, «стереть всё по
  пловцу» (рычаг `SwimmerId`), «стереть всё по юзеру».

**Критерий приёмки:** юзер добавил YouTube-ссылку к заплыву → видит её сам; после
переключения в public её видят все; админ может скрыть.

## Фаза 6 — Автозабор данных с isr.org.il

**Цель:** новые соревнования обнаруживаются и затягиваются с
https://isr.org.il/competitions.asp полуавтоматически (человек подтверждает, машина делает).

- ✅ 6.1. (2026-07-15) `ICompetitionDiscoveryProvider` + `IsrOrgDiscoveryProvider`
  (`Swimm.Parsing/Discovery`): список competitions.asp (regex, UTF-8, диапазоны дат
  «19-20.6.2026»), детальная comp.asp (площадка, loglig-id). Вежливый троттлинг 2с,
  User-Agent, whitelist доменов. **Важно: PDF-протоколов на isr.org.il больше нет** —
  результаты в iframe loglig.com; PDF добывается экспортом
  `loglig.com/Leagues/ExportSwimmingCompetitionResults?competitionId=<logligId>` и
  парсится существующим IsrOrgParser (проверено вживую, language=he обязателен).
- ✅ 6.2. (2026-07-15) Админ-«входящие»: `Sys_DiscoveredCompetitions` (миграция
  AddDiscoveredCompetitions) + страница `/Admin/Discovery` + `DiscoveryAdminController`
  (`/api/admin/discovery`): sync, матчинг «уже импортировано» по дате+нормализованному
  имени, «Затянуть» → PDF → превью парсера → импорт через очередь (категории, событие
  для многодневных), ручное скачивание PDF, скрытие строк.
- ✅ 6.3. (2026-07-15) `CompetitionDiscoveryBackgroundService`: настройки
  `DiscoveryEnabled` (выкл. по умолчанию) / `DiscoveryIntervalHours`, перечитываются
  на лету. Плюс разовый прогон `dotnet run -- --discovery-sync`.
- ✅ 6.4. (2026-07-15) Устойчивость: снапшоты HTML/PDF на диск (конфиг
  `Discovery:SnapshotDir`), 0 распознанных строк / не-PDF от loglig / отсутствие
  loglig-iframe → явные ошибки в админке (LastError у строки, 502 у sync).
  Тесты — на снапшотах живого HTML в `Swimm.Tests/Fixtures/Discovery` (сеть в тестах
  запрещена).

**Критерий приёмки:** новое соревнование на isr.org.il появляется во «входящих» без участия
человека; импорт — в два клика.

## Фаза 7 — Админка: максимальное удобство ввода

**Цель:** любые данные правятся быстро, безопасно и без SQL. (Каркас уже хороший:
Razor + Tailwind, Competitions/Categories CRUD, фоновый импорт.)

- ☐ 7.1. Универсальные паттерны для всех CRUD: inline-edit в таблицах, поиск+фильтры,
  сортировка, пагинация — вынести в разделяемые partial/JS-хелперы, чтобы новый CRUD
  собирался из готовых блоков.
- ☐ 7.2. CRUD для оставшихся сущностей: Swimmers (merge дублей!), Clubs, Styles,
  Records (из фазы 2), точечное редактирование Result (исправить опечатку времени/имени).
- ☐ 7.3. Массовые операции: перенос результатов между соревнованиями, переименование клуба
  с каскадом, пересчёт агрегатов после ручных правок (инвалидация кэша автоматом).
- ☐ 7.4. Безопасность правок: подтверждение разрушающих действий, ImportHistory-стиль
  аудит для ручных мутаций (кто/что/когда), «dry-run» превью у массовых операций.
- ☐ 7.5. Дашборд: счётчики записей, последние импорты, здоровье БД/кэша, битые ссылки
  UserMedia.

**Критерий приёмки:** типовые операции («поправить время», «слить двух пловцов»,
«переименовать клуб») делаются из UI за <1 минуту без риска.

## Фаза 8 — HubGroups: тренировочные группы

Неформальные объединения пловцов из разных клубов (пример: DolphinNetanyaMasters) с публичной
страницей-агрегатом. Дизайн и спека: `docs/tasks/hubgroups-phase1-2-sonnet.md`.
**Модель ролей/участия (зафиксированные решения) — `docs/hubgroups-architecture.md`.**

- ☑ 8.1. Схема: `HubGroup`/`HubGroupMember` (бизнес-таблицы, grant `swimm_ro`), миграция
  `AddHubGroups`; настройки `HubGroupCreationPolicy`/`HubGroupMaxPerUser`/`HubGroupVisibility`
  с enum-валидацией.
- ☑ 8.2. Админ-CRUD `/Admin/HubGroups`: форма группы (slug-автогенерация, ссылки, привязка
  к клубу), участники с ролями member/captain/coach и поиском по справочнику.
- ☑ 8.3. Публичный API: `GET /api/hub-groups` (ETag-кэш, ключ включает Visibility),
  `GET /api/hub-groups/{slug}` — инфо + участники + 25 последних заплывов + «рекорды группы»
  (лучшее время по оси стиль+дистанция+бассейн+пол, без эстафет/DSQ); инвалидация кэша
  из админ-CRUD.
- ☑ 8.4. Клиент `groups.html` (+ пункт Groups в хедере): список карточек, страница группы;
  виртуальная группа «Моё избранное» (`/api/hub-groups/favorites`, `[Authorize]`) — тот же
  экран поверх `Sys_UserFavorites`.
- ☑ 8.5. Внутренний зачёт группы: сезонный рейтинг участников по клубным очкам
  (`ClubPointsRule`/`Entry`, правило по дате+scope; DSQ/эстафеты не в счёте), сезон
  с 1 сентября (`season_label`), поле `standings` в `HubGroupDetailsDto`, карточка
  **Season standings** на странице группы. Работает и для favorites. «Ближайшие старты»
  отложены до фазы 6 (будущих соревнований в БД нет).
- ☑ 8.6. Самообслуживание: enforcement `CreationPolicy=coach/any` + лимит `HubGroupMaxPerUser`
  по владельцу; пользовательский API `[Authorize]` (создать/править/удалить свою группу,
  участники, со-тренеры) поверх общей логики с `HubGroupAdminService`; со-тренеры
  (`Sys_HubGroupManagers`, права проверяются в одном хелпере); приватность per-group
  (владелец/со-тренер видят свою скрытую группу через rw-контекст); клиентская панель
  «Моя группа». Заявки на вступление вынесены из скоупа.
- ☑ 8.7. Официальные клубные группы: `HubGroup.IsOfficial` (одна на клуб), заявки
  `Sys_HubGroupClubRequests`, админ-одобрение (транзакция: official+ClubId+роль Coach+email),
  бейдж «Official Group of {Club}». Термин «со-тренер» упразднён → **админы группы**
  (`Sys_HubGroupAdmins`, рефактор из `Sys_HubGroupManagers`).
- ☑ 8.8. Участники-аккаунты: `Sys_HubGroupUserMembers` (приватно), самозапись в публичные
  группы (`Status='active'`), добавление владельцем/админом по email; списки «владею/админю»
  и «участвую» раздельно.
- ☑ 8.9. Локальные пловцы и приватные тренировки: `Swimmer.Origin='isr'|'local'` (фантом =
  обычный Swimmer), тренировки в `Sys_TrainingSessions`/`Sys_TrainingResults` (НЕ Results);
  единый хаб `results_main.html?group=<slug>&tab=trainings|competitions` (доступ к тренировкам
  решает сервер: владелец/админ/участник-аккаунт).
- ☑ 8.10. Шапка хаба (вариант 1) + галерея группы: богатая шапка из `HubGroupDetails`
  (иконка, official-бейдж, links-чипы, счётчик) с табами Overview/Members/Records (ссылки на
  groups.html) и Competitions/🔒Trainings (локальный toggle; Activity-фильтр в group-режиме
  убран — дублировал toggle); `Sys_HubGroupMedia` — медиа-ссылки (image/video/album,
  `TrainingId` null = публичная галерея на groups.html, иначе приватные фото тренировки),
  `POST/DELETE /api/hub-groups/{id}/media` (CanEdit+antiforgery+инвалидация кэша), MediaEditor
  в панели «Мои группы», медиа-полоска и «📷 N» в виде по сессиям. Прошло /code-review
  (8 находок исправлено). См. `docs/hubgroups-architecture.md` §8.

**Критерий приёмки (8.1–8.10, выполнено):** группа создаётся в админке и пользователем по правам,
публичная страница показывает состав/рекорды/заплывы/сезонный зачёт/галерею, избранное работает
как личная группа, админы группы управляют ею в рамках прав, тренировки и их фото приватны.

## Фаза 9 — Поддержка стран (мультирегиональность)

**Цель:** страна — рабочая ось везде, где есть данные; «израильскость» вынесена в
конфигурируемые швы, деплой под другую страну = правка констант, не кода.
**Решение (ARCHITECTURE.md, 2026-07-13):** коды стран — alpha-3 World Aquatics (`ISR`) в
данных/БД/API; ISO alpha-2 (`il`) — только флагам (единственная конвертация в `flag-icon.tsx`).

- ✅ 9.1. (2026-07-13) Флаги: `UI_FlagEmoji` принимает alpha-3 и alpha-2, конвертация таблицей
  `ALPHA3_TO_ALPHA2` (фикс бага: `ISR` резался в `is` = флаг Исландии).
- ✅ 9.2. (2026-07-13) Импорт PDF: страна и язык протокола выбираются в DDL на Admin/Import
  (override по шву poolType до `IsrOrgParser.ResolveCountry/ResolveLanguage`), легаси-конвенция
  имени файла `*_ISR_he.pdf` — fallback; общий каталог `Pages/Admin/Shared/CountryCatalog`.
- ✅ 9.3. (2026-07-13) Рекорды: шов `HOME_REGION`/`HOME_REGION_LABEL`
  (`client/src/utils/constants/home-region.ts`) — вся «израильскость» рекордов оттуда; ключ
  дерева `ISR`→`NR` (национальный, не страна); GUID страны для NR-фетча worldaquatics —
  конфиг `RecordsImport:WorldAquaticsNationalCountryId`.
- ✅ 9.4. (2026-07-13, Sonnet/delegate №1) Нормативы: фильтр `country` в
  `/api/normative-standards` + админ-листинге, страна в кэш-ключах; клиент запрашивает свою
  систему через `NORMATIVE_COUNTRY='RUS'` (≠ HOME_REGION) — готовит сосуществование нескольких
  наборов нормативов без взаимной перезаписи деревьев.
- ✅ 9.5. (2026-07-13) HubGroups: страна группы — `HubGroup.CountryId` FK (миграция
  `AddHubGroupCountry`), `ApplyCountryAsync` find-or-create в админ- и пользовательском CRUD,
  флаг на карточке списка / странице группы / шапке group-режима.
- ✅ 9.6. (2026-07-13, Sonnet/delegate №2) Соревнования и результаты: фильтр `country` в
  `/api/competitions` (событие = любой ISR-день) и `/api/results`; витрина «Season · Israel»
  из `HOME_REGION_LABEL` + текущий год.
- ✅ 9.7. (2026-07-13) Данные-консистентность: слияние дубля Израиля `IL`(alpha-2)→`ISR`
  (миграция `MergeCountryIlIntoIsr` — 3186 пловцов/197 клубов/9656 результатов перецелены,
  дубль удалён); `Competition.Country` строка → FK на Countries (миграция
  `CompetitionCountryToFk` с бэкфиллом, как у Swimmer/Club/HubGroup). Countries теперь
  1-строчный (только ISR) — JOIN в результатах дешёвый.
- ☐ 9.8. `AppUser`: страна / настройка «мой регион» (какие рекорды и систему нормативов
  показывать лично) — продуктовая фича с UI профиля.
- ☐ 9.9. UI-переключатель региона рекордов — **заблокирован данными**: реальный набор рекордов
  только у `ISR` (1654) + `world` (101), у прочих стран по 1 стрелой записи. Ждёт
  мульти-региональных данных (напр. NR-фетч других стран через 9.3-конфиг).

**Критерий приёмки:** страна фильтруется в API соревнований/результатов/нормативов, у групп
есть страна и флаг, коды alpha-3 консистентны; деплой под другую страну = `HOME_REGION` +
`NORMATIVE_COUNTRY` + `WorldAquaticsNationalCountryId`. **Основное (9.1–9.7) выполнено;
9.8–9.9 — по продуктовому поводу / данным.**

---

## Хостинг — решение (2026-07-15, зафиксировано, не реализовано)

**Весь стек на Azure, без гибридов:**
- **App Service (Linux, план B1)** — Swimm.API, он же раздаёт прод-сборку клиента из
  `wwwroot` (`npm run build:azure`). Один origin — cookie-auth/antiforgery работают как
  в деве, CORS не открываем. Отдельный фронт-хостинг (Vercel/Static Web Apps) отвергнут:
  второй origin ломает cookie-модель (SameSite=None, CORS+credentials, OAuth-редиректы).
- **Azure Database for PostgreSQL Flexible Server (B1ms)** — роли
  `swimm`/`swimm_rw`/`swimm_ro` переносятся как есть. Регион БД = регион App Service
  (West Europe): кросс-регионные/кросс-провайдерные задержки убивают наш перф-бюджет.
  Гибриды (БД на Railway/Neon/Supabase) отвергнуты: экономия ~$15/мес не стоит
  cold start'ов и второго провайдера.
- **Email** — любой SMTP-провайдер (Resend/Postmark/…): `Email__Smtp__*` в конфигурации
  App Service; код уже готов (фаза 4.4).
- Секреты — App Service Configuration/Key Vault; прод-redirect URI в Google OAuth Console;
  `ASPNETCORE_ENVIRONMENT=Production`; миграции — явный шаг (`--migrate`), не автостарт.
- Ориентир по цене: ~$30+/мес. Альтернатива «весь Railway» (~$5–15/мес) осознанно
  отклонена в пользу managed-БД с бэкапами и запаса на рост.

Реализация (создание ресурсов, CI/CD) — отдельной задачей, пока НЕ делаем.

## Порядок и зависимости

```
Фаза 1 (Parsing lib) ──► Фаза 2 (Records в БД) ──► Фаза 6 (isr.org.il)
Фаза 3 (кэш/масштаб) — независима, можно параллельно после 1
Фаза 4 (логин UI) ──► Фаза 5 (галереи)
Фаза 7 (админка) — сквозная, этапы добираются после фаз 2 и 6
Фаза 9 (страны) — сквозная; 9.1–9.7 сделаны, 9.8–9.9 ждут данных/продуктового повода
```

Рекомендуемая последовательность: **1 → 2 → 3 → 4 → 5 → 6 → 7**, при этом 7.1–7.2 можно
подтягивать сразу после фазы 2 (CRUD рекордов), а 3.1 (OutputCache) — дешёвый ранний выигрыш.
