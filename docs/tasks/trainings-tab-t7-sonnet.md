# Задание Sonnet — Т7 таба Trainings: вид Lanes (01.10.2026)

## Контекст

Таб Trainings группы переделывается по high-fidelity хендоффу
`!design_handoff/design_handoff_trainings_tab/` (`README.md` — **Structure → Mobile → Lanes**,
**Desktop → 6b**, **Tokens**, **Rules**; прототип `Trainings Tab.dc.html`: участник — секция
**1e** «Member», строки ~870–890; редактор тренера на телефоне — **1e** «Coach · editing»,
строки ~890–905; редактор на десктопе — **6b**, строки ~27–55). План —
`docs/plans/trainings-tab-redesign-plan.md` (§4 этап Т7, **§6 решение Р6** — читать целиком,
это решения Влада; §3½ и таблица «Каркас Т2»).

Вид Lanes сейчас — прежние `components/group-lanes.tsx` (дата ‹ › + поле даты, статус, Edit /
Publish / Delete, режим правки), `group-lane-board.tsx` (доска просмотра: «Your lane» +
карточки дорожек), `group-lane-editor.tsx` (редактор: корзины, перетаскивание мышью, select у
каждого пловца, ↑, Distribute, Auto lanes, Copy). Обёрнут секцией
`trainings/sections/lane-plan-section.tsx` (`bare`). Т7 **доводит существующие файлы** до
прототипа и решений Р6 — с нуля не писать: модель корзин, Distribute, Auto lanes, копия плана,
сохранение и проверки остаются.

## Решения (зафиксированы, не пересматривать)

1. **Файлы**: правишь `components/group-lanes.tsx`, `group-lane-board.tsx`,
   `group-lane-editor.tsx` на месте; новые мелкие части — рядом (`group-lane-chip.tsx`,
   `group-lane-savebar.tsx` и т.п.) или в `trainings/lanes/` — на твой выбор, но один блок =
   один файл. Серверных правок нет: `lane-plans-api.ts` (`list`, `get`, `save`, `distribute`,
   `autoLanes`, `publish`, `unpublish`, `remove`, `levels`) — как есть.
2. **Шапка вида (обе роли)** — навигация по датам планов, как сейчас (стрелки ходят по
   существующим планам; участнику сервер отдаёт только опубликованные), вид по прототипу:
   - телефон: кнопки ‹ › 40×40 (радиус 10, рамка `--deep-card-border`, `--t-text-3`,
     `disabled` на краях); посередине дата 15px/800 «Wed 30 Sep» (`formatPlanDate`), у
     управляющего после неё «▾» `--deep-ow` 11px — это `<input type="date">` поверх даты
     (прозрачный, во всю кнопку) — открыть любой день; под датой 10.5px/800: «PUBLISHED»
     `--deep-live` / «DRAFT — only coaches see it» `--deep-gold` / «NO PLAN» `--t-text-3`;
   - десктоп (6b): строка: «← Back to Next» (13px/800 `--t-text-3`; это уже рисует
     `trainings-tab.tsx` над секцией — НЕ дублируй, оставь там), разделитель 1×20
     `--deep-card-border`, ‹ › 36×36, заголовок `var(--deep-font-display)` 18px «Lanes · Wed 30
     Sep» + «▾» (выбор даты у управляющего), пилюля статуса (черновик — рамка
     `--deep-gold-border`, фон `--deep-gold-chip`, текст `--deep-gold`, точка
     `--deep-gold-bar`, 11px/800; опубликован — то же на `--deep-live`); справа у управляющего
     — тулбар (п. 5).
3. **Участник** (`group-lane-board.tsx`, вид 1e):
   - **«Your lane»** — первой и крупно, по `my_swimmers` (сам, семья): карточка радиус 16,
     рамка `--deep-accent-border`, фон `--deep-pool-water`, padding 14, gap 8: подпись «YOUR
     LANE» (у семьи — «LANE FOR <name>», `<bdi>`) 10px/800 uppercase `letter-spacing .07em`
     `--deep-pool-label`; `«2 · medium»` — `var(--deep-font-display)` 28px `line-height 1`;
     задание моно 13px/800 `line-height 1.6`, переводы строк сохраняются; затем товарищи по
     дорожке столбиком (gap 4): кружок 24 (буква, фон — цвет уровня дорожки `levelColor`, нет
     уровня — `--deep-text-mute`; текст `--deep-accent-ink` 10px/900) + имя 12.5px/800 `<bdi>`
     + у себя «you» 10.5px `--deep-accent`; ведущий — первый. Пловец в Unassigned — «On the
     list for today — the coach hasn’t picked a lane yet.» (как сейчас).
   - **золотая заметка о переносе** — если у моего пловца `moved_from_lane != null`: над
     «YOUR LANE» пилюля (рамка `--deep-gold-border`, фон `--deep-gold-chip`, 11px/700, точка
     7px `--deep-gold-bar`): «Coach moved you from lane 3 to 2» (у семьи — «Coach moved <name>
     from lane 3 to 2»; новый номер — текущая дорожка; в Unassigned — «… from lane 3 to
     unassigned»). Метку НЕ гасим — `PUT …/lane-plans/seen` вызовет Т8.
   - **остальные дорожки** — компактно ОДНИМ бассейном (рамка `--deep-pool-border`, фон
     `--deep-pool-water`, радиус 12): строка `min-height 48`, `padding 6px 10px`, между
     дорожками `1px dashed var(--deep-pool-rope)`; слева колонка 96px: `«1 · fast»` моно
     10.5px/800 `--deep-pool-label` и под ней задание моно 10px/700 `--t-text-3` одной строкой
     с многоточием; справа кружки 26 (как выше), перенос разрешён. Своя дорожка в этот список
     не входит.
   - заметка плана (`note`) — над дорожками, как сейчас; «Not placed in a lane yet» — как сейчас.
   - нет плана — «No lane plan for Thu, Oct 1.» 13px/700 `--t-text-3` в пунктирной рамке.
4. **Тренер — без отдельного режима правки** (Р6 п.2): открыл Lanes на дату с планом — сразу
   редактор этого плана; плана нет — карточка «No lane plan for …» с «Create plan» и «Copy
   from <дата>» (как сейчас) → редактор. Кнопок Edit / Cancel больше нет. Доска просмотра
   (`GroupLaneBoard`) у тренера не показывается.
5. **Тулбар тренера** (Р6 п.3): кнопки высотой 40 (телефон) / 36 (десктоп), радиус 10,
   12px/800, перенос строк разрешён (на телефоне — горизонтальная прокрутка ряда НЕЛЬЗЯ,
   только перенос):
   - «3 lanes − +» — рамка `--deep-card-border`, кнопки −/+ 30×30 (десктоп 26×26) радиус 8 на
     `--deep-seg-track`;
   - «Distribute» — рамка `--deep-ow-border`, текст `--deep-ow` (с подтверждением, как сейчас);
   - «Auto lanes» — так же (подтверждение, как сейчас);
   - «Copy from Sun 27» — рамка `--deep-card-border`, `--t-text-3` — показывать ВСЕГДА, когда
     есть другой план до этой даты (иначе самый свежий); если на дату уже есть план или есть
     несохранённые правки — `ConfirmDialog` «Replace this plan with a copy of Sun 27?» (текст:
     «Lanes, levels, workouts and who swims where are copied; today’s people who left the
     group drop out. Nothing is saved until you press Save.»). Копия — заготовка в редакторе,
     как сейчас `template`;
   - «Delete» — только у существующего плана: текстовая 12px/800 `--deep-danger` (подтверждение,
     как сейчас).
   - **заметка для всех** (`note`) — поле под тулбаром во всю ширину, placeholder «Note for
     everyone — e.g. Warm-up together 400 easy».
6. **Пловец в редакторе — плашка** (`group-lane-chip.tsx`) вместо строки с `select`:
   - телефон (1e): высота 36, радиус 999, `padding 0 10px 0 4px`, рамка `--deep-card-border`,
     фон `--deep-card-bg`; кружок 26 (буква; фон — цвет ТЕКУЩЕГО уровня пловца `levelColor`,
     нет — `--deep-text-mute`; `--deep-accent-ink` 10px/900); имя 12px/800 `<bdi>`; ушедший из
     группы — имя `--t-text-3`. Плашки дорожки — в строку с переносом, gap 6;
   - десктоп (6b): высота 36, радиус 10, во всю ширину колонки, столбиком (gap 6): «⠿»
     (11px `--t-text-3`, ручка) + кружок 26 + имя (`flex:1`) + «⋮» (кнопка 24×32, `aria-label`
     «Move <name>»), `cursor: grab`;
   - **выбранная плашка** (тап / ⋮) — рамка `--deep-ow`, фон `--deep-ow-chip`.
7. **Перенос** (Р6 п.1):
   - **тап по плашке (телефон) / ⋮ (десктоп)** выбирает пловца; в КАРТОЧКЕ его дорожки (или
     корзины) под плашками появляется строка (радиус 10, фон `--deep-ow-soft`, рамка
     `--deep-ow-border`, `padding 6px 8px`, gap 6, перенос разрешён): «Move <name> to»
     (11px/800 `--deep-ow`, `margin-right auto`) и кнопки 36×36 (радиус 8, рамка
     `--deep-ow-border`, текст `--deep-ow`, 800): номера ДРУГИХ видимых дорожек, затем
     «Unassigned» и «Not today» (широкие, `padding 0 10px`), а если пловец в дорожке не первый
     — «Lead» (поставить первым). Повторный тап по той же плашке / Esc — снять выбор. Выбор
     один на весь редактор;
   - **десктоп: перетаскивание** (HTML5 drag, как сейчас) с ручки или всей плашки: бросок на
     колонку дорожки / корзину — в конец; бросок НА другую плашку той же или другой дорожки —
     вставить ПЕРЕД ней (так меняется порядок — кнопка ↑ больше не нужна, удалить); зона «Drop
     here» внизу каждой колонки (высота 36, радиус 10, `1px dashed var(--deep-pool-border)`,
     11px/700 `--deep-pool-label`). Подсветка цели — как сейчас (`outline` `--deep-accent`).
8. **Дорожка в редакторе**:
   - карточка: радиус 16, рамка `--deep-pool-border`, фон `--deep-pool-water`, padding 10px
     12px (десктоп 12), gap 8 (десктоп 10); шапка: номер `var(--deep-font-display)` 18px
     (десктоп 22) `--deep-pool-label`; `select` уровня (высота 34/32, радиус 8, рамка
     `--deep-card-border`, фон `--deep-card-bg`, 12px/800); справа число людей моно 10.5px
     `--t-text-3` («3» / десктоп «3 swimmers»);
   - задание: `textarea` (радиус 8, рамка `--deep-card-border`, фон `--deep-card-bg`, моно
     12px/700, `line-height 1.5`, `min-height 56`), «Same workout as lane N» — как сейчас;
   - плашки (п. 6), строка переноса (п. 7), на десктопе — «Drop here».
9. **Раскладка дорожек** (Р6 п.5): телефон — карточки столбиком, gap 12. Десктоп — при
   **≤ 6 дорожках** колонки в ряд + колонка Unassigned 220px
   (`grid-template-columns: repeat(N, minmax(0,1fr)) 220px`, gap 12, `align-items: start`,
   `min-height 300` у колонки дорожки); при **> 6** — сетка
   `repeat(auto-fill, minmax(220px, 1fr))` и Unassigned отдельной строкой под ней.
   **Горизонтальной прокрутки нет** ни при каком числе дорожек.
10. **Корзины**:
    - **Unassigned** — телефон: карточка после дорожек; десктоп: колонка 220. Рамка
      `1px dashed var(--deep-gold-border)`, радиус 16, padding 12, gap 8; заголовок «●
      UNASSIGNED · 2» 10px/800 uppercase `--deep-gold` (точка `--deep-gold-bar`); подсказка
      11px/700 `--t-text-3`: «In today’s plan, no lane yet. Distribute places them by level.»
      (НЕ «Going or maybe» — Unassigned из ответов не строим, Р6 п.6). Принимает бросок.
    - **Not today** (Р6 п.4) — свёрнутой строкой под всем: «Not today · 3 ›» (12px/800
      `--t-text-3`, `min-height 44`, рамка `1px dashed var(--deep-card-border)`, радиус 12);
      тап — раскрыть плашки (с тем же переносом); подпись «not in this plan · members don’t
      see this list». Принимает бросок и в свёрнутом виде.
11. **Липкая панель сохранения** (Р6 п.2, `group-lane-savebar.tsx`) — внизу редактора,
    `position: sticky`; на телефоне `bottom: calc(96px + env(safe-area-inset-bottom))` (над
    закреплённой панелью COACH TOOLS каркаса — вторую фиксированную панель не заводим), на
    десктопе `bottom: 16px`. Вид: `min-height 56`, радиус 14, `padding 8px 8px 8px 14px`, фон
    `var(--t-surface-strong)`, рамка `--deep-ow-border`, тень `var(--deep-card-shadow)`, gap 10:
    - есть несохранённые правки — слева «● Unsaved changes» 12.5px/800 `--deep-gold` (точка
      `--deep-gold-bar`), справа «Save» (высота 44, `padding 0 18px`, радиус 12, фон
      `--deep-ow`, текст `--deep-accent-ink`, 13px/800); идёт запись — «Saving…»;
    - правок нет, черновик — «Saved · draft» `--t-text-3` + «Publish» (тот же вид, что Save);
    - правок нет, опубликован — «Saved · published» `--deep-live` + «Unpublish» (рамка
      `--deep-ow-border`, текст `--deep-ow`, прозрачный);
    - ошибка записи — строкой 12px `--deep-danger` над панелью.
    «Несохранённые правки» — черновик отличается от того, с чего редактор открыт (число
    дорожек, заметка, уровни/задания видимых дорожек, корзины и порядок). Save — как сейчас
    (`lanePlansApi.save`), после — редактор остаётся открытым на сохранённом плане. Publish /
    Unpublish / Delete / Save — после успеха `reloadLanePlans()` из контекста таба (Т6: вид
    Next тренера показывает статус плана) и перечитать список дат вида.
    **Уход с несохранёнными правками** (стрелки дат, выбор даты, «← Back to Next», сегменты
    видов) — `ConfirmDialog` «Discard unsaved changes?» («Discard» / Cancel). Для «← Back to
    Next» и сегментов: экспортируй из контекста флаг `lanesDirty` + `setLanesDirty`, а
    `setView` при `lanesDirty` сначала спрашивает (подтверждение — в `trainings-tab.tsx` или
    контексте, одно место).
12. Подтверждения Distribute / Auto lanes / Delete — тексты как сейчас (`ConfirmDialog`).
13. Тексты — английские, дословно. Имена — `<bdi>` + `dir="auto"`. Цвета — только роли; цвет
    уровня — `levelColor` (это данные тренера, не тема). Тап-таргеты ≥ 36 в редакторе, ≥ 40
    у навигации. `scrollIntoView` не использовать.

## Что уже готово (не переделывать)

- `group-lane-editor.tsx`: `initialDraft` (план / копия / пустой), корзины `Buckets`,
  `move`, Distribute (`lanePlansApi.distribute`), Auto lanes, «убранные дорожки → Unassigned на
  лету», сохранение (`LanePlanInput`), подтверждения — ЛОГИКУ сохранить; меняется вид и
  способы переноса, `raise` заменяется вставкой «перед плашкой» и кнопкой «Lead».
- `group-lanes.tsx`: список планов, дата по умолчанию (`pickDefaultDate`), загрузка плана,
  уровни для редактора, копия (`template`), публикация/удаление — логику сохранить.
- `group-lane-board.tsx`: `findPlace`, `SwimmerName`, `LevelChip`.
- Контекст таба `trainings/trainings-context.tsx` (`manages`, `isDesktop`, `setView`,
  `lanePlans`, `reloadLanePlans`), секция `sections/lane-plan-section.tsx`, `ConfirmDialog`,
  `levelColor`, `formatPlanDate`, `todayInIsrael`. Типы `LanePlan`
  (`my_swimmers[].moved_from_lane`), `LanePlanSummary` — `types.ts`.

## Шаги

1. Контекст: `lanesDirty` / `setLanesDirty` и подтверждение ухода в `setView`.
2. `group-lane-chip.tsx`, `group-lane-savebar.tsx` (и прочие мелкие части — по файлу).
3. `group-lane-editor.tsx` → решения 5–12; `group-lanes.tsx` → решения 2, 4, 11;
   `group-lane-board.tsx` → решение 3.
4. Строки в `docs/ui-components.md` (если там есть редактор/доска дорожек — обнови).
5. План `docs/plans/trainings-tab-redesign-plan.md` не трогать.

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

- В рабочей копии много чужих незакоммиченных правок (Т1–Т6 и файлы Влада) — **ничего не
  коммитить, `git add` не делать**, трогать только файлы задания.
- `key={date}` у редактора сейчас пересоздаёт его на смене даты — не потеряй это, иначе черновик
  одной даты уедет в другую.
- Сравнение «есть ли правки» — по нормализованному входу сохранения (`LanePlanInput` из
  черновика vs из исходного), не по ссылкам на массивы.
- HTML5 drag на телефоне не работает — там перенос только тапом (п. 7); не вешай на телефоне
  `draggable`, он мешает прокрутке.
- Роли `--t-*` есть только внутри класса темы — на странице группы он стоит; `ConfirmDialog`
  сам ставит класс на портал.

## Вне скоупа (не делать)

- `PUT …/lane-plans/seen` и метки (золотая точка сегмента и т.д.) — Т8.
- Unassigned из ответов «иду / не уверен» (Р6 п.6) — отдельным решением позже.
- Пустые состояния целиком («нет расписания — Lanes скрыт») — Т9.
- Сервер, новые токены, вид Next.
