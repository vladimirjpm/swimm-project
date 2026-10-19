# Задание (Sonnet): Б5 — все варианты куба и вид Meet days

## Контекст

Шаг Б5 плана [`docs/plans/competitions-home-plan.md`](../plans/competitions-home-plan.md).
**Сначала прочитай общую спецификацию
[`competitions-home-calendar-spec.md`](competitions-home-calendar-spec.md).** Готово: каркас (Б2),
Full year + бейдж (Б3), Full month + базовый куб `meet-cube.tsx` (Б4) в
`client/src/projects/competitions-project/calendar/`. Прочитай `meet-cube.tsx`,
`month-view.tsx`, `calendar-model.ts`, `competition-calendar.tsx` перед работой.

В Б5: куб получает все варианты (многодневка в процессе — 2 колонки с полосой дней; Cancelled;
Postponed), появляется вид Meet days, и dev-режим с фейковыми meets, чтобы эти варианты
можно было увидеть (сервер `cancelled`/`postponed` пока не отдаёт — решение В4).

Референс: `screenshots/05-light-meetdays.png`, `06-dark-meetdays.png`, README §«Куб
соревнования», §«Вид Meet days», макет (`toCard`, `mkDays` в скрипте).

## Решения (зафиксированы, не пересматривать)

### Многодневка в процессе — 2 колонки и полоса дней

- Условие: `meet.status === 'in_progress'` (частично загружена; решение В8: загружена целиком
  — `complete` — куб снова в 1 колонку, без полосы, действие `Full results →`, это уже есть).
- В сетке кубов (`lg:grid-cols-4`) такой куб — `lg:col-span-2` (на `sm` 2-колоночной сетке —
  тоже `sm:col-span-2`; на телефоне 1 колонка, span не нужен).
- **Полоса дней** — под мета-строкой: сетка `repeat(N, minmax(0,1fr))` по `meet.days`,
  `gap 4px`, `padding-top 2px`. Каждый день: сегмент 4px высотой, радиус 2, фон `--t-accent`
  если `loaded`, иначе `--t-border`; под ним подпись 10px `--t-text-2`, одна строка с `…`:
  `Day 1 · 16 Oct ✓` (✓ только у загруженного; загруженный — текст `--t-text`).
  Больше 7 дней — показывай первые 6 и сегмент-хвост `+N days`; `aria-label` полосы:
  `1 of 3 days loaded`.

### Cancelled / Postponed

- `cancelled`: весь куб `opacity .5`, **не ссылка** (`<div>` вместо `<a>`, `aria-disabled`),
  тег `Cancelled` (фон `--t-nested`, текст `--t-text-2`), действия нет, hover не меняет фон.
- `postponed`: обычная ссылка, тег-обводка `Postponed → 21 Nov` (рамка 1px `--t-accent`, текст
  `--t-accent-text`; дата из `postponed_to`, формат `d Mon`; без даты — `Postponed`), действие
  `Details →`. Куб стоит на СТАРОЙ дате (как приходит).

### Вид Meet days — `calendar/meet-days-view.tsx`

`MeetDaysView({ month, meets, today })`: все meets месяца (после фильтра; правило «meet в
месяце» — спецификация §4) кубами, сортировка по `start`, затем имя; сетка как у кубов дня
(`grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-[10px]`, многодневка в процессе — span 2).
Под сеткой — `CalendarLegend`. Пусто — «No meets this month». Мобильную ленту дней над кубами
сделает Б6 — здесь только сетка.

### Фейковые данные (dev) — `calendar/calendar-demo.ts`

- Включается **только** в dev-сборке (`import.meta.env.DEV`) и параметром `?calendarDemo=1`.
  В прод-сборке код не должен срабатывать (проверка `import.meta.env.DEV` до чтения query).
- Функция `withDemoMeets(meets, month, today): CalendarMeet[]` добавляет в ТЕКУЩИЙ показанный
  месяц 4 выдуманных meets (имена на иврите, `key` с префиксом `demo:`):
  1) многодневный чемпионат бассейна, 3 дня подряд, загружен 1 (`in_progress`,
     `group` J 14–17 winter, `pool` 50m, `venue` «מכון וינגייט»);
  2) однодневный `cancelled`;
  3) однодневный `postponed`, `postponed_to` = +4 недели;
  4) чемпионат открытой воды (`type: 'open_water'`, без `group`, `is_championship: true`,
     `status: 'upcoming'`, `has_start_list: true`, `kind: 'upcoming'`).
  Даты — в пределах показанного месяца (например 10–12, 14, 15, 21 число).
- Подключение — в оркестраторе одной строкой при сборке meets месяца. Плашка/чип «DEMO DATA»
  (11px, `--t-warn`) над календарём, пока режим включён, чтобы не перепутать с правдой.

## Шаги

1. `meet-cube.tsx` — варианты выше (полоса дней — отдельный маленький компонент в том же
   файле `MeetDayStrip`); `statusTag`/`actionLabel` из Б4 уже учитывают cancelled/postponed —
   проверь.
2. Сетки кубов (кубы дня в `month-view.tsx` и `meet-days-view.tsx`) — span 2 для `in_progress`.
3. `meet-days-view.tsx`; заглушку Meet days в `competition-calendar.tsx` заменить.
4. `calendar-demo.ts` + подключение.
5. `docs/ui-components.md` — дополнить строку про `MeetCube` (варианты).

## Тесты

Клиентских тестов нет. `typecheck` + браузер.

## Проверка

1. `npm --prefix client run typecheck`.
2. Стенд `stack-5181-api5079-personas`:
   - `/competitions?view=meets&month=2026-07` — кубы всех стартов июля 2026, Maccabiah
     (`in_progress`) на 2 колонки с полосой дней, чемпионаты золотые;
   - `/competitions?calendarDemo=1` — в октябре 2026 четыре демо-куба: многодневный на 2
     колонки с `Day 1 · … ✓`, приглушённый Cancelled без ссылки, Postponed с датой, чемпионат
     открытой воды с текстом `🏆 Open water championship` вместо бейджа; плашка DEMO DATA;
     в Full month демо-meets видны плашками; без параметра — их нет;
   - месяц без meets в Meet days — «No meets this month» (сегмент Meet days при этом
     disabled — вход в пустой вид закрыт, проверь, что остаёмся в виде, если месяц сменили ‹ ›);
   - светлая/тёмная, 1180 и 375px (на 375 кубы в 1 колонку, без горизонтальной прокрутки).
3. Остановить стенд.

## Footguns

Спецификация §6. `col-span-2` в 1-колоночной сетке растягивает сетку до 2 колонок — span только с
`sm:`/`lg:`. Демо-режим не должен попадать в адрес при `writeCalendarQuery` (сохраняй чужие
параметры как есть, но сам `calendarDemo` не добавляй и не удаляй).

## Вне скоупа (не делать)

Телефон (неделя, лента дней, Type) — Б6; Records — Б7; сервер.

## Отчёт

Список файлов, что проверено (тема, ширина), раздел **«Отклонения»**.
