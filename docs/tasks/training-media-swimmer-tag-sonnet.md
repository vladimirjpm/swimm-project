# Задание Sonnet — «Who's in it»: тренер отмечает пловца на видео тренировки (01.10.2026)

## Контекст

К тренировке группы цепляются ссылки на видео / фото / альбомы (форма «Add video», лента
Trainings, лайтбокс). Влад хочет, чтобы тренер мог добавить видео **про пловца** — технический
разбор, съёмка одного человека. Обсуждено и решено 01.10.2026 (см. «Решения»): это **метка «кто на
видео»**, а не «добавлено от имени пловца». Автором ссылки остаётся тот, кто её добавил.

Колонка под метку уже есть: `Sys_HubGroupMedia.SwimmerId` (`HubGroupMedia.SwimmerId`) — сейчас
её заполняет только закрытая галерея группы (members-слой, «разбор пловца»), склейка дублей
пловцов (`SwimmerMergeService`) её уже переносит. Для медиа тренировок (`TrainingId != null`)
колонка сейчас всегда `null`. **Новой миграции не нужно.**

Связано: тип тренировки (Р7 в `docs/plans/trainings-tab-redesign-plan.md`, сделан 01.10.2026) —
у тренировки типа `technique` поле «Who's in it» в форме раскрыто сразу.

## Решения (зафиксированы, не пересматривать)

1. **Метка, не авторство.** `CreatedByUserId` — как сейчас (кто добавил). Квота «N of 10 yours»,
   «Added by you», право удалить — по автору, как сейчас. Пловец-метка прав не получает никаких
   (удалить / снять метку он не может — правило «primary favorite недоверенный»: связь
   «я — этот пловец» на сайте не доверенная).
2. **Отмечает только управляющий** (`isManager`). Участник прислал `swimmer_id` → **403** «Only the
   coach tags a swimmer.» (решение Влада: «участники не отмечают»).
3. **Один пловец на ссылку** — та самая колонка `SwimmerId`. Таблицы отметок нет.
4. **Пловец — из видимого состава группы**: строка `HubGroupMembers` этой группы с
   `!IsExcluded` (так же считает состав `LanePlanService.LoadRosterAsync`; пловцы группы без
   федерации, Р71, там тоже есть). Иначе **400** «This swimmer isn't in the group.» Ушедший из
   состава ПОСЛЕ отметки — метка остаётся (снимок, как у плана дорожек).
5. **Сменить / снять метку** у существующей ссылки — новая ручка
   `PUT /api/hub-groups/{id}/sessions/{sessionId}/media/{mediaId}/swimmer`, тело
   `{ "swimmer_id": 123 }` или `{ "swimmer_id": null }`, только управляющий (`ManagerAsync`, как
   у `key-set`), ответ — обновлённое медиа (`TrainingSessionMediaDto`); 404 — нет ссылки в этой
   тренировке этой группы; 400 — пловец не из состава.
6. **Ответ медиа** (`TrainingSessionMediaDto`, snake_case): новые поля
   - `swimmer`: `{ "id", "name", "name_en" }` или `null` — новый класс `TrainingMediaSwimmerDto`
     рядом; имя — `LastName + " " + FirstName` и `LastNameEn + " " + FirstNameEn` с `Trim()`, как
     в `HubGroupMediaService.GetMembersMediaAsync`;
   - `for_me`: `true`, если отмеченный пловец — «мой» для зрителя (`MySwimmersQuery.LoadAsync`:
     сам и семья; это только подсветка, прав не даёт). `MySwimmersQuery` грузить ОДИН раз на
     запрос и только если хоть у одной ссылки есть метка.
7. **Видимость не меняется.** Метка не выводит медиа тренировки ни на публичную страницу
   пловца, ни в галерею группы: `GetGalleryAsync` и `GetMembersMediaAsync` уже фильтруют
   `TrainingId == null` — так и оставить, покрыть тестом (п. «Тесты»).
8. **Клиент — форма «Add video»** (`trainings/sheets/add-video-sheet.tsx`): только у управляющего
   (`manages` из контекста) — поле **«Who's in it»** между «Link» и «Type»: `<select>` в стиле
   `SHEET_FIELD_CLS`, первый вариант «Whole group» (значение пусто → `swimmer_id: null`), дальше
   состав — `group.members` контекста (там уже и пловцы группы без федерации), подпись варианта
   — `name || name_en` (иврит первым — правило проекта). Если выбранная тренировка не `technique`
   (тип — `trainingTypeOf(session).id` из `trainings/training-types.ts`), поле свёрнуто в
   текстовую кнопку «+ Tag a swimmer» (12px/800 `--deep-ow`), по нажатию раскрывается; у
   `technique` — раскрыто сразу. Участнику поля нет вовсе.
9. **Клиент — плитка** (`training-table/training-by-session/training-media-strip.tsx`, общая с
   журналом тренировок): новый необязательный проп `tagOf?: (item: T) => { name: string; mine:
   boolean } | null`. Есть метка — нижняя полоса плитки показывает имя вместо подписи (`caption`
   остаётся в лайтбоксе): класс `tbs-media-tag` в `training-by-session.css` по образцу
   `.tbs-media-caption` (тот же градиент, белый текст, многоточие), имя в `<bdi>`; при `mine` —
   плашка во всю полосу фоном `var(--deep-accent)` и текстом `var(--deep-accent-ink)` (у журнала
   тренировок пропа нет — там ничего не меняется). Передаёт его лента:
   `trainings/feed/training-card.tsx` → `tagOf={(m) => m.swimmer ? { name: m.swimmer.name ||
   m.swimmer.name_en, mine: !!m.for_me } : null}`.
10. **Клиент — лайтбокс** (`trainings/sheets/training-lightbox.tsx`, всегда тёмный):
    - строка меты (сейчас `Added by you · <источник>`) получает первым `For <имя>` (в `<bdi>`),
      если есть метка;
    - управляющему — над кнопками подвала строка «Who's in it» с тем же `<select>` (Whole group +
      состав), смена сохраняет сразу через новую ручку (п. 5); пока идёт запись — `disabled`;
      ошибка — строкой `--deep-danger` над кнопками; после успеха список тренировок
      перечитывается (как после `setKeySet`).
11. Тексты — английские, дословно: «Who's in it», «Whole group», «+ Tag a swimmer», «For <name>»,
    «Only the coach tags a swimmer.», «This swimmer isn't in the group.». Цвета — роли; в CSS
    плитки — те же литералы, что у соседних `.tbs-media-*` (это общий журнал, роли там не везде).

## Что уже готово (не переделывать)

- `Sys_HubGroupMedia.SwimmerId` + FK, перенос при склейке пловцов (`SwimmerMergeService`),
  «не сирота» в чистках (`JsonImportService`, `SwimmerDedupService`) — всё уже учитывает колонку.
- `HubGroupMediaService.AddAsync` — проверки ссылки (тип, источник, https, длины). Для медиа
  тренировки он сейчас НЕ ставит якоря (`swimmerId` остаётся null) — см. шаг 2.
- `TrainingSessionService.AddMediaAsync` / `DeleteMediaAsync` / `LoadMediaAsync`, `Viewer`,
  `MySwimmersQuery` (`Swimm.Infrastructure/Services/MySwimmersQuery.cs`).
- Контроллер `HubGroupTrainingSessionsController` (`ViewerAsync`, `ManagerAsync`, `Reply`).
- Клиент: `use-training-sessions.ts` (`addMedia`, `write`, перечитывание), `types.ts`
  (`TrainingSessionMedia`, `TrainingSessionMediaInput`), `trainings/training-types.ts`
  (`trainingTypeOf`), формы на `DeepSheet`.

## Шаги

**Сервер**
1. `Swimm.Application/Dtos/TrainingSessionDtos.cs`: `TrainingSessionMediaInputDto.SwimmerId`
   (`swimmer_id`, `int?`); `TrainingSessionMediaDto.Swimmer` (`swimmer`) + `ForMe` (`for_me`);
   класс `TrainingMediaSwimmerDto` (`id`, `name`, `name_en`); вход ручки метки
   `TrainingMediaSwimmerInputDto` (`swimmer_id`, `int?`).
2. `HubGroupMediaService.AddAsync`: в ветке медиа тренировки (`input.TrainingId != null`)
   принять `input.SwimmerId` как есть — комментарий: «проверку состава и права сделал вызывающий
   (`TrainingSessionService`)». Members-ветку и public-ветку не трогать.
3. `TrainingSessionService.AddMediaAsync`: `swimmer_id` прислан → не управляющий — 403 (реш. 2);
   не из состава — 400 (реш. 4); передать `SwimmerId` в `HubGroupMediaInputDto`. Проверку
   состава — приватным методом `InRosterAsync(hubGroupId, swimmerId)`.
4. `TrainingSessionService.SetMediaSwimmerAsync(hubGroupId, sessionId, mediaId, actorUserId,
   TrainingMediaSwimmerInputDto)` → `TrainingSessionMediaSaveResult` (реш. 5); в
   `ITrainingSessionService` — сигнатура; в контроллере — `[HttpPut("{sessionId:int}/media/{mediaId:int}/swimmer")]`
   через `ManagerAsync`, `Cache-Control: private, no-store`, как соседи.
5. `LoadMediaAsync`: выбрать `SwimmerId` + имена пловца; заполнить `Swimmer` и `ForMe` (реш. 6).

**Клиент**
6. `types.ts`: `TrainingSessionMedia.swimmer?: { id: number; name: string; name_en: string } | null`,
   `for_me?: boolean`; `TrainingSessionMediaInput.swimmer_id?: number | null`.
7. `use-training-sessions.ts`: `setMediaSwimmer(sessionId, mediaId, swimmerId | null)` по образцу
   `setKeySet` (тот же `write`, текст ошибки «Could not change who's in it.»).
8. `add-video-sheet.tsx` (реш. 8), `training-media-strip.tsx` + `training-by-session.css` (реш. 9),
   `training-card.tsx` (`tagOf`), `training-lightbox.tsx` (реш. 10).
9. Доки: `docs/media-page.md` — короткий раздел «Метка пловца на видео тренировки (01.10.2026)»:
   что это, кто ставит, что метка не меняет видимость и не даёт прав; `docs/ui-components.md` —
   в строке `TrainingMediaStrip` упомянуть проп `tagOf`. План и передачу не трогать.

## Тесты (обязательно, `server/Swimm.Tests/TrainingSessionTests.cs`, InMemory, образец — `Member_AddsLink_MineAndDeleteFlagsPerViewer`)

- управляющий добавляет ссылку с `swimmer_id` пловца из состава → в ответе `swimmer` с именем;
  в базе `SwimmerId` проставлен;
- участник с `swimmer_id` → 403, ссылки нет;
- пловец не из состава (другая группа / `IsExcluded`) → 400;
- `for_me`: зритель, у которого `AppUser.SwimmerId` = отмеченный, видит `for_me = true`, другой
  участник — `false`; без меток `MySwimmersQuery` не нужен (достаточно проверить результат);
- `SetMediaSwimmerAsync`: смена на другого пловца, снятие (`null`), 404 на чужую тренировку /
  чужую ссылку, 400 на пловца не из состава;
- **приватность**: отмеченная ссылка тренировки НЕ попадает в `HubGroupMediaService.GetGalleryAsync`
  и `GetMembersMediaAsync` той же группы.

Для сида состава понадобится строка `HubGroupMember` (`HubGroupId`, `SwimmerId`, `IsExcluded`) —
посмотри, как её заводят соседние тесты (`grep -rn "new HubGroupMember" server/Swimm.Tests`).

## Проверка

```bash
dotnet build server/Swimm.sln -c Release
dotnet test server/Swimm.Tests -c Release --filter "FullyQualifiedName~TrainingSession"
cd client && npx tsc --noEmit -p .
cd client && npx vite build
```
Всё без ошибок. ⚠ Debug-сборку держит API Влада на :5078 (`MSB3027` / `MSB3021`) — **процесс не
трогать**, работать в `-c Release`. Стенд не запускай — живую проверку делает ревьюер.

## Footguns

- В рабочей копии есть чужие файлы Влада (`!design_handoff/…`) — **ничего не коммитить, `git add`
  не делать**, трогать только файлы задания.
- Два `DbContext`: всё здесь — `SwimmDbContext` (таблицы `Sys_*`), `swimm_ro` их не видит — так и надо.
- `HubGroupMedia.Visibility` у медиа тренировки не используется — не ставь туда `members`, иначе
  ссылка уедет в закрытую галерею группы.
- `TrainingMediaStrip` общий с журналом тренировок (`training-by-session`) — без `tagOf` его вид
  не должен измениться ни на пиксель.
- Лайтбокс всегда тёмный (`.theme-deep`) — `<select>` внутри красить ролями тёмной темы, как
  остальное в лайтбоксе.
- Имена на иврите — `<bdi>` + `dir="auto"`; в `<option>` `<bdi>` нельзя — просто текст.
- `scrollIntoView` не использовать.

## Вне скоупа (не делать)

- «Save to My media» у отмеченного пловца (отложено Владом).
- Несколько пловцов на одной ссылке, метка «вся дорожка».
- Снятие метки самим пловцом («Not me»), метка участником.
- Показ отмеченных видео на странице пловца / в «My media» / в Following.
- Метки внимания за «видео про тебя» (новые видео уже дают акцентную метку ленты).
- Разовые занятия в Next.
