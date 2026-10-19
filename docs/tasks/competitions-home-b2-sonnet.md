# Задание (Sonnet): Б2 — каркас календаря на `/competitions`

## Контекст

Шаг Б2 плана [`docs/plans/competitions-home-plan.md`](../plans/competitions-home-plan.md)
(часть Б). **Сначала прочитай целиком общую спецификацию
[`competitions-home-calendar-spec.md`](competitions-home-calendar-spec.md)** — контракт API,
раскладка файлов, правила вида, роли цветов, footguns. Здесь — только то, что делаешь в Б2.

Б2 = каркас: убрать старые секции страницы, загрузка данных, состояние и его синхронизация с
адресом, строка управления (‹ месяц ›, сегмент видов, чипы типов). Тела видов в Б2 —
**временные заглушки** (простой список), их заменят Б3–Б5.

## Решения (зафиксированы, не пересматривать)

1. **Убрать со страницы** (решение В1): массив `PAGES` и секцию плиток категорий
   («Competition pages»), `MeetsSection`, `UpcomingSection` — вместе с ставшими ненужными
   импортами. **Оставить**: топбар, шапку (надзаголовок `Season … · …`, `h1 Competitions`,
   подзаголовок — как есть), `FavoritesUpcomingSection`, `RecordTicker`, `UI_ModeToggle`.
2. **Порядок секций**: шапка → `<CompetitionCalendar />` → `FavoritesUpcomingSection` →
   (место под Records & standards — Б7, сейчас ничего) → `RecordTicker` → `UI_ModeToggle`.
3. Обёртка календаря — `<section aria-label="Competition calendar">` с отступами как у прежних
   секций: `px-4 pt-[26px] lg:px-16 lg:pt-12`.
4. **Строка управления (десктоп, ≥ lg)** — одна строка, `justify-between`, `flex-wrap`:
   - слева группа: кнопка ‹ (иконка chevron, цвет `--t-accent-text`, 32×32, без фона), подпись
     периода (`October 2026` / `Season 2026/27`; 22px, `font-extrabold`, `--t-text`,
     `min-width` ~190px, чтобы сегмент не прыгал при смене месяца), кнопка ›;
     затем сегмент видов `Full year · Full month · Meet days` (высота 38px, 15px, радиус 10,
     обводка 1px `--t-divider`, разделители между опциями 1px `--t-divider`; выбранный —
     `--deep-seg-active-bg`/`-fg`; прочие прозрачные `--t-text`);
   - справа чипы типов: `All · Pool swimming · Open water · Artistic swimming`, каждый: иконка
     16px + подпись 14px + число 12px `--t-text-2`; высота 34px, радиус 10, рамка 1px
     `--t-border`, фон `--t-surface`; выбранный — рамка `--t-accent` + фон `--t-accent-soft`;
     hover — рамка `--t-accent`. All выбран, когда фильтр пуст.
   - На ширине < lg в Б2 строка просто переносится (`flex-wrap`); мобильный вид (короткие
     подписи Year·Month·Meet, кнопка Type) — шаг Б6, не делай его сейчас.
5. **Иконки** — `calendar-icons.tsx`: маленькие компоненты-SVG (`viewBox="0 0 24 24"`,
   `stroke="currentColor"`, `fill="none"`, `strokeWidth 1.8`, `aria-hidden`), рисуешь сам по
   мотивам Phosphor: `IconCaretLeft`, `IconCaretRight`, `IconSquaresFour` (All — 4 квадрата),
   `IconSwim` (Pool — пловец/волна с головой), `IconWaves` (Open water — 2–3 волны),
   `IconLotus` (Artistic — цветок лотоса). Пропсы: `size?: number`, `className?: string`.
6. **Загрузка**: `useCompetitionCalendar(season: number)` → `{ data, loading, error }`.
   `fetch('/api/competitions/calendar?season=' + season, { credentials: 'same-origin' })`.
   Ответы кэшируются в модуле (`Map<number, CompetitionCalendar>`), повторный заход в сезон —
   без запроса. Ошибка — `console.error` + `error` в результате; календарь тогда показывает
   строку «Could not load the calendar.» (`--t-text-2`) вместо тела вида.
7. **Состояние** (`calendar-state.ts`): тип
   `CalendarView = 'year' | 'month' | 'meets'`, `MeetType = 'pool' | 'open_water' | 'artistic'`,
   состояние `{ view, month: { year, month }, selectedDay: string | null, typeFilter: MeetType[] }`.
   - `readCalendarQuery(search)` / `writeCalendarQuery(state)` — разбор и запись query
     (`view`, `month=YYYY-MM`, `day=YYYY-MM-DD`, `type=a,b`), умолчания не пишутся,
     `history.replaceState(null, '', url)` с сохранением прочих параметров и `hash`.
   - Начальный месяц: `?month=` если валиден, иначе месяц «сегодня» по Израилю.
   - Выбранный день — по правилу спецификации §4 («Выбранный день»): функция в
     `calendar-model.ts` `defaultSelectedDay(meets, month, today): string | null`; применяется
     при смене месяца и когда данные сезона пришли; `?day=` из адреса важнее, если он в месяце.
8. **Навигация ‹ ›**: Year — `season ± 1`, месяц переносится в тот же календарный месяц
   нового сезона (Oct 2026 → Oct 2025); Month/Meet — месяц ± 1. Границы — по `seasons` ответа
   (спецификация §4); пока данные текущего сезона не пришли — кнопки disabled.
9. **Временные тела видов** (заглушки, будут заменены):
   - Year: 12 строк `Sep 2026 — N meets` (сезон текущего месяца), клик по строке → вид Month
     этого месяца (это поведение остаётся и в Б3, сделай его в оркестраторе через колбэк
     `onPickMonth(month)`);
   - Month: список дней месяца с meets (`24 Oct — name, name`), клик → `selectedDay`; ниже
     строка `Selected: 2026-10-24 · N meets` или «No meets on this day.»; месяц без meets —
     «No meets this month»;
   - Meet days: список meets месяца (`start — name`), месяц без meets — «No meets this month».
   Заглушки — простыми `div`/`button` в `competition-calendar.tsx`, без стилей сверх
   читаемости (`--t-text`, 14px).

## Что уже готово (не переделывать)

- API `GET /api/competitions/calendar` (спецификация §2), сервер не трогать.
- `client/src/pages/competitions-page.tsx` — точка входа, не трогать.
- `client/src/projects/competitions-project/competitions.tsx` — страница (правишь её).
- `seasonStartYear`, `seasonLabel` — `client/src/utils/helpers/season-helper.ts`.
- `routes` — `client/src/utils/routes.ts`.
- Классы темы на корне страницы уже есть (`useDeepThemeClass()`).

## Шаги

1. `calendar/calendar-types.ts` — типы ответа (`CompetitionCalendar`, `CalendarMeet`,
   `CalendarMeetDay`, `CalendarGroup`, `MeetStatus`, `MeetKind`, `MeetType`) — один в один с
   §2 спецификации (snake_case полей как в JSON).
2. `calendar/calendar-model.ts` — чистые функции (экспорт по одной, с JSDoc по-русски):
   - `israelToday(): string` — ISO-дата Asia/Jerusalem;
   - `monthKey({year, month}): string` (`2026-10`), `parseMonthKey(s): {year, month} | null`;
   - `addMonths(m, n)`, `seasonOfMonth(m): number` (через `seasonStartYear` от 1-го числа),
     `seasonMonths(season): {year, month}[]` (Sep→Aug, 12 штук);
   - `monthLabel(m)` → `October 2026`, `seasonTitle(season)` → `Season 2026/27`;
   - `isDayInMonth(iso, m)`, `meetInMonth(meet, m)`, `meetOnDay(meet, iso)`;
   - `applyTypeFilter(meets, filter)`;
   - `typeCounts(meets): Record<MeetType | 'all', number>` (вызывающий передаёт meets области);
   - `defaultSelectedDay(meets, m, today)` — правило §4;
   - `navBounds(seasons): { min: {year,month}, max: {year,month} } | null`.
3. `calendar/use-competition-calendar.ts` — хук загрузки (решение 6).
4. `calendar/calendar-state.ts` — типы состояния, `readCalendarQuery`, `writeCalendarQuery`.
5. `calendar/calendar-icons.tsx` — иконки (решение 5).
6. `calendar/calendar-controls.tsx` — `CalendarControls` (пропсы: `title`, `onPrev`, `onNext`,
   `prevDisabled`, `nextDisabled`, `view`, `onView`, `disabledViews: CalendarView[]`,
   `typeFilter`, `onTypeFilter`, `counts`). Сегмент и чипы — по решению 4 и спецификации §4
   (radiogroup, aria, Enter/Space, focus-visible).
7. `calendar/competition-calendar.tsx` — оркестратор: состояние, загрузка сезона месяца
   (и для Year — тот же сезон), вычисление области/счётчиков/disabled видов, запись query на
   каждое изменение, строка управления + временное тело.
8. `calendar/competition-calendar.css` — пока только `:focus-visible`-правила, если их удобнее
   задать классом (`.cc-focus:focus-visible { outline: 2px solid var(--t-accent); outline-offset: 2px; }`);
   подключить импортом в `competition-calendar.tsx`.
9. `competitions.tsx` — решения 1–3.
10. `docs/ui-components.md` §6 — строка: «Сегмент-контрол с обводкой `--t-divider` и чипы-фильтры
    календаря — своя вёрстка в `competitions-project/calendar/calendar-controls.tsx`: `.deep-seg`
    — пилюля без обводки и разделителей, макет календаря требует radiogroup с разделителями».

## Тесты

Клиентского тестового раннера в проекте нет — тестов не пишем. Вместо них: `typecheck`
чистый + проверка в браузере (ниже). Функции `calendar-model.ts` держи чистыми — их проверит
ревьюер.

## Проверка

1. `npm --prefix client run typecheck` — без ошибок.
2. Если Release-сборки API ещё нет: `dotnet build server/Swimm.sln -c Release`.
3. Поднять стенд `stack-5181-api5079-personas` (preview_start по имени), открыть
   `http://localhost:5181/competitions`:
   - старых плиток и секций Meets/Upcoming нет; шапка как была;
   - по умолчанию Full month, октябрь 2026; в заглушке — 09 Oct и три meets; выбран 9 Oct
     (сегодня 19 Oct, meets после сегодня нет → последний день с meets до сегодня);
   - ‹ листает в сентябрь, август 2026 (это сезон 2025/26 — подгружается), дальше назад до
     сентября 2024; › — до августа 2027; на границах кнопка неактивна;
   - Full year: `Season 2026/27`, 12 строк; ‹ → `Season 2025/26`; клик по строке → Month;
   - чипы считают: в октябре 2026 All 3 / Pool 3 / Open water 0 / Artistic 0; в апреле 2026
     есть Open water (эйлатский старт 25–27 апреля); мульти-выбор, All сбрасывает;
   - адрес меняется (`?view=year`, `?month=2026-04&type=open_water`…), перезагрузка
     восстанавливает вид; `/competitions` без параметров — умолчания;
   - светлая и тёмная тема (тоггл внизу страницы) — сегмент и чипы читаются;
   - консоль без ошибок.
4. Остановить стенд после проверки (preview_stop), чтобы не держать порты.

## Footguns

- См. спецификацию §6 (порт :5078 и Debug-сборка — чужие, не трогать; не коммитить).
- `new Date('2026-10-09')` — это UTC-полночь, а `getDate()` отдаёт локальное число: в поясе
  западнее UTC получишь 8-е. Для дат ответа — строки или `Date.UTC` + `getUTC*`.
- `history.replaceState` на каждый рендер не зови — только в эффекте на изменение состояния.
- Хук загрузки: при быстрой смене сезона не дай устаревшему ответу перетереть свежий
  (флаг отмены в эффекте, как в старом `MeetsSection`).

## Вне скоупа (не делать)

- Настоящие виды Year/Month/Meet days, куб, бейдж группы (Б3–Б5).
- Мобильная строка управления, неделя, лента дней, анимация Type (Б6).
- Records & standards (Б7). Сервер, маршруты, `routes.ts`, топбар.

## Отчёт

В конце: список созданных/изменённых файлов, что проверено в браузере (с какой темой и
шириной), раздел **«Отклонения»** — всё, что сделано не так, как написано здесь, и почему.
