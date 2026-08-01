# Club Page — карточки

Каждая карточка — самостоятельный блок `.deep-card`. Страница = массив карточек; убрать/добавить = убрать/добавить элемент, ничего не ломается. Все карточки получают сверху общий scope: `{ season: 'all' | seasonId, group: 'all' | groupId }`. Данные — `GET /api/clubs/{id}/overview?season=&group=`.

Группы зачёта: `K 8–11 · Y 11–14 · N נוער · B בוגרים · M Masters · OW3 🌊 3km · OW5 🌊 5km`.
OW-группы — один зачёт в сезон (без ❄/☀), везде красятся в `--deep-ow`.

---
## 1. Hero
Лого (квадрат 96, initials-fallback), имя клуба ивритом крупно (56px display) + латиницей мелко под ним, всё align-left; бейджи (#N national, official group, N swimmers); KPI-ряд (POINTS / MEDALS / COMPETITIONS / BEST RANK — display 34px); фото 4:3 (плейсхолдер); CTA Follow (accent) + share.
Данные: `{ name_he, name_en, logoUrl?, badges[], kpi: {points, medals, comps, bestRank} }`.

## 2. Global filters
Два свайп-ряда с закреплённым «All»:
- Seasons: `.deep-pill`, 20+ сезонов, скролл вбок, скроллбар скрыт.
- Groups: `.deep-group-tile` 176px — буква-кружок, название, ранги `❄ #n · ☀ #n` (OW: `#n`); повторный клик по активной = All; #1 подсвечен gold.
Состояние — вверх, в page state; карточки перерисовываются от него.

## 3. Season × Group grid  ⭐ главная карточка
Год = секция (display-цифра слева). Внутри — строка на каждую группу: буква-кружок · название · линии чемпионатов:
`[ранг-сегменты][❄ #3]` / `[сегменты][☀ #2]` / OW: `[сегменты][🌊 #5]`.
Сегменты: #10 и хуже = 1, каждое место выше +1, #1 = 10 (см. `.deep-rank-seg--*`; текущий сезон — яркий + glow у #1, прошлые — `-dim`).
Клик по ❄/☀-линии → выбирает зачёт, карточка Standings показывает его таблицу.
Параметр `gridSeasons` (int, default 3) — сколько сезонов видно при Season=All.
Пустой сезон → «no results in {season}».
Данные: `{ years: [{ season, rows: [{ group, winter?: {rank}, summer?: {rank}, ow?: {rank} }] }] }`.

## 4. Standings (детализация выбранного зачёта)
Заголовок: `❄/☀ · группа · сезон · N clubs`. 5 строк `.deep-stand-row`: ранг (display, #1 gold) · лого-аватар · имя (+ под ним `N swimmers · N swims`) · справа очки + медали `🥇n 🥈n 🥉n`. Наш клуб = `--us`. Если мы ниже #4 — показываем 1, 2, наш±1. Футер: gap to #1 / «champions 🏆» + «N competitions left | final».

## 5. Record wall
Заголовок + `.deep-count-badge` «N RECORDS» + сабтайтл scope + локальный `.deep-seg` All/25m/50m (единственный локальный фильтр на странице; при All к дисциплине добавляется суффикс `· 25M/50M`). Грид 3×N `.deep-record-tile--m/f`: дисциплина, время (display 30px), имя (линк на пловца), год, ♂/♀ в углу.

## 6. Swimmers
Узкий список (≤440px): фильтры-пилюли возрастов `All · 8–11 · 12–14 · 15–16 · 17+` со счётчиками + пилюли пола ♂/♀ (toggle). Строка: аватар (пол-цвет) · имя → страница пловца · age N · N comps. Футер «Show all N».

## 7. Coaches
Грид 4: фото-аватар 52 (initials-fallback, пунктир) · имя · роль (HEAD gold / AGE GROUP / MASTERS accent) · группы.

## 8. Competition timeline
Вертикальная линия: ранг-чип (#1 gold glow) · бейдж `❄/☀ · группа` · название (иврит) → competition page · дата · started/finished · очки (display) · медали.

## 9. Top swimmers
Топ-5 по очкам: ранг · аватар · имя · age/медали · очки. Клик → страница пловца.

---
## Мобайл (390)
Те же карточки одной колонкой, тот же порядок. Hero сжат (лого 46, имя 20px, 3 KPI + Follow). Фильтры — те же свайп-ряды (группы — компакт-чипы c рангом). Грид: год 13px, сегменты 5×7px. Standings без sub-строки и медалей. Record wall — 2 тайла. Swimmers — топ-4.

## Как добавить новую карточку
1. `.deep-card` + `.deep-card-title` (+ `.deep-card-sub`).
2. Только `var(--deep-*)` — работает в обеих темах автоматически.
3. Читает глобальный scope, свои фильтры не заводит (исключение — физический параметр вроде пула).
4. Цвет по ролям: accent=мы/CTA/winter, gold=медали/#1/summer, ow=открытая вода, danger=DSQ/DNS.
5. Один glow на карточку.
