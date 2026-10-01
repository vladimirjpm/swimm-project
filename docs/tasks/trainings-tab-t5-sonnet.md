# Задание Sonnet — Т5 таба Trainings: вид Next участника (01.10.2026)

## Контекст

Таб Trainings группы переделывается по high-fidelity хендоффу
`!design_handoff/design_handoff_trainings_tab/` (`README.md` — **Structure → Mobile → Next**
(пункты Member), **Desktop → 3a**, **Tokens**, **Light theme**, **Rules**; прототип
`Trainings Tab.dc.html`: телефон участника — секция **2a**, строки ~419–440; десктоп участника —
**3a**, левая колонка, строки ~296–311). План — `docs/plans/trainings-tab-redesign-plan.md`
(§4 этап Т5, §3½ и таблица «Каркас Т2», §3 «Контракт Т1б»).

Сейчас вид Next у всех — прежняя карточка `trainings/sections/next-training-section.tsx`
(одно занятие, полоса/бассейн, сегмент ответа, перерыв, у тренера — список людей). Т5 строит
**вид Next УЧАСТНИКА** по прототипу. Вид тренера — этап Т6: у управляющего остаётся прежняя
секция, её не трогай.

## Решения (зафиксированы, не пересматривать)

1. **Кто видит новый вид**: `!manages` (участник-аккаунт). Управляющий (`manages`, в т.ч.
   тренер-участник) — прежняя `NextTrainingSection` до Т6. Реестр `trainings-sections.tsx`:
   ```ts
   NEXT_SECTIONS = [
     { id: 'member-next', when: (t) => !t.manages, Component: MemberNextSection },
     { id: 'next-training', when: (t) => t.manages, Component: NextTrainingSection },
     { id: 'lanes-entry', when: (t) => t.isDesktop && t.manages, Component: LanesEntrySection },
   ]
   ```
   (участнику на десктопе вход в Lanes — ссылка «Lanes ›» в бассейне, п. 6).
2. **Новые файлы** — `client/src/projects/hub-groups-project/components/trainings/next/`:
   - `answers-block.tsx` — «Your answers» на 3 занятия (п. 4);
   - `lane-block.tsx` — «Your lane» + бассейн + счётчики (п. 5–6);
   - `break-block.tsx` — строка перерыва (п. 7);
   - `next-format.ts` — чистые функции (дата строки, «Tonight», отсчёт «IN 2 H», «Back on»);
   - `trainings/sections/member-next-section.tsx` — секция: складывает блоки (п. 3).
3. **Раскладка секции** `member-next`:
   - **телефон** (2a) — три отдельные карточки столбиком, gap 14: (а) «Your answers» —
     `TrainingsSection` без заголовка-карточки (своя мелкая подпись, п. 4); (б) карточка
     ближайшего занятия с «Your lane», бассейном, счётчиками и быстрыми заметками; рамка этой
     карточки — `--deep-live-border` (как в прототипе); (в) строка перерыва (п. 7) — без карточки;
   - **десктоп** (3a) — ОДНА карточка (`TrainingsSection`, рамка `--deep-live-border`, padding
     16, gap 16): шапка (п. 5, крупно), «Your answers» сеткой из 3 мини-карточек, ряд быстрых
     заметок + справа ссылка «⏸ Taking a break?», затем полный бассейн (п. 6).
   - Нет `group.next_training` — одна карточка «Next training» `status="empty"`, текст как в
     нынешней секции: «No regular schedule yet — the next training shows here once the coach
     sets it.» (полноценные пустые состояния — Т9).
4. **«Your answers»** — занятия `group.next_trainings` (до 3; нет поля — `[group.next_training]`).
   Ответы: первое занятие — `rsvp` из контекста (тот же экземпляр, что у шапки страницы — ответ
   сразу виден и там); второе и третье — НОВЫЕ экземпляры `useTrainingRsvp(group.id, id,
   enabled)` в `trainings-context.tsx`: поле контекста `upcoming: (TrainingRsvpState | null)[]`
   длиной 3 (`[0]` — `rsvp`, `[1]`/`[2]` — новые хуки; хуки вызывать ВСЕГДА, с `sessionId`
   `undefined`, если занятия нет; `enabled` — как у страничного: `rsvp?.rsvp != null`).
   Строка (телефон): `min-height 48`, между строками рамка сверху 1px `--deep-card-border`;
   слева дата 13px/800 без переноса — `«Wed 30 Sep · 20:00»`, под ней подпись 10.5px/700:
   - ответ `yes` — «Going» (`--deep-live`); `maybe` — «Not sure» (`--deep-gold`); `no` —
     «Can't come» (`--deep-danger`); `set_by_coach` → « · by coach»;
   - нет ответа — у ПЕРВОГО занятия «No answer yet» цветом `--deep-danger`, у остальных
     `--t-text-3`; перед подписью «Tonight · » / «Tomorrow · », если дата — сегодня/завтра по
     Израилю (`todayInIsrael()` из `components/lane-plans-api.ts`);
   - зритель на перерыве (`on_break`) и не ответил — «On break» `--t-text-3`.
   Справа три кнопки 40×40, радиус 10, 14px/900, gap 4: ✓ ? ✕. Выбранная: ✓ — фон
   `--deep-live`, текст `--deep-accent-ink`, рамка `--deep-live`; ? — фон `--deep-gold-chip`,
   текст `--deep-gold`, рамка `--deep-gold-border`; ✕ — фон `--deep-danger-soft`, текст
   `--deep-danger`, рамка `--deep-danger`. Невыбранная — прозрачная, рамка `--deep-card-border`,
   текст `--t-text-3`. **Повторное нажатие выбранной снимает ответ** (`answer(null)`); заметку
   (`note`) при смене `yes`↔`maybe` сохраняем: `answer(next, mine?.note ?? null)`.
   `aria-label` «Going / Not sure / Can't come — Wed 30 Sep», `aria-pressed`. Нельзя ответить
   (`!can_answer`) или хук ещё грузит — кнопки `disabled`. Ошибка хука (`error`) — строкой
   12px `--deep-danger` под строкой. Подпись над списком: «YOUR ANSWERS» 10px/800 uppercase
   `letter-spacing .07em` `--t-text-3`.
   **Десктоп**: сетка `repeat(3, minmax(0,1fr))`, gap 8; мини-карточка радиус 12, фон
   `--deep-card-bg-raised`, рамка `--deep-card-border` (у первого занятия без ответа —
   `--deep-danger`), padding 10, gap 8; кнопки — сетка 3 колонки gap 4, высота 34, радиус 8,
   13px/900. Возле подписи «YOUR ANSWERS» точка 7px `--deep-danger`, если первое занятие без
   ответа и `can_answer`.
   После успешного «✓» у занятия, где `on_break`, перерыв снят сервером — перечитай перерывы
   (`breaks.reload()` — добавь в `components/use-group-breaks.ts` `reload`, по образцу
   `useTrainingRsvp.reload`) и ответы остальных двух (`reload()`).
5. **Шапка ближайшего занятия** (первое из списка; `r = rsvp.rsvp`):
   - телефон: слева `«Wed 30 · »` 15px/800 + время моно; справа отсчёт 10.5px/800 `--deep-live`
     (`untilLabel(date, start)`: сегодня — «IN 2 H» / «IN 40 MIN» (меньше часа) / «NOW» (уже
     началось, до конца); завтра — «TOMORROW»; иначе «IN N DAYS»). Время Израиля — через
     `Intl.DateTimeFormat('en-GB', { timeZone: 'Asia/Jerusalem', … })`, не часы машины;
   - десктоп (3a): строка 10px/800 uppercase `--deep-live` с точкой 7px «Next · tonight · in 2 h»
     (части: «Next», «tonight»/«tomorrow» если так, отсчёт в нижнем регистре); ниже
     `var(--deep-font-display)` 22px «Wed 30 Sep · » + время моно 20px «20:00–21:00»; ниже
     12.5px/700 `--t-text-3` место (`<bdi>`) и бассейн «· 50m»; справа счётчики моно 12px
     «**5** in · **1** maybe · **2** out» (числа цветами `--deep-live` / `--deep-gold` /
     `--deep-danger`).
6. **«Your lane» и бассейн** — из `r.lane_view` (`TrainingLaneView`, `types.ts`): моя дорожка —
   та, где в `people` есть `is_me`.
   - Есть дорожка: подпись «YOUR LANE» 10px/800 uppercase `--deep-pool-label` + `«2 · medium»`
     (`lane_no` + ` · ` + `level.name` в нижнем регистре, если уровень есть) —
     `var(--deep-font-display)` 18px; ниже задание `workout` моно 12px/800 (одной строкой:
     переводы строк → « · »; нет задания — строки нет).
   - Нет дорожки: ответа нет — «Answer “Going” or “Not sure” to get a lane.»; ответ есть, но я в
     `no_lane` — «You’re on the list — the coach hasn’t picked a lane yet.» (12px/700 `--t-text-3`).
   - **Мини-бассейн (телефон)**: рамка `--deep-pool-border`, фон `--deep-pool-water`, радиус 12;
     дорожка — строка `min-height 32`, `padding 4px 10px`, gap 8, между дорожками рамка сверху
     `1px dashed var(--deep-pool-rope)`, моя дорожка — фон `--deep-accent-chip`; слева подпись
     моно 10px/800 `--deep-pool-label` шириной 76 (`«1 · fast»`); справа кружки людей 20px.
     Весь бассейн — кнопка (`role="button"`, `aria-label="Open lanes"`) → `setView('lanes')`.
     `no_lane` — последней строкой «no lane». `source === 'water'` — одна строка «pool».
   - **Полный бассейн (десктоп)**: строки сеткой `110px 170px minmax(0,1fr) 40px`, gap 10,
     `min-height 46`, `padding 6px 14px`: подпись дорожки моно 11px/800 `--deep-pool-label`;
     задание моно 11px/700 `--t-text-3` (обрезка многоточием); кружки 26px, gap 5; в моей
     дорожке справа «YOU» 10px/800 `--deep-accent`. Над бассейном строка: слева 10px/800
     uppercase `--t-text-3` «Lanes · coach's plan · published» (подпись источника — как
     `sourceCaption` в `group-pool.tsx`, вынеси её экспортом), справа ссылка «Lanes ›» 12px/800
     `--deep-accent` → `setView('lanes')`.
   - **Кружки** — существующий `PoolCircle` из `components/group-pool.tsx`: экспортируй его и
     добавь проп `size?: 20 | 26` (по умолчанию 26; у 20 — шрифт 9px). Своё кольцо у `is_me` —
     как сейчас.
   - Под бассейном (телефон): слева моно 12px `--t-text-3` «**5** going · **1** maybe» (числа
     `--deep-live` / `--deep-gold`), справа «Lanes ›» 12px/800 `--deep-accent` →
     `setView('lanes')`.
   - `r.lane_view == null` (вида по дорожкам нет) — бассейна нет; счётчики остаются.
7. **Быстрые заметки и перерыв**:
   - заметки — существующий `RsvpNotes` (`group-pool.tsx`) для ПЕРВОГО занятия, только при
     моём ответе `yes`/`maybe` и `can_answer`: `onPick={(note) => rsvp.answer(mine, note)}`.
     Телефон — внизу карточки занятия; десктоп — ряд под «Your answers», справа ссылка
     «⏸ Taking a break?» 12px/800 `--deep-accent` (`margin-left: auto`);
   - **строка перерыва (телефон, `break-block.tsx`)** — только `r.is_member` и
     `breaks.data` загружен: `min-height 48`, `padding 0 12px`, радиус 12, рамка
     `1px dashed var(--deep-card-border)`, 12.5px/700 `--t-text-3`:
     - не на перерыве: «⏸» · «Taking a break?» (`flex:1`) · справа «Set dates ›» 800
       `--deep-accent` — вся строка кнопка → `openSheet({ kind: 'break' })`;
     - на перерыве (`breaks.data.mine`, `kind === 'break'`): «⏸ On break · back on Thu 15 Oct»
       (день возвращения = `until + 1 день`; без `until` — «⏸ On break») · справа кнопка
       «I'm back» 800 `--deep-accent` → `breaks.setBreak({ on_break: false })`;
     - `kind === 'not_training'` — строка без кнопок: «The coach marked you as not training
       with the group — if you come, answer and you’re counted that day.» (текст из нынешней
       секции);
   - на десктопе «Taking a break?» — ссылка в ряду заметок (выше); на перерыве вместо неё —
     «⏸ On break · back on … · I'm back» там же.
   - Подпись перерыва в прежней `MyBreakControl` (`group-pool.tsx`, её ещё рисует секция
     тренера) тоже переведи на «back on <день возвращения>» вместо «until <последний день>».
8. **Кто ещё рисует то же**: `LanePool`, `RsvpSegment`, `RsvpBar`, `RsvpPeopleList` остаются
   для тренерской секции (Т6) и полосы расписания — не удалять и не менять их вид (кроме
   экспорта `PoolCircle`/`sourceCaption` и пропа `size`).
9. Тексты — английские, дословно. Имена/места на иврите — `<bdi>`, `dir="auto"`. Цвета — только
   роли (`--t-*`, `--deep-*`). Тап-таргеты ≥ 40px (кнопки ответа 40 + отступы строки).
   `scrollIntoView` не использовать.

## Что уже готово (не переделывать)

- Каркас таба: `trainings-context.tsx` (`group`, `manages`, `isDesktop`, `view`/`setView`,
  `rsvp`, `breaks`, `openSheet`, `marks`), `trainings-section.tsx` (`TrainingsSection`),
  `trainings-sections.tsx` (реестр), шторки Т3 (`openSheet({ kind: 'break' })` — форма перерыва
  уже есть).
- `use-training-rsvp.ts` (`useTrainingRsvp(groupId, sessionId, enabled)` → `rsvp`, `loading`,
  `error`, `answer(next, note)`, `reload`), `components/use-group-breaks.ts`.
- `HubGroupDetails.next_trainings` (до 3, первое = `next_training`; `id` — ключ ответов) — сервер
  Т1б, готов. Типы `TrainingRsvp`, `TrainingLaneView`, `TrainingLanePerson` — `types.ts`.
- `formatNextDate` (`components/group-training-slots.tsx`), `todayInIsrael`, `formatPlanDate`
  (`components/lane-plans-api.ts`) — пользуйся, не дублируй.

## Шаги

1. `use-group-breaks.ts`: `reload()`. `trainings-context.tsx`: `upcoming` (решение 4).
2. `group-pool.tsx`: экспорт `PoolCircle` (+ `size`) и `sourceCaption`; подпись перерыва в
   `MyBreakControl` — «back on …».
3. `next/next-format.ts`, `next/answers-block.tsx`, `next/lane-block.tsx`, `next/break-block.tsx`.
4. `sections/member-next-section.tsx`; реестр `trainings-sections.tsx` (решение 1).
5. Строки в `docs/ui-components.md`: `PoolCircle` (экспорт и `size`), блоки `trainings/next/`.
6. План `docs/plans/trainings-tab-redesign-plan.md` не трогать — статус ставит ревьюер.

## Тесты

Клиентского раннера тестов нет — юнит-тестов не пиши. Проверка — `tsc` и сборка; живую
проверку на стенде делает ревьюер.

## Проверка

```bash
cd client && npx tsc --noEmit -p .
cd client && npx vite build
```
Оба без ошибок. Стенд не запускай, `dotnet` не трогай.

## Footguns

- В рабочей копии много чужих незакоммиченных правок (Т1–Т4 и файлы Влада) — **ничего не
  коммитить, `git add` не делать**, трогать только файлы задания.
- Хуки — безусловно и в одном порядке: два новых `useTrainingRsvp` в провайдере вызывай всегда.
- Даты — строки `yyyy-MM-dd` по Израилю: день недели и сдвиги — через `Date.UTC`/`getUTC*`;
  «сейчас» — через `Intl.DateTimeFormat` с `timeZone: 'Asia/Jerusalem'`.
- `until` перерыва — ПОСЛЕДНИЙ день; «back on» = `until + 1`.
- Роли `--t-*` есть только внутри класса темы — на странице группы он стоит.
- `PoolCircle` сейчас внутренний и с тенью `rgba(...)` — тень не трогай, только `size`.

## Вне скоупа (не делать)

- Вид Next тренера, перекличка, нижняя панель тренера, тост Undo — Т6.
- Золотая заметка «Coach moved you from lane 3 to 2» и метки внимания — Т8 (нужен
  `LanePlan.my_swimmers[].moved_from_lane`).
- Вид Lanes — Т7. Пустые состояния целиком — Т9.
- Сервер, новые токены, шапка страницы группы и баннер «Are you coming?».
