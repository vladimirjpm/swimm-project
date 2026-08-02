# Архитектура Swimm — целевое состояние

Документ для людей и AI-агентов: как устроена система сейчас, куда она движется и через какие
швы (интерфейсы) проходят все будущие изменения. План работ по фазам — в [ROADMAP.md](ROADMAP.md).
Операционные инструкции (build/run/migrate) — в корневом [CLAUDE.md](../CLAUDE.md).

---

## 1. Общая картина

```
                    ┌─────────────────────── источники данных ───────────────────────┐
                    │  PDF-протоколы (isr.org.il)   сайт isr.org.il/competitions.asp  │
                    │  JSON-файлы (легаси)          будущие федерации/форматы         │
                    └────────────┬─────────────────────────┬─────────────────────────┘
                                 │ IResultSourceProvider   │ ICompetitionDiscoveryProvider
                                 ▼                         ▼
   ┌──────────────────────────── Swimm.API (один деплой) ────────────────────────────┐
   │                                                                                 │
   │  Swimm.Parsing (class lib) ──► IImportService ──► PostgreSQL                    │
   │       парсеры PDF/HTML            импорт+превью      два DbContext:             │
   │                                                      SwimmDbContext (rw/owner)  │
   │  Admin (Razor Pages) ────────► Application ◄──────── SwimmReadDbContext (ro)    │
   │       CRUD, импорт, превью        интерфейсы                                    │
   │                                                                                 │
   │  REST API (/api/*) ◄── ICacheService (memory → Redis) + HTTP-кэш (ETag/OutputCache)
   └────────────────────────────────────▲────────────────────────────────────────────┘
                                        │ fetch /api/*, /auth/*
                          React SPA (client/) — Vite, Redux Toolkit, Tailwind v4
```

Принципиальные решения (зафиксированы, не пересматривать без причины):

- **Один деплой-процесс.** Админка живёт в том же ASP.NET-приложении, что и API; изоляция —
  через least-privilege роли Postgres (`swimm` owner / `swimm_rw` / `swimm_ro`), а не через
  разделение процессов. Swimm.Parser как отдельный веб-сервис **упраздняется** — логика
  парсинга переезжает в class library `Swimm.Parsing`, вызываемую из API (решение 2026-07-09).
- **Источники данных — за абстракциями.** Всё, что добывает данные извне (PDF, HTML-скрейпинг,
  JSON), реализует провайдер-интерфейсы (см. §4). Смена/добавление источника = новый класс +
  регистрация в DI, ноль изменений в импорте и админке.
- **Медиа пользователей — только внешние ссылки** (YouTube/Vimeo/URL картинки). Бинарники не
  хостим, `IMediaStorage` не нужен (решение 2026-07-09).
- **Коды стран — alpha-3 World Aquatics (`ISR`, `GER`, `NED`…) везде в данных**: БД
  (`Countries.CountryCode`, `Competition.Country`, `Record.RegionCode`/`HolderCountry`,
  `NormativeStandard.Country`), импорт результатов и рекордов, API. Импорт рекордов с
  api.worldaquatics.com отдаёт именно эти коды — храним как есть, без конвертации.
  ISO alpha-2 (`il`, `de`…) существует **только для отрисовки флагов** (flagcdn понимает
  только alpha-2): единственная точка конвертации — таблица `ALPHA3_TO_ALPHA2` в
  `client/src/projects/components/mix/flag-icon/flag-icon.tsx`. Не менять данные на alpha-2
  и не резать alpha-3 до двух букв (`ISR`.slice(0,2) = `is` = флаг Исландии) — новые
  потребители флагов обязаны идти через `UI_FlagEmoji` (решение 2026-07-13).
- **Хостинг пока не выбран** — ничего деплой-специфичного в коде; всё внешнее (кэш, почта)
  за интерфейсами с локальными реализациями по умолчанию.

## 2. Слои сервера (Clean Architecture — уже соблюдается, поддерживать)

| Проект | Роль | Правило зависимостей |
|---|---|---|
| `Swimm.Domain` | Сущности, ноль зависимостей | ни от кого |
| `Swimm.Application` | Интерфейсы (`Abstractions/`), DTO, маппинги | только Domain |
| `Swimm.Infrastructure` | EF Core, репозитории, реализации сервисов | Application + Domain |
| `Swimm.Parsing` *(новый, из Swimm.Parser)* | Парсеры PDF/HTML, чистые функции «байты → DTO» | Application (DTO) + Domain |
| `Swimm.API` | Контроллеры, Razor-админка, DI-композиция, auth | все выше, инжектит только интерфейсы Application |
| `Swimm.Tests` | xUnit | что тестирует |

Контроллеры и Razor-страницы **никогда** не берут `Swimm.Infrastructure`-типы напрямую —
только интерфейсы из `Swimm.Application.Abstractions`. Новая возможность = интерфейс в
Application → реализация в Infrastructure → регистрация в `DependencyInjection.cs`.

## 3. Данные и БД

- **Два DbContext** (не забывать `--context SwimmDbContext` в EF-командах):
  `SwimmReadDbContext` (роль `swimm_ro`, NoTracking) — только публичное чтение через
  `ResultRepository` и будущий `RecordRepository`; `SwimmDbContext` (роль `swimm_rw`) — всё,
  что пишет или трогает `Sys_*`.
- **Новая публичная таблица ⇒ обязательный грант** `swimm_ro` в `server/db/setup-roles.sql`
  (fail-closed: забыл грант — публичный путь падает, это намеренно).
- **Рекорды живут в БД** (таблицы `Records`/`NormativeStandards`, фаза 2). Модель `Records` —
  три независимые оси, расширяемые на любые страны/континенты/категории без изменения схемы:
  территория (`RegionType` world/continent/country + `RegionCode` ''/EU/ISR/…) → категория
  (`Category` open/age/junior/masters + `AgeKey`) → дисциплина (Gender/PoolType/Style/Distance).
  Страна держателя (`HolderCountry`) ≠ территория рекорда. API и кэш режутся по регионам:
  `GET /api/records?region=…&category=…`, ключ кэша `records:{region}:{category}`.
  `NormativeStandards`: kind (regular/masters) + `Country` — система нормативов (сейчас RUS),
  другие системы лягут рядом. Легаси `client/public/data/normative*.js` удаляются на 2.7.
- **Масштаб «миллионы результатов»:** ключевые индексы по `(CompetitionId)`,
  `(SwimmerId)`, `(StyleId, Distance, Gender, PoolType)`; пагинация — только keyset или
  LIMIT/OFFSET с потолком pageSize=500 (уже есть); никаких `SELECT *` без фильтра.

## 4. Швы (интерфейсы) — через них проходят все будущие изменения

| Интерфейс | Сейчас | Потом |
|---|---|---|
| `ICacheService` | `MemoryCacheService` (токен-инвалидация всего кэша при импорте) | `RedisCacheService` при горизонтальном масштабировании — только замена регистрации |
| `IResultSourceProvider` *(новый)* | `PdfResultSourceProvider` (обёртка над парсерами IsrOrg) | `IsrOrgWebSourceProvider` — скрейпинг isr.org.il; другие федерации |
| `ICompetitionDiscoveryProvider` *(новый)* | — | обнаружение новых соревнований на isr.org.il/competitions.asp → «входящие» в админке |
| `IRecordSourceProvider` *(новый)* | импорт из существующих JS/JSON + парсеры `IsrOrgAgeRecords`/`IsrOrgMastersRecords`/`WorldRecords` | автообновление рекордов из веба |
| `IImportService` | `JsonImportService` | принимает `ParsedCompetition` DTO от любого провайдера, а не только JSON-файл |
| `IEmailSender` | `LoggingEmailSender` (dev) | SMTP/Resend в проде |
| `IDataCheck` *(новый, фаза Д3)* | проверки данных живут россыпью: `DataQualityService`, `SuspectResultDetector`, `SwimmerDedupService`, `RecordQualityService` | единый реестр проверок с severity, историей и одной страницей `/Admin/Health`; существующие сервисы — адаптеры. См. [data-integrity.md](data-integrity.md) |

Контракт всех провайдеров: возвращают **общий DTO** (`ParsedCompetition` / `ParsedRecordSet`
в Application), никогда не пишут в БД сами. Пишет только `IImportService` — единая точка
валидации, дедупликации и инвалидации кэша.

## 5. Кэширование (три уровня, от дешёвого к дорогому)

1. **HTTP-уровень** — `OutputCache`/ETag на публичных GET (`/api/results`, `/api/records`,
   `/api/competitions`, `/api/categories`, `/api/club-points`). Результаты завершённого
   соревнования **иммутабельны** — им длинный TTL + ETag; список соревнований — короткий TTL.
2. **Приложение** — `ICacheService` для собранных ответов репозиториев. Ключ =
   нормализованный фильтр. Инвалидация — `InvalidateAllAsync()` после любого импорта/CRUD
   (уже реализовано токеном).
3. **БД** — индексы + `SwimmReadDbContext` NoTracking. Только этот уровень платит за промах.

Правило: авторизованные и персональные данные (`/auth/*`, favorites, user media) —
**никогда** не кэшируются на уровнях 1–2.

## 6. Клиент

- Роутера нет; экраны — через Redux (`store.ts`, единый `rootSlice`). Точка входа результатов —
  `results_main.html?category=&competitionId=`.
- Единственный шов выбора данных — `filter-data-source-ddl.tsx` (уже на `/api/*`).
- **Убрать `<script src="/data/normative*.js">`** — helpers (`helper-normative.ts`) переходят
  на `/api/records` + `/api/normative-standards` с кэшем по образцу `CategoryHelper`
  (fetch → localStorage/память → fallback). Глобальные `window.normative*` исчезают.
- Фильтрация при больших датасетах уезжает на сервер: клиент не выкачивает все страницы
  `/api/results`, а передаёт фильтры query-параметрами (бэкенд уже принимает их в
  `ResultFilter` — клиент просто не пользуется). Переключение — админ-настройка
  `ResultsLoadMode` (`full`/`paged` принудительно, `client` = выбор через `?loadMode=`),
  доставляется клиенту через `GET /api/client-config` → `ResultsLoadModeHelper`;
  `paged`-ветка наполняется в фазе 3.2.
- Auth-состояние: `/auth/me` (уже используется в `useFavorites`) → выделить в общий
  `useAuth`-хук при добавлении логин-UI.

## 7. Бэклог рефакторинга (технический долг, вне фаз — брать попутно)

1. `client/src/pages/*` — мёртвые маршруты без роутера: удалить или подключить осознанно.
2. Следы CRA (`reportWebVitals`, `react-app-env.d.ts`, `setupTests.ts`) — удалить.
3. `data-helper2.js` и прочий JS среди TS — типизировать при первом же касании.
4. Поле стора `debigConfig` (опечатка) — переименовать одним атомарным PR со всеми ссылками.
5. `loadFromApi` в `filter-data-source-ddl.tsx` качает все страницы в цикле — заменить на
   серверную фильтрацию (фаза 3).
6. Дублированные `using` в `Program.cs` (Swimm.API) — косметика.
7. `Gallery`/`GalleryItem` (импортные медиа заплыва) НЕ смешивать с `Sys_UserMedia`
   (пользовательские ссылки) — это разные вещи по владению и приватности.
8. ~~Соревнование сопоставляется с источником по названию~~ — закрыто фазой Д2
   (2026-08-03): при известном compID день ищется по дате внутри связанного события,
   резолв — в `CompetitionIdentity`. Матчинг по имени остался фоллбеком для источников
   без штампа. См. [data-integrity.md](data-integrity.md).

## 8. Конвенции для вайб-кодинга (чтобы агент делал правильно с первого раза)

- Новый публичный эндпоинт: интерфейс в `Application/Abstractions` → реализация-репозиторий в
  `Infrastructure/Repositories` (через `SwimmReadDbContext`, если только чтение) → контроллер →
  грант `swimm_ro` при новой таблице → кэш через `ICacheService`.
- Новый экран клиента: папка в `client/src/projects/<фича>/` по образцу `results-table/`
  (`feature.tsx` + `components/` + локальный `types.ts`).
- Комментарии — RU-проза, идентификаторы — EN (стиль репо).
- Всё security-чувствительное (auth, роли, antiforgery) — читать `auth`-раздел корневого
  CLAUDE.md; инварианты не ослаблять.
- Всё, что касается достоверности данных (новая проверка, предикат «дыры», правка импорта
  или парсера) — читать [data-integrity.md](data-integrity.md) и записывать решение туда.
  Два правила оттуда нарушаются чаще всего: предикат живёт в ОДНОМ месте (копия = будущий
  инцидент), и поля ключа upsert (`ResultMatcher.KeyOfPersisted`) менять нельзя — иначе
  следующий переимпорт наплодит дубликаты.
