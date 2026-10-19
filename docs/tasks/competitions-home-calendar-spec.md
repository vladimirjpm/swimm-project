# Календарь сезона на `/competitions` — общая спецификация для заданий Б2–Б8

Общий документ для всех заданий вёрстки части Б плана
[`docs/plans/competitions-home-plan.md`](../plans/competitions-home-plan.md). Каждое задание
`competitions-home-b<N>-sonnet.md` ссылается сюда и добавляет только свой шаг. **Прочитай этот
файл целиком перед любым шагом.** Решения здесь зафиксированы — не пересматривай их; если
что-то не сходится с кодом, напиши об этом в отчёте («Отклонения»), а не решай молча.

## 1. Что строим

Главная `/competitions` становится **календарём сезона** по данным нашей базы: три вида
(Full year · Full month · Meet days), кубы соревнований, золото чемпионатов, фильтр по типу,
полоса «Records & standards», мобильные варианты.

**Дизайн-референс (вне git, только читать, НЕ коммитить):**
`!design_handoff/design_handoff_competitions_home/`
- `README.md` — описание экрана, состояний, доступности, краевых случаев;
- `screenshots/01-light-month.png`, `02-dark-month.png` — Full month (выбран 24 Oct);
  `03/04` — Full year; `05/06` — Meet days. Слева десктоп 1180, справа телефон 375;
- `competitions-home.dc.html` — макет с инлайн-стилями (точные размеры/отступы ищи там,
  строки длинные — читай grep'ом/по кускам). Цвета в макете — переменные `--color-*` макета;
  **у нас только роли `--t-*`** (таблица соответствия — §5 ниже).

Отличия от макета, которые решены (не «чинить»):
- Шрифты — текущие проекта (заголовки/крупные числа — как в остальной семье hp), радиусы
  deep (карточка 16, тайл 14, строка 12, мелкие плашки 6–8).
- Площадка (`venue`) приходит **на иврите** («בריכת וייסגל רחובות») — рисуй `dir="auto"`.
- Возраст на бейдже — из данных (`group.ages`): J «14–17», A «17+», M «25+» (в макете 15–17/18+).
- Листание ‹ › идёт и через границу сезона (у нас есть история) — в пределах `seasons` ответа.

## 2. Данные — `GET /api/competitions/calendar?season=<год начала>`

Сервер готов (шаг Б1, `server/Swimm.Application/Dtos/CompetitionCalendarDtos.cs` — читай
комментарии там). Без `season` — текущий сезон по дате Израиля. Анонимно, ETag,
`Cache-Control: public, max-age=60`.

```jsonc
{
  "season": 2026, "season_label": "2026/27",
  "today": "2026-10-19",          // «сегодня» по Asia/Jerusalem
  "seasons": [2024, 2025, 2026],  // где у нас есть старты, по возрастанию — границы листания
  "meets": [{                     // по дате начала, затем по имени
    "key": "event:12",            // уникален: event:{EventId} | competition:{Id} | upcoming:{OrgCompId}
    "kind": "event",              // event | competition | upcoming
    "id": 12,
    "name": "אליפות ישראל לנוער — חורף",
    "start": "2026-10-16", "end": "2026-10-18",
    "days": [{"date": "2026-10-16", "loaded": true}, {"date": "2026-10-17", "loaded": false}, …],
    "days_loaded": 1, "days_total": 3,
    "pool": "50m",                // "25m" | "50m" | "" (нет данных / море / артистика)
    "venue": "מכון וינגייט",      // null — нет
    "type": "pool",               // pool | open_water | artistic
    "is_championship": true,
    "group": {"letter": "J", "label": "Juniors", "ages": "14–17", "season": "winter"}, // или null
    "status": "in_progress",      // upcoming | live | in_progress | complete | done | cancelled | postponed
    "postponed_to": null,         // ISO-дата при postponed
    "has_start_list": false
  }]
}
```

⚠ **Дни события не обязательно подряд**: у «בוגרים קיץ 2026» дни идут с 23 мая по 20 июля
кусками. Многодневная плашка в ячейках тянется ТОЛЬКО по датам из `days`, а непрерывной
полосой — только по идущим подряд датам. Между `start` и `end` дни без записи в `days` — пустые.

`cancelled` / `postponed` сервер пока не отдаёт никогда (решение В4) — вид для них всё равно
делаем; проверяем на фейковых данных (dev-флаг, см. Б5).

## 3. Устройство кода (раскладка на все шаги)

Папка фичи — `client/src/projects/competitions-project/` (точка входа страницы уже есть:
`client/src/pages/competitions-page.tsx` → `competitions.tsx`).

```
competitions-project/
  competitions.tsx                 — страница: топбар, шапка (как сейчас), <CompetitionCalendar/>,
                                     секция избранного, <RecordsStandards/> (Б7), лента рекордов, тоггл темы
  calendar/
    calendar-types.ts              — TS-зеркало контракта + типы состояния (Б2)
    use-competition-calendar.ts    — загрузка сезона, кэш по сезону в модуле (Б2)
    calendar-model.ts              — ЧИСТЫЕ функции: даты, сетка месяца, фильтры, счётчики,
                                     подписи, ссылки и действия куба (Б2, дополняется дальше)
    calendar-state.ts              — состояние вида + синхронизация с query (Б2)
    competition-calendar.tsx       — оркестратор: состояние, строка управления, тело вида (Б2)
    calendar-controls.tsx          — ‹ месяц ›, сегмент видов, чипы типов (Б2; телефон — Б6)
    calendar-icons.tsx             — инлайн-SVG иконки (без новых npm-зависимостей) (Б2+)
    group-badge.tsx                — бейдж группы: mini (ячейка/полоса/точка) и full (куб) (Б3)
    year-view.tsx                  — Full year (Б3)
    month-view.tsx                 — Full month десктоп + кубы дня (Б4)
    meet-cube.tsx                  — куб соревнования, все варианты (Б5)
    meet-days-view.tsx             — Meet days (Б5)
    month-mobile.tsx               — телефон: неделя/месяц, лента дней Meet (Б6)
    records-standards.tsx          — полоса Records & standards (Б7)
    competition-calendar.css       — то, что неудобно в Tailwind (line-clamp, анимация Type…)
```

- **Логика — в `calendar-model.ts` чистыми функциями**, компоненты только рисуют. Тестового
  раннера у клиента нет (vitest/jest не подключены) — проверка `tsc` + глазами; поэтому функции
  держи маленькими и очевидными.
- **Состояние — локальное (`useState`/`useReducer`) в оркестраторе**, не Redux: оно живёт
  только на этой странице.
- **Вид — в query** (правило `client/CLAUDE.md`: в путь — только идентичность, вид — в query):
  `view=year|month|meets` (умолчание `month` не пишем), `month=2026-10`, `day=2026-10-24`,
  `type=pool,open_water`. Писать `history.replaceState` (не `pushState`), читать при старте.
  Пустые/умолчательные значения в адрес не пишутся. Неверные значения → умолчание.
- **Ссылки — только через `routes.*()`** (`client/src/utils/routes.ts`): событие →
  `` `${routes.results()}?eventId=${id}` ``; однодневный → `routes.competition(id)`; предстоящий →
  `routes.competitionUpcoming(id)`. Рекорды — `routes.records({…})`, `routes.recordsCompare({})`.

## 4. Правила вида (общие для всех шагов)

**Месяц и сезон.** Сезон = сентябрь–август, год начала (`seasonStartYear` из
`client/src/utils/helpers/season-helper.ts` — используй его, не пиши своё). Месяц в состоянии —
`{ year, month }` (month 1–12). Сезон месяца = сезон его 1-го числа. Сезоны грузятся по
требованию: перешли в месяц другого сезона — `useCompetitionCalendar(season)` подтянет его.

**Границы ‹ ›**: от сентября `min(seasons)` до августа `max(seasons)`; на границе кнопка
disabled (`aria-disabled`, opacity .45). В Year ‹ › листают сезоны (подпись `Season 2026/27`),
в Month/Meet — месяцы (подпись `October 2026`).

**«Сегодня»** — поле `today` ответа; до прихода данных — дата Asia/Jerusalem через
`Intl.DateTimeFormat('en-CA', { timeZone: 'Asia/Jerusalem', … })`. Никогда `new Date()`
по локали браузера для «сегодня». Даты из ответа (`yyyy-MM-dd`) сравнивай СТРОКАМИ или разбирай
в UTC-полночь — не через `new Date('2026-10-19')` с локальным поясом в арифметике дней.

**Meet в месяце** — если хоть один его день (`days[].date`) в этом месяце (старт, идущий через
границу месяцев, считается в обоих). Одно правило для счётчиков, Year, Month и Meet days.
**Meet в дне** — дата есть в `days`.

**Фильтр по типу.** `typeFilter: MeetType[]`, пусто = All. Мульти-выбор, клик по All —
сброс. Счётчик в чипе — сколько meets этого типа в ОБЛАСТИ вида без учёта самого фильтра:
область — текущий месяц (Month, Meet days) или сезон (Year). Чип All — всего в области.

**Выбранный день (Month).** При заходе в месяц и при смене месяца: в месяце «сегодня» —
ближайший день с meets начиная с сегодня, иначе последний день с meets до сегодня; в другом
месяце — первый день с meets; meets нет — `null`. Клик по дню ставит его (даже пустой). День
из `?day=` важнее правила, если он в текущем месяце.

**Сегмент видов.** Порядок Full year · Full month · Meet days (телефон: Year · Month · Meet),
по умолчанию Full month. `role="radiogroup"`, кнопки — `role="radio"` + `aria-checked` (и
`aria-pressed` не нужен). Выбранный — сплошной `--deep-seg-active-bg` / `--deep-seg-active-fg`,
остальные прозрачные, текст `--t-text`; обводка группы `--t-divider`, разделители между
опциями `--t-divider`. У нативных `button` сбросить браузерный фон/рамку. **Disabled вид**:
Meet days, когда в текущем месяце (после фильтра) нет meets; Full year — когда в сезоне нет
ни одного; opacity .45, `cursor:not-allowed`, `aria-disabled="true"`, клик ничего не делает.
Если уже в виде, который стал пустым, — остаёмся и показываем пустое состояние.

**Пустые состояния (тексты дословно):** день без meets — «No meets on this day.»; месяц без
meets — «No meets this month»; карточка месяца в Year — 0 и «No championships».

**Доступность.** Все кликабельные (ячейки дня, кубы, карточки месяцев, чипы, сегменты, ‹ ›) —
`button` или `role="button"` + `tabIndex=0` + Enter/Space. `:focus-visible` — `outline: 2px solid
var(--t-accent); outline-offset: 2px`. Hover ячейки/куба — фон `--t-nested`; hover чипа —
рамка `--t-accent`. Анимации уважают `prefers-reduced-motion`.

**Имена.** Название meet: `dir="auto"` + `text-align: left` (не `rtl`: у части стартов имя латиницей — «Maccabiah 2026 Masters», и с `rtl` многоточие отрезало начало) (иврит и мета под ним начинаются с
одного края). Плашки в ячейках — одна строка с `…`; в кубе — ровно 2 строки
(line-clamp 2, min-height 2.7em). Интерфейс — только английский.

**Неделя** начинается с воскресенья: Sun Mon Tue Wed Thu Fri Sat (телефон: S M T W T F S).

## 5. Цвета — только роли `--t-*` (никаких хексов в разметке)

Роли объявлены в `client/src/projects/components/deep/deep-theme.css` для обеих тем; таблица —
`docs/design-colors.md` §3. Страница уже носит класс темы (`useDeepThemeClass()` в
`competitions.tsx`).

| Что | Роль |
|---|---|
| Поверхность ячейки/куба/карточки | `--t-surface` (карточка `--t-card`), тень `--t-shadow` |
| Вложенное (обычная плашка meet, hover, выбранный день, текущий месяц) | `--t-nested` |
| Рамки | `--t-border`; тонкие внутренние — `--t-border-2` |
| Обводка/разделители сегмента | `--t-divider` |
| Текст / вторичный / третичный | `--t-text` / `--t-text-2` / `--t-text-3` |
| Акцент (обводка «сегодня», выбранного, фокус, сегмент полоса дней) | `--t-accent` |
| Акцент МЕЛКИМ текстом (месяц в плитке даты, «+N more», ссылки действий) | `--t-accent-text` |
| Выбранный чип типа | рамка `--t-accent`, фон `--t-accent-soft` |
| Сегмент выбранный | `--deep-seg-active-bg` / `--deep-seg-active-fg` |
| Чемпионат: фон куба / рамка | `--t-champ-bg` / `--t-champ-border` |
| Чемпионат: плашка в ячейке / её текст, 🏆 у типа | `--t-champ-chip-bg` / `--t-champ-ink` |
| Полоса бейджей в карточке месяца, день с чемпионатом на телефоне | `--t-champ-strip-bg` / `--t-champ-strip-border` |
| Бейдж группы: середина / буква / возраст | `--t-accent` / `--t-accent-ink` / `--t-badge-ink` на `--t-surface` |
| Live | `--t-live` |

⚠ **Стекло в тёмной.** В тёмной теме `--t-surface`/`--t-nested` полупрозрачные, а под страницей —
фото бассейна. Каждой поверхности календаря (карточка месяца, ячейка дня, куб, тайл ленты,
плитка Records) — `backdrop-blur-[14px]`, как у карточек семьи hp; без него текст на фото плывёт.

Золото чемпионатов — **только** `--t-champ-*`, не `--t-warn`. ⚠ Tailwind: градиент в
произвольном значении — `bg-[image:var(--…)]`; цвета — `bg-[var(--t-nested)]`,
`text-[var(--t-accent-text)]`, `border-[var(--t-border)]`.

## 6. Общие footguns

- **API для проверки** — Release-сборка на :5079 (Debug держит запущенный API Влада на :5078 —
  его НЕ трогать и не убивать): `dotnet build server/Swimm.sln -c Release`, затем стенд
  `stack-5181-api5079-personas` из `.claude/launch.json` (API :5079 + клиент :5181). Сервер в
  этих шагах не меняется.
- **Тема сайта** переключается кнопкой-луной внизу страницы (`UI_ModeToggle`, `aria-label`
  «Switch to dark mode»), а не `prefers-color-scheme` — эмуляция цветовой схемы в браузере
  тему не меняет. Выбор запоминается; после проверки верни светлую.
- `npm --prefix client run typecheck` (это `tsc --noEmit`) должен быть чистым.
- Реестр UI-компонентов `docs/ui-components.md`: перед вёрсткой ячейки/бейджа посмотри, нет
  ли готового; новое своё — строка в §6 («чего нет — и почему своё»), это часть шага.
- Класс-маркеры `src-<файл>` — только у вызовов `UI_SwimmStyleIcon` (здесь их нет).
- Комментарии в коде — по-русски, как в соседних файлах; идентификаторы — английские.
- **Не коммитить.** Не трогать файлы вне перечисленных в задании (в рабочей копии лежат чужие
  незакоммиченные файлы — `!design_handoff/…` и др.). Не делать `git add -A`, `git stash`,
  не переключать ветки.
