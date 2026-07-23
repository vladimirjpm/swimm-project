# Задание: привязка личного медиа (2A) к заплыву — result_id в API + UI в карточке пловца

## Контекст

Личное owner-only медиа (`UserMedia`, «Мои ссылки» в карточке пловца) сейчас поддерживает
только уровень `swimmer` — сервер игнорирует привязку к заплыву, хотя схема БД её уже имеет
(`UserMedia.ResultId`, `CompetitionId`, `Level`, см. `server/Swimm.Domain/Entities/UserMedia.cs`).
Влад добавил видео заплыва, и оно легло «на пловца вообще». Задача: дать возможность
привязать ссылку к конкретному заплыву — добавлять её прямо со строки результата в карточке
пловца — и показывать иконку на строках, у которых есть видео (пока видит только владелец).

Это этап 1 большой медиа-фичи (модель видимости — см. память media-visibility-model, но она
**вне скоупа**: здесь всё остаётся `Visibility="private"`).

## Решения (зафиксированы, не пересматривать)

- `AddUserMediaRequest` получает **опциональные** `result_id` (long?) и `competition_id` (int?).
- `Level` клиент НЕ передаёт — сервер выводит сам: есть `result_id` → `"result"`;
  нет `result_id`, есть `competition_id` → `"competition"`; ничего → `"swimmer"` (как сейчас).
- Валидация владения (в репозитории, до вставки):
  - `result_id` задан → заплыв существует **и** `Result.SwimmerId == request.SwimmerId`,
    иначе отказ (`null` из AddAsync → 400 в контроллере). Никогда не доверять клиенту.
  - при `result_id` сервер сам заполняет `CompetitionId` из заплыва (клиентский
    `competition_id` тогда игнорируется);
  - `competition_id` без `result_id` → соревнование существует, иначе отказ.
- `Visibility` по-прежнему всегда `"private"`. Никаких новых уровней видимости.
- `UserMediaDto` дополняется `result_id`, `competition_id` (JsonPropertyName snake_case).
- UI-точка добавления «к заплыву» — в карточке пловца (`sportsmen-details.tsx`), на строках
  таблиц результатов карточки (компоненты `TopResultsTabs`/`ResultsTable` внутри этого файла):
  маленькая кнопка «+ видео», видна только залогиненному и только если у строки есть
  DB id заплыва. Клик → открывается та же секция «Мои ссылки» с преднабранным контекстом
  привязки (см. Шаги), НЕ отдельный попап-редактор.
- Иконка «есть видео» на строке: рендерится, если среди загруженного `media` владельца есть
  запись с `result_id === row.id`. Клик по иконке → лайтбокс с этим видео (существующий
  механизм `UI_SwimmerGallery` в `MyMediaSection`). Только owner-view — общий флаг
  hasMedia для всех зрителей будет отдельным этапом.
- В списке «Мои ссылки» у привязанных элементов — маленький чип с дистанцией/стилем
  (например «50m Breast») или датой; данных в `UserMediaDto` для названия нет — чип строй
  по совпадению `result_id` со строками текущей карточки, иначе чип «заплыв».

## Что уже готово (не переделывать)

- Сервер: `server/Swimm.API/Controllers/MediaController.cs` (`/api/me/media`, antiforgery,
  authorize), `server/Swimm.Infrastructure/Repositories/UserMediaRepository.cs`,
  `server/Swimm.Application/Dtos/UserMediaDtos.cs`,
  `server/Swimm.Application/Validation/MediaUrlValidator.cs` (не трогать),
  entity `UserMedia` — поля уже есть, **миграция НЕ нужна**.
- `/api/results` уже отдаёт `id` заплыва (`ResultDto.Id`, long) — серверных правок
  для этого не нужно.
- Клиент: `client/src/hooks/useUserMedia.ts` (типы + add/remove),
  `client/src/projects/sportsmen-details/sportsmen-details.tsx` — секция `MyMediaSection`
  (форма, список, лайтбокс) и локальные `TopResultsTabs` (~строка 574) / `ResultsTable`
  (~строка 643).
- Паттерн тестов репозитория: `server/Swimm.Tests/UserMediaRepositoryTests.cs`
  (in-memory SQLite, смотри `CreateDb`).

## Шаги

1. **DTO** (`UserMediaDtos.cs`): в `AddUserMediaRequest` добавь `ResultId` (`long?`,
   `[JsonPropertyName("result_id")]`) и `CompetitionId` (`int?`, `"competition_id"`).
   В `UserMediaDto` — те же два поля. Обнови xml-comment класса (Level выводится сервером).
2. **Репозиторий** (`UserMediaRepository.cs`): в `AddAsync` реализуй валидацию и вывод
   `Level` по правилам из «Решений». Заполняй `ResultId`/`CompetitionId` в entity и в
   возвращаемом DTO. В `GetForUserAsync` — прокинь новые поля в DTO.
3. **Тесты** (`UserMediaRepositoryTests.cs`, по образцу существующих):
   - result_id валидный (заплыв этого пловца) → Level="result", CompetitionId взят из заплыва;
   - result_id чужого пловца → null (отказ);
   - result_id несуществующий → null;
   - competition_id без result_id, существующее → Level="competition";
   - competition_id несуществующее → null;
   - без обоих → Level="swimmer" (регресс не сломан);
   - при result_id клиентский competition_id игнорируется (подставлен из заплыва).
4. **Клиент — типы**: в `useUserMedia.ts` добавь `result_id?: number | null`,
   `competition_id?: number | null` в `UserMediaDto` и `AddUserMediaInput`; `level` в DTO
   расширь до `'swimmer' | 'competition' | 'result'`. В `client/src/utils/interfaces/results.ts`
   объяви в `Result` поле `id?: number` (сервер его уже шлёт; на статике его нет).
5. **Клиент — кнопка «+ видео» на строке** (`sportsmen-details.tsx`): в строках
   `ResultsTable` (и через него `TopResultsTabs`) добавь компактную кнопку/иконку «+»,
   видимую при `isAuthenticated && row.id != null`. Клик поднимает состояние
   `pendingResultId` вверх (в `SportsmenDetails`) и прокидывается в `MyMediaSection`
   пропом `attachResultId?: number`; секция показывает над формой заметный чип
   «Привязать к заплыву: …» с крестиком-отменой, и `handleAdd` передаёт
   `result_id: attachResultId` в `add(...)`. После успешного добавления привязка
   сбрасывается. Проп-дриллинг простой — Redux не трогать.
6. **Клиент — иконка на строке**: media владельца уже грузится в `MyMediaSection`;
   подними `useUserMedia(swimmerId)` из `MyMediaSection` в `SportsmenDetails` и передай
   вниз (и в таблицы — set из result_id, и в секцию — сам hook-объект), чтобы не было
   двух запросов. На строке с совпадением — маленькая иконка видео; клик открывает
   лайтбокс этого видео (проще всего: коллбэк `onOpenMedia(resultId)` вверх →
   `MyMediaSection`/лайтбокс). Стилистика — как существующие мелкие иконки строк
   (см. `UI_*` в строках, следуй локальному стилю Tailwind + var(--theme-*)).
7. **Клиент — чип в «Моих ссылках»**: для элементов с `result_id` — чип с
   `event_style_len + event_style_name` строки карточки с тем же id, если строка
   найдена среди `allSwimmerResults`; иначе текст «заплыв».

## Проверка

```bash
dotnet build server/Swimm.sln --configuration Release
dotnet test server/Swimm.Tests --configuration Release
npx --prefix client tsc --noEmit -p client
```

Живая проверка: API на :5079 (`dotnet run --project server/Swimm.API --urls http://localhost:5079`
+ клиент `client-5079` из `client/.claude`/launch-конфига — vite proxy на 5079, см. footguns),
залогиниться, открыть results_main → карточку пловца с результатами из API-источника,
добавить ссылку с кнопки «+» на строке, убедиться: чип привязки в форме, после добавления —
иконка на строке, элемент в «Моих ссылках» с чипом, `GET /api/me/media?swimmerId=…` содержит
`result_id`/`competition_id`, уровень `result`.

## Footguns

- **Build-lock**: Visual Studio/старый `dotnet run` держит Swimm.API.dll → MSB3027.
  Обход: собирай/тестируй с `--configuration Release`, живой прогон на порту :5079.
- Два DbContext: сюда миграции не нужны, но если запустишь ef — всегда
  `--context SwimmDbContext`.
- `Result.id` есть ТОЛЬКО у данных с `/api/results`; статические JSON-источники его не
  имеют — кнопка и иконка должны просто не рендериться (`row.id == null`), без ошибок.
- В `sportsmen-details.tsx` результаты типизированы как `any[]` — не начинай глобальную
  типизацию, возьми `row.id` аккуратно (`typeof row.id === 'number'`).
- Antiforgery уже обрабатывается в `useUserMedia` — не дублируй.
- `ResultDto.Id` — `long`; в JSON это number, на клиенте обычный `number` — ок.

## Вне скоупа (не делать)

- Любая видимость кроме `private` (публикации в группы — отдельные этапы).
- Флаг hasMedia/иконка для НЕ-владельца, изменения `/api/results`.
- Иконки в общей таблице результатов (`projects/results-table`) — только карточка пловца.
- Вкладка «Медиа» в профиле, миграции схемы, перенос двух существующих видео Влада.
- Рефакторинг `sportsmen-details.tsx` сверх описанного подъёма state.
