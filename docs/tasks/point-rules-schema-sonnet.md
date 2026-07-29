# Задание Sonnet — Э0: схема правил очков (`PointRules*`)

## 1. Контекст

Готовим привязку правил начисления очков к конкретному соревнованию и вводим второй вид
правил — очки пловца (High Point Swimmer). Полный план: [`../points-rules-per-competition-plan.md`](../points-rules-per-competition-plan.md).

Это **этап Э0** — только схема и переименование. **Поведение не меняется ни на байт**: новые
колонки/таблицы пустые, никакой расчёт их пока не читает. Задача считается выполненной, если
`dotnet build` и `dotnet test` зелёные, а API отдаёт ровно то же, что до правки.

Сейчас правила клубных очков лежат в `ClubPointsRules` + `ClubPointsRuleEntries` (шкала
«место → очки»), выбираются только по дате соревнования и scope. Мы приводим их к общему
префиксу с будущими правилами пловцов.

## 2. Решения (зафиксированы, НЕ пересматривать)

**Именование.** Таблицы и доменные сущности переезжают на префикс `PointRules*`:

| Было | Стало |
|---|---|
| таблица `ClubPointsRules` | `PointRulesClubs` |
| таблица `ClubPointsRuleEntries` | `PointRulesClubsEntries` |
| класс `ClubPointsRule` | `PointRuleClubs` |
| класс `ClubPointsRuleEntry` | `PointRuleClubsEntry` |
| `ClubPointsScoring` | `PointRulesClubsScoring` |
| `SwimmDbContext.ClubPointsRules` | `PointRulesClubs` |
| `SwimmDbContext.ClubPointsRuleEntries` | `PointRulesClubsEntries` |

**Граница переименования.** API-слой НЕ трогаем — он назван по публичному ресурсу
`/api/club-points`, и это внешний контракт клиента. Остаются как есть:
`ClubPointsController`, `IClubPointsRepository`, `ClubPointsRepository`, `ClubPointsRuleDto`,
маршрут `api/club-points`, ключи кэша `club-points:rules` / `http:club-points`, JSON-поля ответа.
Внутри репозитория меняются только типы сущностей и имена DbSet.

**FK — `ON DELETE RESTRICT`**, не `SET NULL`: удаление используемого правила должно падать,
а не переводить соревнования на другую шкалу молча.

**Сид сохраняется как есть.** Правила Id 1 (`2025.01`, 24 места) и Id 2 (`2025.01-masters`,
12 мест) и все 36 строк шкалы остаются с теми же Id и значениями — меняются только имена
таблиц и типов в `HasData`. Новые таблицы правил пловцов сидом НЕ заполняются: шкалу пришлёт
Влад позже.

**Шкала — дочерняя таблица**, не jsonb (обсуждалось, решили в пользу unique-индекса
`(RuleId, Place)`).

## 3. Что уже готово (не переделывать)

- `server/Swimm.Infrastructure/Data/SwimmDbContext.cs` — конфигурация + `HasData` сида
  (строки ~255–336). `SwimmReadDbContext` наследует модель, **правок не требует**.
- `server/Swimm.Infrastructure/Services/ClubPointsScoring.cs` — чистый расчёт (`PointsFor`,
  `SelectRule`). Логику НЕ менять, только имя типа/файла.
- `server/Swimm.Tests/ClubPointsScoringTests.cs` — 9 тестов расчёта, образец стиля.
- `server/db/setup-roles.sql` — грант `SELECT` для `swimm_ro`, явный список таблиц
  (fail-closed: новая публичная таблица добавляется руками).
- Два `DbContext` → **все команды EF требуют `--context SwimmDbContext`**.

## 4. Шаги

### 4.1 Переименование доменных сущностей

`server/Swimm.Domain/Entities/`: `ClubPointsRule.cs` → `PointRuleClubs.cs`,
`ClubPointsRuleEntry.cs` → `PointRuleClubsEntry.cs`. Классы, XML-doc и навигационные
свойства — по таблице из §2. Состав полей не меняется.

### 4.2 Новые сущности — правила очков пловца

`server/Swimm.Domain/Entities/PointRuleSwimmers.cs`:

```csharp
public class PointRuleSwimmers
{
    public int Id { get; set; }
    [MaxLength(50)]  public string Version { get; set; } = string.Empty;   // unique
    public DateOnly EffectiveFrom { get; set; }
    [MaxLength(300)] public string? Description { get; set; }
    [MaxLength(20)]  public string Scope { get; set; } = string.Empty;      // all | masters | non-masters
    /// <summary>placement — по шкале мест (Entries); fina — сумма international points.</summary>
    [MaxLength(20)]  public string PointsSource { get; set; } = "placement";
    public int DefaultPoints { get; set; }
    public int? MaxScoringPlace { get; set; }
    /// <summary>Считать только N лучших заплывов; null — все.</summary>
    public int? CountBestSwims { get; set; }
    /// <summary>age | age-group | none — как формировать номинации.</summary>
    [MaxLength(20)]  public string GroupBy { get; set; } = "age";
    public bool SplitByGender { get; set; } = true;
    public bool IncludeRelays { get; set; }
    /// <summary>Минимум заплывов для попадания в зачёт; null — без ограничения.</summary>
    public int? MinSwims { get; set; }
    public ICollection<PointRuleSwimmersEntry> Entries { get; set; } = [];
}
```

`PointRuleSwimmersEntry.cs` — структурная копия `PointRuleClubsEntry`: `Id`, `RuleId`
(FK, cascade), `Place`, `Points`.

Атрибуты `[Key]`/`[DatabaseGenerated]`/`[ForeignKey]` — как в существующих сущностях.

### 4.3 `Competition` — два nullable FK

`server/Swimm.Domain/Entities/Competition.cs`:

```csharp
/// <summary>Правило клубных очков, привязанное вручную. null — подбор по дате и scope.</summary>
public int? PointRuleClubsId { get; set; }
[ForeignKey(nameof(PointRuleClubsId))] public PointRuleClubs? PointRuleClubs { get; set; }

/// <summary>Правило очков пловца (High Point). null — legacy-расчёт по FINA.</summary>
public int? PointRuleSwimmersId { get; set; }
[ForeignKey(nameof(PointRuleSwimmersId))] public PointRuleSwimmers? PointRuleSwimmers { get; set; }
```

### 4.4 `SwimmDbContext`

- DbSet'ы: переименовать два клубных, добавить `PointRulesSwimmers` / `PointRulesSwimmersEntries`.
- `ToTable(...)` — новые имена; `HasIndex(e => e.Version).IsUnique()` и
  `HasIndex(e => new { e.RuleId, e.Place }).IsUnique()` для обеих семей.
- `HasData` клубного сида — те же данные, новые типы.
- Для `Competition` — оба FK с `.OnDelete(DeleteBehavior.Restrict)`.

### 4.5 Сервис и репозитории

- `ClubPointsScoring.cs` → `PointRulesClubsScoring.cs` (**логику не менять**).
- `ClubPointsRepository.cs`, `ResultRepository.cs`, `HubGroupPublicRepository.cs` — обновить
  типы и имена DbSet. Тела запросов и маппинг в DTO не трогать.

### 4.6 Миграция

Одна миграция (`/db-migrate` или вручную):

```bash
dotnet ef migrations add PointRulesSchema \
  --project server/Swimm.Infrastructure --startup-project server/Swimm.API --context SwimmDbContext
```

⚠️ EF при переименовании сущности по умолчанию генерит `DROP TABLE` + `CREATE TABLE` —
**это уничтожит данные**. Проверь сгенерированный код и, если так вышло, замени на
`migrationBuilder.RenameTable(...)` (+ `RenameIndex` для затронутых индексов) вручную.
Данные и Id обязаны сохраниться.

В `Up()` добавить raw SQL с грантами для существующих БД:

```csharp
migrationBuilder.Sql(@"GRANT SELECT ON ""PointRulesClubs"", ""PointRulesClubsEntries"",
    ""PointRulesSwimmers"", ""PointRulesSwimmersEntries"" TO swimm_ro;");
```

Оберни в `DO $$ ... EXCEPTION WHEN undefined_object THEN NULL; END $$;` (или проверку
`pg_roles`), чтобы миграция не падала там, где роли `swimm_ro` нет.

### 4.7 `server/db/setup-roles.sql`

В списке `GRANT SELECT ON` заменить `"ClubPointsRules"`, `"ClubPointsRuleEntries"` на
`"PointRulesClubs"`, `"PointRulesClubsEntries"` и добавить `"PointRulesSwimmers"`,
`"PointRulesSwimmersEntries"`.

## 5. Тесты (обязательно)

- `ClubPointsScoringTests.cs` → `PointRulesClubsScoringTests.cs`: переименовать типы,
  **все 9 кейсов должны проходить без изменения ожиданий** — это и есть доказательство, что
  переименование чисто механическое.
- Добавить в этот же файл 2 кейса на новые сущности (по образцу существующих `Rule(...)`):
  правило пловцов со шкалой корректно отдаёт очки за место и `DefaultPoints` за место вне шкалы.
  Расчётного сервиса для пловцов ещё нет — проверяй через ту же `PointRulesClubsScoring`, если
  сигнатура подходит; если не подходит, ограничься тестом, что сущность с `Entries`
  конфигурируется и сохраняется (in-memory/SQLite-контекст, как в `ResultRepositoryTests`).
- Ничего из существующих тестов не «чинить» подгонкой ожиданий. Красный тест = регрессия,
  разбирайся с причиной.

## 6. Проверка

```bash
dotnet build server/Swimm.sln
dotnet test server/Swimm.sln
```

Миграция на живой БД (Docker может быть не поднят — подними):

```bash
docker compose -f server/docker-compose.yml up -d
dotnet ef database update --project server/Swimm.Infrastructure --startup-project server/Swimm.API --context SwimmDbContext
```

После применения убедись SQL-ом, что **данные на месте**:

```bash
docker exec swimm-postgres psql -U swimm -d swimm -c 'SELECT COUNT(*) FROM "PointRulesClubs";'        # ожидается 2
docker exec swimm-postgres psql -U swimm -d swimm -c 'SELECT COUNT(*) FROM "PointRulesClubsEntries";' # ожидается 36
```

И что API не изменился — `GET /api/club-points` отдаёт те же два правила со шкалами
(скилл `/smoke-test-api`).

## 7. Footguns

- **Build-lock:** если `dotnet build` падает с `MSB3027/MSB3021 … Swimm.API.dll is locked` —
  жив предыдущий `dotnet run` (часто из Visual Studio). Обход: `--configuration Release`
  и API на порту :5079. Либо `Get-CimInstance Win32_Process -Filter "Name='Swimm.API.exe'" | ForEach-Object { Stop-Process -Id $_.ProcessId -Force }`.
- **Два DbContext** — каждая команда EF с `--context SwimmDbContext`, иначе EF ругнётся на
  неоднозначность.
- **`swimm_ro` fail-closed:** забыл грант на новую публичную таблицу → read-путь падает
  в рантайме, а не тихо деградирует. Гранты нужны в ДВУХ местах: миграция (существующие БД)
  и `setup-roles.sql` (новые).
- **Кэш правил** — 10 мин в репозитории + 1 ч payload + `Cache-Control: 300`. Если после
  миграции API отдаёт старое, это кэш, а не баг схемы.

## 8. Вне скоупа (НЕ делать)

- Не менять маршрут `/api/club-points`, `ClubPointsRuleDto` и JSON-контракт ответа.
- Не трогать `ClubPointsController` / `IClubPointsRepository` / `ClubPointsRepository`
  дальше замены типов сущностей.
- Не реализовывать выбор правила по FK, расчёт очков пловцов, колонки `CombinedPlace` /
  `ClubPoints` в `Results` — это этапы Э1–Э2.5.
- Не трогать клиент (`client/`) вообще.
- Не заполнять `PointRulesSwimmers` данными — шкалу пришлёт Влад.
- Не коммитить и не пушить: разложу по коммитам сам при приёмке.
