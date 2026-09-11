# CLAUDE.md — client (фронтенд)

Гайд по фронтенду. Подгружается автоматически при работе в `client/`.
Здесь только то, чего не видно из дерева файлов: стек, конвенции, точки входа, footguns.
Глобальные правила и backend — в корневом [`../CLAUDE.md`](../CLAUDE.md).

**Доменные справочники (читай ПЕРВЫМ при багах этих подсистем — они и клиент, и сервер):**
- [`../docs/relays.md`](../docs/relays.md) — эстафеты (`is_relay`/`relay_swimmers`/
  `member_swimmer_ids`, членство через `RelayMembers`, чек-лист отладки).
- [`../docs/media-page.md`](../docs/media-page.md) — My media / медиа (`media.html`,
  Add link, `useUserMedia`/`useAllMyMedia`/`useCompetitionMedia`, публикации, footguns).
- [`../docs/ui-components.md`](../docs/ui-components.md) — **реестр UI-компонентов**: что
  уже есть, какие пропы, чего сознательно нет. Читай ПЕРЕД тем, как верстать новую ячейку.

## Стек

React 18 + TypeScript + **Vite** (не CRA, хотя следы CRA остались: `reportWebVitals`,
`react-app-env.d.ts`). State — **Redux Toolkit**. Стили — **Tailwind v4** (через
`@tailwindcss/vite`). Дропдауны — `react-select`.

```bash
npm --prefix client run dev        # Vite dev server
npm --prefix client run build      # prebuild авто-генерит club-icons manifest
```

## Точки входа и навигация

- **Multi-page Vite-сборка** (`vite.config.js` → `rollupOptions.input`): каждый html в корне
  client/ — отдельная страница со своей точкой входа: `index.html` → `src/index.tsx`,
  `results_main.html` → `src/pages/results-main-page.tsx`, `home/about/competitions/groups.html` →
  соответствующие `src/pages/*-page.tsx`. **SPA-роутера нет** — «страницы» это отдельные html,
  а переключение экранов внутри страницы — через Redux-state.
- **Чистые URL (rewrite, не .html наружу).** Снаружи адреса выглядят как `/groups/{slug}`,
  `/groups/{slug}/results`, `/competitions/{id}`, `/swimmers/{id}`, `/results`, `/my-media` —
  физический html подставляет rewrite. Контракт в **одном месте** — [`src/utils/routes.ts`](src/utils/routes.ts):
  `routes.*()` генерят ссылки, `parseRoute()` достаёт идентичность из `location.pathname`.
  **Правило: в путь — только идентичность ресурса; вид (`tab`,`filter`,`club`,`swim`,`eventId`,
  `cat`,`season`) остаётся в query.** Три синхронных зеркала контракта — держи их вместе:
  `routes.ts` (клиент), `cleanUrlRewrite` в [`vite.config.js`](vite.config.js) (dev),
  rewrite-middleware в `server/Swimm.API/Program.cs` перед `UseStaticFiles` (прод).
  Читая идентичность в компоненте — бери `parseRoute()`, с легаси-фоллбеком на `?group=`/`?swimmer=`
  (старьё было до прода, редиректов нет). Новые ссылки строй ТОЛЬКО через `routes.*()`, не хардкодь `.html`.
- Инициализацию, нужную каждой странице (напр. `RecordsHelper.warmUp()`), добавляй в точку
  входа конкретной страницы — правка только `index.tsx` до `results_main.html` не доедет.
- **Dev-витрина `/ui-kit.html`** (`src/dev/ui-kit/`) — живой стенд: лесенка `content-box-*`,
  `PAGE_CONTAINER` и компоненты с переключаемыми пропсами, оси `data-mode`/`data-theme` и
  состояние в адресе. Её сознательно НЕТ в `rollupOptions.input`: так она есть в гите, но не
  в прод-сборке. Не добавляй её во входы и не ставь ссылку из топбара. Новый компонент на
  витрину = одна запись в `src/dev/ui-kit/registry.tsx`.

## Карта `src/`

- `projects/` — **фичи-экраны** (`results-table`, `training-table`, `home-project`,
  `sportsmen-details`, …). Каждая фича = папка.
- `projects/components/` — переиспользуемые блоки фич (filter-section, popup, data-source-ddl).
- `projects/components/mix/` — **атомарные ячейки/иконки**. Конвенция: одна папка = один мелкий
  компонент (`club-icon/`, `date-icon/`, `medal-icon/` …), экспортится как `UI_*`. Их ~30, и
  все перечислены в [`../docs/ui-components.md`](../docs/ui-components.md) — здесь не дублируем.
- `store/store.ts` — **весь Redux в одном файле**: единый `rootSlice` + единственный reducer
  `updateState` (мердж `Partial<StateInterface>`). Новых slice нет — добавляй поле в
  `StateInterface` + `initialState`. Хуки: `useAppDispatch`, `useAppSelector`.
- `utils/interfaces/` — все TS-типы домена (`results.ts`, `filter-selected.ts`, `enums.ts`).
- `utils/helpers/` — чистая бизнес-логика (расчёт времени, нормативов, очков клуба).
  Экспортируются классами-namespace (`Helper`, `HelperSwimmer`, …) через `index.ts`.
- `utils/constants/` — константы фильтров. `hooks/` — `useFavorites`, `useTheme`.

## Паттерн «фича» (эталон — `results-table/`)

1. Корневой `feature.tsx` — оркестратор: читает state через `useAppSelector`, тянет
   helpers, собирает данные.
2. `components/` фичи — презентационные части, включая **отдельные адаптивные варианты**
   (`*-mobile`, `*-desktop`, `*-2xl`) + локальный `types.ts`.
3. Соседний `feature.css` для того, что неудобно на Tailwind.

Делая новый экран — повторяй эту раскладку.

## Данные

Исторически статика в `public/data/{json,excel,config}`, картинки в `public/images/`. Выбранный
источник данных живёт в `state.dataSourceSelected` (`ResultWrap`).

**Идёт перевод данных со статики на серверный API** (бэкенд в `server/`). Уже на API:
- очки клубов — `club-points-helper.ts` → `GET /api/club-points` (вместо `config/club-points-config.json`).
- список/результаты соревнований — `filter-data-source-ddl.tsx` → `GET /api/competitions` + постранично
  `GET /api/results` (`sources-config*.json` удалён, был мёртвым кодом).
- name/badge категорий — `category-helper.ts` (`CategoryHelper`) → `GET /api/categories` (кэш + fallback,
  по образцу `ClubPointsHelper`). Сами канонические ключи категорий (`all`/`young8_11`/`junior`/`masters`,
  URL-контракт, темы оформления) остаются **client-only** в `results-categories.ts` — в БД их нет
  (`all` синтетический), маппинг канонический↔Category.Key живёт внутри `CategoryHelper`.

**Дев-связка с API:** в [`vite.config.js`](vite.config.js) есть proxy `/api`,`/auth` →
`http://localhost:5078`, поэтому относительные `fetch('/api/...')` работают как same-origin
(куки/antiforgery без CORS). Запусти API на :5078 (через Visual Studio или `dotnet run`) + `npm run dev`.
**Другой инстанс API** — переменная окружения `SWIMM_API_TARGET` (`vite.config.js` подставляет её
вместо `http://localhost:5078`): нужна, когда :5078 занят чужой сборкой и глушить её нельзя —
подними вторую на :5079 и запусти `SWIMM_API_TARGET=http://localhost:5079 npm run dev`
(готовая связка — конфиги `swimm-api-5079` / `client-5079` в `.claude/launch.json`).
Точечные API-вызовы уже есть (напр. избранное в [`hooks/useFavorites.ts`](src/hooks/useFavorites.ts)).

## Класс-маркер источника: `src-<имя файла>`

Каждый вызов `UI_SwimmStyleIcon` несёт в `className` маркер вида `src-results-table-desktop`,
`src-swim-row`, `src-h2h-event-card` — по имени файла, где он написан. Смысл один: открыл
inspect-element, увидел `src-*` — знаешь, какой файл править, без поиска по дереву.

**Префикс `src-` обязателен, голое имя файла брать нельзя.** У двух вызывающих (`swim-row`,
`training-show-full-table`) имя файла уже занято настоящим стилевым классом, и маркер без
префикса притащил бы иконке чужую вёрстку.

Маркер ничего не красит: в CSS правил `src-*` нет и быть не должно. Нужен стиль — заводи
отдельный класс. Добавляя новый вызов, ставь маркер сразу — иначе смысл «увидел класс →
нашёл файл» держится ровно до первого пропуска.

## Правило: сначала ищи компонент, потом верстай

**Перед тем как сверстать ячейку, строку, бейдж или плашку — проверь реестр
[`../docs/ui-components.md`](../docs/ui-components.md).** Там перечислены все `UI_*` из
`components/mix/`, крупные блоки (карусель сезонов, табы, карточка фильтра, полоса
выбранных фильтров) и отдельным разделом — то, чего сознательно нет.

Причина не в красоте: своя вёрстка молча теряет поведение, которое компонент уже несёт, —
пометку «время под вопросом», фоллбек эмблемы клуба через манифест, единый формат даты,
пояснение к раунду, направление RTL-строки. Расходится это не сразу, а через месяц на новом
экране, и чинится потом в пяти местах.

Порядок: нашёл компонент → бери его (почти у всех есть `className` / `*ClassName` для
перекраски). Не нашёл → проверь §6 реестра, вдруг отсутствие уже обдумано. Написал своё →
впиши строку в §6 с причиной. Завёл новый `UI_*` → впиши в §1–§4. **Обновление реестра —
часть работы, а не необязательный довесок.**

## Правило: время заплыва — только через `UI_SwimTime`

Время заплыва выводится **единственным способом** — компонентом
[`components/mix/swim-time`](src/projects/components/mix/swim-time/swim-time.tsx). Он же
рисует значок «время под вопросом» и объяснялку к нему. Причина та же, что у `routes.*()`:
признак качества должен быть виден везде, где показано время (а показано оно в дюжине мест),
и держать это перечислением мест невозможно — забудут в первом же новом экране.

Качества два, и они не взаимозаменяемы: `protocol` — ошибка самого протокола федерации
(`Results.SuspectReason`), `record` — спорная запись справочника рекордов (`Sys_RecordIssues`).
Тексты объяснений разные, оба на EN/RU/HE. Источник мы не правим — помечаем.

**Носитель пометки (строка/карточка) тоже оформляется из одного места** — хелпер
`swimFlaggedRowProps(quality)` из того же модуля отдаёт класс `swim-flagged-row`
(caution-лента слева, стили и токены `--theme-flag-*` — в `src/index.css`) и полный текст
в `title`/`aria-label`. Так оформлены строка таблицы результатов и карточка «Best times by
style»; чип «⚠ Under review» вместо голого значка включается пропом `qualityMarker="chip"`
у `UI_SwimmerTimeCell`. Отступ под ленту (`pl-*`) добавляет вызывающий — у каждой
поверхности свой базовый паддинг. Спека — `!design_handoff/design_handoff_competition_overview_rec-warning/RECORD-FLAG.md`
(карточка рекорда — вариант 13a, строка — гибрид 15d).

Инвариант на стороне API: **DTO, несущий время заплыва, несёт и признак качества**
(И11, см. `docs/data-integrity.md`). План перевода экранов —
[`docs/plans/swim-time-quality-everywhere-plan.md`](../docs/plans/swim-time-quality-everywhere-plan.md).

## Роли `--t-*`: вёрстка просит смысл, а не цвет

Страницы темы **deep** (`/my-media`, семья hp — `/home`, `/competitions`, `/groups`) красятся
не токенами палитры напрямую, а РОЛЯМИ: `--t-accent`, `--t-text` / `--t-text-2` / `--t-text-3`,
`--t-surface` / `--t-surface-strong` / `--t-card` / `--t-input-bg`, `--t-border`, `--t-scrim`,
`--t-warn`, `--t-danger`, `--t-live`, `--t-like`, `--t-list-head`, `--t-plate`. Карта ролей на палитру — **одна
на продукт**, в [`deep-theme.css`](src/projects/components/deep/deep-theme.css), объявлена на
тех же `.theme-deep` / `.theme-deep-light`: у кого есть класс темы — у того есть и роли.

Зачем прослойка: `#7dd3fc` в разметке отвечает на вопрос «какой цвет», а `--t-accent` — на
вопрос «зачем». Первое переизобретается на каждом экране (в семье hp одного этого хекса было
135 штук, и светлой темы у неё не было вовсе), второе переезжает вместе с темой.

Роль может быть ПУСТОЙ в одном режиме — это нормально и уже так сделано: `--t-list-head`
(подложка шапки списка) в тёмной теме `transparent`, потому что там карточка сама тёмное
стекло и полоса не нужна, а свечения темы (`--deep-wheel-glow`) в светлой равны `none`.
⚠ Оверрайд режима пиши ПОСЛЕ общего блока ролей: селекторы одной специфичности, решает
порядок в файле.

**Новый цвет — это новая РОЛЬ здесь, а не хекс по месту.** Исключения ровно два и оба
осознанные: бренд-цвета чужих сервисов (логотип Google) и `--t-plate` — светлая подложка под
иконки стилей, нарисованные чёрным силуэтом, одинаковая в обеих темах.

⚠ **Порталу класс темы нужен свой** (`useDeepThemeClass()` на его корне): он рендерится в
`body`, снаружи корня страницы, и там `--t-*` пустые. Так сделаны шторка фильтров, пилюля и
модалка логина.

⚠ **Градиент в произвольном значении Tailwind требует подсказки типа**:
`bg-[image:var(--t-accent-grad)]`. Без `image:` Tailwind считает значение ЦВЕТОМ фона, а
градиент в `background-color` невалиден — плашка выходит прозрачной (поймано 08.09.2026 на
логотипе SwimHub).

## Правило парных токенов (темизация)

Текст на тем-зависимой поверхности красится ТОЛЬКО парным токеном той же поверхности,
никаких фиксированных hex: фон `--theme-primary` → текст `--theme-mode-accent-text`
(вторичная строка — тот же токен с opacity), фон `--theme-topbar-bg` → `--theme-topbar-text`,
фон `--theme-mode-surface*` → `--theme-mode-text*`. Меняешь/добавляешь оверрайд токена в
одном режиме — проверь его ПАРУ в обоих режимах (light и dark, `data-mode` на `<html>`),
**Акцент бывает ДВУХ ролей, и их нельзя путать.** `--theme-mode-accent-text` — текст НА фоне
`--theme-primary` (шапки, активные табы). `--theme-mode-accent-on-surface` — наоборот, сам
акцент КАК ТЕКСТ на поверхности режима (значение выбранного фильтра, сводка селектора):
`--theme-primary` в dark сознательно не светлеет, потому что живёт фоном, и как текст на
тёмной поверхности даёт 2.5-2.6. Пишешь акцентом текст — бери второй токен.

контраст ≥ 4.5:1. Прецедент: dark-оверрайд `--theme-mode-accent-text` был тёмным при
неизменном `--theme-primary` → нечитаемая шапка селектора (исправлено 2026-07-18).

## Footguns

- **Сезон ≠ календарный год.** Сезон федерации идёт октябрь–август, поэтому старт 31/10/2025
  и старт 15/02/2026 — ОДИН сезон. Всё сезонное живёт в `utils/helpers/season-helper.ts`
  (зеркало серверного `SeasonMath`): `seasonStartYear` — какому сезону принадлежит дата,
  `ageInSeason` — возраст пловца (год окончания сезона − год рождения), `seasonLabel` — подпись
  «2025/26» (формат общий с серверным `SeasonMath.Label`), `recordStepAge` — ступень в справочнике рекордов (там ось календарная, это ЧУЖАЯ
  таблица). `getFullYear()` для сезона — баг: селектор соревнований так уводил октябрьские
  старты в отдельный «Season 2025». Подробности — `docs/season-boundary-rule.md`,
  `docs/data-integrity.md` §13.
- **Возраст в строке результата — `HelperResults.ageLabel(res)`, не `res.event_style_age`.**
  А ключ поиска в справочнике рекордов — `HelperResults.recordStepAge(res)`: он следует
  админ-настройке `RecordAgeAxis` из `GET /api/client-config` (`record-age-axis.ts`). Это
  РАЗНЫЕ числа: возраст пловца сезонный, ступень справочника при `calendar` — календарная.
  Разъедутся — бейдж «★ NEW RECORD» в строке начнёт спорить с карточкой «New records»,
  которую считает сервер.
- **Бейдж SB в строке — ТРЕТЬЕ число возраста.** Season best считается по СЕЗОННОЙ оси
  (`ageInSeason`), а не по календарной оси справочника рекордов: эталон приходит из
  `GET /api/season-best/table` (весь сезон одним ответом, кэш в `SeasonBestTable`), и его
  ступени построены сервером именно так. Там же — порог `peers >= 2` («первый среди одного»
  не достижение) и состав выборки: masters, открытая вода, эстафеты и `SuspectReason` в
  эталон не входят, поэтому такие строки бейджа не получают. Подробности —
  `docs/plans/h2h-page-plan.md` §8-секст.
- **«Сохранил → `location.reload()`» показывает старое, если ответ страницы с `max-age`.**
  Перезагрузка ревалидирует только документ, а `fetch` данных берёт ответ из кэша браузера,
  пока не истёк `max-age`; серверный сброс (`InvalidateAllAsync`) до браузера не дотягивается.
  Поэтому страницы, которые правят из их же таба Admin (группа, клуб), отдаются с
  `Cache-Control: public, no-cache` — браузер сверяет ETag перед каждым использованием (304
  без тела, если не менялось). Заводишь правку данных витрины, у которой `max-age`, — переводи
  её ответ на `no-cache` так же. Поймано 11.09.2026: «расписание не сохраняется», хотя в
  базе оно было.
- Поле в store опечатано как `debigConfig` — не «чини» вслепую, на него могут быть ссылки.
- `data-helper2.js` и часть legacy — JS среди TS; типов нет, проверяй сигнатуры по месту.
- `build:azure` дополнительно вырезает видео — для прод-сборки под Azure используй именно его.
