# Задание (Sonnet 5): страна в нормативах — фильтр API + выбор системы на клиенте

Контекст: аудит поддержки стран (2026-07-13, Fable). У `NormativeStandard` поле `Country`
в схеме **есть** (сейчас весь сид — `"RUS"`, российская система разрядов), но нигде не
работает: публичный API фильтрует только по `kind`, страны нет ни в кэш-ключе, ни в
клиентском запросе. Как только рядом появится второй набор (израильский — это обещано
в комментарии к `NormativeStandard.Country`), деревья на клиенте перезапишут друг друга.

Твоя работа — сделать страну рабочей осью нормативов, **не меняя видимого поведения UI**
(клиент продолжает показывать RUS-систему, просто теперь явно её запрашивает).

## Решения (зафиксированы, не пересматривать)

- Коды стран — **alpha-3 World Aquatics** (`RUS`, `ISR`, …), как везде в данных
  (docs/ARCHITECTURE.md, решение 2026-07-13).
- Пустая строка `Country` в БД = «универсальный» набор: при фильтре по стране такие
  строки тоже отдаются (`Country == country || Country == ""`).
- «Какую систему показывать» на клиенте — константа, НЕ настройка пользователя (это
  отдельная фаза). Важно: `HOME_REGION = 'ISR'` (чьи рекорды) ≠ система нормативов —
  нужна **отдельная** константа `NORMATIVE_COUNTRY = 'RUS'`.

## Что уже готово (не переделывать)

- `NormativeStandard.Country` (Required, "" допустима) + уникальный индекс с Country в осях.
- Шов «домашний регион»: `client/src/utils/constants/home-region.ts` (HOME_REGION/LABEL) —
  клади `NORMATIVE_COUNTRY` рядом, с комментом почему это не HOME_REGION.
- `RecordsHelper` (client/src/utils/helpers/records-helper.ts) — единственный потребитель
  `/api/normative-standards` (метод `fetchStandards`).
- Админ-CRUD стандартов уже принимает country при создании/правке (Admin/Records, вкладка
  Normative standards).

## Шаги

### 1. Сервер — фильтр country

- `IRecordRepository.GetStandardsAsync` (`Swimm.Application/Abstractions`) и реализация в
  `Swimm.Infrastructure/Repositories/RecordRepository.cs`: параметр `string? country = null`.
  Нормализация: trim + upper. `null`/пусто → без фильтра (обратная совместимость);
  задан → `Where(s => s.Country == country || s.Country == "")`.
- Кэш-ключ репозитория: было `normative-standards:{kind|all}` → стало
  `normative-standards:{kind|all}:{country|all}`.
- `RecordsController` (`GET /api/normative-standards`): query-параметр `country`,
  прокинуть в репозиторий, HTTP-кэш ключ `http:normative-standards:{kind|all}:{country|all}`
  (через существующий `CachedJson`).
- `RecordsAdminController.GetStandards` (листинг в админке): добавить фильтр `country`
  в `NormativeStandardFilter`, по образцу соседних полей.

### 2. Клиент

- `home-region.ts`: `export const NORMATIVE_COUNTRY = 'RUS';` + коммент (система разрядов,
  которую показывает UI; не путать с HOME_REGION — регионом рекордов).
- `records-helper.ts` → `fetchStandards`: запрос
  `/api/normative-standards?kind=${kind}&country=${NORMATIVE_COUNTRY}`.
  Деревья/ключи НЕ трогать — раз запрашивается одна система, коллизий нет.

### 3. Тесты (обязательно, xUnit в Swimm.Tests)

По образцу существующих repo-тестов (InMemory `SwimmDbContext`, см. `HubGroupCountryTests.cs`
как свежий пример сетапа):
- фильтр по country отдаёт свою страну + записи с `Country == ""`;
- чужая страна не попадает;
- без country — всё (легаси-поведение);
- нормализация (` rus ` → RUS);
- kind+country вместе.

## Проверка

- `dotnet build server/Swimm.sln` (0 ошибок) + `dotnet test server/Swimm.Tests`.
- `npx tsc --noEmit` в client/.
- Вживую: `curl "http://localhost:5078/api/normative-standards?kind=regular&country=RUS"` =
  тот же набор, что без country (пока в БД только RUS); `country=ISR` → пусто (или только
  ""-универсальные). Попап нормативов на клиенте выглядит как раньше.

## Footguns

- Если :5078 занят отладчиком Visual Studio (build-lock MSB3027, kill может дать access
  denied) — собирай/гоняй с `--configuration Release`, API поднимай из `bin/Release` на
  :5079, клиент — конфигурацией `client-5079` из `.claude/launch.json`.
- Публичный путь нормативов читается через `SwimmReadDbContext` (swimm_ro) — не добавляй
  туда записи.
- Кэш публичных ответов 5 мин + repo-кэш: после правок сервера перезапусти API, прежде
  чем проверять curl-ом.
- В `Countries`-справочнике есть мусорная строка `IL` (id 9) рядом с `ISR` — к нормативам
  отношения не имеет, не трогай.

## Вне скоупа (не делать)

- UI-переключатель системы нормативов и настройка пользователя.
- Перевод ключей уровней (`III_youth`…`MSMK`) — они российские и остаются как есть.
- Сид израильских нормативов.
