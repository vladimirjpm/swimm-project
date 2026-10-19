# Задание (Sonnet): Б3 — вид Full year и бейдж группы

## Контекст

Шаг Б3 плана [`docs/plans/competitions-home-plan.md`](../plans/competitions-home-plan.md).
**Сначала прочитай общую спецификацию
[`competitions-home-calendar-spec.md`](competitions-home-calendar-spec.md)** (контракт, правила,
роли цветов, footguns). Каркас (Б2) уже сделан: `client/src/projects/competitions-project/calendar/`
— оркестратор `competition-calendar.tsx`, `calendar-model.ts`, `calendar-state.ts`,
`calendar-controls.tsx`, `calendar-icons.tsx`. Прочитай их перед работой. В Б3 заменяешь
заглушку вида Year настоящим видом и делаешь общий компонент бейджа группы (его возьмут Б4–Б6).

Референс: `!design_handoff/design_handoff_competitions_home/screenshots/03-light-year.png`,
`04-dark-year.png` (слева десктоп, справа телефон), README §«Вид Full year».

## Решения (зафиксированы, не пересматривать)

### Бейдж группы — `calendar/group-badge.tsx`

`GroupBadge({ group, size, className })`, `group: CalendarGroup` (буква, возраст, сезон),
`size: 'full' | 'mini'`. Три полосы сверху вниз, `overflow:hidden`, без рамки:

| | full (куб, Б5) | mini (полоса месяца, ячейка календаря) |
|---|---|---|
| ширина / радиус | 40px / 6px | 26px / 4px |
| верх (иконка сезона слева, кубок справа) | 11px, иконки 9px, `padding 0 4px` | 8px, иконки 6px, `padding 0 2px` |
| середина — буква | 19px, 14px bold | 13px, 10px bold |
| низ — возраст | 11px, 8px bold, `letter-spacing -.02em` | 8px, 6.5px bold |

- Верх и низ — фон `--t-plate` (светлая подложка, одинаковая в обеих темах — поэтому бейдж
  читается и на тёмной); середина — фон `--t-accent`, буква `--t-accent-ink`; возраст и обе
  иконки — `--t-badge-ink`.
- Иконка сезона: `season === 'winter'` → снежинка, `'summer'` → солнце, `null` → ничего (кубок
  остаётся справа). Возраст `null` → нижняя полоса пустая (высота сохраняется).
- `role="img"`, `aria-label` вида `Kids 8–11 · winter championship` (без сезона — `Kids 8–11
  championship`; без возраста — `Kids championship`).
- Новые иконки в `calendar-icons.tsx`: `IconSnowflake`, `IconSun`, `IconTrophy` (кубок —
  ЗАЛИТЫЙ, `fill="currentColor"`, т.к. в 6–9px контур не читается).

**Где бейдж, а где текст.** Бейдж с буквой — только у чемпионата **бассейна** (`type === 'pool'`)
**с** `group`. Чемпионат открытой воды / артистики — вместо бейджа текст
`🏆 Open water championship` / `🏆 Artistic championship` (11px, 500, `--t-champ-ink`,
кубок — `IconTrophy` 11px, не эмодзи). Чемпионат бассейна без `group` — текст
`🏆 Championship`. Это правило — функцией `champMark(meet): { kind: 'badge', group } |
{ kind: 'text', text }` в `calendar-model.ts`, чтобы Б4–Б6 брали то же.

### Вид Full year — `calendar/year-view.tsx`

`YearView({ season, meets, today, currentMonth, onPickMonth })`, где `meets` — meets сезона
**уже после фильтра по типу**.

- Сетка `grid-cols-2 lg:grid-cols-4`, `gap-[10px]`; 12 карточек — месяцы сезона Sep→Aug
  (`seasonMonths(season)` из модели).
- **Карточка** — `button` (`type="button"`, класс фокуса из Б2), `text-left`, радиус 14,
  `padding 12px 14px`, `flex-col gap-2`, фон `--t-surface`, рамка 1px `--t-border-2`,
  тень `--t-shadow`; hover — фон `--t-nested`.
  - **Текущий месяц** (месяц «сегодня») — рамка `--t-accent`, фон `--t-nested`.
  - **Прошедший месяц** (весь до месяца «сегодня») — `opacity .6`.
  - `aria-label`: `October 2026 — 14 meets, 2 championships` (числа по делу; 1 meet / 1 championship
    в единственном числе), клик → `onPickMonth(month)` (переход в Full month этого месяца —
    уже сделан в Б2).
- **Шапка карточки**: слева `Oct` (17px, 600, `--t-text`) + год `2026` (11px, `--t-text-2`,
  `gap 6px`, baseline); справа общее число meets месяца (18px, 700, `--t-accent-text`).
- **Три строки** `Swimming` / `Open water` / `Artistic` (типы `pool` / `open_water` /
  `artistic`): сетка `minmax(0,1fr) auto`, 12px; подпись `--t-text-2` (с многоточием), число
  `--t-text`; если число 0 — вся строка `opacity .35`. Рядом с подписью — `IconTrophy` 13px
  `--t-champ-ink`, если в месяце есть чемпионат этого типа.
- **Полоса чемпионатов** внизу:
  - есть чемпионаты → `flex flex-wrap gap-1 items-center`, `min-height 29px`, `padding 5px 6px`,
    радиус 6, фон `--t-champ-strip-bg`, рамка 1px `--t-champ-strip-border`; внутри по одному на
    чемпионат месяца (по дате начала): `champMark` → `GroupBadge size="mini"` или текст;
  - нет → `min-height 29px`, `padding-top 6px`, `border-top 1px --t-border-2`, текст
    `No championships` (11px, `--t-text-3`).
- Числа/чемпионаты месяца — по правилу «meet в месяце» (спецификация §4: старт, идущий через
  границу месяцев, считается в обоих).

### Легенда — `CalendarLegend` (в `year-view.tsx` не клади — отдельный маленький компонент в `calendar/calendar-legend.tsx`)

Строка под телом вида (Year и Month; в Meet days — тоже), `flex gap-[14px] flex-wrap`, 11px,
`--t-text-2`: `Championship` — образец 10×10, радиус 3, фон `--t-champ-chip-bg`, рамка 1px
`--t-champ-border`; `Meet` — образец 10×10, радиус 3, фон `--t-nested`, рамка 1px `--t-border`;
`Today` — квадрат 10×10, радиус 2, рамка 1.5px `--t-accent`, без фона. Образцы `aria-hidden`.
(В макете точки — заменены образцами, чтобы легенда совпадала с тем, что в ячейках.)

## Что уже готово (не переделывать)

- Каркас Б2 (см. Контекст): состояние, query, загрузка сезона, строка управления, ‹ › по
  сезонам в Year, `onPickMonth`.
- Счётчики чипов типов в Year уже считаются по сезону (Б2) — проверь, что `YearView` получает
  те же meets сезона, что и счётчики (после фильтра).

## Шаги

1. `calendar-icons.tsx` — `IconSnowflake`, `IconSun`, `IconTrophy`.
2. `calendar-model.ts` — `champMark(meet)`; при необходимости `isPastMonth(m, today)`,
   `isCurrentMonth(m, today)`, `monthShort(m)` (`Oct`), счётчики по типам для месяца (можно
   переиспользовать `typeCounts` из Б2 на meets месяца).
3. `group-badge.tsx`, `calendar-legend.tsx`, `year-view.tsx`.
4. `competition-calendar.tsx` — заглушку Year заменить на `<YearView …/>` + `<CalendarLegend/>`
   под ним; заглушки Month/Meet days не трогать (их заменят Б4/Б5), но легенду под Month тоже
   можно поставить уже сейчас.
5. `docs/ui-components.md` §6 — строка про `GroupBadge` («бейдж группы K/Y/J/A/M календаря —
   своя вёрстка в `competitions-project/calendar/group-badge.tsx`; в `mix/` похожего нет:
   `UI_CategoryBadge`/иконки категорий — другое (если найдёшь похожее — напиши в отчёте)»).

## Тесты

Клиентских тестов в проекте нет. `typecheck` + проверка в браузере.

## Проверка

1. `npm --prefix client run typecheck` — чисто.
2. Стенд `stack-5181-api5079-personas` → `http://localhost:5181/competitions?view=year`:
   - `Season 2026/27`: Sep 2026 — 0 и «No championships», Oct 2026 — 3 (текущий месяц:
     рамка акцента), остальные 0; прошедших в этом сезоне нет кроме Sep (приглушён);
   - ‹ → `Season 2025/26`: все месяцы приглушены (прошли); есть полосы чемпионатов (Dec, Jan,
     Feb, Jul…), бейджи с буквами; в апреле — строка Open water с кубком и текст
     «Open water championship», если эйлатский старт помечен чемпионатом (если не помечен —
     кубка нет, это нормально — проверь по `is_championship` в ответе API);
   - клик по карточке → Full month этого месяца;
   - фильтр Open water → строки Swimming/Artistic приглушены, числа по фильтру;
   - светлая и тёмная тема: бейдж читается в обеих (полосы `--t-plate`), золото полосы видно;
   - узкое окно (375px): 2 колонки, без горизонтальной прокрутки.
3. Остановить стенд.

## Footguns

- Спецификация §6. `IconTrophy` в 6px — проверь глазами, что это не клякса; если не читается,
  упрости форму (чаша + ножка), а не увеличивай размер.
- Месяц «сегодня» — из `today` ответа, не `new Date()`.

## Вне скоупа (не делать)

Full month, кубы, Meet days (Б4–Б5); мобильная строка управления (Б6); Records (Б7).

## Отчёт

Список файлов, что проверено (тема, ширина), раздел **«Отклонения»**.
