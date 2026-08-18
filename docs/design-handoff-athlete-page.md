# Design handoff — страница спортсмена (athlete page)

Материал для claude.ai/design. Задача: спроектировать полноценную страницу спортсмена
(сейчас есть попап-карточка + сырая `/swimmers/{id}`). Ниже — **какие данные реально
есть**, откуда, и что нужно доложить на сервере (статистика по годам, лучшее время
в сезоне).

Дата сборки: 2026-07-29. Ветка: `feature/point-rules-schema`.

---

## 1. Что уже существует в продукте

| Поверхность | Файл | Роль |
|---|---|---|
| **Попап-карточка** (богатая, вся логика тут) | [sportsmen-details.tsx](../client/src/projects/sportsmen-details/sportsmen-details.tsx) | открывается из таблицы результатов; identity-бар, 3 плитки, свитчер scope, список заплывов, My links |
| **Страница** `/swimmers/{id}` (бедная) | [swimmer-project.tsx](../client/src/projects/swimmer-project/swimmer-project.tsx) | hero + 4 плитки + медали + best-times списком + галерея |
| Профиль по id | `GET /api/swimmers/{id}` → `SwimmerProfileDto` | |
| Карьера по **имени** | `GET /api/athletes/career` → `AthleteCareerDto` | |
| Медиа пловца | `GET /api/swimmers/{id}/media` | per-viewer видимость |

Структура попапа (чтобы дизайн не терял ни один блок):

1. **Identity-бар** — фото 76px в кольце `--theme-primary` + флаг страны (badge внизу
   справа), под фото ❤/⭐ и бейдж loglig; справа RTL: имя (иврит), бейдж рекордов 🏆N,
   «age (birth_year)», ссылка «Open full profile →», чип клуба с лого.
2. **3 плитки** (карьерные): `Points` · `Medals` (3 медали с count + тултип «за что») ·
   `Level` (дуга норматива с прогрессом до следующего уровня).
3. **Свитчер scope**: «Это соревнование (Feb 2026)» / «All-time (career stats)».
4. **Градиентный баннер**: в scope=competition — `Npt` + 3 медали этого соревнования;
   в scope=alltime — competitions / races.
5. **Список заплывов** (2-строчные карточки): медаль/место + age · иконка стиля с
   дистанцией + тип бассейна · время (+сплиты, DSQ/DNS-нота) · очки · дата (+соревнование
   в all-time) · иконка видео · дуга уровня. Табы training/competition для masters.
6. **My links** — owner-only медиа, свёрнуто; публикация в группы.

---

## 2. Инвентарь данных — что есть СЕЙЧАС

### 2.1 Профиль (`GET /api/swimmers/{id}`)
`id, fullName, firstName, lastName, firstNameEn, lastNameEn, birthYear, gender (M/F),
clubId, clubName, countryCode (alpha-3), countryName, avatarUrl, origin (isr|local)`

- **Картинка**: `Swimmer.AvatarUrl` (nullable, до 1000 симв.) — **у большинства пловцов
  пусто**, реального пайплайна загрузки нет. Фоллбеки в проде:
  `public/images/swimmers/default-{male|female}.png` (попап) и монограмма-инициал (страница).
  → дизайн обязан красиво работать **без фото**; аватар-слот 76px круглый.
- Лого клуба — `UI_ClubIcon` по имени клуба (манифест иконок в `public/images/club-icons`).
- Флаг — `UI_FlagEmoji` по alpha-3.

### 2.2 Карьера (`GET /api/athletes/career?name=`)
- `competitions`, `races`, `since` (год первого результата), `totalPoints`,
  `gold/silver/bronze`
- `medals[]`: `{position 1|2|3, note "Freestyle 50м", competition, date}`
- `bestByStyle[]`: `{stroke, distance, time, points, pool, competition, date, position,
  gender, eventStyleAge, ageGroup, isMasters, isAward}` — **лучшее время за карьеру по
  (стиль × дистанция)**, без разбивки по годам.

⚠️ Карьера ищется **по полному имени**, не по id (историческое). Для страницы это работает,
но для дизайна важно: сгруппировать данные профиля и карьеры в один блок безопасно.

### 2.3 Заплыв (`ResultDto`, единица списка результатов)
`id, competition, date, event_id/event_name/day_number/sub_name, event_style_name,
event_style_len, event_style_gender, event_style_age, event_category (open/para/mix/«17»),
age_group, pool_type (25m/50m), position, position_age_group, combined_place,
is_best_result, best_time_ms, heat, lane, swimmer_id, club/club_en, birth_year,
time (строка), time_ms, time_split, time_fail + time_fail_note (DSQ/DNS),
international_points, club_points/combined_club_points, note, is_relay, relay_team_name,
relay_swimmers_name, member_swimmer_ids, gallery[], country, is_masters, is_award,
show_combine_all_results`

### 2.4 Производное на клиенте (уже есть)
- **Уровень/разряд** — `Helper.getNormativeLevelInfo(...)` → `currentLevel`,
  `progressToNextLevel` (%), `nextTime`, `normativeAgeGroup`; из БД
  `NormativeStandard` (`/api/normative-standards`). Рисуется дугой-гейджем.
- **Рекорды пловца** — `HelperNormative.getSwimmerRecords(name)` → 🏆N (masters/age
  раздельно) + попап со списком; источник `Record` (`/api/records`, оси
  регион × категория × дисциплина).
- Медали считаются только если соревнование `is_award`; подозрительные строки
  (`SuspectReason`) не бьют рекорды.

### 2.5 Медиа и реакции
- `GET /api/swimmers/{id}/media` → `{media_type, source_type, url}` (youtube/vimeo/image/other).
- `UserReaction`: `like` (❤ на медиа) и `congrats` (🎉 на заплыв) — `POST /api/media/{id}/like`,
  `POST /api/results/{id}/cheer`. **На странице пловца пока не показываются** — есть смысл
  дать счётчики.
- Loglig: `LogligIdStatus` (Suggested/Verified/Rejected) + публичный URL карточки.
- Группы: пловец может состоять в hub-группах (ростер) — на странице сейчас не показано.

---

## 3. Чего НЕТ и что надо доложить (это и просил Влад)

Все три вещи **выводимы из уже имеющихся данных** (`ResultRecord`: `CompetitionDate`,
`TimeMillisecond`, `StyleId`, `Distance`, `InternationalPoints`, `Position`), но
серверного эндпоинта нет. Предлагаемый контракт — `GET /api/athletes/{id}/timeline`:

### 3.1 Статистика по годам / сезонам
Сезон в проекте уже определён единообразно: **1 сентября — 31 августа**, метка по году
начала (`2025` → `2025/26`, см. `HubGroupPublicRepository`, `IMySwimsRepository`).

```jsonc
"seasons": [
  {
    "season": 2025, "label": "2025/26",
    "competitions": 6, "races": 31,
    "points": 12840, "bestPoints": 512,       // сумма и лучший single-swim
    "gold": 3, "silver": 1, "bronze": 2,
    "podiums": 6, "personalBests": 9,          // сколько раз улучшил личник
    "levelPeak": "KMS",                        // лучший достигнутый уровень за сезон
    "clubName": "Hapoel ...",                  // клуб в этом сезоне (может меняться!)
    "ageGroup": "14"
  }
]
```

### 3.2 Лучшее время в сезоне (по дисциплине) + прогресс
```jsonc
"byEvent": [
  {
    "stroke": "freestyle", "distance": "50", "pool": "25m",
    "careerBest": { "time": "26.41", "timeMs": 26410, "date": "…", "competition": "…", "points": 512 },
    "seasonBests": [
      { "season": 2025, "time": "26.41", "timeMs": 26410, "points": 512, "date": "…",
        "competition": "…", "improvementMs": -640, "isPersonalBest": true },
      { "season": 2024, "time": "27.05", "timeMs": 27050, "points": 471, "date": "…" }
    ]
  }
]
```
Это даёт дизайну два сильных визуала: **спарклайн/линия прогресса по дисциплине** и
**таблица season-best по годам** (строки = дисциплины, колонки = сезоны, дельта в цвете).

### 3.3 Разрезы, которые тоже стоит заложить
- pool 25m vs 50m — времена **несравнимы**, season-best нужно считать раздельно (в
  `bestByStyle` сейчас pool просто поле, а не часть ключа — на это надо смотреть внимательно).
- `event_category` (open / para / mix / возрастная) — в одной дисциплине у пловца может
  быть несколько зачётов (Маккабиада: три золота в «50m Freestyle»); медали и Top Clubs
  этого сознательно не учитывают, а вот на странице пловца показать корректно — плюс.
- эстафеты (`is_relay`) — отдельный блок, в личных best-times мешать не стоит.
- `time_fail` (DSQ/DNS/DNF) — не должны попадать в best/PB, но их полезно показать в истории.

---

## 4. Блоки, которые я предлагаю дизайнить

1. **Hero** — фото (или монограмма) + имя (RU/HE/EN, RTL!) + клуб-чип с лого + флаг +
   возраст/год рождения + бейджи (🏆 рекорды, loglig ✓, local) + ❤/⭐.
2. **KPI-полоса** — Competitions · Races · Since · Total points · Medals (3 иконки) · Level.
3. **Season switcher** — «All-time / 2025-26 / 2024-25 / …» (из `seasons[]`).
4. **Прогресс по годам** — график: очки за сезон (столбцы) + линия лучшего результата;
   либо per-event спарклайны.
5. **Season bests матрица** — дисциплина × сезон, дельта к предыдущему сезону цветом,
   раздельно 25m/50m.
6. **Best times by event** — карточка-строка как в попапе (медаль, иконка стиля, время,
   очки, дуга уровня, дата, соревнование).
7. **История заплывов** — сгруппированная по соревнованиям, с DSQ/сплитами, фильтр
   стиль/дистанция/бассейн/сезон.
8. **Медали** — 🥇🥈🥉 с раскрытием «за что» (`medals[]`).
9. **Рекорды** — список рекордов пловца (регион × категория × дисциплина).
10. **Медиа-галерея** — плитки 16:9 (youtube/vimeo thumb), лайтбокс, ❤ счётчик.
11. **Группы** — чипы hub-групп, где пловец в ростере (нужен новый эндпоинт).

---

## 5. Ограничения, которые дизайн обязан учесть

- **RTL**: имена и названия клубов — на иврите; попап использует `dir="rtl"`/`dir="auto"`.
  Смешанные строки (иврит + латиница + цифры времени) — типичный случай.
- **Темы**: light/dark через `data-mode` на `<html>` + правило парных токенов
  (`--theme-primary` ↔ `--theme-mode-accent-text`, `--theme-mode-surface*` ↔
  `--theme-mode-text*`), контраст ≥ 4.5:1. Никаких фиксированных hex на тем-зависимых
  поверхностях.
- **UI строго на английском** (данные могут быть на иврите).
- Мобайл-first: попап живёт в `max-h-[90vh]` со скроллом только контентной части.
- Пустые состояния — норма: нет фото, нет медиа, нет карьеры (`races = 0`), нет очков,
  нет уровня (`—`).
- Стек: React 18 + Tailwind v4, отдельные адаптивные варианты компонентов
  (`*-mobile`/`*-desktop`), иконки-атомы `UI_*` уже существуют — переиспользовать.
