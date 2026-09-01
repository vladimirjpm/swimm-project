# Задача: перенос `dolphin_masters_data.json` в БД

Разовый перенос данных «Дельфин-мастерс» из статического JSON в БД. Опорные решения —
`docs/hubgroups-architecture.md` §7 (локальные пловцы, приватные тренировки, привязка аккаунта).

> **ВАЖНО (уточнение 2026-07-10): переносим ТОЛЬКО тренировки (205 строк).**
> Соревновательные результаты (26 строк) уже есть в БД — их НЕ импортируем. Шаг 2 удалён.
> Соответственно в словарь канонизации попадают только пловцы с тренировками (`n_train>0`);
> у кого есть и соревнования — их `Swimmer` уже в БД → проставляем `existing_swimmer_id`.

## Что в исходнике (факты, проверено скриптом)

`client/public/data/json/dolphin_masters_data.json` — 231 запись:

- **26 соревновательных** (5 masters-соревнований) + **205 тренировочных** (все `competition="Hof Yarok training"`).
- **~15 реальных людей**, но 28 «ключей» из-за грязи: `birth_year` то число `1979`, то строка `"1979"`,
  то `""`; `last_name` то есть, то `null`, то `""`; клички (`מקס`=`מקסים שוסטין`; `דני` без фамилии;
  `אדם ט` ≠ `אדם פ`).
- **Эстафет нет**: 14 записей содержат relay-ключи, но у всех payload пустой → `Relay` не трогаем.
- Галереи: 15 записей (youtube-видео) — только в соревновательных.
- Клуб один: `הפועל דולפין נתניה` / `Hapoel Dolphine Netanya`.
- Поля тренировки (`training.*`): `trainingId(205), trainingName(200), set, order, interval,
  intensity, isPaddles(42), isBuoy(1), expected_time(63)`.

## Зафиксированные решения

1. **Существующие в isr.org.il пловцы (напр. Шустин) — привязываем к их существующему `Swimmer.Id`**,
   local-двойника НЕ заводим (иначе карьера раздваивается).
2. **Словарь канонизации — временный throwaway** для переноса: скрипт выписывает все уникальные
   варианты `имя+год`, Влад руками проставляет «кто это». После импорта словарь не нужен.
3. **Тренировка = индивидуальный заплыв одного пловца** — эстафет на тренировках нет, таблицу
   тренировок не усложняем под команды.

## Шаг 0 — схема (миграция, `--context SwimmDbContext`)

- `Swimmer.Origin` (`string`, `'isr'|'local'`, default `'isr'`). У local — `SwimmerOrgId=null`.
- **`Sys_TrainingSessions`** (приватная, БЕЗ grant `swimm_ro`, только `SwimmDbContext`):
  `Id, HubGroupId(FK→HubGroups), ExternalTrainingId(«20251028»), Name(trainingName), Date, PoolType, Note`.
- **`Sys_TrainingResults`** (приватная):
  `Id, SessionId(FK→Sys_TrainingSessions), SwimmerId(FK→Swimmers), StyleId(FK→Styles), Distance,
  Gender, TimeMillisecond, TimeOriginal, SetNo, OrderNo, IntervalSec, Intensity, IsPaddles, IsBuoy,
  ExpectedTimeMs`. Индекс `(SessionId)`, `(SwimmerId)`.
- `Sys_SwimmerAccountLinks` — из §7; **для импорта не нужна, можно отдельной миграцией позже**.

Приватность структурная: `swimm_ro` эти таблицы не видит физически.

## Шаг 1 — канонизация пловцов (ручной шаг, блокирует всё)

1. Скрипт выписывает уникальные `(first_name,last_name,birth_year-как-строка)` → файл-черновик.
2. Влад проставляет для каждого: `→ существующий SwimmerId` ИЛИ `→ new local: FirstName/LastName/En/BirthYear/Gender`.
3. По словарю создаём недостающих `Swimmer(Origin='local', ClubId=Дельфин)`; существующих не дублируем.
4. Итог: map `вариант-из-JSON → SwimmerId` для шагов 2–3.

## Шаг 2 — УДАЛЁН

Соревновательные результаты уже в БД — не переносим. Галереи/эстафеты из этих 26 строк тоже
не трогаем.

## Шаг 3 — тренировочные (205) → `Sys_TrainingSessions` + `Sys_TrainingResults`

- Группируем по `training.trainingId` → одна `Sys_TrainingSessions`, `HubGroupId` = официальная группа Дельфина.
- Каждая строка → `Sys_TrainingResults` по `SwimmerId` из словаря; `time`→`TimeMillisecond`,
  `expected_time`→`ExpectedTimeMs`, `set/order/interval/intensity/isPaddles/isBuoy` — как есть.
- В рекорды/очки НЕ попадают (другая таблица) — ради этого и разделяли.

## Где живёт / идемпотентность

- Разовая **консольная команда/сидер** (по образцу сидера рекордов), не UI-импорт — данные разовые,
  требуют ручной канонизации.
- Идемпотентность по натуральному ключу: тренировки — `(ExternalTrainingId, SwimmerId, event, SetNo,
  OrderNo)`; соревнования — `(CompetitionId, SwimmerId, StyleId, Distance, Heat, Lane)`. Повторный
  прогон не задваивает.
- Предусловие: официальная группа Дельфина (`HubGroup.IsOfficial`, `ClubId`) уже существует —
  нужна для `HubGroupId` тренировок.

## СТАТУС: перенос выполнен (2026-07-10)

Миграция `AddTrainingTablesAndSwimmerOrigin` применена, сидер прогнан вживую:
**205/205 тренировок → 5 сессий в группе #17**, 15 local-пловцов + 3 existing (Питер 7141,
Владимир 7180, Максим 7167). Идемпотентно. Команда:

```
dotnet run --project server/Swimm.API -- --seed-dolphin-training \
  <abs-path>/dolphin_masters_data.json <abs-path>/docs/tasks/dolphin-canon/canon-resolved.csv \
  --group 17 [--force]
```

Дальше (не сделано **на день постановки**; см. [`README.md`](README.md)): приватный
эндпоинт/UI показа тренировок группы (через `HubGroupPermissionService`) +
`Sys_SwimmerAccountLinks` (привязка аккаунта по инвайту).

> ⚠ Сверено 2026-09-01: тренировки **сделаны** — `GET /api/hub-groups/{slug}/trainings`
> проверяет права через `HubGroupPermissionService`, на клиенте есть `training-table`.
> `Sys_SwimmerAccountLinks` по-прежнему НЕ существует — привязки аккаунта по инвайту нет.

## Открытые мелочи (решить при реализации)

- `pool_type` в тренировках = `"25"` (без `m`), в соревнованиях `"25m"` — нормализовать.
- `event_style_age` местами число, местами строка — приводить к строке.
- Пол пловца брать из `event_style_gender` (на уровне записи), проставить в `Swimmer.Gender` при создании.
