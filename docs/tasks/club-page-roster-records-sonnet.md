# Задание Sonnet — K4.2: API ростера и клубных рекордов

## 1. Контекст

Делаем публичную **страницу клуба** (Фаза 10). Модель и план:
- [docs/plans/club-page-model.md](../plans/club-page-model.md) — модель данных;
- [docs/plans/club-page-plan.md](../plans/club-page-plan.md) — план; это этап **K4.2**.

Уже готово (не переделывать): маршрут `/clubs/{id}` (K1), `club_id` в `ResultDto` и мягкий
merge клубов (K2), материализованный клубный зачёт `ClubCompetitionStandings` (K3),
сезонный шов `SeasonMath` / `SeasonAggregator` (K0).

Твоя часть — **два публичных эндпоинта**, которые карточки страницы догружают по требованию:
ростер клуба («Show all N») и клубные рекорды (переключатель бассейна 25m/50m).
Карточку `overview` делает Fable отдельно — **не трогай её**.

## 2. Решения (зафиксированы, не пересматривать)

1. Маршруты: `GET /api/clubs/{id}/roster` и `GET /api/clubs/{id}/records`.
2. **Новый контроллер** `server/Swimm.API/Controllers/ClubsController.cs` (публичный;
   `ClubsAdminController` — это админка, туда не лезь).
3. Читаем через **`SwimmReadDbContext`** (публичный read-путь, роль `swimm_ro`), как другие
   публичные репозитории. Порт — новый `IClubPublicRepository` в
   `Swimm.Application/Abstractions`, реализация в `Swimm.Infrastructure/Repositories`.
4. **Псевдоклуб** (`Club.IsPseudo`) — оба эндпоинта отдают **404**: это страна/сборная, а не клуб.
5. **Склеенный клуб** (`Club.MergedIntoId != null`) — отдаём данные клуба-приёмника
   (редиректа нет, решение Влада). Разматывать цепочку рекурсивно НЕ надо: merge второго
   уровня запрещён guard-ом, достаточно одного перехода.
6. Кэш + `ETag` как у соседних публичных эндпоинтов (см. `RecordsController` / `HubGroupsController`).
7. UI-строк не добавляем — это чистое API.

## 3. `GET /api/clubs/{id}/roster`

Состав клуба из `Swimmer.ClubId`.

Параметры: `page` (1..), `pageSize` (default 50, максимум 200), `gender` (`male`/`female`,
опционально), `ageFrom`, `ageTo` (опционально), `season` (год начала сезона, опционально).

```jsonc
{ "page": 1, "page_size": 50, "total": 63, "has_more": true,
  "data": [
    { "swimmer_id": 42, "last_name": "…", "first_name": "…",
      "last_name_en": "…", "first_name_en": "…",
      "birth_year": 2012, "age": 14, "gender": "male",
      "competitions": 6, "swims": 31 }
  ] }
```

- **Возраст** = `сезон - BirthYear` (возраст в сезоне), где сезон берётся из
  `SeasonMath.CurrentStartYear()` либо из параметра `season`. Это **НЕ** зачётная группа
  (`Category`) — границы возрастных пилюль макета (`8–11 / 12–14 / 15–16 / 17+`) свои.
- `competitions` — сколько РАЗНЫХ соревнований у пловца за клуб; `swims` — сколько заплывов.
  Если задан `season` — только в границах сезона (`SeasonMath.RangeOf`).
- Сортировка: по фамилии (`LastNameEn`, затем `LastName`), стабильно.

## 4. `GET /api/clubs/{id}/records`

Клубные рекорды — лучшее время по оси **стиль × дистанция × бассейн × пол**.

Параметры: `pool` (`25m`/`50m`, опционально — без него отдаются обе оси).

```jsonc
{ "data": [
    { "style_name": "freestyle", "distance": "100", "pool_type": "25m", "gender": "male",
      "time_original": "00:55.10", "time_ms": 55100,
      "swimmer_id": 42, "swimmer_name": "…", "swimmer_name_en": "…",
      "competition_name": "…", "date": "15/02/2026", "points": 730 }
  ] }
```

**Логику НЕ придумывай — она уже написана** для «рекордов группы» (фаза 8.3):
`server/Swimm.Infrastructure/Repositories/HubGroupPublicRepository.cs`, блок
`dto.Bests = await db.Results...` (около строк 193–228) + DTO `HubGroupBestDto` в
`Swimm.Application/Dtos/HubGroupPublicDtos.cs`. Скопируй ось, группировку и сортировку.

Отличия от группы (обязательные):
1. Отбор строк — по `r.ClubId == id`, а не по списку пловцов.
2. Дополнительно исключить **`r.SuspectReason != null`** (помеченные ошибки протокола —
   такие не должны становиться «рекордом клуба»; у групп этого фильтра нет, это их долг,
   но здесь делаем правильно сразу).
3. Как и у групп: без эстафет (`RelayId == null`), без `TimeFail`, `TimeMillisecond != null`.

⚠ **25m и 50m — разные рекорды**, объединять времена нельзя ни при каких условиях.

## 5. Тесты (обязательно)

Файл `server/Swimm.Tests/ClubPublicRepositoryTests.cs`, xUnit + InMemory —
образец структуры и хелперов: `Swimm.Tests/ClubStandingServiceTests.cs` (свежий, там же
показано, как сеять `Competition`/`Club`/`Style`/`ResultRecord`).

Обязательные кейсы:
- ростер: пагинация, фильтр по полу, фильтр по возрасту, счётчики `competitions`/`swims`;
- ростер: возраст считается от сезона, а не от текущей даты (передать `season`);
- рекорды: 25m и 50m дают **разные** строки для одной дисциплины;
- рекорды: `TimeFail`, эстафеты и `SuspectReason` исключены;
- рекорды: при равенстве времени берётся более ранний заплыв;
- псевдоклуб → 404 (на уровне репозитория — null/пусто, а контроллер отдаёт 404);
- склеенный клуб (`MergedIntoId`) отдаёт данные приёмника.

## 6. Проверка

```bash
dotnet build server/Swimm.sln --configuration Release
dotnet test server/Swimm.Tests/Swimm.Tests.csproj --configuration Release
```
Сюита сейчас **847/847 зелёная** — должна остаться зелёной и вырасти на твои тесты.

Живая проверка на реальной БД (API на :5079, конфигурация `swimm-api-5079`):
```bash
curl -s "http://localhost:5079/api/clubs/452/roster?pageSize=5"
curl -s "http://localhost:5079/api/clubs/452/records?pool=25m"
```
Клуб 452 — הפועל דולפין נתניה, у него много данных. Проверь, что ETag возвращается и что
повторный запрос с `If-None-Match` даёт 304. **Останови API после проверки.**

## 7. Footguns

- **Build-lock:** Visual Studio держит Debug-выход `Swimm.API`. Собирай `--configuration Release`,
  API поднимай на :5079. **Не убивай чужие процессы.**
- **Два DbContext.** Публичное чтение — `SwimmReadDbContext`; `SwimmDbContext` только там, где
  пишут или трогают `Sys_*`. Миграции в этом задании не нужны вовсе.
- **Clean Architecture:** контроллер инжектит только интерфейс из `Swimm.Application/Abstractions`,
  никаких типов `Swimm.Infrastructure`. Регистрация — в `Swimm.Infrastructure/DependencyInjection.cs`.
- **Перф:** фильтровать по `Id`, не по `Name` (JOIN по имени убивает индексы — так уже
  ловили падение p95 с 5 мс до 14 с). Ростер и рекорды — по `ClubId`.
- Даты соревнований хранятся строкой `dd/MM/yyyy` (`Competition.Date`), а в результатах есть
  `CompetitionDate` типа `DateTime` — для фильтра по сезону бери второе.

## 8. Вне скоупа (НЕ делать)

- ❌ `GET /api/clubs/{id}/overview` и вообще карточка overview — её делает Fable.
- ❌ Любые изменения в `ClubStandingService`, `ClubStandingCalculator`, `ResultDto`,
  `ClubMergeService`, правилах очков. Даже «мелкую правку рядом».
- ❌ Миграции и изменения сущностей.
- ❌ Клиентский код (`client/`) — этап K5.
- ❌ Правка `HubGroupPublicRepository` — оттуда только копируем логику.

## 9. Отчёт

Что сделано, какие файлы, результат `build`/`test`, что проверено вживую (с примером ответа),
и раздел **«Отклонения»** — всё, где отступил от задания, с причиной.
