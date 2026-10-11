# Задание Sonnet — разовые занятия и отмена занятия в Next (01.10.2026)

## Контекст

Вид **Next** таба Trainings группы показывает три ближайших занятия, на них отвечают «иду / не
уверен / не приду» (RSVP). Сейчас ближайшие занятия берутся **только из недельного расписания**
группы (`GroupTrainingSchedule.NextOccurrences`, JSON в `HubGroups.TrainingSchedule`), а ответ
сервер принимает только на занятие, которое есть в расписании (`TrainingRsvpRules.FindSlot`).

Влад хочет (решения 01.10.2026, см. Р9 в `docs/plans/trainings-tab-redesign-plan.md`):

1. **Разовое занятие** — тренер заводит тренировку («+ New training») на будущую дату с временем
   начала и ставит галку **«Ask who's coming»** → она появляется в Next среди ближайших, на неё
   отвечают, у тренера — перекличка, как у обычного занятия.
2. **Отмена занятия** — «в этот вторник тренировки нет»: тренер отменяет конкретное занятие
   (по расписанию или разовое) на дату; оно остаётся в Next с пометкой «Cancelled», ответить на него
   нельзя; отмену можно вернуть.

Адрес занятия везде — **ключ `yyyy-MM-dd-HHmm`** (`TrainingRsvpRules.SessionKey`), им адресуются и
ответы (`/api/hub-groups/{id}/rsvp/{key}`). Разовое и отменённое — тот же ключ: если разовое
совпало по дате и времени со слотом расписания, это ОДНО занятие.

Все пути — от корня репозитория.

## Решения (зафиксированы, не пересматривать)

### Модель
1. **Разовое = тренировка с галкой.** `TrainingSession.AsksRsvp` (`bool`, по умолчанию `false`,
   колонка `"AsksRsvp" boolean NOT NULL DEFAULT false`). Занятием (occurrence) она считается, если
   `AsksRsvp && StartTime != null`. Без галки тренировка — просто запись ленты, как сейчас.
   Галку можно поставить только при заданном `Start` и дате **не раньше сегодняшней** (по Израилю);
   иначе 400 «Only a future training with a start time can ask who's coming.» Снять галку можно
   всегда. Правка даты / времени у тренировки с галкой — ключ меняется, ответы на старый ключ
   остаются в базе, но к занятию больше не относятся (так же, как при правке расписания сейчас).
2. **Отмена — отдельная таблица** `Sys_HubGroupTrainingCancellations`: `Id`, `HubGroupId` (FK
   `HubGroups`, cascade), `Date` (`date`), `Start` (`varchar(5)`, «HH:mm»), `Reason` (`varchar(200)`,
   null), `CreatedByUserId` (FK `Sys_AppUsers`, `SET NULL`), `CreatedAt` (`timestamptz`).
   **UNIQUE (`HubGroupId`, `Date`, `Start`)**. Сущность `HubGroupTrainingCancellation` в
   `Swimm.Domain/Entities/`. Приватная (`Sys_`, без grant `swimm_ro`). В
   `server/db/seed-tables.txt` — в комментарий-перечень не переносимых (у неё FK на
   `Sys_AppUsers`), рядом с `Sys_HubGroupBreaks`. Отменить можно только занятие, которое
   существует (слот расписания или разовое) и лежит в окне ответов
   (`[сегодня − ManagerDaysBack; сегодня + DaysAhead]`); иначе 400 «There is no such training.»
   Повторная отмена — 200 (идемпотентно, причина обновляется). Ответы при отмене **не удаляются**
   (вернул отмену — ответы на месте).
3. **Одна миграция** `AddOneOffAndCancelledTrainings` (колонка + таблица). ⚠ Перед тем как писать
   код, выполни `docker exec swimm-postgres psql -U swimm -d swimm -c '\d "Sys_TrainingSessions"'`
   и прочитай Check constraints — InMemory-тесты их не видят (урок Р8). Сгенерированную миграцию
   прочитай целиком: `DEFAULT false` у колонки обязателен.

### Ближайшие занятия (чистая функция)
4. Новый файл `server/Swimm.Application/Mapping/TrainingOccurrences.cs` — чистые функции, «сейчас»
   параметром в местном времени Израиля (как `NextOccurrences`):
   - `record OneOff(int TrainingId, DateOnly Date, string Start, string? Title, string Type, string? PoolType)`;
   - `record Cancellation(DateOnly Date, string Start, string? Reason)`;
   - `record Occurrence(DateOnly Date, string Start, string? End, int? TrainingId, string? Title,
     string? Type, string? PoolType, bool OneOff, bool Cancelled, string? CancelReason)`;
   - `Find(schedule, oneOffs, cancellations, date, start) → Occurrence?` — занятие с этим ключом:
     слот расписания (по дню недели и `Start`) или разовое; разовое на тот же ключ, что и слот, —
     одно занятие: `End` от слота, `TrainingId`/`Title`/`Type`/`PoolType` от тренировки,
     `OneOff = false` (оно в расписании). `Cancelled` — есть отмена на ключ;
   - `Upcoming(schedule, oneOffs, cancellations, nowLocal, count) → List<Occurrence>` — слоты
     расписания (`schedule.NextOccurrences(nowLocal, count + число отмен в окне)` с запасом) +
     разовые, у которых дата+начало позже `nowLocal` (начавшееся — уже не ближайшее, как у
     расписания); склейка по ключу; сортировка по дате и началу; берём занятия, пока **не
     отменённых** не наберётся `count`; отменённые, попавшие ДО последнего взятого, остаются в
     списке на своих местах (флаг `Cancelled`).
5. `TrainingRsvpRules`: `IsViewable` и `EditBlockReason` принимают `Occurrence?` вместо поиска
   слота внутри (`FindSlot` оставить — им пользуется `Find`). `EditBlockReason`: `null`
   занятие → «There is no such training in the schedule.» (текст как сейчас); `Cancelled` → «This
   training is cancelled.» (и участнику, и управляющему); дальше — как сейчас, время начала —
   `occurrence.Start`.

### Сервер — где занятия берутся
6. **Страница группы (публичная, кэшируемая)** — `HubGroupPublicRepository`: `next_trainings` /
   `next_training` **не меняются** (только расписание). Разовые и отмены приватные: гостю их не
   видно, в кэш страницы (К4) они не попадают.
7. **Новая приватная ручка** `GET /api/hub-groups/{id}/next-trainings` — в
   `server/Swimm.API/Controllers/HubGroupTrainingRsvpController.cs` (абсолютный маршрут
   `[HttpGet("/api/hub-groups/{id:int}/next-trainings")]`), тем же гейтом, что `GET …/rsvp/{key}`
   (участник или управляющий, иначе 403), `Cache-Control: private, no-store`. Ответ — список
   `NextTrainingDto` (до 3 не отменённых + отменённые между ними, реш. 4). В `NextTrainingDto`
   новые поля: `title` (string?), `type` (string?), `training_id` (int?), `one_off` (bool),
   `cancelled` (bool), `cancel_reason` (string?). Для публичной страницы новые поля просто
   пустые/false.
8. **Ответы** (`TrainingRsvpService.GetAsync` / `SetAsync` / `BuildAsync`): занятие — через
   `TrainingOccurrences.Find` (загрузить разовые группы на эту дату с `AsksRsvp` и отмену на ключ —
   по одному запросу); `End` — `occurrence.End`; в `TrainingRsvpDto` добавить `cancelled`,
   `cancel_reason`, `title`, `type` (для шапки занятия). `CanAnswer` у отменённого — `false`.
9. **Отмена — ручки** (только управляющий, antiforgery как у соседних мутаций, `no-store`):
   `PUT /api/hub-groups/{id}/rsvp/{key}/cancel` (тело `{ "reason": "…" | null }`) и
   `DELETE /api/hub-groups/{id}/rsvp/{key}/cancel` (вернуть) — в том же
   `HubGroupTrainingRsvpController`. Ответ — обновлённый
   `TrainingRsvpDto` (как GET). Логика — в `TrainingRsvpService` (`CancelAsync`, `RestoreAsync`),
   в контроллере только гейт и ответ.
10. **Тренировка** (`TrainingSessionRules` / `TrainingSessionService` / DTO):
    `TrainingSessionInputDto.AsksRsvp` (`asks_rsvp`, `bool?`: null — при создании false, при
    правке не менять), `TrainingSessionDto.AsksRsvp`. Проверка реш. 1 — в `Normalize` нельзя (там
    нет `Start` по умолчанию из расписания) — в сервисе после вычисления итогового `start`.

### Клиент
11. **Данные Next своим** — `hub-groups-project/group-page.tsx`: по образцу `privateMembers`
    (тот же гейт `insiderSlug`) загрузить `GET /api/hub-groups/{id}/next-trainings` и в `group`
    подменить `next_trainings` и `next_training` (= первое НЕ отменённое) — так шапка, липкий чип,
    вид Next и метки получают разовые без правок. Перечитывание — счётчик версии в
    `group-page.tsx`; наружу — проп `onNextTrainingsChanged` у `GroupTrainingsTab` → поле контекста
    `reloadNextTrainings()`. Зовут его: форма тренировки после сохранения / удаления, отмена и
    возврат занятия.
12. **Типы** (`types.ts`): `NextTraining` + `title?`, `type?`, `training_id?`, `one_off?`,
    `cancelled?`, `cancel_reason?`; `TrainingRsvp` + `cancelled?`, `cancel_reason?`, `title?`,
    `type?`; `TrainingSession.asks_rsvp?`, `TrainingSessionInput.asks_rsvp?`.
13. **Форма тренировки** (`trainings/sheets/training-form-sheet.tsx`): галка **«Ask who's coming»**
    (чекбокс 20px + подпись 12.5px/800, под ней 11px `--t-text-3` «Shows in Next — members answer
    Going / Not sure / Can't.») — видна, когда задано `Start` и дата ≥ сегодня (`todayInIsrael()`);
    иначе скрыта и в запрос уходит `asks_rsvp: false` только если была `true` (снять). По
    умолчанию у новой тренировки галка **выключена** (при любом типе).
14. **Вид Next — отменённое занятие** (и участнику, и тренеру): строка/карточка занятия получает
    пилюлю «Cancelled» (рамка `--deep-danger`, текст `--deep-danger`, 11px/800) и причину строкой
    12px `--t-text-3`; кнопки ответа скрыты (сервер всё равно не примет). Места: участник —
    `trainings/next/answers-block.tsx` (строка занятия) и шапка ближайшего
    (`trainings/sections/member-next-section.tsx`, если ближайшее отменено — показать «Cancelled»
    вместо кнопок); тренер — `trainings/coach/session-nav.tsx` (пилюля под датой) и перекличка
    (`coach/roll-call.tsx`: при `cancelled` вместо строк — «Training cancelled» + причина).
15. **Вид Next — разовое**: в навигации тренера и строке ответов участника рядом с датой —
    название тренировки (`title`, `<bdi>`) и значок типа (`trainingTypeOf` из
    `trainings/training-types.ts`, кроме swim), 11.5px/800 `--t-text-2`.
16. **Отмена у тренера** — в `coach/session-nav.tsx` (вид Next тренера) у выбранного занятия
    текстовая кнопка **«Cancel training»** (12px/800 `--deep-danger`) → `ConfirmDialog` «Cancel the
    training on Tue 6 Oct?» с полем причины (input, placeholder «Reason — optional, e.g. Pool
    closed», maxLength 200) и текстом «Members see it as cancelled and can't answer. Answers are
    kept — restore brings them back.»; подтверждение «Cancel training». У отменённого — кнопка
    **«Restore»** (12px/800 `--deep-ow`) без подтверждения. После — `reloadNextTrainings()` и
    перечитать ответы занятия (`roll.reload()`).
17. Тексты — английские, дословно как выше. Цвета — роли. `scrollIntoView` не использовать.

## Что уже готово (не переделывать)

- `GroupTrainingSchedule.NextOccurrences`, `TrainingRsvpRules` (`SessionKey`, `TryParseSessionKey`,
  `FindSlot`, окна `DaysAhead` / `ManagerDaysBack`), `TrainingRsvpService`, `IsraelTime`.
- `TrainingSession` (+ `Type`, Р7), `TrainingSessionRules`, `TrainingSessionService`, форма
  `training-form-sheet.tsx` (дата, старт, тип, название, заметка).
- Клиент: `nextSessions(group)` (`trainings/next/next-format.ts`), контекст таба
  (`upcoming` — ответы на 3 занятия по `nextSessions`), `group-page.tsx` (образец `privateMembers`),
  `ConfirmDialog`, `trainingTypeOf`.

## Шаги

**A. Сервер: модель и чистые правила**
1. Сущности, `SwimmDbContext` (индекс UNIQUE, FK), миграция (реш. 1–3); `seed-tables.txt`.
2. `TrainingOccurrences.cs` (реш. 4); правка `TrainingRsvpRules` (реш. 5).

**B. Сервер: сервисы и ручки**
3. `TrainingSessionService` / DTO / rules — `asks_rsvp` (реш. 1, 10).
4. `TrainingRsvpService`: `Find` вместо `FindSlot`, поля DTO, `CancelAsync` / `RestoreAsync`,
   `GetNextAsync(hubGroupId, nowLocal?)` для ручки next-trainings (реш. 7–9); интерфейс в
   `Swimm.Application/Abstractions`; ручки в контроллере ответов.

**C. Клиент**
5. Типы, `group-page.tsx` (реш. 11), контекст и `trainings-tab.tsx` (`onNextTrainingsChanged`).
6. Форма (реш. 13), вид Next участника и тренера (реш. 14–16).
7. Доки: `docs/plans/trainings-tab-handoff.md` не трогать; в `docs/hubgroups-architecture.md`
   рядом с описанием тренировок кнопкой — абзац «Разовые занятия и отмена (Р9)».

## Тесты (обязательно)

**Чистые** — новый `server/Swimm.Tests/TrainingOccurrencesTests.cs` (образец —
`GroupTrainingScheduleTests` `NextOccurrences_*`):
- только расписание — как `NextOccurrences`;
- разовое между слотами встаёт по времени; разовое без `AsksRsvp` в функцию не передаётся
  (фильтр в сервисе) — тест на `Find`/`Upcoming` с переданным списком;
- разовое на ключ слота — одно занятие, `End` от слота, `Title`/`Type` от тренировки;
- начавшееся сегодня разовое — не ближайшее;
- отменённое: остаётся в списке с `Cancelled`, а не отменённых всё равно `count`; отмена после
  последнего взятого — не в списке;
- группа без расписания, только разовые — список из разовых.

**Правила ответов** — в существующих тестах `TrainingRsvpRules`/`TrainingRsvpTests`: отменённое →
«This training is cancelled.» и управляющему; разовое принимает ответ; несуществующее — прежний текст.

**Сервисы (InMemory)** — `TrainingRsvpTests` / `TrainingSessionTests`:
- `asks_rsvp` на прошлую дату или без начала → 400; снять можно всегда;
- ответ на разовое сохраняется, `GetNextAsync` его отдаёт с `one_off`, `title`, `type`;
- `CancelAsync` / `RestoreAsync`: ответ на отменённое — 400, после возврата — снова можно, старые
  ответы на месте; отмена несуществующего — 400; не управляющий — отказ (проверь в сервисе, если
  гейт там, или через контроллер-гейт — как устроены соседние ручки).

**Живой Postgres** — новый `server/Swimm.Tests/TrainingOccurrencePgTests.cs` по образцу
`TrainingMediaTagPgTests` (своя группа, cleanup в finally, пропуск без PG): создать тренировку с
`AsksRsvp`, отменить занятие дважды (идемпотентность упирается в UNIQUE), вернуть; ответ на
разовое записывается.

## Проверка

```bash
dotnet build server/Swimm.sln -c Release
dotnet ef migrations add AddOneOffAndCancelledTrainings --project server/Swimm.Infrastructure --startup-project server/Swimm.API --context SwimmDbContext --configuration Release
dotnet test server/Swimm.Tests -c Release
cd client && npx tsc --noEmit -p .
cd client && npx vite build
```
Всё без ошибок. ⚠ Debug-сборку держит API Влада на :5078 — процесс не трогать, всё в
`-c Release`. Миграцию к базе **не применяй** (`database update` не запускать) — применит ревьюер.
Стенд не запускай.

## Footguns

- В рабочей копии есть чужие файлы Влада (`!design_handoff/…`) — **ничего не коммитить, `git add`
  не делать**.
- Два `DbContext` — всё здесь в `SwimmDbContext`; `swimm_ro` новую таблицу не видит — так и надо.
- Страница группы кэшируется (К4) — разовые и отмены в её DTO НЕ класть (реш. 6), иначе гость
  увидит приватное, а кэш не сбросится при правке тренировки.
- Ключ занятия — в пути URL, без двоеточий (`2026-10-06-2000`).
- `NextOccurrences` уже считает «начавшееся сегодня — не ближайшее»; для разовых то же правило.
- Время — местное Израиля (`IsraelTime.ToLocal`), даты в базе — `DateOnly` / `date`; `timestamptz`
  писать только UTC (`DateTime.UtcNow`).
- Контекст таба вызывает хуки ответов по `nextSessions(group)` — после подмены `group` в
  `group-page.tsx` ключи занятий меняются; хуки уже переживают смену `sessionId`, но проверь, что
  `rollIndex` не выходит за край (там есть `Math.min`).
- Клиентского раннера тестов нет — клиентских юнит-тестов не писать.

## Вне скоупа (не делать)

- Уведомления об отмене / разовом (нет прод-SMTP).
- Перерыв «Skips N trainings» по-прежнему считает только расписание — не трогать.
- План дорожек отменённого дня (остаётся как есть), Lanes для разового.
- Повторяющиеся разовые («каждую пятницу в октябре»), перенос занятия на другое время одной
  кнопкой (это отмена + разовое).
- Показ разовых и отмен гостю / на публичной странице группы.
