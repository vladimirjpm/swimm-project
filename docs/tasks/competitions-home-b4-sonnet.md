# Задание (Sonnet): Б4 — вид Full month (десктоп) и кубы выбранного дня

## Контекст

Шаг Б4 плана [`docs/plans/competitions-home-plan.md`](../plans/competitions-home-plan.md).
**Сначала прочитай общую спецификацию
[`competitions-home-calendar-spec.md`](competitions-home-calendar-spec.md).** Готово: каркас (Б2)
и Full year + бейдж группы + легенда (Б3) в `client/src/projects/competitions-project/calendar/`
— прочитай `competition-calendar.tsx`, `calendar-model.ts`, `group-badge.tsx`,
`calendar-legend.tsx` перед работой.

В Б4: сетка месяца вместо заглушки Month и под ней кубы выбранного дня. Куб делаешь в
**базовом виде** (`meet-cube.tsx`); полосу дней многодневки, 2 колонки, Cancelled/Postponed и
вид Meet days добавит Б5 — оставь для этого места (см. «Вне скоупа»).

Референс: `screenshots/01-light-month.png`, `02-dark-month.png` (левая часть — десктоп), README
§«Вид Full month», §«Куб соревнования», макет `competitions-home.dc.html` (функция `buildCal`
и `toCard` в скрипте внизу — там все размеры плашек и куба).

## Решения (зафиксированы, не пересматривать)

### Сетка месяца — `calendar/month-view.tsx`

`MonthView({ month, meets, today, selectedDay, onSelectDay })`, `meets` — meets месяца после
фильтра по типу.

- Шапка дней недели: 7 колонок `SUN MON TUE WED THU FRI SAT`, 11px, `uppercase`,
  `tracking-[.08em]`, `--t-text-2`, `padding 0 8px 6px`.
- Сетка: 7 колонок `minmax(0,1fr)`, `gap 4px`; недели с воскресенья; пустые клетки до 1-го и
  после последнего числа — прозрачные, без рамки, не кликаются. Чистая функция
  `monthGrid(month): (string | null)[][]` (недели × 7, ISO-даты или null) — в `calendar-model.ts`.
- **Ячейка дня** — `button` (класс фокуса Б2), `min-height 112px`, `padding 6px 7px`,
  `flex-col gap 4px`, `text-left`, радиус 8, фон `--t-surface`, тень `--t-shadow`;
  hover — фон `--t-nested`.
  - число дня — 12px, 500, `--t-text`;
  - **прошедший день** (дата < today) — `opacity .6`;
  - **сегодня** — `outline 1.5px solid var(--t-accent)`, `outline-offset -1.5px`, число 700
    `--t-accent-text`;
  - **выбранный** — фон `--t-nested` + `outline 2px solid var(--t-accent)`, `outline-offset -2px`
    (сильнее «сегодня»; сегодня и выбран — как выбранный, число как у сегодня);
  - `aria-label`: `Saturday, 24 October — 4 meets` / `— no meets`; `aria-pressed` = выбран.
- **Плашки meets** в ячейке: до 3, дальше строка `+N more` (11px, `--t-accent-text`).
  Порядок — как в ответе (дата начала, имя) — тогда продолжения многодневок идут первыми.
  - обычная: 11px, `padding 3px 6px`, фон `--t-nested`, текст `--t-text`, одна строка с `…`
    (`whitespace-nowrap overflow-hidden text-ellipsis`), `dir="rtl"` + `text-left` для имени;
  - чемпионат: фон `--t-champ-chip-bg`, текст `--t-champ-ink`, `padding 2px 6px 2px 2px`, слева
    `champMark` (Б3): бейдж → `GroupBadge size="mini"` (ширина 24 вместо 26 — добавь в бейдж
    размер `'cell'` = mini, но 24px) ; текстовая метка → только `IconTrophy` 11px;
  - **многодневка** (дат в `days` > 1): подпись в первый её день в ячейке — имя; в следующие —
    `↳ Day N`, где N — порядковый номер этой даты в `meet.days` (1-based), а НЕ разница дат
    (дни бывают не подряд). Скругление и «перелив»: левый край скруглён (4px) и без отступа,
    если предыдущей календарной даты НЕТ в `meet.days`, иначе плоский и `margin-left -7px`
    (заходит в промежуток); правый — так же по следующей дате (`margin-right -7px`).
    Так плашка тянется по дням подряд, обрывается на разрыве и на конце месяца без
    скругления, а в следующем месяце начинается плоской с `↳ Day N`. Функция
    `chipShape(meet, iso): { first: boolean; joinLeft: boolean; joinRight: boolean; dayNo: number }`
    в модели;
  - у каждой плашки `title` = полное имя (подсказка при наведении), но плашка не кликабельна
    отдельно — клик по ячейке выбирает день.
- Под сеткой — `CalendarLegend` (Б3).
- Месяц без meets (после фильтра) — сетка остаётся, под ней строка «No meets this month»
  (13px, `--t-text-2`) вместо кубов.

### Кубы выбранного дня

Под легендой: `selectedDay` есть и в нём meets → сетка кубов `grid-cols-1 sm:grid-cols-2
lg:grid-cols-4`, `gap 10px`, meets дня по порядку ответа; meets нет → «No meets on this day.»
(13px, `--t-text-2`); `selectedDay === null` — ничего. Контейнер — `aria-live="polite"`.

### Куб (база) — `calendar/meet-cube.tsx`

`MeetCube({ meet, today })`.

- **Весь куб — ссылка** `<a href>` (`meetHref(meet)` в модели: событие →
  `` `${routes.results()}?eventId=${id}` ``, однодневный → `routes.competition(id)`, предстоящий →
  `routes.competitionUpcoming(id)`), класс фокуса Б2, `no-underline text-inherit`,
  `aria-label` = `name, date label` (например `אליפות…, Sat–Sun 24–25 Oct`). Hover — фон
  `--t-nested` (у чемпионата — без смены фона, только рамка `--t-accent`).
- Коробка: радиус 14, рамка 1px `--t-border-2`, фон `--t-surface`, тень `--t-shadow`,
  `overflow-hidden`, `flex-col`, `min-height 180px` от `lg` (на телефоне без минимума).
  **Чемпионат**: класс `.cc-cube--champ` в `competition-calendar.css`:
  `background: linear-gradient(var(--t-champ-bg), var(--t-champ-bg)), var(--t-surface);
  border-color: var(--t-champ-border);` (в тёмной `--t-champ-bg` полупрозрачный — слой поверх
  поверхности, поэтому градиент, а не просто цвет).
- **Верхняя строка**: `flex justify-between items-start`.
  - Плитка даты (левый верхний угол, её левый и верхний край — рамка куба): `flex-col
    items-center gap-px`, `padding 8px 14px`, `min-width 64px`, рамка только справа и снизу 1px
    `--t-border-2`, скругление только нижнего правого угла 10px. Строки: день недели 10px
    `uppercase tracking-[.08em]` `--t-text-2` (`SAT` / `SAT–SUN`), число 20px 600 `--t-text`
    (`24` / `24–25`), месяц 11px `--t-accent-text` (`Oct`; старт через границу месяцев —
    `Oct–Nov`). Функция `dateTile(meet): { wd, day, mon }` в модели (по `start`/`end`).
  - Справа (`padding 10px 10px 0 0`) — только у чемпионата: `champMark` → `GroupBadge
    size="full"` или текст (11px, 500, `--t-champ-ink`, `IconTrophy` 11px, `max-width 60%`
    справа).
- **Тело** `flex-col gap 6px`, `padding 12px 14px 14px`, `flex-1`:
  - имя: `dir="rtl"`, `text-left`, 14px, 600, `line-height 1.35`, ровно 2 строки
    (`.cc-clamp2 { display:-webkit-box; -webkit-line-clamp:2; -webkit-box-orient:vertical;
    overflow:hidden; min-height:2.7em; }` в css);
  - мета: 12px `--t-text-2`: `[pool, venue, group?.label]` без пустых через ` · `; площадка —
    отдельный `<span dir="auto">` (она на иврите);
  - низ: `flex justify-between items-center gap 6px mt-auto pt-1.5 flex-wrap` — слева тег
    статуса, справа подпись действия.
- **Тег статуса** (11px, 600, радиус 6, `padding 3px 8px`) — функция `statusTag(meet)`:
  `live` → `Live` (текст `--t-live`, рамка 1px `--t-live`); иначе если `has_start_list` и
  статус `upcoming` → `Startlist` (фон `--t-accent-soft`, текст `--t-accent-text`); `in_progress`
  → `In progress` (рамка 1px `--t-accent`, текст `--t-accent-text`); `complete` → `Complete`
  (фон `--t-nested`, текст `--t-text-2`); `cancelled` → `Cancelled`; `postponed` →
  `Postponed → 21 Nov`; `done`/прочее — без тега. (Вид Cancelled/Postponed доделает Б5 — тег
  уже отдавай.)
- **Действие** (12px, 600, `--t-accent-text`; подпись, не отдельная ссылка — ссылка весь куб) —
  функция `actionLabel(meet)`: `cancelled` → нет; `postponed` → `Details →`; `kind === 'upcoming'`
  → `Startlist →`; `in_progress` → `Day N results →` (N = `days_loaded`); `complete` →
  `Full results →`; `live`/`done` → `Results →`; прочее (будущий без протокола) → `Details →`.

## Что уже готово (не переделывать)

Каркас Б2 (состояние `selectedDay`, правило дня по умолчанию, query `?day=`), Б3 (`GroupBadge`,
`champMark`, `CalendarLegend`, иконки), `routes.*`.

## Шаги

1. `calendar-model.ts`: `monthGrid`, `chipShape`, `meetHref`, `dateTile`, `statusTag`,
   `actionLabel`, `meetsOnDay(meets, iso)`, форматтер `dayAriaLabel(iso, n)`.
2. `group-badge.tsx` — размер `'cell'` (24px; полосы как у mini).
3. `meet-cube.tsx`, `month-view.tsx` (сетка + легенда + кубы дня).
4. `competition-calendar.css` — `.cc-cube--champ`, `.cc-clamp2`.
5. `competition-calendar.tsx` — заглушку Month заменить на `<MonthView …/>`. Заглушку Meet days
   не трогать (Б5).
6. `docs/ui-components.md` §6 — строка про куб соревнования («`MeetCube` — своя вёрстка календаря:
   `SwimRow`/карточки соревнований витрины несут другое (место, время); куб — дата-плитка +
   статус + действие»).

## Тесты

Клиентских тестов нет. `typecheck` + браузер.

## Проверка

1. `npm --prefix client run typecheck`.
2. Стенд `stack-5181-api5079-personas` → `/competitions` (Full month, Oct 2026):
   - 9 Oct — три плашки, выбран по умолчанию, ниже 3 куба с площадками на иврите,
     действие `Results →`, ссылки ведут на `/competitions/<id>`;
   - 19 Oct обведён (сегодня), 1–18 приглушены;
   - ‹ в сезон 2025/26: декабрь 2025 (23–27 — многодневный чемпионат «ארנה» нוער: плашка
     тянется, `↳ Day N`, мини-бейдж J), май–июль 2026 (событие בוגרים קיץ с днями не подряд:
     плашка рвётся на разрывах), июль 2026 (несколько чемпионатов: золото), февраль 2026
     (день с 4+ meets → `+N more`, если есть);
   - клик по дню → кубы этого дня; пустой день → «No meets on this day.»;
   - куб события ведёт на `/results?eventId=…`; действие у многодневки `complete` —
     `Full results →`, у `in_progress` (Maccabiah, июль 2026) — `Day 3 results →`;
   - светлая и тёмная: золото чемпионата в плашке и кубе читается, бейджи видны.
3. Остановить стенд.

## Footguns

- Спецификация §6. Перелив плашки (`-7px`) должен совпадать с `padding 7px` ячейки и `gap 4px`
  сетки — проверь, что соседние плашки одной многодневки визуально сливаются.
- День недели/число в плитке даты считай по UTC (`Date.UTC` + `getUTCDay`), не локально.

## Вне скоупа (не делать)

Полоса дней многодневки в кубе, куб на 2 колонки, вид Cancelled (opacity) и Postponed,
вид Meet days, фейковые данные (всё — Б5); телефон (Б6); Records (Б7).

## Отчёт

Список файлов, что проверено (тема, ширина), раздел **«Отклонения»**.
