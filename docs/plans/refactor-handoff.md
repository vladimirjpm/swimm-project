# ПЕРЕДАЧА — деление больших файлов (с 08.10.2026)

Точка входа для нового чата, если продолжаешь рефакторинг. Бэклог и правило — в
[`../ARCHITECTURE.md`](../ARCHITECTURE.md) §7 п.6; данные и импорт той же сессии —
[`data-health-handoff.md`](data-health-handoff.md). Зачем вообще: в огромных методах правки
делаются вслепую окнами по 30 строк, а копии логики расходятся молча — так консольный `--repull`
разошёлся с кнопкой админки и завёл тень пловца (И-46 в `../data-integrity.md`).

## 1. Где мы

- Ветка `entity-hero-roles`, **не закоммичено** (список своих файлов — `data-health-handoff.md` §2).
- Сделаны шаги 1–3 ниже (3 — 08.10 вечер, тесты после него не гонялись: модель сверена слепком); полный прогон на сборке индекса + своих файлов: **2839 зелёных, 3
  известных падения** (`DirectMemoryCache_…`, `TrainingResults_ReadOnly…` — сканеры вне дерева;
  `ClubMergeServiceTests.DryRun…` — нестабильный, не наш).

## 2. Что сделано

| Шаг | Что | Как проверено |
|---|---|---|
| 1 | **`Program.cs` 1808 → 578 строк.** 35 консольных команд (`if (args.Contains(…))`) — в `server/Swimm.API/Cli/*Cli.cs` по темам (10 файлов: Database, Recalc, Discovery, StartList, Loglig, Records, Seed, DataChecks, Merge, Multiday), реестр и порядок — `Cli/CliCommands.cs`; в `Program.cs` одна строка `if (await CliCommands.TryRunAsync(app, args)) return;`. Лишние `using` убраны пробными сборками | Скрипт сверил все 35: условие, порядок, комментарий, тело — байт в байт (с поправкой на отступ). Живьём: `--repull 228 --dry-run`, `--merge-swimmers` без `--apply`, `--check-data`; без флага API поднимается (`/healthz`, `/readyz` с БД) |
| 2 | **Поиск пловца по строке протокола** — из `JsonImportService` (2667 → 2296) в `Services/ImportSwimmerResolver.cs`: индексы (точный ключ, EN, полное имя, написание), цепочка фоллбеков, новая карточка, страж тёзок И-11, ноги эстафет, `GenderOf`. В импорте — три вызова (`ResolveAsync` → `EnrichSwimmerFromResult` → `FillEnglishName`), порядок прежний. Doc-комментарии, разъехавшиеся по соседним методам, поставлены на место | Скрипт: перенесённый код = исходник с точностью до переименований (`swimmerCache` → `_byKey`, `competition.Id` → `competitionId`), 0 отличий. Новые `ImportSwimmerResolverTests` (7 правил по одному на InMemory). Все 166 тестов импорта зелёные. Настоящий импорт на базе после выноса НЕ гонялся |

| 3 | **`SwimmDbContext.OnModelCreating` 1483 строки → одна** (`ApplyConfigurationsFromAssembly`); файл 1622 → 146. 70 классов `<Entity>Configuration : IEntityTypeConfiguration<T>` в `Infrastructure/Data/Configurations/*Configurations.cs`, 14 файлов по темам (Reference, StartList, Records, PointRule, Result, Import, User, Favorite, Media, HubGroup, Admin, Training, LevelAndLanePlan, Category). Перенос — скриптом (сдвиг на 4 влево, кроме продолжения многострочного `@"…"` в `CK_UserMediaPublications_TargetShape` — отступ там часть SQL). Два разъехавшихся комментария поставлены на место (`RecordIssue`, `LanePlan`) | Слепок модели `model.ToDebugString(LongDefault)` до и после — **побайтно одинаковый** (2455 строк, 72 типа); `IMigrationsModelDiffer` текущей модели против снимка миграций — 0 операций (тот же расчёт, что у `migrations add`, вместе с `HasData`). Скрипт слепка — file-based C# (`#:project …Infrastructure.csproj`, `#:property PublishAot=false`). Сборка решения зелёная |

## 3. Что дальше (порядок — польза на единицу риска)

| # | Что | Размер | Как | Проверка |
|---|---|---|---|---|
| 4 | **Эталонные тесты парсеров** (подготовка к п. 7) | — | каждая фикстура `Fixtures/Parsing/*.pdf` → сохранённый JSON разбора, тест сравнивает побайтно | сам по себе страхует любую правку парсера |
| 5 | `ResultRepository.LoadCompetitionOverviewAsync` | 599 строк | по карточке Overview на метод (Top clubs, Best swim, Most decorated, High Point…) — правила в `../competition-overview-cards.md` | тесты карточек по одной |
| 6 | `JsonImportService.ImportAsync` | ещё 1099 строк | тем же приёмом, что шаг 2: соревнование/день (Д2), клубы, upsert | 166 тестов импорта |
| 7 | `IsrOrgCompetitionParser.ParseLines` | 1348 строк, 19 `yield return current` | только после п. 4 | эталоны п. 4 |
| 8 | JS из `.cshtml` админки | `Competitions/Index` — 1780 строк JS (файл 2843), `Import` — 1073, `Index` — 996 | в `wwwroot/js/admin/*.js` | ⚠ Tailwind сканирует только `.cshtml`/`.cs` — добавить `@source` для `.js`, иначе классы молча выпадут из `admin.min.css` (`../pre-push-rules.md`); значения `@Model…` — через data-атрибуты; смотреть глазами |
| 9 | Клиент | `my-groups-panel.tsx` 1257 (~16 компонентов), `swimmer-panels.tsx` 1245 (~15) — разнести по файлам; `filter-data-source-ddl.tsx` 1255 (1 компонент, 27 хуков), `my-media.tsx` (45 хуков), `popup-content-normative.tsx` 907 — логику в хуки | — | браузер |
| — | мелочь | `--check-data` и `--data-checks` — одно и то же (`Cli/DataChecksCli.cs`) | одну оставить, вторую сделать псевдонимом | — |

## 4. Как делать безопасно (выработано на шагах 1–2)

- **Механический перенос — скриптом, а не руками**, и сразу скриптом же сверить перенесённое с
  исходником (нормализовать отступы и переименования, сравнить построчно). Копия исходника до
  правки — в scratchpad.
- Перед переносом проверить: нет ли многострочных строковых литералов (отступ — часть
  содержимого), какие общие переменные блок берёт снаружи, все ли ветки заканчиваются `return`.
- Порядок проверок/команд, если он значим, — сохранить явно и написать об этом в коде.
- Лишние `using` — пробными сборками `--no-dependencies` по одному (IDE0005 в этой сборке не
  включается).
- Сборка и тесты — на сборке ИНДЕКСА + своих файлах (`data-health-handoff.md` §5), не на рабочей
  копии: в ней чужие правки.
- Рабочая копия общая с Владом и параллельной сессией: делить файлы, которые прямо сейчас правит
  кто-то ещё, — гарантированный конфликт; смотреть `git status` перед стартом.
