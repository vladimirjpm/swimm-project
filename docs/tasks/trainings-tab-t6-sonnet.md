# Задание Sonnet — Т6 таба Trainings: вид Next тренера (01.10.2026)

## Контекст

Таб Trainings группы переделывается по high-fidelity хендоффу
`!design_handoff/design_handoff_trainings_tab/` (`README.md` — **Structure → Mobile → Next**
(пункты Coach), **Desktop → 3b**, **Tokens**, **Rules**, **State**; прототип
`Trainings Tab.dc.html`: телефон тренера — секция **2a**, строки ~484–512; десктоп тренера —
**3b**, строки ~340–372; шторка человека — **1d #3**, строки ~847–860; данные статусов —
`const PILL`, `rows1c`, `f1c`, строки ~963–965). План — `docs/plans/trainings-tab-redesign-plan.md`
(§4 этап Т6, §3½ и таблица «Каркас Т2»).

Т5 сделал вид Next участника (`trainings/next/`, секция `member-next`). У управляющего пока
прежняя `sections/next-training-section.tsx` (одно занятие, бассейн, длинный `RsvpPeopleList`).
Т6 заменяет её **видом тренера по прототипу**: навигация по трём ближайшим занятиям ‹ ›,
полоса ответов, мини-бассейн со статусом плана, фильтр-чипы, перекличка, где у человека ОДНА
кнопка статуса по циклу и ⋮, шторка человека, тост `… · Undo` 5 с.

## Решения (зафиксированы, не пересматривать)

1. **Реестр** `trainings/trainings-sections.tsx`:
   ```ts
   NEXT_SECTIONS = [
     { id: 'member-next', when: (t) => !t.manages, Component: MemberNextSection },
     { id: 'coach-next',  when: (t) => t.manages,  Component: CoachNextSection },
   ]
   ```
   `sections/next-training-section.tsx` и `sections/lanes-entry-section.tsx` — удалить
   (вход в Lanes у тренера теперь в блоке бассейна, п. 5). Компоненты `group-pool.tsx` /
   `group-rsvp.tsx`, у которых после этого НЕ останется потребителей (проверь `grep` по
   `client/src`: вероятно `RsvpPeopleList`, `MyBreakControl`, `LanePool`, `RsvpSegment`), —
   удалить вместе с их строками в `docs/ui-components.md`. Что ещё используется (`RsvpBar`,
   `RsvpButtons`, `RsvpStaffRow`, `RsvpNotes`, `PoolCircle`, `sourceCaption`) — не трогать.
2. **Нижней панели тренера из прототипа НЕ делаем.** На телефоне у управляющего уже закреплена
   снизу панель каркаса «COACH TOOLS» (Trainings / Admin — `deep-tabs-tools`,
   `components/deep/deep-theme.css`); вторая фиксированная панель легла бы на неё. «✎ YOU CAN
   EDIT» уже есть у панели таба, «+ New training» — в ленте (Т4). Тост (п. 9) встаёт НАД этой
   панелью.
3. **Новые файлы** — `client/src/projects/hub-groups-project/components/trainings/coach/`:
   - `session-nav.tsx` — навигация ‹ › по занятиям (п. 4);
   - `coach-pool-block.tsx` — бассейн + статус плана + «Lanes ›» / «✎ Edit lanes» / «Publish» (п. 5);
   - `roll-call.tsx` — сводка, полоса, фильтр-чипы, строки (п. 6–8);
   - `roll-toast.tsx` — тост Undo (п. 9);
   - `roll-format.ts` — чистые функции: группа человека, порядок, подписи статуса, мета;
   - `trainings/sheets/person-sheet.tsx` — шторка человека (п. 10), запись `person` в реестре
     `trainings-sheets.tsx`;
   - `trainings/sections/coach-next-section.tsx` — секция: складывает блоки (п. 11).
4. **Выбранное занятие** — состояние контекста `trainings-context.tsx`: `rollIndex: number`
   (0..2, по умолчанию 0) + `setRollIndex`. Ответы занятия — `upcoming[rollIndex]` (Т5: `[0]` —
   страничный `rsvp`, `[1]`/`[2]` — свои хуки; у управляющего сервер отдаёт `people`). Список
   занятий — `group.next_trainings` (нет — `[group.next_training]`).
   **Навигация** (`session-nav.tsx`): кнопки ‹ › (телефон 40×40, десктоп 36×36, радиус 10,
   рамка `--deep-card-border`, текст `--t-text-3`, `disabled` на краях, `aria-label` «Previous
   training» / «Next training»); посередине:
   - телефон — по центру дата 15px/800 `«Wed 30 Sep · »` + время моно, под ней 10.5px/800
     `--deep-live`: «NEXT · IN 2 H» (у первого занятия; у остальных — только отсчёт «IN 4
     DAYS»). Отсчёт — `untilLabel` из `trainings/next/next-format.ts` (Т5), не дублировать;
   - десктоп — слева: строка 10px/800 uppercase `--deep-live` с точкой 7px «Next · in 2 h»
     (первое) / «In 4 days»; ниже `var(--deep-font-display)` 20px дата + время моно 18px
     «20:00–21:00».
5. **Бассейн тренера** (`coach-pool-block.tsx`) — из `upcoming[rollIndex].rsvp.lane_view`:
   - телефон — мини-бассейн `MiniPool` из `trainings/next/lane-block.tsx` (Т5) без подсветки
     «моей» дорожки; тап — `setView('lanes')`;
   - десктоп — полный бассейн `FullPool` оттуда же, без колонки YOU;
   - **статус плана на дату занятия** — новое поле контекста `lanePlans: LanePlanSummary[] |
     null` (только `manages`: `lanePlansApi.list(group.id)` из `components/lane-plans-api.ts`,
     один раз при открытии таба + `reloadLanePlans()`). По дате занятия: черновик — золотая
     пилюля «● Draft plan» (рамка `--deep-gold-border`, фон `--deep-gold-chip`, текст
     `--deep-gold`, точка `--deep-gold-bar`, 10–11px/700); опубликован — «Published»
     `--deep-live`; плана нет — «No lane plan» `--t-text-3`. Число неразложенных НЕ
     показываем (его даёт только полный план — Т7/Т8).
   - телефон: эта строка — подвал мини-бассейна (`padding 6px 10px`, рамка сверху
     `--deep-pool-border`, 11px/700): слева статус, справа «Lanes ›» 800 `--deep-ow` →
     `setView('lanes')`;
   - десктоп: строка над бассейном: слева «LANES» 10px/800 uppercase `--t-text-3` + пилюля
     статуса; справа кнопки 30px (радиус 9, 12px/800): «✎ Edit lanes» (рамка
     `--deep-ow-border`, фон `--deep-ow-chip`, текст `--deep-ow`) → `setView('lanes')`; при
     черновике — «Publish» (фон `--deep-ow`, текст `--deep-accent-ink`) →
     `lanePlansApi.publish(group.id, date)`, затем `reloadLanePlans()` и `reload()` ответов
     занятия (вид по дорожкам поменяется на план); ошибка — строкой `--deep-danger`.
   - `lane_view == null` — бассейна нет, строка статуса плана остаётся.
6. **Сводка и полоса** (`roll-call.tsx`): строка «ROLL CALL» 10px/800 uppercase `--t-text-3`
   + красная пилюля с числом не ответивших (п. 7, группа `none`; 18px, фон `--deep-danger`,
   текст `--deep-accent-ink`, моно 10.5px; ноль — пилюли нет); справа моно 11.5px
   `--t-text-3`: «**5** going · **1** maybe · **2** out · 2 —» (числа `--deep-live` /
   `--deep-gold` / `--deep-danger`; «— » — не ответившие). Под ней полоса 6px, радиус 3, фон
   `--deep-seg-track`: доли yes / maybe / no от `total` цветами `--deep-live` /
   `--deep-gold-bar` / `--deep-danger`. На телефоне строка «ROLL CALL» не нужна — сводка и
   полоса идут сразу под бассейном.
7. **Группы и порядок** (`roll-format.ts`, `groupOf(person)`): `not_training` — человек с
   `not_training` и без ответа; `break` — `on_break` и без ответа; иначе по `answer`: `yes` /
   `maybe` / `no` / `none` (нет ответа). Порядок строк: `none`, `yes`, `maybe`, `no`, `break`,
   `not_training`; внутри — как пришло с сервера.
   **Фильтр-чипы**: `All · — · ✓ · ? · ✕ · ⏸` с числом (моно 10.5px), высота 34 (телефон) /
   30 (десктоп), `padding 0 10px`, радиус 999, 12px/800, gap 5, перенос строк разрешён.
   Выбранный — `--deep-ow-chip` / `--deep-ow-border` / `--deep-ow`; невыбранный — прозрачный,
   рамка `--deep-card-border`, текст `--t-text-3`. `⏸` — группа `break`; `not_training`
   видно только в `All`. Пустая группа — чип есть, число 0. Фильтр — состояние блока, по
   умолчанию `All`; смена занятия (‹ ›) фильтр не сбрасывает.
8. **Строка человека** (`roll-call.tsx`):
   - телефон: список в карточке (радиус 16, рамка `--deep-card-border`, фон `--deep-card-bg`,
     padding 6, gap 2); строка `min-height 52`, радиус 12, `padding 4px 0 4px 8px`, gap 8;
     аватар 28 (буква имени, фон по полу `--deep-female` / `--deep-male`, иначе
     `--deep-text-mute`; текст `--deep-accent-ink` 11px/900); имя 13px/800 `<bdi>`,
     `dir="auto"`, обрезка в конце; мета 10.5px/700 `--t-text-3` (п. ниже); **кнопка статуса**
     `min-width 100`, высота 40, радиус 10, 12px/800; **⋮** 30×44, 16px/900 `--t-text-3`,
     `aria-label` «More for <name>»;
   - десктоп: **две колонки** (`grid repeat(2, minmax(0,1fr))`, gap 4), без внешней карточки;
     строка `min-height 44`, радиус 10, `padding 3px 0 3px 8px`; аватар 26 (10.5px); имя 12.5px;
     мета 10px; кнопка статуса `min-width 88`, высота 32, радиус 8, 11.5px; ⋮ 24×32 15px. Под
     сеткой подсказка 10.5px/700 `--t-text-3`: «Click status: — → ✓ → ? → ✕ · ⋮ for status,
     break, note».
   - **кнопка статуса** (стили — `PILL` прототипа на ролях): ✓ «✓ Going» — фон `--deep-live`,
     текст `--deep-accent-ink`, рамка `--deep-live`; ? «? Not sure» — `--deep-gold-chip` /
     `--deep-gold` / `--deep-gold-border`; ✕ «✕ Can't» — `--deep-danger-soft` /
     `--deep-danger` / `--deep-danger`; нет ответа «— Answer» — прозрачный, `--t-text-3`, рамка
     `1px dashed var(--deep-card-border)`; перерыв без ответа «⏸ Break» — прозрачный,
     `--t-text-3`, рамка сплошная `--deep-card-border`. **Нажатие — цикл** `— → ✓ → ? → ✕ → —`
     (у «⏸ Break» и `not_training` следующий — ✓): `rsvp.answerFor(person, next,
     person.note ?? null)`. Нельзя ответить (`!can_answer`) — `disabled`. После ✓ у человека на
     перерыве — `breaks.reload()` (сервер снял перерыв). `aria-label` «<name>: <статус>.
     Change».
   - **мета** (`roll-format.ts`, части через « · »): заметка («late ~10 min», «1st hour only»,
     «leaving early» — нижний регистр подписей `RsvpNotes`); «by coach» при `set_by_coach`;
     «no account» при `user_id == null`; на перерыве — «until Oct 12» (последний день из
     `breaks.data.breaks` по `user_id`/`swimmer_id`; бессрочно — «on break»); `not_training` —
     «doesn't train with us»; `back_after_days` — «back after N days».
   - **долгое нажатие** на строку (500 мс, отмена при сдвиге > 8px или отпускании) и ⋮ →
     `openSheet({ kind: 'person', person })`; клик, завершивший долгое нажатие, кнопку статуса
     не нажимает.
   - строка, изменённая последней (пока виден тост), — фон `--deep-ow-soft`, остальные —
     `--deep-card-bg-raised`.
   - людей нет — «The group has no member accounts yet — people answer after they join the
     group.» (текст из нынешнего `RsvpPeopleList`).
9. **Тост** (`roll-toast.tsx`): после каждого изменения ответа тренером (кнопка статуса или
   шторка) — `«<name> → ✓ Going»` (`<bdi>` у имени; «→ — No answer» при снятии) и кнопка
   «Undo»; живёт 5 с, новое изменение заменяет прежний тост. Undo — `answerFor(person,
   prevAnswer, prevNote)` и тост закрывается. Вид: `min-height 48`, радиус 12,
   `padding 0 6px 0 14px`, 12.5px/700, фон `--deep-text`, текст `--deep-page-bg` (инверсия
   темы); «Undo» — высота 36, `padding 0 12px`, радиус 8, рамка `1px solid var(--deep-page-bg)`,
   800. Портал в `body` с классом `useDeepThemeClass()`, `position: fixed`, `z-[110]` (выше
   панели каркаса 40, ниже шторок 125): телефон — `left: 12px; right: 12px; bottom:
   calc(96px + env(safe-area-inset-bottom))` (над панелью COACH TOOLS); десктоп — `left: 20px;
   bottom: 20px; width: 420px`. `role="status"`, `aria-live="polite"`. Состояние тоста и
   «последней изменённой строки» — в `roll-call.tsx`; шторка человека сообщает об изменении
   через колбэк контекста (п. 10).
10. **Шторка человека** (`person-sheet.tsx`, `DeepSheet` из `components/deep/deep-sheet.tsx`,
    тон `coach`):
    - заголовок — имя (`<bdi>`); `sub`: дорожка из `lane_view` занятия («Lane 2 · medium»), если
      человек на дорожке; «joined account» при `user_id`, иначе «no account — the coach
      answers for them»;
    - блок ответа: подпись `«WED 30 SEP · ANSWER»` 10px/800 uppercase `--t-text-3`; сегмент из
      трёх кнопок высотой 44 на дорожке `--deep-seg-track` (радиус 12, padding 3): «✓ Going ·
      ? Not sure · ✕ Can't», выбранная — цвета п. 8; повторное нажатие выбранной — «нет
      ответа»; при `yes`/`maybe` — чипы заметок `RsvpNotes` (`group-pool.tsx`), повторный тап —
      снять;
    - блок статуса: подпись «STATUS»; три строки-радио (`min-height 48`, радиус 12,
      `padding 0 12px`, рамка `--deep-card-border`, выбранная — рамка `--deep-ow-border`, фон
      `--deep-ow-soft`, кружок 18px с точкой `--deep-ow`): «Active» / «Trains with the group»;
      «⏸ On break» / «Answering "Going" ends it» + справа поле даты (моно 12px/800, высота 34,
      рамка `--deep-ow-border`, текст `--deep-ow`) — ПОСЛЕДНИЙ день перерыва, пусто — бессрочно;
      «Doesn't train with us» / «In records, never in the pool or lanes». Текущее — по
      `on_break` / `not_training` и `breaks.data.breaks`;
    - подвал: Cancel / «Save» (coach). **Всё применяется по Save**: изменился ответ/заметка —
      `answerFor(person, answer, note)`; изменился статус или дата — `breaks.setBreak({ user_id
      | swimmer_id, status, until })` (`status: 'active' | 'break' | 'not_training'`,
      `until` — только у `break`), затем `rsvp.reload()`. Ошибки — `SheetError`, шторка
      остаётся. Изменение ответа даёт тост (п. 9) — через новое поле контекста
      `onRollChange?: (change: { person, prev: {answer, note}, next: {answer, note} }) => void`,
      которое регистрирует `roll-call.tsx`.
    - `useTrainingRsvp.answerFor` получает третий параметр `note: RsvpNote | null = null` и
      шлёт его в теле (`{ answer, note, user_id }` / `{ answer, note, swimmer_id }`); сервер
      заметку за другого уже принимает.
11. **Раскладка секции** `coach-next`:
    - телефон (2a) — столбиком, gap 12: навигация; бассейн с подвалом статуса плана; сводка и
      полоса; фильтр-чипы; список. Всё вне карточек, кроме бассейна и списка (как в 2a), в
      боковой линии `deep-panel-row`;
    - десктоп (3b) — одна карточка `TrainingsSection` (рамка `--deep-live-border`, padding 16,
      gap 14 — проп `style`, есть с Т5): навигация; блок бассейна (п. 5); блок переклички (п.
      6–8, две колонки).
    - нет занятия (`next_training == null`) — `status="empty"` с текстом «No regular schedule
      yet — set it in Admin → Training schedule.» (полноценное — Т9); ответы грузятся —
      `loading`; ошибка — `error`.
12. Тексты — английские, дословно. Имена — `<bdi>` + `dir="auto"`. Цвета — только роли.
    Тап-таргеты ≥ 40 (⋮ — 44 в высоту). `scrollIntoView` не использовать.

## Что уже готово (не переделывать)

- Каркас и контекст `trainings/trainings-context.tsx` (`group`, `manages`, `isDesktop`,
  `setView`, `upcoming`, `rsvp`, `breaks` с `reload`, `openSheet`); рамка `TrainingsSection`
  (`style`); реестр форм `trainings-sheets.tsx` (тип `person` уже объявлен: `{ kind: 'person';
  person: TrainingRsvpPerson }`); `DeepSheet`, `SheetButton`, `SheetField`, `SheetError`,
  `SheetFooter`.
- Т5: `trainings/next/lane-block.tsx` (`MiniPool`, `FullPool`), `next-format.ts`
  (`untilLabel`, даты), `PoolCircle`, `sourceCaption`, `RsvpNotes`.
- `use-training-rsvp.ts` (`answerFor`, `reload`, `rsvpPersonKey`), `use-group-breaks.ts`
  (`setBreak`, `reload`, `data.breaks`), `lane-plans-api.ts` (`list`, `publish`), типы
  `TrainingRsvpPerson`, `BreakInput`, `PersonStatus`, `LanePlanSummary` — `types.ts`.
- `components/person-status.tsx` (`statusOf`) — посмотри, переиспользуй, что подходит.

## Шаги

1. `use-training-rsvp.ts`: `note` в `answerFor`. `trainings-context.tsx`: `rollIndex`,
   `lanePlans` + `reloadLanePlans`, `onRollChange` (регистрация колбэка).
2. `coach/roll-format.ts`, `coach/session-nav.tsx`, `coach/coach-pool-block.tsx`,
   `coach/roll-call.tsx`, `coach/roll-toast.tsx`.
3. `sheets/person-sheet.tsx` + запись `person` в `SHEETS`.
4. `sections/coach-next-section.tsx`; реестр (решение 1); удалить старые секции и осиротевшие
   компоненты.
5. `docs/ui-components.md`: строки новых блоков `trainings/coach/`, удалённых компонентов.
6. План `docs/plans/trainings-tab-redesign-plan.md` не трогать.

## Тесты

Клиентского раннера тестов нет — юнит-тестов не пиши. Проверка — `tsc` и сборка; живую
проверку делает ревьюер.

## Проверка

```bash
cd client && npx tsc --noEmit -p .
cd client && npx vite build
```
Оба без ошибок. Стенд не запускай, `dotnet` не трогай.

## Footguns

- В рабочей копии много чужих незакоммиченных правок (Т1–Т5 и файлы Влада) — **ничего не
  коммитить, `git add` не делать**, трогать только файлы задания.
- `upcoming[i]` — хук ответов; у управляющего `people` есть, у участника — `null`.
- Перерыв: `until` — ПОСЛЕДНИЙ день; у тренера можно без даты (бессрочно).
- Порталу (тост) — свой класс темы, иначе `--t-*`/`--deep-*` пустые.
- Долгое нажатие: `pointerdown`/`pointermove`/`pointerup`/`pointercancel`, таймер чистить при
  размонтировании; не мешать прокрутке (не `preventDefault` на `pointerdown`).
- `sessions`/`rsvp` из контекста — новые объекты на каждый рендер; не клади их в зависимости
  эффектов, которые что-то пишут.

## Вне скоупа (не делать)

- Нижняя панель тренера (решение 2). Метки на сегментах/табе, красная пилюля на сегменте Next —
  Т8 (в Т6 — только пилюля в «ROLL CALL»).
- Число неразложенных в плане, редактор дорожек — Т7. Пустые состояния целиком — Т9.
- Прошлые занятия (сервер даёт тренеру 7 дней назад) — навигация только по трём ближайшим.
- Сервер, новые токены, вид участника (Т5).
