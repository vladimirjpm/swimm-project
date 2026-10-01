# Задание Sonnet — Т8 таба Trainings: метки внимания (01.10.2026)

## Контекст

Таб Trainings группы переделывается по хендоффу `!design_handoff/design_handoff_trainings_tab/`
(`README.md` — разделы **Attention marks** и **Related places (6a)**; `marks-spec.md` — правила
меток целиком, прочитай его первым). План — `docs/plans/trainings-tab-redesign-plan.md` (§3
«Контракт Т1б» — что отдаёт сервер; §3½ — правило модульных секций; §4 строка Т8).

Метка внимания — точка или пилюля-число рядом с местом, где тренеру или участнику есть что
сделать. Сервер для них готов (Т1б), клиент пока рисует места под метки, но считает их
заглушкой: `trainings/use-training-marks.ts` возвращает `NO_MARKS`. Т8 — посчитать метки,
показать их во всех местах ниже и гасить «новое» при открытии вида.

Все пути ниже — от `client/src/projects/hub-groups-project/`.

## Решения (зафиксированы, не пересматривать)

1. **Три метки, одна на место, зелёных нет** (`marks-spec.md`):
   - **красная** (`red`) — нет ответа на БЛИЖАЙШЕЕ занятие (`group.next_training`, то есть
     `rsvp` контекста, а не выбранное у тренера `rollIndex`):
     - тренер — число `rsvp.people.filter(p => p.answer == null && !p.on_break && !p.not_training).length`,
       метка только при числе > 0, `count` = число, `label` «N haven’t answered»;
     - участник — точка, если `rsvp.can_answer && rsvp.mine == null && !rsvp.on_break`,
       `label` «You haven’t answered»;
   - **золотая** (`gold`) — план дорожек на дату ближайшего занятия:
     - тренер — план есть и (`status === 'draft'` или `unassigned.length > 0`); `label`
       «Draft plan» / «N unassigned» / «Draft · N unassigned» (по тому, что верно); без числа —
       точка;
     - участник — в этом плане у кого-то из `my_swimmers` `moved_from_lane != null`; `label`
       «Coach moved you» (есть `kind: 'me'`) / «Coach moved your swimmer»;
   - **акцентная** (`accent`) — `sessions.data.new_media_count > 0`, точка, `label`
     «N new videos».
   Куда какая: `marks.next` — красная, `marks.lanes` — золотая, `marks.trainings` — акцентная.
2. **Расчёт — чистыми функциями** в `trainings/use-training-marks.ts` (экспорт, без React):
   `redMark(rsvp: TrainingRsvp | null): TrainingMark | null`,
   `goldMark(plan: LanePlan | null, manages: boolean): TrainingMark | null`,
   `accentMark(list: TrainingSessionList | null): TrainingMark | null`. Хук
   `useTrainingMarks(input)` собирает из них `TrainingMarks` и учитывает «уже видел» (п. 5).
   `redMark` нужен и вне таба (п. 7) — поэтому функция, а не только хук.
3. **Полный план дорожек ближайших занятий — в контексте таба** (`trainings-context.tsx`):
   новое поле `upcomingPlans: Record<string, LanePlan | null>` — ключ дата (`yyyy-MM-dd`), для
   РАЗНЫХ дат `nextSessions(group)` (до трёх). Грузится `lanePlansApi.get(group.id, date)`; 404
   → `null` (плана нет — это не ошибка); пока не пришло — ключа нет. Обеим ролям (участнику
   сервер отдаёт только опубликованный — черновик даёт 404, это и нужно). Перечитывается по
   `reloadLanePlans()` (тот же счётчик версии, что у `lanePlans`). Секции планы сами НЕ грузят.
4. **«Draft · N unassigned» у тренера** (`trainings/coach/coach-pool-block.tsx`, `PlanPill`):
   для черновика с неразложенными — «Draft · 3 unassigned» (тот же золотой вид, что «Draft
   plan»); черновик без неразложенных — «Draft plan», как сейчас; опубликованный с
   неразложенными — «Published · 3 unassigned» (текст `--deep-live`, число — `--deep-gold`).
   Число — `upcomingPlans[session.date]?.unassigned.length` выбранного занятия; плана в
   `upcomingPlans` ещё нет — пилюля как сейчас, без числа. Убери из шапки файла фразу «Число
   неразложенных не показываем — его даёт только полный план (Т7/Т8)».
5. **Гашение «нового»**:
   - **Trainings** — при первом показе ленты за жизнь таба вызвать `sessions.markSeen()`
     (`PUT …/sessions/seen`, уже есть в `use-training-sessions.ts`). «Показ ленты» — телефон:
     `view === 'trainings'`; десктоп: `view !== 'lanes'` (лента — правая колонка). Сразу после
     вызова акцентная метка гаснет ЛОКАЛЬНО (флаг `trainingsSeen` в контексте → `useTrainingMarks`
     отдаёт `trainings: null`); ленту НЕ перечитывать — подписи «3 NEW» на карточках остаются до
     следующего захода (так и задумано: метка гаснет, подпись объясняет, что нового).
     Вызывать только если `new_media_count > 0` (иначе гасить нечего). Один раз за монтирование.
   - **Lanes** — участнику (`!manages`) при первом открытии `view === 'lanes'` за жизнь таба
     вызвать `lanePlansApi.markSeen(group.id)` (`PUT …/lane-plans/seen`, уже есть в
     `lane-plans-api.ts`), только если золотая метка участника сейчас горит. После — золотая
     гаснет локально (флаг `lanesSeen`); золотую заметку «Coach moved you…» на доске НЕ трогать —
     план уже загружен, она видна до следующего захода. Тренеру Lanes не гасит ничего — его
     золотая метка держится, пока план черновик / есть неразложенные (red и gold «persist until
     resolved»).
   - Флаги и вызовы — в контексте (одно место), не в секциях.
6. **Места внутри таба:**
   - сегменты `Next · Trainings · Lanes` (`trainings-segments.tsx`) — уже рисуют `marks[id]`
     через `MarkDot`; ничего не менять, кроме того, что метки теперь не пустые;
   - десктоп: заголовок ленты уже рисует `marks.trainings`; золотая — **точка рядом с «Lanes ›»**
     в блоках бассейна: `coach/coach-pool-block.tsx` (кнопка «Lanes ›» в подвале на телефоне и
     в строке над бассейном на десктопе) и `next/lane-block.tsx` (обе ссылки «Lanes ›»
     участника — подвал мини-бассейна и шапка полного). Точка — `MarkDot` из
     `trainings-section.tsx` с `marks.lanes` (без числа: если у метки есть `count`, в этом месте
     его не рисуй — передай `{ ...mark, count: undefined }`). Это и есть «pool footer» хендоффа;
   - `MarkDot` точкой — 8px сейчас; хендофф говорит «Red dot 7px» — поменяй на 7×7.
7. **Места вне таба** (страница группы, `group-page.tsx` и компоненты шапки) — только
   **красная**, она бесплатна (ответы `rsvp` страница уже грузит). Золотую и акцентную вне
   таба НЕ показываем (`marks-spec.md`: «load lazily…» — отложено, см. «Вне скоупа»):
   - **таб группы «Trainings» у тренера** (закреплённый «Coach tools»): красная пилюля с
     числом. В `components/deep/tabs.tsx` у `DeepTabItem` новое поле
     `badgeTone?: 'danger'`; при нём бейдж — пилюля: `min-width 18px; height 18px; padding 0
     5px; border-radius 999px; background var(--deep-danger); color var(--deep-accent-ink);`
     моно 10.5px/800, `opacity 1`, без префикса «· » мобильной версии. Класс-модификатор
     `deep-tool-tab__badge--danger` в `components/deep/deep-theme.css` рядом с
     `.deep-tool-tab__badge` (строки ~507 и ~693 — мобильное правило `::before { content: '· ' }`
     для модификатора отключить). В `group-page.tsx` у таба `trainings`: `badge` = число из
     `redMark(rsvp.rsvp)` только при `manages`, `badgeTone: 'danger'`; `entity-page.tsx`
     пробрасывает поля таба — допиши `badgeTone` туда же, где `badge` (строки ~128–130).
     Участнику таб не помечаем (у него таб не закреплён, бейджа там нет — так и оставить);
   - **полоса «Next» в шапке** (`components/group-training-slots.tsx`, строка с «Next · дата ·
     время»): после времени — `MarkDot` красной метки (участнику точка, тренеру пилюля с
     числом), если `redMark(r)` не null;
   - **липкий чип** (`components/group-rsvp.tsx` → `rsvpStickyChip`, рисует `GroupStickyStatus`
     в `group-page.tsx`): участнику без ответа — текст `Answer · Wed 20:00` с красной точкой
     ПЕРЕД текстом, рамка `--deep-danger`, фон `--deep-card-bg`, текст `--deep-text` (было
     «Wed 20:00 · going?» зелёным); тренеру — как сейчас «5 ✓ · 1 ? · 2 ✕» и после него красная
     пилюля с числом не ответивших. Для этого `rsvpStickyChip` возвращает ещё
     `mark: TrainingMark | null`, а `GroupStickyStatus` рисует `MarkDot` (участнику — до текста,
     тренеру — после);
   - баннер «Are you coming?» (`group-rsvp-banner.tsx`) уже показывается только не ответившим —
     не трогать.
8. Цвета — только роли (`--deep-*`, `--t-*`), без хексов. Тексты — английские, дословно как
   выше. `MarkDot` уже ставит `role="img"` + `aria-label` + `title` = `label` — так и оставить.
9. **Порядок хуков**: новые запросы в контексте — через `useEffect` с отменой (`cancelled`),
   как у `lanePlans`; хуки вызываются всегда и в одном порядке.

## Что уже готово (не переделывать)

- Сервер Т1б целиком: `new_media_count` / `is_new` в `GET …/sessions`, `PUT …/sessions/seen`,
  `my_swimmers[].moved_from_lane` в `GET …/lane-plans/{date}`, `PUT …/lane-plans/seen`. Типы —
  `types.ts` (`TrainingSessionList.new_media_count`, `LanePlan.my_swimmers[].moved_from_lane`,
  `TrainingRsvp`, `TrainingRsvpPerson.on_break / not_training`).
- `use-training-sessions.ts`: `markSeen()`; `lane-plans-api.ts`: `get`, `list`, `markSeen`.
- `trainings/use-training-marks.ts`: типы `TrainingMark`, `TrainingMarks`, `NO_MARKS`,
  `MARK_COLOR` — контракт, на него уже смотрят места показа.
- `trainings/trainings-section.tsx`: `MarkDot`, проп `mark` у `TrainingsSection`.
- `trainings/trainings-segments.tsx`, `sections/training-feed-section.tsx` — уже рисуют метки.
- `trainings/feed/training-card.tsx` — подпись «N NEW» по `is_new` уже есть.
- `trainings/next/next-format.ts`: `nextSessions(group)` — до трёх ближайших занятий.
- Контекст: `lanePlans` + `reloadLanePlans` (список планов тренеру), `rsvp`, `sessions`.
- Золотая заметка «Coach moved you from lane 3 to 2» на доске участника (`group-lane-board.tsx`, Т7).

## Шаги

1. `use-training-marks.ts`: чистые `redMark`, `goldMark`, `accentMark` (решения 1–2), хук
   `useTrainingMarks({ rsvp, manages, nextPlan, sessions, trainingsSeen, lanesSeen })` → `TrainingMarks`.
   Обнови шапочный комментарий файла (убери «⚠ Т2 — только контракт…»).
2. `trainings-context.tsx`: `upcomingPlans` (решение 3), флаги `trainingsSeen` / `lanesSeen` и
   вызовы `markSeen` (решение 5), `marks` — из нового хука (план ближайшего —
   `upcomingPlans[group.next_training?.date]`). Новое поле `upcomingPlans` — в
   `TrainingsTabState` с комментарием. Обнови шапочный комментарий контекста.
3. `coach/coach-pool-block.tsx` — «Draft · N unassigned» (решение 4) + золотая точка у «Lanes ›».
4. `next/lane-block.tsx` — золотая точка у «Lanes ›» участника.
5. `trainings-section.tsx` — `MarkDot` точкой 7×7.
6. Вне таба (решение 7): `components/deep/tabs.tsx` + `deep-theme.css` + `entity-page.tsx`
   (`badgeTone`), `group-page.tsx` (бейдж таба, `GroupStickyStatus`), `components/group-rsvp.tsx`
   (`rsvpStickyChip`), `components/group-training-slots.tsx` (точка у «Next»).
7. `docs/ui-components.md` — если там описаны `DeepTabs` / бейдж таба или `MarkDot`, допиши
   `badgeTone` / метки; если нет — не заводи новый раздел.
8. План `docs/plans/trainings-tab-redesign-plan.md` и передачу не трогай — их обновит ревьюер.

## Тесты

Клиентского раннера тестов нет — юнит-тестов не пиши. Серверных правок нет. Проверка — `tsc`
и сборка; живую проверку делает ревьюер.

## Проверка

```bash
cd client && npx tsc --noEmit -p .
cd client && npx vite build
```
Оба без ошибок. Стенд не запускай, `dotnet` не трогай.

## Footguns

- В рабочей копии есть чужие файлы Влада (`!design_handoff/…`) — **ничего не коммитить,
  `git add` не делать**, трогать только файлы задания.
- `rsvp` (ближайшее занятие) и `upcoming[rollIndex]` (выбранное тренером) — разные вещи:
  красная метка и бейдж таба — всегда по `rsvp`, «Draft · N unassigned» в бассейне — по
  выбранному занятию.
- `group.next_training` может быть `null` (нет расписания) — тогда меток нет, запросов нет.
- `lanePlansApi.get` участнику на черновик отвечает 404 — это «плана нет», не ошибка; не
  показывай ошибку и не ретрай.
- `markSeen` вызывать один раз за монтирование таба (флаг в `useRef`), не на каждый рендер и не
  на каждое переключение видов туда-обратно.
- `MarkDot` рисует пилюлю, когда у метки есть `count` — в местах «только точка» (`Lanes ›`)
  передавай метку без `count`.
- `deep-theme.css` общий для всех страниц темы deep — модификатор только добавляй, чужие
  правила не меняй.
- Роли `--t-*` есть только внутри класса темы — на странице группы он стоит.
- `scrollIntoView` не использовать.

## Вне скоупа (не делать)

- Золотая и акцентная метки ВНЕ таба (таб группы, шапка) — отдельным решением, если понадобится
  (`marks-spec.md` предлагает ленивую подгрузку; `/me/marks` не заводить).
- Метки на карточках группы в списке групп / в «My groups».
- Меню статуса в Following с описаниями (было в строке Т8 плана) — вынесено, решит ревьюер.
- Пустые состояния (Т9), сервер, новые токены, новые эндпоинты.
- Редизайн полосы «Next», баннера, липкой полосы сверх решения 7.
