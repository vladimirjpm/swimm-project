# Задание: таб Media + Add media в шапке соревнования

## Контекст

Строится шапка соревнования с табами на results_main.html
(`docs/design-handoff-competition-header.md`; дизайн-прототип
`!design_handoff/design_handoff_competition_overview/Competition Overview.dc.html` —
открой в браузере, README там же). Каркас готов и работает:
`client/src/projects/results-main-project/components/competition-header/`
(header-top, tabs, overview, clubs) + врезка в `results-main-project.tsx`
(`compTab`, `?tab=` в URL). Таб Media сейчас — заглушка «Media — coming soon», счётчик
таба `mediaCount` прокинут как `null`, кнопки Add media в hero нет (слот `onAddMedia`
в `competition-header-top.tsx` готов: кнопка рендерится, только если колбэк передан).

## Решения (зафиксированы, не пересматривать)

- **Таб Media** — грид «кубиков» 16:9 (превью + play-триангл для видео), клик →
  лайтбокс `UI_SwimmerGallery` (существующий компонент — найди его использование
  в results-table/my-media и повтори). Десктоп ≥lg — 4 колонки, <lg — 2.
- Источник данных — хук `useCompetitionMedia(sourceParams)` (`client/src/hooks/useCompetitionMedia.ts`),
  он УЖЕ возвращает `{ byResultId, items, refresh }`; `items` — плоский список
  всего видимого медиа, включая competition-level (`result_id: null`). Сервер уже
  отдаёт competition-level медиа — ничего серверного не делать.
- **Счётчик таба Media** = `items.length` (0 → бейдж не показывается, таб остаётся).
- **Add media в hero**: только залогиненному (`useAuth().isAuthenticated`).
  Открывает существующий `AddLinkModal`
  (`client/src/projects/my-media-project/components/add-link-modal.tsx`) в
  single-step режиме: `fixedCompetitionId` = `sourceParams.competitionId`
  (число), а для события (`eventId`) — `competition_id` ПЕРВОГО дня из
  `overview.days[0].competition_id`. `contextLabel` — название соревнования.
  `swimmers` для модала и `initialSwimmerId` — как в `my-media.tsx` (блок
  `addCompTarget`, строки ~635-649): список пловцов юзера берётся из
  `useAllMyMedia` / `GET /api/me/media` — грузи ЛЕНИВО (только по клику на кнопку),
  не при маунте шапки. `onSave` → `addUserMedia(input)` из `use-all-my-media`,
  после успеха — `refresh()` медиа-хука.
- Превью youtube/vimeo — через существующий `HelperMedia`
  (`client/src/utils/helpers/helper-media.ts`), не изобретать.
- Темизация: только тем-токены (`--theme-mode-surface`, `--theme-mode-border`,
  `--theme-mode-text*`, карточные радиусы 12/вложенные 7-8) — по образцу соседних
  компонентов competition-header. Весь UI-текст — English.

## Что уже готово (не переделывать)

- `competition-header-top.tsx` — слот `onAddMedia`; `competition-tabs.tsx` — бейдж
  `mediaCount`; `results-main-project.tsx` — ветка `compTab === 'media'` (заглушка),
  `compSourceParams`, `compOverview`.
- `useCompetitionMedia` расширен (items), сервер включает competition-level медиа.
- `AddLinkModal`, `useAllMyMedia`/`addUserMedia`, `UI_SwimmerGallery`, `HelperMedia`.
- Справочник подсистемы: `docs/media-page.md` (обязательно прочитай §5 и §9-footguns —
  антифорж-токен, rate-limit).

## Шаги

1. Новый `competition-media.tsx` в `components/competition-header/` — контент таба:
   грид кубиков из `items`, подпись под кубиком (для result-level можно без подписи в v1),
   пустое состояние «No media yet» + кнопка «＋ Add the first video / photo» (тот же
   Add-флоу, только залогиненному; гостю — текст «Sign in to add media»).
2. Оркестрация Add media: логика (лениво получить пловцов, открыть AddLinkModal,
   onSave → addUserMedia → refresh) — либо внутри `competition-media.tsx` с экспортом
   хука/компонента, либо отдельным небольшим модулем в той же папке; из
   `results-main-project.tsx` прокинь `onAddMedia` в `CompetitionHeader` и замени
   заглушку таба media на новый компонент.
3. `mediaCount` в `CompetitionHeader` — из `useCompetitionMedia(compSourceParams).items.length`
   (вызов хука в `results-main-project.tsx`; учти, что results-table внутри тоже зовёт
   этот хук — это ок, кэша у него нет намеренно, per-viewer).
4. `npx tsc --noEmit` в client/ — чисто.

## Проверка

- API уже может быть запущен на :5078 (Vite proxy настроен). Если порт занят чужим
  процессом — конфиг `client-5079`+`swimm-api-5079` из `.claude/launch.json`.
- Открой `results_main.html?competitionId=last&tab=media` — грид/пустое состояние
  рендерится, консоль чистая. Залогиниться в дев-среде можно через Login (Google) —
  если логина нет, проверь гостевой рендер и опиши в отчёте, что залогиненный флоу
  проверен только типами.

## Footguns

- Build-lock: сервер НЕ пересобирай (серверных правок нет). Если всё же нужно —
  только `--configuration Release`.
- Антифорж-токен кэшируется в каждом хуке отдельно (`media-page.md §9`) — используй
  готовый `addUserMedia`, он всё делает сам.
- `sourceParams.competitionId` может быть строкой `"last"` до резолва — Add media
  показывай только когда есть числовой id (та же проверка `isNum`, что в хуке).
- В `results-main-project.tsx` не трогай group-ветку и Swims-ветку.

## Вне скоупа (не делать)

- Персональная полоса, filter=my|favorites, drill-down клубов, Records.
- Серверные изменения, миграции.
- Пиксельная доводка hero/Overview.
