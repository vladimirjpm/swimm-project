# Задание (Sonnet): многодневные события — карьера атлета + живой список на странице Competitions

Контекст: многодневные соревнования в БД — это `CompetitionEvent` (родитель) + `Competition`
(дни). Сервер (`/api/competitions`) и селектор клиента (`filter-data-source-ddl.tsx`) уже
сворачивают дни в одну запись (`kind: 'event'`). Осталось два места, где дни всё ещё
считаются/показываются как отдельные соревнования:

1. **Карьера атлета**: `AthleteCareerDto.Competitions` считает `Distinct()` по
   `CompetitionId` — трёхдневная Maccabiah даёт «3 competitions» вместо 1.
2. **Публичная страница `competitions.html`**: показывает только 5 статических
   категорийных карточек, живого списка соревнований (с группировкой событий) нет вообще.

ВАЖНО: не запускай других агентов — делай всё сам.

## Решения (зафиксированы, не пересматривать)

- Соревнование в карьере = событие, если день привязан к `EventId`; иначе сам день.
  Ключ дедупликации: `EventId != null ? $"e{EventId}" : $"c{CompetitionId}"`.
- На странице Competitions статические категорийные карточки ОСТАЮТСЯ (сверху, как есть);
  под ними добавляется новая секция **«Meets»** с живым списком из `/api/competitions`.
- В секции Meets показываем: все `live`/`upcoming` + завершённые за текущий и предыдущий
  календарный месяц (по дате начала). Остальное не показываем (архив есть в селекторе
  results_main) — никакой пагинации.
- Ссылка карточки: `./results_main.html?eventId=<id>` для `kind === 'event'`,
  `./results_main.html?competitionId=<id>` для одиночных.
- Общие хелперы дат/типа выносим в новый модуль
  `client/src/utils/helpers/competition-source.ts`, селектор переводим на него
  (убрать локальные дубли), поведение селектора не менять.

## Что уже готово (не переделывать)

- `GET /api/competitions` уже отдаёт события свёрнуто: элементы
  `{ kind: 'event'|'competition', id, name, date, date_end, pool_type, category,
  categories, status, day_count, result_count, day_dates }` — тип `CompetitionSource`
  и хелперы `parseDate`/`dateLabel`/`monthLabel` живут локально в
  `client/src/projects/components/filter-data-source-ddl/filter-data-source-ddl.tsx`
  (строки ~66–124). Их и выносишь в `competition-source.ts`.
- Карьера: `GET /api/athletes/career?name=` → `ResultRepository.GetAthleteCareerAsync`
  (`server/Swimm.Infrastructure/Repositories/ResultRepository.cs`, ~строка 532).
  Клиент (`sportsmen-details.tsx`) просто показывает `career.competitions` — его не трогать.
- Страница: `client/competitions.html` → `src/pages/competitions-page.tsx` →
  `src/projects/competitions-project/competitions.tsx` (стили home-стека: `hp-card-std`,
  палитра `#7dd3fc`/`#f3f8fd`, см. существующие карточки — новую секцию делай в том же
  визуальном языке).

## Шаги

### 1. Сервер — счёт соревнований карьеры по событиям

В `GetAthleteCareerAsync` (ResultRepository.cs):
- в обе проекции (`rows`, ~строка 549, и `relayCandidates`, ~строка 582) добавить
  `EventId = (int?)r.Competition.EventId`;
- `Competitions = rows.Select(…).Concat(relayMedals.Select(…))` считать по ключу из
  раздела «Решения», `Distinct().Count()`.
Больше в DTO ничего не менять.

### 2. Клиент — общий модуль

`client/src/utils/helpers/competition-source.ts`: экспортировать тип `CompetitionSource`
и функции `parseDate`, `dateLabel`, `monthLabel`, `sourceUrlParam` (перенос 1-в-1 из
`filter-data-source-ddl.tsx`, вместе с комментариями). Селектор импортирует их оттуда;
никаких изменений поведения/вёрстки селектора.

### 3. Клиент — секция Meets на competitions.tsx

- `fetch('/api/competitions', { credentials: 'same-origin' })` в `useEffect`
  (образец загрузки — тот же селектор). Ошибка/пусто → секцию не рендерим.
- Фильтр: `status !== 'done'` ИЛИ дата начала ≥ 1-е число предыдущего месяца.
- Порядок: live → upcoming → done (внутри групп по дате начала desc; /api/competitions
  уже отсортирован по дате desc — достаточно стабильной сортировки по статусу).
- Карточка: название (dir="rtl", как в селекторе), даты через `dateLabel`
  (у событий это диапазон «1–3 Jul»), `day_count > 1` → бейдж «N days»,
  `pool_type`, статус: live → зелёный «● LIVE» (стиль как у существующей карточки
  Latest meet), upcoming → «starts …». Вся карточка — ссылка (`<a>`, см. «Решения»).
- Заголовок секции — в стиле заголовков страницы («Meets», caps-label как
  `{year} Season · …`).

### 4. Тесты (обязательно, xUnit в Swimm.Tests)

По образцу существующих repo-тестов на InMemory `SwimmDbContext`:
- событие с 2 днями + результаты пловца в обоих днях → `Competitions == 1`;
- 2 дня события + 1 одиночное соревнование → `Competitions == 2`;
- эстафетная медаль в дне того же события не добавляет отдельного соревнования;
- одиночные без EventId — как раньше (регрессия).

## Проверка

- `dotnet build server/Swimm.sln` (0 ошибок), `dotnet test server/Swimm.Tests` (все зелёные).
- `npx tsc --noEmit` в client/ (через `npm --prefix client exec tsc -- --noEmit`).
- Вживую: API поднять, открыть `http://localhost:5173/competitions.html` (Vite dev,
  прокси `/api` уже настроен) — секция Meets показывает список, многодневные одной
  карточкой с диапазоном дат; клик ведёт на results_main с загруженным событием.
  Карточка атлета (участника многодневного) показывает уменьшенный счётчик competitions.

## Footguns

- Если :5078 занят Visual Studio (MSB3027 build-lock) — собирай `--configuration Release`,
  API на :5079, клиент конфигурацией `client-5079` из `.claude/launch.json`
  (vite-прокси через `SWIMM_API_TARGET`).
- `GetAthleteCareerAsync` кэширует ответ (`athlete-career:*`, in-memory) — перед живой
  проверкой перезапусти API.
- В локальной БД ~3М синтетических результатов (`Note='SYNTH'`) — не удивляйся объёмам
  и ничего не чисти.
- Redux в клиенте — один rootSlice (`store.ts`); для competitions.tsx store НЕ нужен,
  обычный `useState`.
- Не добавляй новые npm-зависимости.

## Вне скоупа (не делать)

- Селектор results_main: вёрстку/логику не менять (только импорт хелперов из нового модуля).
- Архив/пагинация/поиск/фильтры категорий в секции Meets.
- Медальные разбивки карьеры (Note у медалей) — оставить как есть.
- Любые миграции БД.
