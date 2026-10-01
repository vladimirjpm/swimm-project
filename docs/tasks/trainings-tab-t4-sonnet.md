# Задание Sonnet — Т4 таба Trainings: лента тренировок (01.10.2026)

## Контекст

Таб Trainings группы переделывается по high-fidelity хендоффу
`!design_handoff/design_handoff_trainings_tab/` (`README.md` — разделы **Trainings**,
**Desktop**, **Tokens**, **Light theme**, **Rules**; прототип `Trainings Tab.dc.html`: лента
участника на телефоне — секция **2a** (строки ~445–460), лента на десктопе — **3a** (строки
~312–330), лента тренера — **3b** (строки ~372–392), раскрытые «Sets & times» — **5a**
(строки ~170–196)). План — `docs/plans/trainings-tab-redesign-plan.md` (§4 этап Т4; §3½ и
таблица «Каркас Т2»; §3 «Контракт Т1а»; §6 решения Р1–Р6).

Сейчас вид Trainings — прежний список `group-sessions.tsx` (строки тренировок с кнопками),
переведённый в Т3 на шторки. Т4 заменяет его **одной лентой по прототипу**: колонка даты,
карточка с показателями и строкой «You», полоса медиа, раскрываемые на месте «Sets & times» с
★ ключевого сета у тренера, фильтры и «Older trainings ↓».

## Решения (зафиксированы, не пересматривать)

1. **Лента — новые файлы в `client/src/projects/hub-groups-project/components/trainings/feed/`**,
   `group-sessions.tsx` удаляется целиком (его единственный потребитель —
   `trainings/sections/training-feed-section.tsx`). Состав:
   - `feed-format.ts` — чистые функции подписи (ниже п. 4, 6, 9);
   - `feed-filters.tsx` — чипы `All · ⏱ Times · ▶ Videos`;
   - `training-card.tsx` — одна запись ленты (колонка даты + карточка);
   - `sets-and-times.tsx` — раскрытые «Sets & times»;
   - `training-feed.tsx` — сама лента (фильтр, «Older trainings ↓», какая карточка раскрыта, кэш
     сетов). Секция `training-feed-section.tsx` только кладёт её в рамку `TrainingsSection`.
2. **Данные — только из контекста таба** `useTrainingsTab()` (`trainings/trainings-context.tsx`):
   `sessions.data` (`TrainingSessionList`), `sessions.loadSets(id)`, `sessions.setKeySet(id, n)`,
   `sessions.removeMedia(...)`, `sessions.busy`, `manages`, `isDesktop`, `openSheet`, `marks`.
   Новых `fetch` нет. Поля Т1а уже в типах (`client/src/projects/hub-groups-project/types.ts`):
   `TrainingSession.stats` (`meters`, `swimmers`), `key_set_no`, `my_key`;
   `TrainingSessionMedia.is_new`; `TrainingSessionSets` / `TrainingSet` / `TrainingSetRow`.
3. **Рамка секции** (`trainings-section.tsx`, `TrainingsSection`):
   - **десктоп** (`isDesktop`): карточка с `title="Trainings"`, `mark={marks.trainings}`,
     `sub` «🔒 private — group members and admins» (+ « · only the coach adds videos», если
     `!can_add_media`), `actions` — у управляющего кнопка «+ New training» (высота 36, радиус 10,
     рамка `--deep-ow-border`, фон `--deep-ow-chip`, текст `--deep-ow`, 12.5px/800) →
     `openSheet({ kind: 'new-training' })`;
   - **телефон**: `bare` (без карточки — сегмент `Trainings` уже назвал вид, как в 2a); у
     управляющего над фильтрами строка с той же кнопкой «+ New training» справа (высота 44 —
     тап-таргет). ⚠ В Т6 она может переехать в нижнюю панель тренера — держи её отдельным
     маленьким блоком в `training-feed.tsx`, не вшивай в фильтры.
   - загрузка / ошибка — `status="loading" | "error"` рамки (как сейчас в `group-sessions.tsx`).
4. **Фильтры** (`feed-filters.tsx`): `All · ⏱ Times · ▶ Videos` с числом справа (моно 10.5px,
   `opacity .75`). Times — тренировки с `has_results`; Videos — с `media.length > 0`. Чип:
   высота 34 (телефон) / 32 (десктоп), `padding 0 12px`, радиус 999, 12px/800, без переноса.
   Выбранный — у участника фон `--deep-accent-chip`, рамка `--deep-accent-border`, текст
   `--deep-accent`; у управляющего — `--deep-ow-chip` / `--deep-ow-border` / `--deep-ow`.
   Невыбранный — прозрачный, рамка `--deep-card-border`, текст `--t-text-3`. `aria-pressed`.
   Фильтр — состояние ленты (`useState`, по умолчанию `all`), в адрес не пишем. Смена фильтра
   сбрасывает «сколько показано» к 6 и сворачивает раскрытую карточку.
5. **Запись ленты** (`training-card.tsx`) — сетка `40px minmax(0,1fr)`, gap 8 (десктоп `44px`,
   gap 10):
   - **колонка даты**: день недели 9.5px/800 uppercase `--t-text-3`; число — моно 15px/800
     (десктоп 16); под ним вертикальная линия 1px `--deep-card-border` до низа записи
     (`flex:1`, `margin-top 4px`);
   - **разделитель месяца**: когда месяц записи отличается от предыдущей (и перед первой) — строка
     над записью «SEPTEMBER 2026» (10px/800 uppercase, `letter-spacing .06em`, `--t-text-3`,
     `padding-left` = ширина колонки даты + gap). Дата в карточке без месяца, а лента тянется на
     несколько сезонов — без этого «Tue 29» непонятно, какого месяца. (Отступление от прототипа
     — согласовано.)
   - **карточка**: радиус 14, рамка `--deep-card-border`, `margin-bottom 10px`, колонка с gap 8;
     фон — телефон `--deep-card-bg`, десктоп `--deep-card-bg-raised` (там она внутри
     карточки секции); `padding` 10px 12px (десктоп 12px 14px). Тренировка без времён и без
     медиа — `opacity .6` («nothing recorded»).
   - **шапка карточки**: слева название 13.5px/800 (десктоп 14) — `sessionLabel(session)` из
     `use-training-sessions.ts`, в `<bdi>`, `dir="auto"`, обрезка многоточием; под ним мета —
     моно 10.5px/700 (десктоп 11) `--t-text-3` (п. 6). Справа (без сжатия, `gap 12`):
     - «● N NEW» — если у тренировки есть медиа с `is_new` (N — их число): точка 7px
       `--deep-accent`, текст 10px/800 `letter-spacing .05em` `--deep-accent`;
     - у управляющего — текстовые кнопки 12px/800: «Edit» (`--deep-ow`) →
       `openSheet({ kind: 'edit-training', session })`; «Delete» (`--deep-danger`) — ТОЛЬКО
       если `!has_results` → `openSheet({ kind: 'delete-training', session })`.
   - **показатели** (если `stats`): `km` — `(meters / 1000).toFixed(1)`, `swimmers` — число.
     Цифра — `var(--deep-font-display)` 16px (десктоп 17), `line-height 1`; подпись 9px/800
     uppercase `letter-spacing .05em` `--t-text-3`, `margin-top 2px`. Между показателями gap 16
     (десктоп 20). «Avg effort» НЕТ (решение Р3).
   - **строка «You»** (если `my_key`): плашка радиус 10, фон `--deep-accent-chip`, `padding 7px
     10px`, 11.5px/800; слева подпись `--t-text-3`: «You» при `kind === 'me'`, иначе имя пловца
     (`name || name_en`, в `<bdi>`); справа моно `--deep-accent`: `«100 free · »` + время через
     **`UI_SwimTime`** (`client/src/projects/components/mix/swim-time/swim-time.tsx`, без
     `quality` — это тренировочный замер). Телефон — отдельной строкой во всю ширину под
     показателями (`justify-content: space-between`); десктоп — в той же строке, что
     показатели, справа (`margin-left: auto`).
   - **медиа** — существующая `TrainingMediaStrip`
     (`client/src/projects/training-table/training-by-session/training-media-strip.tsx`):
     `onOpen={(i) => openSheet({ kind: 'lightbox', sessionId: s.id, index: i })}`,
     `onRemove` + `canRemove={(m) => m.can_delete}` (✕ на плитке: тренеру — на всех, участнику —
     на своих; подтверждение — `ConfirmDialog`, как сейчас в `group-sessions.tsx`, перенеси).
     Плитки 86×60 уже есть; на десктопе — **110×72**: добавь полосе проп `size?: 'md' | 'lg'`
     (по умолчанию `md`) → класс `tbs-media-strip--lg` и правило в
     `training-by-session.css` (`.tbs-media-strip--lg .tbs-media-tile { width: 110px; height:
     72px; }`). Журнал тренировок (`training-by-session.tsx`) не трогаем.
   - **действия** (строка, gap 16, 12px/800 `--deep-accent`, текстовые кнопки без рамки):
     «+ Add video» — если `can_add_media` → `openSheet({ kind: 'add-video', sessionId })`;
     «Sets & times ›» — если `has_results`; раскрыта — «Sets & times ▴».
6. **Мета карточки** (`feed-format.ts`, `trainingMeta(session, manages)`), части через « · »:
   - начало `start` (если есть);
   - `has_results` → «⏱ times»; иначе медиа есть → «no times»; иначе «nothing recorded»;
   - только у управляющего: при `has_results` и `stats` — «N swimmers»; при медиа —
     «N links» («1 link»).
   Примеры: «20:00 · ⏱ times», «19:30 · no times · 3 links» (тренер), «nothing recorded».
7. **«Sets & times»** (`sets-and-times.tsx`), раскрывается ВНУТРИ карточки, под действиями;
   раскрыта одна карточка за раз (`expanded: id | null` в `training-feed.tsx`):
   - сеты грузятся лениво `sessions.loadSets(id)` при первом раскрытии; кэш — `Map<id,
     TrainingSessionSets>` в `training-feed.tsx`; пока грузится — «Loading…» 12px `--t-text-3`;
     `null` — «Could not load the sets.» `--deep-danger`;
   - строка над сетами: справа переключатель `All / Me` — дорожка `--deep-seg-track`, радиус
     999, `padding 2px`, 11px/800, выбранный `--deep-seg-active-bg` / `--deep-seg-active-fg`,
     кнопки `padding 4px 10px`. Показывать ТОЛЬКО если есть строки с `mine`. `Me` — только
     строки `mine`; сет без них скрыт;
   - **заголовок сета**: 10px/800 uppercase `letter-spacing .06em` `--t-text-3`,
     `padding-top 4px`: «Set 1» + сводка `setSummary(set)` (`feed-format.ts`): если у всех строк
     одна дистанция и стиль — « · 4×100 free» (4 — число разных `order_no`), иначе ничего.
     Ключевой сет (`is_key`) — после текста ★ цвета `--deep-gold`.
   - **★ у управляющего** (`can_manage`): справа в заголовке сета кнопка 28×28 без рамки: ★
     (`--deep-gold`) у ключевого, ☆ (`--t-text-3`) у остальных; `title` «Key set — members see
     their best swim here as “You”» / «Mark as the key set»; `aria-pressed`. Клик:
     `sessions.setKeySet(id, isKey ? null : set_no)`; ошибка — строкой `--deep-danger` над
     сетами. После успеха — перечитать сеты этой тренировки (`loadSets`) и обновить кэш; список
     тренировок хук перечитает сам (и `my_key` в карточке обновится). Пока идёт запись —
     кнопки `disabled`. Участник ★ не нажимает — видит только значок у ключевого сета.
   - **строка повтора**: сетка `28px minmax(0,1fr) 62px 26px 14px`, gap 6, `min-height 32`,
     радиус 8, `padding 0 8px`; фон `--deep-card-bg-raised`, у `mine` — `--deep-accent-chip`:
     `«1.1»` (`set_no.order_no`) моно 10.5px/700 `--t-text-3`; имя 12px/800, `dir="auto"`,
     `<bdi>`, обрезка многоточием (`name || name_en`); время — `UI_SwimTime`, моно 12px/800,
     вправо, цвет `--deep-accent` у `mine`, иначе `--t-text`; `time === null` — «—»
     `--t-text-3`; интенсивность — моно 10px/700 `--t-text-3`, `«V3»` (верхний регистр), нет —
     пусто; уровень — точка 8px цвета `levelColor(level)`
     (`hub-groups-project/components/level-color.ts`) с `title={level.name}`, нет — пусто.
8. **«Older trainings ↓»**: показано 6 записей (после фильтра); кнопка 12px/800
   `--deep-accent`, без рамки, `padding-left` = колонка даты + gap, добавляет ещё 6. Все
   показаны — кнопки нет. Прокрутку НЕ трогаем (никакого `scrollIntoView`).
9. **Стиль в «You» и сводке сета** (`feed-format.ts`, `shortStyle`): `freestyle → free`,
   `backstroke → back`, `breaststroke → breast`, `butterfly → fly`, `medley`/`individual
   medley`/`im` → `IM`; без учёта регистра; прочее — как есть в нижнем регистре.
10. **Пустые состояния** (минимум, полноценные — Т9): тренировок нет — «No trainings yet.»
    (управляющему: «No trainings yet. Add one — e.g. “Underwater filming” — and attach videos
    to it. Times aren’t needed.»); фильтр пуст — «No trainings with times yet.» / «No trainings
    with videos yet.». 12.5px/700 `--t-text-3`.
11. Тексты — английские, дословно как выше. Имена/названия на иврите — `<bdi>` + `dir="auto"`.
    Цвета — только роли (`--t-*`, `--deep-*`), хексов в разметке нет. `scrollIntoView` не
    использовать.

## Что уже готово (не переделывать)

- Каркас: `trainings/trainings-context.tsx`, `trainings-section.tsx` (`TrainingsSection`:
  `title`, `sub`, `mark`, `actions`, `status`, `bare`), `trainings-sections.tsx` (реестр — секция
  `training-feed` уже в `TRAININGS_SECTIONS`, менять реестр не нужно), шторки Т3
  (`trainings/sheets/`, реестр `trainings-sheets.tsx` — открываются через `openSheet`).
- Хук данных `use-training-sessions.ts` (после записи сам перечитывает список; `loadSets` не
  кэширует — кэш твой), `sessionLabel()`.
- `TrainingMediaStrip` (`onOpen`, `onRemove`, `canRemove`, `className`), `ConfirmDialog`,
  `UI_SwimTime`, `levelColor`.
- Сервер (`GET …/sessions`, `GET …/sessions/{sid}/sets`, `PUT …/sessions/{sid}/key-set`) — готов
  и проверен (Т1а). Сервер не трогать.

## Шаги

1. `feed/feed-format.ts`: `trainingMeta`, `formatKm`, `shortStyle`, `setSummary`, `monthLabel`
   (`«SEPTEMBER 2026»` из `yyyy-MM-dd`, считать через `Date.UTC`), `weekdayShort`, `dayOfMonth`.
2. `feed/feed-filters.tsx`, `feed/sets-and-times.tsx`, `feed/training-card.tsx`,
   `feed/training-feed.tsx` по решениям 3–10.
3. `TrainingMediaStrip`: проп `size` + правило в `training-by-session.css`.
4. `trainings/sections/training-feed-section.tsx` → рамка по решению 3 + `TrainingFeed`.
5. Удалить `components/group-sessions.tsx`; поправить упоминания: комментарий в
   `trainings/sheets/link-helpers.ts`, `training-media-strip.tsx` (шапка-комментарий), строки
   `GroupSessions` / `TrainingMediaStrip` в `docs/ui-components.md` (новая строка про ленту
   `trainings/feed/` вместо `GroupSessions`).
6. В `docs/plans/trainings-tab-redesign-plan.md` ничего не отмечай — статус ставит ревьюер.

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

- В рабочей копии много чужих незакоммиченных правок (Т1–Т3 и файлы Влада) — **ничего не
  коммить, `git add` не делать**, трогать только файлы задания.
- Даты — строки `yyyy-MM-dd` (Израиль); день недели/месяц считать через `Date.UTC` +
  `getUTC*`, иначе часовой пояс машины сдвинет день.
- `sessions` из контекста — новый объект на каждый рендер: не клади его в зависимости эффекта,
  который грузит сеты (зацикливание); бери `sessions.loadSets` через ref или грузи в
  обработчике клика.
- `UI_SwimTime` — единственный способ вывести время (правило `client/CLAUDE.md`), даже для
  тренировочных замеров.
- Роли `--t-*` есть только внутри класса темы — на странице группы он уже стоит.

## Вне скоупа (не делать)

- Метка «новое» на сегменте/заголовке и `PUT …/sessions/seen` (`markSeen`) — Т8.
- Пунктирная плитка «+ Add video» у тренировки без видео, полноценные пустые состояния — Т9.
- Нижняя панель тренера на телефоне — Т6. Вид Next и Lanes — Т5–Т7.
- Сервер, новые токены, журнал тренировок `training-by-session.tsx`.
