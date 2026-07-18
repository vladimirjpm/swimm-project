# CLAUDE.md — client (фронтенд)

Гайд по фронтенду. Подгружается автоматически при работе в `client/`.
Здесь только то, чего не видно из дерева файлов: стек, конвенции, точки входа, footguns.
Глобальные правила и backend — в корневом [`../CLAUDE.md`](../CLAUDE.md).

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
- Инициализацию, нужную каждой странице (напр. `RecordsHelper.warmUp()`), добавляй в точку
  входа конкретной страницы — правка только `index.tsx` до `results_main.html` не доедет.

## Карта `src/`

- `projects/` — **фичи-экраны** (`results-table`, `training-table`, `home-project`,
  `sportsmen-details`, …). Каждая фича = папка.
- `projects/components/` — переиспользуемые блоки фич (filter-section, popup, data-source-ddl).
- `projects/components/mix/` — **атомарные ячейки/иконки**. Конвенция: одна папка = один мелкий
  компонент (`club-icon/`, `date-icon/`, `medal-icon/` …), экспортится как `UI_*`. Их ~25 и
  они однотипны — не описывай каждую отдельно.
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
Точечные API-вызовы уже есть (напр. избранное в [`hooks/useFavorites.ts`](src/hooks/useFavorites.ts)).

## Правило парных токенов (темизация)

Текст на тем-зависимой поверхности красится ТОЛЬКО парным токеном той же поверхности,
никаких фиксированных hex: фон `--theme-primary` → текст `--theme-mode-accent-text`
(вторичная строка — тот же токен с opacity), фон `--theme-topbar-bg` → `--theme-topbar-text`,
фон `--theme-mode-surface*` → `--theme-mode-text*`. Меняешь/добавляешь оверрайд токена в
одном режиме — проверь его ПАРУ в обоих режимах (light и dark, `data-mode` на `<html>`),
контраст ≥ 4.5:1. Прецедент: dark-оверрайд `--theme-mode-accent-text` был тёмным при
неизменном `--theme-primary` → нечитаемая шапка селектора (исправлено 2026-07-18).

## Footguns

- Поле в store опечатано как `debigConfig` — не «чини» вслепую, на него могут быть ссылки.
- `data-helper2.js` и часть legacy — JS среди TS; типов нет, проверяй сигнатуры по месту.
- `build:azure` дополнительно вырезает видео — для прод-сборки под Azure используй именно его.
