# Задание (Sonnet): Б7 — полоса «Records & standards»

## Контекст

Шаг Б7 плана [`docs/plans/competitions-home-plan.md`](../plans/competitions-home-plan.md).
**Сначала прочитай общую спецификацию
[`competitions-home-calendar-spec.md`](competitions-home-calendar-spec.md)** (§5 цвета, §6 footguns).
Календарь (Б2–Б6) готов; Б7 — отдельная полоса ссылок под календарём на странице
`client/src/projects/competitions-project/competitions.tsx`.

Референс: низ скриншотов `01…06` (десктоп — 5 плиток в ряд; телефон — 2×2 + строка Normatives),
README §2 п.5 и §«Мобайл (375)», макет (`records` в скрипте и разметка `Records &amp; standards`).

## Решения (зафиксированы, не пересматривать)

1. **Пять плиток**, порядок и ссылки (только через `routes.*()`):

   | Плитка | Подпись | Ссылка | Иконка |
   |---|---|---|---|
   | National records | By country | `routes.records()` | флаг |
   | World records | LCM · SCM | `routes.records({ tab: 'world' })` | глобус |
   | World junior | Under 18/19 | `routes.records({ tab: 'junior' })` | звезда |
   | H2H records | Compare two countries | `routes.recordsCompare()` | две стрелки ⇄ |
   | Normatives | Qualifying times | **нет** — страницы нормативов нет | линейка |

   Пути в подписях (`· /records` в макете) — пометки дизайнера, на витрину не идут.
2. **Normatives — «Soon», не кликается** (решение В7 по умолчанию: страницы нормативов нет, в
   топбаре это тоже неактивный `span`). Плитка — `div` (не ссылка, не в порядке Tab),
   `aria-disabled="true"`, рамка `1px dashed var(--t-border)`, фон прозрачный, `opacity .7`;
   справа сверху тег `Soon` (11px, 600, радиус 6, `padding 2px 8px`, фон `--t-border-2`, текст
   `--t-text-2` — так тег не теряется в светлой, это поправка хендоффа к `.tag-neutral`); иконка
   `--t-text-2`.
3. **Плитка-ссылка**: `<a>` (класс фокуса календаря), фон `--t-surface`, рамка 1px `--t-border-2`,
   радиус 14, `padding 16px`, `flex-col gap 10px`, `min-height 96px`, тень `--t-shadow`, hover —
   рамка `--t-accent`; иконка 22px `--t-accent`; заголовок 14px 600 `--t-text`; подпись 12px
   `--t-text-2`. `no-underline text-inherit`.
4. **Раскладка**: десктоп (≥ lg) — `grid-cols-5 gap-3`; телефон — `grid-cols-2 gap-2` для
   четырёх ссылок (на телефоне подпись не показываем — только иконка и заголовок, как в макете),
   Normatives — отдельной строкой во всю ширину: `flex justify-between items-center`, высота
   44px, подпись `Normatives` 13px `--t-text-2` + тег `Soon`.
5. Заголовок секции `Records & standards` — `h2`, 20px, `font-extrabold`, `--t-text`, отступ
   снизу 12px. Секция — `<section aria-label="Records and standards">` с теми же
   горизонтальными отступами, что календарь (`px-4 lg:px-16`), сверху `pt-[38px] lg:pt-16`.
   Место — после `FavoritesUpcomingSection`, перед `RecordTicker`.
6. Иконки — в `calendar/calendar-icons.tsx` (`IconFlag`, `IconGlobe`, `IconStar`,
   `IconArrowsLeftRight`, `IconRuler`), стиль как у остальных.

## Шаги

1. Иконки.
2. `calendar/records-standards.tsx` — `RecordsStandards()` по решениям 1–5.
3. `competitions.tsx` — подключить.
4. `docs/ui-components.md` §6 — не нужно, если плитка — простая ссылка; если завёл общий
   компонент — строка.

## Тесты

Клиентских тестов нет. `typecheck` + браузер.

## Проверка

1. `npm --prefix client run typecheck`.
2. Стенд `stack-5181-api5079-personas` → `/competitions`: 1180 — 5 плиток в ряд; клики ведут на
   `/records`, `/records?tab=world`, `/records?tab=junior`, `/records/compare` (страницы
   открываются); Normatives не кликается, Tab его пропускает; 375 — 2×2 + строка Normatives;
   светлая/тёмная — тег Soon читается.
3. Остановить стенд.

## Вне скоупа (не делать)

Страница нормативов, топбар, календарь.

## Отчёт

Список файлов, что проверено, раздел **«Отклонения»**.
