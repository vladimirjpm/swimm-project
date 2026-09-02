# Rivals (Head-to-Head) — компоненты, макет 1b в `Canvas.dc.html`

Страница: `/swimmers/:id?tab=rivals&rival=:rivalId`. Тема — Deep (`deep-theme.css`), все цвета ниже — значения dark-темы; в коде брать парные токены.

Общие правила:
- Cyan `#22d3ee` = более быстрое время / лидер по статам. Gold — только медали. Красный `#f87171` = favorite (♥).
- Числа: `font-variant-numeric: tabular-nums`.
- RTL: `dir="auto"` только на текстовых спанах (имя, клуб), никогда на flex/grid-контейнерах.
- Все чипы/пилюли: `white-space: nowrap` (ломаются на «9 y · 2017» и иврит-именах).

---

## 1. `H2HMiniCard` — мини-карточка спортсмена (скрин 1)

Props:
- `swimmer: { name, club, age, birthYear, avatarUrl }`
- `align: 'left' | 'right'` — сторона в шапке compare. Фото всегда К ЦЕНТРУ (левая карточка: текст → фото; правая: фото → текст), текст выровнен к фото: align=left → text-align:right; align=right → text-align:left.
- `isFavorite: boolean`, `onToggleFavorite`
- клик по карточке → страница пловца.

Разметка: контейнер `position:relative; display:flex; align-items:center; gap:14px; padding:12px 16px; border-radius:14px; background:rgba(255,255,255,.05); border:1px solid rgba(255,255,255,.12);` (align=left ещё `justify-content:flex-end`).
- Аватар: круг 72px, `border:2px solid rgba(34,211,238,.45)`, фон `#0a2740`, img cover.
- Текстовая колонка: имя 16px/800 `#fff`; клуб 12px `rgba(255,255,255,.5)`; чип возраста `9 y · 2017` — 11px/800 `#7b93a8`, `border:1px solid rgba(255,255,255,.15)`, radius 999, padding 1px 9px, nowrap; `align-self` к стороне текста.
- Сердечко-фаворит: `position:absolute; top:8px;` во ВНЕШНЕМ углу (align=left → `left:10px`, align=right → `right:10px`), 16px, cursor:pointer. On: `♥ #f87171` + `text-shadow:0 0 8px rgba(248,113,113,.45)`. Off: `♡ rgba(255,255,255,.3)`.

⚠ У текстовой колонки при align=left: `align-items:stretch; text-align:right` (НЕ flex-end — иврит-спаны возьмут max-content и наедут на фото).

---

## 2. `H2HStatRow` — строка сравнения статов (скрин 2)

Шапка compare — grid `1fr 200px 1fr` (карточка · центр · карточка). В центре только «3–2 / faster times» (Archivo Black 30px cyan). Ниже, отделённые `border-top/bottom: 1px solid rgba(255,255,255,.08)`, статы в столбик (`gap:10px`), каждая строка — тот же grid `1fr 200px 1fr; gap:14px`.

Props:
- `label: string` — по центру: 10px/800, uppercase, letter-spacing 1px, `rgba(255,255,255,.4)`.
- `left, right: number | MedalSet` — значения ПРИЖАТЫ К ЦЕНТРУ (левое text-align:right, правое text-align:left), 18px/800.
- `winner: 'left' | 'right' | null` — значение победителя cyan `#22d3ee`, второе `#fff`. У медалей подсветки нет.

Виды строк: `season bests` (число), `medals` (MedalTriple), `best FINA pts` (число).

`MedalTriple`: три круга 20px в ряд (gap 3px), число внутри 10.5px/900 цвета `#04182b`:
- gold `linear-gradient(180deg,#fbbf24,#b45309)`, border `rgba(251,191,36,.6)`
- silver `linear-gradient(180deg,#e5e7eb,#9ca3af)`, border `rgba(229,231,235,.5)`
- bronze `linear-gradient(180deg,#f0a36a,#9a5b2d)`, border `rgba(240,163,106,.5)`

---

## 3. `H2HEventCard` + `H2HTimeCell` — карточка заплыва (скрин 3)

Порядок карточек: сначала заплывы, где есть результаты у ОБОИХ (orderby стиль → дистанция), затем разделитель «ONLY ONE SWIMMER» и односторонние.

`H2HEventCard` props: `event: { styleIconUrl, distance, stroke }`, `rows: PoolRow[]`, `oneSided: boolean`.
- Контейнер: `border-radius:14px; padding:14px 18px; background:rgba(255,255,255,.04); border:1px solid rgba(255,255,255,.08)`. oneSided: `background:rgba(255,255,255,.02); border:1px dashed rgba(255,255,255,.10); opacity:.9`, иконка `filter:grayscale(.4)`.
- Шапка: grid `1fr auto 1fr`, по бокам hairline `1px rgba(255,255,255,.08)`, в центре иконка стиля (UI_SwimStyle, 132×90, `object-fit:cover; object-position:top; border-radius:10px`) — текстового названия стиля/дистанции НЕТ, их несёт иконка.

`PoolRow` (одна на бассейн, 25m и/или 50m): grid `1fr 120px 1fr; gap:10px; align-items:center`.
- Центр (столбик, gap 4px): UI_PoolType — текст `--25m--` / `-----50m-----`, 11px/800, `#7b93a8`, nowrap; под ним дельта 12.5px/700 (`−` = левый быстрее → cyan; `+` → `rgba(255,255,255,.55)`). Нет пары — дельты нет.
- По бокам `H2HTimeCell`.

`H2HTimeCell` props: `time?: string`, `date?: string`, `isWinner: boolean`, `badge?: 'SB' | 'REC'`, `side: 'left' | 'right'`.
- `time == null` → `—` 19px/800 `rgba(255,255,255,.3)` (выравнивание по side).
- Обычное: время 19px/800 `#fff`, дата 11.5px `rgba(255,255,255,.4)`. side=left → всё прижато вправо, side=right → влево.
- Победитель: плашка `display:inline-block; background:rgba(34,211,238,.08); border:1px solid rgba(34,211,238,.35); border-radius:10px; padding:5px 16px;` время cyan, дата внутри; text-align плашки = side.
- Строка «время + бейдж»: `display:flex; align-items:center; gap:6px; white-space:nowrap; justify-content: flex-end|flex-start` по side — иначе бейдж переносится под время (проверено).
- Бейджи: 9.5px/900, radius 4, padding 1px 5px. SB: цвет+border cyan (`rgba(34,211,238,.45)`), без фона. REC: gold `#fbbf24`, фон `rgba(251,191,36,.14)`, border `rgba(251,191,36,.45)`.

Клик по строке результата → заплыв в `results_main.html` (см. навигационный контракт в CLAUDE.md).

---

## 4. Состояния выбора (панели внизу 1b)

- Выбран один: слева `H2HMiniCard`, в центре «vs» (Archivo Black 22px `rgba(255,255,255,.3)`), справа `H2HEmptySlot` — пунктирная рамка `1px dashed rgba(255,255,255,.18)`, radius 14, min-height 96px, внутри круг «＋» 44px и подпись `בחר יריב · choose a rival` 13px/700 `rgba(255,255,255,.45)`.
- Никто не выбран: два `H2HEmptySlot`.
- Под слотами: полоса Favorites — label `FAVORITES` (10.5px/800 uppercase) + чипы `♥ имя` (border `rgba(248,113,113,.35)`, фон `rgba(248,113,113,.08)`, radius 999, padding 5px 13px, nowrap, сердечко `#f87171`); клик по чипу подставляет пловца в свободный слот. Ниже — поиск: пилюля `border:1px solid rgba(255,255,255,.14); padding:10px 18px`, placeholder `rgba(255,255,255,.35)`.
- Compare рендерится только когда выбраны оба.


---

## 5. Рекорды: национальный vs «малые» (Age records, Masters) — утверждён вариант 2b

Три класса рекорда, разный вес бейджа:

| Класс | Бейдж | Стиль |
|---|---|---|
| Национальный (ISR, open) | `REC` | gold: цвет `#fbbf24`, фон `rgba(251,191,36,.14)`, border `rgba(251,191,36,.45)` |
| Age record (возрастной) | `REC·AGE` | silver: цвет `#cbd5e1`, фон `rgba(203,213,225,.10)`, border `rgba(203,213,225,.4)` |
| Masters | `REC·M` | silver, как выше |

Бейдж: 9.5px/900, letter-spacing .5px, radius 4, padding 1px 5px, nowrap. Суффикс (`·AGE`, `·M`) — тот же span, `opacity:.7; font-weight:800`. Season best (`SB`) без изменений — cyan контур. Gold остаётся ТОЛЬКО за национальным рекордом и медалями; малые рекорды никогда не золотые.

### Компонент `RecordBadge`
Props: `kind: 'national' | 'age' | 'masters'`. Один компонент вместо разрозненных «REC»-спанов — использовать везде: H2H, Athlete Page (record wall), Results, Season Best.

### Данные
- В результате заплыва поле `record?: { kind: 'national' | 'age' | 'masters', scope?: string }` (scope — «9y», «M40» — в бейдж НЕ выводить, только в tooltip/title).
- Если результат — одновременно и age, и национальный рекорд, показывать один бейдж старшего класса (national > age > masters).
- В `H2HTimeCell` prop `badge` меняется с `'SB' | 'REC'` на `badge?: 'SB' | { record: kind }`. SB и REC вместе не показываем — рекорд важнее.

### Строка статов «RECORDS · ALL TIME» (H2HStatRow, новый вид `records`)
Между «season bests» и «medals». У каждого пловца три счётчика в ряд (gap 5px), прижаты к центру как остальные значения:
- `{n} REC` — 18px/800 `#fff`, бейдж gold;
- `{n} REC·AGE` — 15px/800 `rgba(255,255,255,.75)`, бейдж silver;
- `{n} REC·M` — 15px/800 `rgba(255,255,255,.75)`, бейдж silver.
Нули показываем (ряд должен быть симметричен). Подсветки лидера cyan нет — цветов в строке уже хватает.
API: `records: { national: number, age: number, masters: number }` на каждого пловца в ответе `/h2h`.
