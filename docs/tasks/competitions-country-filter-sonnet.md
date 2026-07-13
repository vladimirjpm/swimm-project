# Задание (Sonnet 5): фильтр по стране в /api/competitions и /api/results + витрина из HOME_REGION

Контекст: аудит поддержки стран (2026-07-13, Fable). У `Competition.Country` (строка,
alpha-3: "ISR"…) фильтрации нет нигде: `/api/competitions` вообще без параметров,
в `/api/results` страны нет среди FromQuery. Плюс на витрине захардкожено
«2026 Season · Israel». Твоя работа — сделать страну фильтруемой осью соревнований
(API-уровень, БЕЗ UI-переключателя) и убрать хардкод с витрины.

## Решения (зафиксированы, не пересматривать)

- Коды стран — **alpha-3 World Aquatics** (`ISR`…), как везде (docs/ARCHITECTURE.md).
  Нормализация входа: trim + upper; `null`/пусто — без фильтра (легаси-поведение).
- `Competition.Country` остаётся **строкой** — перевод на FK (`CountryId`) — отдельная
  задача с бэкфиллом, НЕ твоя (вне скоупа).
- Страна многодневного события = страна любого из его дней (`Any`), т.к. у
  `CompetitionEvent` собственного поля страны нет.
- UI-фильтра/переключателя страны НЕ делать — только API + подпись витрины.

## Что уже готово (не переделывать)

- Шов клиента: `client/src/utils/constants/home-region.ts` — `HOME_REGION = 'ISR'`,
  `HOME_REGION_LABEL = 'Israel'`, `NORMATIVE_COUNTRY = 'RUS'`.
- Свежий образец точно такой же работы — фильтр country в нормативах (коммит
  «Normatives: страна — рабочая ось»): `RecordRepository.GetStandardsAsync` +
  `RecordsController.GetStandards` — повтори этот паттерн (нормализация, страна
  в кэш-ключах, необязательный параметр).

## Шаги

### 1. /api/competitions (список источников селектора)

- `IResultRepository.GetSourcesAsync()` (`server/Swimm.Application/Abstractions/IResultRepository.cs`)
  → `GetSourcesAsync(string? country = null)`.
- Реализация `server/Swimm.Infrastructure/Repositories/ResultRepository.cs`:
  - кэш-ключ был `competition-sources:all` → `competition-sources:{country|all}`;
  - фильтр: однодневные — `c.Country == country`; события — оставить те, у которых
    `Any` день с `Country == country`. Фильтруй ПОСЛЕ существующей сборки списков
    (в памяти, датасет маленький) — не усложняй EF-запросы.
- `ResultsController.GetSources` (`server/Swimm.API/Controllers/ResultsController.cs`,
  `[HttpGet("/api/competitions")]`): параметр `[FromQuery] string? country`,
  нормализовать, HTTP-кэш ключ был `http:competition-sources` →
  `http:competition-sources:{country|all}`.

### 2. /api/results

- `ResultFilter` (`server/Swimm.Application/Dtos/ResultFilter.cs`): свойство
  `public string? Country { get; set; }` с XML-комментом (alpha-3 страны соревнования).
- В реализации фильтра (`ResultRepository`, метод, строящий query для `GetPagedAsync` —
  найди, где применяются `filter.Competition`/`filter.PoolType`, добавь рядом):
  `query.Where(r => r.Competition.Country == country)` при заданной стране.
- `ResultsController.GetResults`: `[FromQuery] string? country`, нормализация,
  прокинуть в `ResultFilter`. Проверь, участвует ли фильтр в ключе HTTP-кэша
  результатов (как другие параметры) — страна должна попасть туда же, тем же способом.

### 3. Витрина

- `client/src/projects/home-project/home.tsx` (строка ~16) и
  `client/src/projects/competitions-project/competitions.tsx` (строка ~57):
  «2026 Season · Israel» → `` `${new Date().getFullYear()} Season · ${HOME_REGION_LABEL}` ``
  с импортом из `utils/constants/home-region`.

## Тесты (обязательно)

`server/Swimm.Tests`, по образцу `ResultRepositoryTests.cs` (InMemory read-контекст) и
`NormativeStandardCountryFilterTests.cs`:
- sources: без country — все; `ISR` — только израильские (одиночные + событие,
  у которого хотя бы один день ISR); чужая страна — пусто;
- sources: нормализация (` isr ` → ISR);
- results: `GetPagedAsync` с `Country` в фильтре отдаёт только результаты
  соревнований этой страны; без — все.

## Проверка

- `dotnet build server/Swimm.sln` (0 ошибок) + `dotnet test server/Swimm.Tests` (все зелёные).
- `npx tsc --noEmit` в `client/` — чисто.
- Живой curl (если поднимешь API): `/api/competitions?country=ISR` = текущий список;
  `?country=USA` = `[]`; `/api/results?competitionId=last&country=ISR` — не пусто.

## Footguns

- Если Debug-bin залочен отладчиком Visual Studio (MSB3027) — собирай/гоняй с
  `--configuration Release`; API при нужде — из `bin/Release` на :5079, клиент —
  конфигурация `client-5079` из `.claude/launch.json`.
- Публичный путь results читается через `SwimmReadDbContext` (swimm_ro).
- Кэш `/api/competitions` — 5 мин: при живой проверке перезапусти API после правок.
- У части старых/ручных соревнований `Country` может быть пустой строкой — при фильтре
  они отваливаются; это ожидаемо, НЕ добавляй для них спец-логику.

## Вне скоупа (не делать)

- Миграция `Competition.Country` → FK `CountryId` (бэкфилл — отдельная задача Fable).
- UI-фильтр/переключатель страны на клиенте.
- Чистка мусорной записи `IL` в справочнике Countries.
