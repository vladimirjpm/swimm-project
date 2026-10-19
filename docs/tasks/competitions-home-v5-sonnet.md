# Задание (Sonnet): В5′ — куб будущего / незатянутого старта и `Details ↗`

## Контекст

Часть В плана [`docs/plans/competitions-home-plan.md`](../plans/competitions-home-plan.md): календарь
теперь показывает ВЕСЬ сезон — кроме затянутого, ещё строки «Входящих» isr.org.il, которых у нас нет
(будущие старты и незатянутые прошлые). Сервер готов (шаги В1′–В4′). **Сначала прочитай общую
спецификацию [`competitions-home-calendar-spec.md`](competitions-home-calendar-spec.md)** и код
`client/src/projects/competitions-project/calendar/` (`meet-cube.tsx`, `calendar-model.ts` —
`meetHref`, `statusTag`, `actionLabel`).

## Что пришло с сервера

- Новые meets `kind: "upcoming"` из входящих: `id` = compID федерации, `has_start_list: false`,
  `days_loaded: 0`, `pool: ""`, статус по датам (`upcoming` / `live` / `done`).
- Новое поле у каждой meet: **`source_url`** (`string | null`) — у `kind: "upcoming"` это страница
  федерации `https://isr.org.il/comp.asp?compID=…`, у остальных `null`. Тип в `calendar-types.ts`
  уже добавлен.
- **Postponed** теперь настоящий: на ПРЕЖНИХ датах приходит «призрак» `key: "postponed:{id}"`,
  `status: "postponed"`, `postponed_to` = новая дата; сам старт — отдельной meet на новых датах.
- **Cancelled** настоящий: `kind: "upcoming"` со `status: "cancelled"`.

## Решения (зафиксированы)

1. **Куда ведёт куб/действие** — функция `meetHref(meet)` и `actionLabel(meet)` в модели:
   - `kind: "upcoming"` и `has_start_list` → как сейчас: наша страница
     `routes.competitionUpcoming(id)`, действие `Startlist →`;
   - `kind: "upcoming"` без заявок и есть `source_url` → **внешняя ссылка** на `source_url`,
     действие **`Details ↗`** (стрелка ↗ — «уходим на другой сайт»); у `<a>` —
     `target="_blank" rel="noopener noreferrer"`, `aria-label` дополнить « (opens isr.org.il)»;
   - `postponed` → `Details ↗` на `source_url` (если нет — без ссылки);
   - `cancelled` → без ссылки (как в Б5);
   - затянутые (event / competition) — без изменений.
   Вынеси «внешняя ли ссылка» в модель (`meetLink(meet): { href, external } | null`), а `meetHref`
   и разметку куба переведи на неё — одно место правды.
2. **Тег статуса** будущего без заявок — нет тега (как `done`). Прошедший незатянутый (`done`, но
   `days_loaded === 0` и `kind: "upcoming"`) — тег `No results` (фон `--t-nested`, текст
   `--t-text-2`): у нас нет его результатов, честно говорим об этом.
3. **Мета-строка** — у таких meets нет бассейна; `venue` и `group.label` как обычно (пустые
   части не выводить — уже так).
4. **Демо-режим** (`calendar-demo.ts`): Postponed/Cancelled теперь приходят настоящие — демо-meets
   переведи на тот же вид: призрак postponed с `key: "demo:postponed"` и `source_url`, cancelled —
   `kind: "upcoming"`; добавь одну будущую без заявок с `source_url` (`Details ↗`).
5. Плашки в ячейке месяца, точки на телефоне, Year — ничего не меняется (meets из входящих —
   обычные meets; чемпионат по названию приходит в `is_championship`).

## Шаги

1. `calendar-model.ts`: `meetLink`, правка `meetHref`/`actionLabel`/`statusTag`.
2. `meet-cube.tsx`: внешняя ссылка (`target`, `rel`, `aria-label`), действие `Details ↗`.
3. `calendar-demo.ts`: решение 4.
4. `docs/ui-components.md` — дополнить строку `MeetCube` («внешняя ссылка на isr.org.il у
   незатянутых без заявок»).

## Проверка

1. `npm --prefix client run typecheck`.
2. Стенд `stack-5181-api5079-personas` (Release-API уже собран со всеми серверными правками —
   ничего не пересобирать; Debug и :5078 не трогать). `/competitions`:
   - Oct 2026 — кроме трёх затянутых 9 Oct, будущие старты из входящих; кубы будущих без заявок —
     `Details ↗`, открывают isr.org.il в новой вкладке (проверь `href`/`target`, не переходя);
   - Full year 2026/27 — будущие месяцы больше не нули, чемпионаты по названию — золотые;
   - `?calendarDemo=1` — призрак Postponed `Postponed → …` + `Details ↗`, Cancelled без ссылки;
   - светлая/тёмная (тоггл-луна внизу), 1180 и 375, без горизонтальной прокрутки.
3. Остановить стенд, тема светлая, viewport desktop.

## Вне скоупа

Сервер, страница `/competitions/upcoming/{id}`, другие виды.

## Отчёт

Список файлов, что проверено, **«Отклонения»**. Ничего не коммитить.
