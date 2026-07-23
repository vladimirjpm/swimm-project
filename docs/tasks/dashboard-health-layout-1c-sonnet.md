# Задание (Sonnet 5): дашборд «здоровье данных» — T2, вёрстка варианта 1c

Контекст: редизайн `/Admin` (план `docs/plans/admin-dashboard-health-2-plan.md`). T1 (бэкенд)
уже смержен: `GET /api/admin/dashboard/status` отдаёт 8 блоков (см. форму в
`server/Swimm.Application/Dtos/DashboardStatusDtos.cs`, JSON — camelCase). Твоя работа —
**только вёрстка**: воспроизвести выбранный вариант **1c (master–detail)** из дизайн-прототипа
в `Pages/Admin/Index.cshtml`.

**Дизайн-референс (читать обязательно, до кода):**
- `!design_handoff/design_handoff_data_health_dashboard/README.md` — спецификация: блоки,
  метрики, контракт deep-links, типографика, интеракции, токены.
- `!design_handoff/design_handoff_data_health_dashboard/dashboard-data-health.dc.html` —
  HTML-прототип; вариант 1c — верхний. Это референс, НЕ production-код: данные и href в нём —
  в классе Component в конце файла, оттуда бери структуру карточек и href'ы.

## Решения (зафиксированы, не пересматривать)

- Реализуется **только 1c**: строки-блоки слева (drag-reorder за ручку «⠿»), sticky-панель
  деталей справа (540px, top 24px). Варианты 1a/1b из прототипа НЕ переносить (включая
  их детальный оверлей — в 1c детали живут в правой панели).
- Заменяется **stats-grid + status-grid** текущего Index.cshtml. Вкладку **Users не трогать**
  (фильтры, таблица, панель деталей, весь её JS остаются как есть). Запрос `/api/admin/stats`
  больше не нужен — тоталы приходят в блоках status (`loadStats` удалить).
- Данные: **один** запрос `GET /api/admin/dashboard/status`. Имена полей — как в
  `DashboardStatusDtos.cs`, camelCase: `swimmers.total/originIsr/originLocal/synthetic/
  sureCandidates/unsureCandidates/orphans/noOrgId/noResults/loglig.{verified,suggested,rejected,unlinked}`,
  `clubs.total/pseudo/sureCandidates/unsureCandidates/noSwimmers/noCountry/clubRequestsPending`,
  `competitions.total/withResults/discoveryImported/discoveryNew/discoveryIgnored/discoveryErrors/noOrgCompId`,
  `results.total/timeFail/fkAnomalies/emptyRelays`,
  `recordSets[].{regionType,regionCode,count,lastUpdatedAt}`,
  `media.total/video/photo/broken/unchecked/moderationPending`,
  `usersGroups.usersTotal/active7d/deactivated/groupsTotal/groupsOfficial/joinRequestsPending`,
  `system.lastImportAt/lastImportApproved/lastMediaCheckAt/lastDiscoverySeenAt/auditActions7d`.
  Производные: Empty-соревнования = `total - withResults`; реальные клубы = `total - pseudo`.
- Href каждой метрики — строго по карте «Контракт ссылок» из README хендоффа (фильтры на
  целевых страницах делает T3 параллельно; неизвестный `filter` страница игнорирует — ссылки
  ставить уже сейчас).
- Порядок блоков: дефолт — как в README (Пловцы, Клубы, Соревнования, Результаты, Рекорды,
  Медиа, Пользователи и группы); системная строка — всегда внизу, вне reorder. Сохранение
  порядка: `localStorage` ключ `admin:dashboard:block-order` (массив ключей блоков). Блок,
  ставший первым после drag, автоматически выбирается (его детали открываются).
- Нулевые метрики → чипы «✓ Название · 0»: за флагом `const COLLAPSE_ZERO = true` в JS.
- Рекорды: карточка на каждый элемент `recordSets`. Заголовок набора: `regionType=='world'` →
  «World», `regionType=='country' && regionCode=='ISR'` → «Israel», иначе
  `${regionType}:${regionCode}`. «Устарело» — серым, если `lastUpdatedAt` старше
  `const RECORDS_STALE_MONTHS = 3`. Кнопка «Проверить апдейты» — **отрисовать, но disabled**
  с `title="Скоро (T4)"`; симуляцию прогресса из прототипа НЕ переносить (её делает T4).
- «＋ Кастом-карточка» — отрисовать пунктирный плейсхолдер как в прототипе, без функционала
  (клик — ничего).
- Цвета/токены — **только** Tailwind-токены темы из `server/Swimm.API/Styles/admin.css`
  (`admin-bg`, `admin-surface`, `admin-border`, `admin-accent`, …), не хардкод-hex (кроме
  цветов состояний значений, если для них нет токена — тогда завести utility в
  `@layer components` или использовать существующие admin-токены; сверься с `@theme`).
- Семантика цвета значения (из README): зелёный = 0 проблем, оранжевый = есть работа,
  красный = блокирующее (`discoveryErrors`, `fkAnomalies`, `media.broken`), серый =
  не проверялось/долг (`unchecked`, `noOrgCompId`, устаревшие рекорды), голубой = инфо
  (loglig, timeFail, тоталы).

## Что уже готово (не переделывать)

- API и DTO (T1). Ничего на сервере не менять — C#-код в этом таске не трогается вообще
  (кроме, при необходимости, ViewData["PageSubtitle"] в Index.cshtml).
- Существующий JS-рендер статус-карточек в `Index.cshtml` (`statusCard`, `loadStatus`) —
  заменяется твоим кодом 1c; Users-часть скрипта не трогать.
- Пайплайн CSS: `cd server/Swimm.API && npm run css:build` → `wwwroot/css/admin.min.css`
  (коммитится). `@source` уже сканирует `Pages/Admin/**/*.cshtml`.

## Шаги

1. Прочитать README хендоффа + прототип (класс Component в конце `dashboard-data-health.dc.html`).
2. `Pages/Admin/Index.cshtml`: заменить разметку stats/status-grid на каркас 1c
   (левая колонка строк-блоков + правая sticky-панель), подзаголовок страницы обновить
   («Здоровье данных»). Вкладка Users остаётся ниже, как сейчас.
3. JS в `@section Scripts`: fetch status → рендер строк-блоков (hero-число + inline-пилюли
   метрик), выбор строки, панель деталей (карточки метрик со ссылками, подстроки), drag-reorder
   (HTML5 draggable за «⠿», порядок в localStorage), чипы нулей, системная строка, тосты не
   нужны (ссылки настоящие). Стиль кода — как в существующем inline JS (ванильный, esc(),
   api()-хелпер).
4. `npm run css:build`, убедиться что новые классы попали в `admin.min.css`.
5. Обновить `docs/admin-pages/index.md` — описание новой структуры дашборда (1c, блоки,
   deep-links, где хранится порядок).

## Проверка

- `dotnet build server/Swimm.sln --configuration Release` — 0 ошибок (Razor компилируется).
- `dotnet test server/Swimm.Tests --configuration Release` — 602/602 (ничего не должно упасть).
- `npm run css:build` без ошибок; в diff `admin.min.css` появились новые классы.
- Открой страницу вживую: подними API (Release, :5079 если :5078 занят), залогинься не выйдет —
  поэтому минимум: страница `/Admin` отвечает 302→login (не 500), а JS-синтаксис проверь
  `node --check` на извлечённом скрипте ЛИБО внимательной вычиткой. Живой прогон в браузере
  сделает ревьюер (T5).

## Footguns

- Build-lock MSB3027: жив старый `dotnet run`/VS — убей `Swimm.API.exe` или работай в Release.
- `admin.min.css` НЕ править руками — только через `npm run css:build`; закоммитить результат.
- В прототипе цвета захардкожены hex'ами — в проекте им соответствуют токены `@theme` в
  `Styles/admin.css`; маппинг сверить по значениям (bg #14161a, surface #1c1f26, …).
- `Date` для «устарело» — `lastUpdatedAt` приходит ISO-строкой UTC; сравнение через
  `new Date()`, месяцы — приблизительно (30.4 дня * 3 — ок).
- Antiforgery не нужен — страница только читает GET.

## Вне скоупа (не делать)

- Варианты 1a/1b, детальный оверлей, тренды/снапшоты.
- Работающая кнопка «Проверить апдейты» и её эндпоинты — T4.
- Query-фильтры на целевых страницах — T3 (идёт параллельно, не трогай их файлы).
- Функционал кастом-карточек.
- Любые изменения C#/DTO/сервисов.
