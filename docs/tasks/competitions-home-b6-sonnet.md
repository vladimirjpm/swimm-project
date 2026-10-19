# Задание (Sonnet): Б6 — телефон: строка управления, неделя/месяц, лента дней Meet, Type

## Контекст

Шаг Б6 плана [`docs/plans/competitions-home-plan.md`](../plans/competitions-home-plan.md).
**Сначала прочитай общую спецификацию
[`competitions-home-calendar-spec.md`](competitions-home-calendar-spec.md).** Готово (Б2–Б5) в
`client/src/projects/competitions-project/calendar/`: оркестратор, модель, строка управления,
Full year, Full month (сетка + кубы дня), куб со всеми вариантами, Meet days, демо-режим
`?calendarDemo=1`. Прочитай их перед работой.

Б6 — мобильная раскладка (ширина **< lg**, т.е. < 1024px; проверяем на 375). Десктоп (≥ lg) не
меняется. Референс — правые части скриншотов `01…06` (375px) и README §«Мобайл (375)»; макет —
блоки после `width:375px` в `competitions-home.dc.html` (`mCell`, `mTile`, `typesWrap`,
`qmCard`).

## Решения (зафиксированы, не пересматривать)

### Строка управления (< lg) — в `calendar-controls.tsx`, отдельной разметкой `lg:hidden`, десктопная — `hidden lg:flex`

1. Строка 1: ‹ + подпись периода (16px, 500) + › ; в виде Month подпись — кнопка-тоггл
   разворота месяца: `October 2026 ▾` / `▴` (иконка caret 13px `--t-text-2`), `aria-expanded`.
   В Year/Meet подпись — просто текст.
2. Строка 2: сетка `minmax(0,1fr) auto`, `gap 8px`: сегмент `Year · Month · Meet` (3 равные
   колонки, те же правила, что у десктопного: radiogroup, `--t-divider`, disabled) + кнопка
   **Type** (иконка funnel 15px + `Type` + число выбранных типов, если выбраны, — 13px; высота
   как у сегмента; рамка 1px `--t-border`, фон `--t-surface`; при выбранных типах — рамка
   `--t-accent`, число `--t-accent-text`; `aria-expanded`, `aria-controls`).
3. Под строкой — панель типов: 4 кнопки в сетке `repeat(4, minmax(0,1fr))`, `gap 6px`; каждая —
   колонка: иконка 16px, короткая подпись (`All` / `Pool` / `Open w.` / `Artistic`) 11px, число
   10px `--t-text-2`; выбранная — рамка `--t-accent` + фон `--t-accent-soft`. **Раскрытие
   плавное**: обёртка `display:grid; grid-template-rows: 0fr → 1fr` (`.28s ease`), `opacity 0 → 1`
   (`.22s ease`), внутренний блок `overflow:hidden; min-height:0`; закрытая — `aria-hidden`,
   кнопки `tabIndex=-1`. `@media (prefers-reduced-motion: reduce)` — без `transition`. Классы —
   в `competition-calendar.css` (`.cc-types`, `.cc-types--open`). Не через `height`.

### Month на телефоне — `calendar/month-mobile.tsx`

- **По умолчанию одна неделя** (свёрнуто). Карточка: фон `--t-surface`, радиус 14, `padding 8px`,
  `flex-col gap 6px`. Шапка: ‹ `Week · 18–24 Oct` › (12px, `--t-text-2`; через границу месяца —
  `28 Sep – 4 Oct`); строка букв `S M T W T F S` (10px caps `--t-text-2`, по центру); 7 ячеек.
  Неделя по умолчанию — та, где выбранный день (иначе где «сегодня», иначе первая). ‹ › недели
  листают внутри месяца (на первой/последней неделе месяца кнопка ведёт в соседний месяц —
  и это та же навигация, что ‹ › месяца). Индекс недели — локальное состояние, сбрасывается при
  смене месяца.
- **Развёрнуто** (тап по подписи месяца) — все недели месяца теми же ячейками, без шапки недели.
  Состояние `monthExpanded` — локальное, в адрес не пишется.
- **Ячейка** — `button`, высота 52px, `flex-col items-center justify-center gap 4px`, радиус 8:
  число 12px (сегодня — 700 `--t-accent-text`); под ним до 4 меток в строку высотой 18px,
  `gap 2px`: обычный meet — точка 5px `--t-text-3`; чемпионат с бейджем — мини-бейдж 14×18
  (`GroupBadge size="dot"`: радиус 3, полоса 4px `--t-plate` сверху и снизу, середина
  `--t-accent` с буквой 9px 700 `--t-accent-ink`, без иконок и возраста); чемпионат без бейджа
  (море/артистика) — точка 7px `--t-champ-border`. Фон: выбранный — `--t-accent-soft`; день с
  чемпионатом — `--t-champ-strip-bg` + рамка 1px `--t-champ-strip-border`; сегодня — рамка 1px
  `--t-accent`; прошедший — `opacity .6`; иначе прозрачный. Дни вне месяца — пустые.
- Ниже — та же легенда и кубы выбранного дня, что на десктопе (кубы уже в 1 колонку).
- Подключение: в `month-view.tsx` десктопная сетка `hidden lg:block`, `MonthMobile` —
  `lg:hidden`; кубы дня и легенда — общие под обоими.

### Meet на телефоне — лента дней (в `meet-days-view.tsx`, `lg:hidden`)

- Над кубами: горизонтальная лента ТОЛЬКО дней месяца, где есть meets (после фильтра):
  `flex gap 6px overflow-x-auto`, без полосы прокрутки (`scrollbar-width:none` +
  `::-webkit-scrollbar{display:none}`). Тайл — `button` 52px шириной, `padding 6px 0`, радиус 8,
  `flex-col items-center gap 3px`: день недели 9px caps `--t-text-2`, число 17px 500, метки как в
  ячейке недели. Фон `--t-surface` + рамка 1px `--t-border-2`; день с чемпионатом — фон
  `--t-champ-strip-bg` + рамка `--t-champ-strip-border`; выбранный — фон `--t-accent-soft` +
  рамка `--t-accent`; прошедший — `opacity .6`.
- Под лентой подсказка 11px `--t-text-2`: без выбора — `All meet days`; с выбором —
  `Sat · 24 Oct · tap again for all`.
- Тап по дню — кубы только этого дня; повторный тап — снова все. Состояние `meetDayFilter`
  локальное, сбрасывается при смене месяца. На десктопе ленты нет и фильтр не действует.

### Куб и Year на телефоне

- Куб (< lg): плитка даты `padding 7px 10px`, `min-width 54px`, строки 9px / 17px / 10px;
  тело `padding 10px 10px 12px`, имя 13px, мета 11px; `GroupBadge` — новый размер `'full-sm'`
  34px (полосы 9 / 15 / 9, буква 11px, возраст 7px, иконки 7px). Через классы `lg:` — без
  второго компонента.
- Year уже 2 колонки (Б3) — проверь на 375, что карточка не ломается (подпись `Open water`
  с кубком влезает или режется многоточием).

## Шаги

1. `calendar-controls.tsx` — мобильная строка + панель типов; `competition-calendar.css` —
   `.cc-types*`, скрытие полосы прокрутки ленты.
2. `group-badge.tsx` — размеры `'dot'`, `'full-sm'`.
3. `month-mobile.tsx` (+ функции недель в модели: `weekIndexOf(grid, iso)`, `weekLabel(week)`).
4. `month-view.tsx`, `meet-days-view.tsx`, `meet-cube.tsx` — по решениям выше.

## Тесты

Клиентских тестов нет. `typecheck` + браузер.

## Проверка

1. `npm --prefix client run typecheck`.
2. Стенд `stack-5181-api5079-personas`, окно 375×812 (`resize_window` preset mobile) →
   `/competitions`:
   - Month: одна неделя (с 9 Oct → неделя 4–10 Oct, выбран 9-е), ‹ › недели, на краю — соседний
     месяц; тап по `October 2026 ▾` разворачивает весь месяц, `▴` сворачивает;
   - в декабре 2025 у дней чемпионата 23–27 — золотой фон и мини-бейдж J;
   - Type раскрывается плавно (в DevTools «Emulate prefers-reduced-motion: reduce» — сразу,
     без анимации); выбор типа — кнопка Type с рамкой и числом;
   - Meet: лента дней, тап фильтрует кубы, повторный — все, подсказка меняется;
   - `?calendarDemo=1`: демо-кубы в 1 колонку, полоса дней не ломает ширину;
   - нет горизонтальной прокрутки страницы ни в одном виде (`document.documentElement.scrollWidth
     <= innerWidth`);
   - светлая и тёмная; затем вернуть десктоп (preset desktop) и убедиться, что 1180 не изменился.
3. Остановить стенд.

## Footguns

Спецификация §6. `grid-template-rows` анимируется только между `0fr` и `1fr` при `display:grid`
у обёртки; дочерний элемент обязан иметь `min-height:0` и `overflow:hidden`, иначе 0fr не
схлопнется.

## Вне скоупа (не делать)

Records (Б7), пустые состояния/фокус сверх уже сделанного (Б8), сервер, топбар.

## Отчёт

Список файлов, что проверено (тема, ширина), раздел **«Отклонения»**.
