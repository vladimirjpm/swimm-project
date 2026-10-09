# ПЕРЕДАЧА — деление больших файлов (с 08.10.2026)

Точка входа для нового чата, если продолжаешь рефакторинг. Бэклог и правило — в
[`../ARCHITECTURE.md`](../ARCHITECTURE.md) §7 п.6; данные и импорт той же сессии —
[`data-health-handoff.md`](data-health-handoff.md). Зачем вообще: в огромных методах правки
делаются вслепую окнами по 30 строк, а копии логики расходятся молча — так консольный `--repull`
разошёлся с кнопкой админки и завёл тень пловца (И-46 в `../data-integrity.md`).

## 1. Где мы

- Ветка `entity-hero-roles`, **не запушено**. Сделаны шаги 1–7: `1a2514da` (1–3, вместе с данными
  И-46…И-49), `e7ccf55c` (4, 7), `e37d1960` (6), шаг 5 — 09.10 вечером. Осталось: 8 и 9 (§3). Мелочь
  закрыта: `--data-checks` — псевдоним `--check-data` (вторая копия с другим выводом удалена).
- Полный прогон на сборке индекса (09.10, перед коммитом шагов 4 и 7): **2887 зелёных, 3 известных
  падения** (`DirectMemoryCache_…`, `TrainingResults_ReadOnly…` — сканеры вне дерева;
  `ClubMergeServiceTests.DryRun…` — нестабильный, не наш).
- Скрипты, которые пригодились больше одного раза, — [`server/tools/refactor/`](../../server/tools/refactor/README.md).

**С чего начать новый чат:** прочитать §3 (что дальше) и §4 (как безопасно), `git status` — в
рабочей копии всегда есть чужие правки (Влад: тренировки, `deep/*`, `CLAUDE.md`, ROADMAP;
`SwimmerDedupService` с тестом; абзац «Починка (02.10)» в `data-integrity.md`), их не брать.
Следующий по плану — шаг 8 (JS из `.cshtml` админки) или тесты карточек Overview по одной (их
по-прежнему мало; эталон ответа — `server/tools/refactor/overview_dump.cs`).

## 2. Что сделано

| Шаг | Что | Как проверено |
|---|---|---|
| 1 | **`Program.cs` 1808 → 578 строк.** 35 консольных команд (`if (args.Contains(…))`) — в `server/Swimm.API/Cli/*Cli.cs` по темам (10 файлов: Database, Recalc, Discovery, StartList, Loglig, Records, Seed, DataChecks, Merge, Multiday), реестр и порядок — `Cli/CliCommands.cs`; в `Program.cs` одна строка `if (await CliCommands.TryRunAsync(app, args)) return;`. Лишние `using` убраны пробными сборками | Скрипт сверил все 35: условие, порядок, комментарий, тело — байт в байт (с поправкой на отступ). Живьём: `--repull 228 --dry-run`, `--merge-swimmers` без `--apply`, `--check-data`; без флага API поднимается (`/healthz`, `/readyz` с БД) |
| 2 | **Поиск пловца по строке протокола** — из `JsonImportService` (2667 → 2296) в `Services/ImportSwimmerResolver.cs`: индексы (точный ключ, EN, полное имя, написание), цепочка фоллбеков, новая карточка, страж тёзок И-11, ноги эстафет, `GenderOf`. В импорте — три вызова (`ResolveAsync` → `EnrichSwimmerFromResult` → `FillEnglishName`), порядок прежний. Doc-комментарии, разъехавшиеся по соседним методам, поставлены на место | Скрипт: перенесённый код = исходник с точностью до переименований (`swimmerCache` → `_byKey`, `competition.Id` → `competitionId`), 0 отличий. Новые `ImportSwimmerResolverTests` (7 правил по одному на InMemory). Все 166 тестов импорта зелёные. Настоящий импорт на базе после выноса НЕ гонялся |
| 3 | **`SwimmDbContext.OnModelCreating` 1483 строки → одна** (`ApplyConfigurationsFromAssembly`); файл 1622 → 146. 70 классов `<Entity>Configuration : IEntityTypeConfiguration<T>` в `Infrastructure/Data/Configurations/*Configurations.cs`, 14 файлов по темам (Reference, StartList, Records, PointRule, Result, Import, User, Favorite, Media, HubGroup, Admin, Training, LevelAndLanePlan, Category). Перенос — скриптом (сдвиг на 4 влево, кроме продолжения многострочного `@"…"` в `CK_UserMediaPublications_TargetShape` — отступ там часть SQL). Два разъехавшихся комментария поставлены на место (`RecordIssue`, `LanePlan`) | Слепок модели `model.ToDebugString(LongDefault)` до и после — **побайтно одинаковый** (2455 строк, 72 типа); `IMigrationsModelDiffer` текущей модели против снимка миграций — 0 операций (тот же расчёт, что у `migrations add`, вместе с `HasData`). Скрипт слепка — file-based C# (`#:project …Infrastructure.csproj`, `#:property PublishAot=false`). Сборка решения зелёная |
| 4 | **Эталонные тесты парсеров** — `Swimm.Tests/ParserGoldenTests.cs`: 11 протоколов (`IsrOrgCompetitionParser.ParseCompetitions`, язык по имени файла) и 12 PDF промежуточных (`LogligRelaySplitParser`/`LogligIndividualSplitParser`) → `Fixtures/Parsing/golden/*.json` (23 файла, ~3,5 МБ, иврит читаемо). Расхождение — первая отличающаяся строка + весь разбор в `bin/…/*.actual.json`. Пересоздать после НАМЕРЕННОЙ правки парсера: `SWIMM_UPDATE_GOLDEN=1 dotnet test --filter ParserGoldenTests`, дифф — в git. Сторож `EveryParsingFixture_HasGolden`: новая PDF без эталона — красный (справочники рекордов — `NotCovered`). Попутно журнал разбора `_debugLog` стал `[ThreadStatic]` — общий `List` на параллельные разборы был гонкой | Два прогона подряд — зелёные (разбор детерминирован); порча одного эталона («Heat» 1 → 99) — красный с нужной строкой; 621 тест парсеров зелёный |
| 5 | **`ResultRepository.LoadCompetitionOverviewAsync` 599 строк → partial-файл `ResultRepository.Overview.cs`, метод на карточку** (`ResultRepository.cs` 1787 → 1006). Оркестратор ~80 строк: дни (`LoadOverviewDaysAsync`, `LoadOrgCompIdAsync`, `BuildStartListSourcesAsync`), счётчики, Best swim (`BestSwimProjection` — бывшая локальная функция), Most decorated (`LoadMedalRowsAsync` = `LoadPersonalMedalsAsync` + `LoadRelayMedalsAsync` + схлопывание раундов, `BuildMedalists`), High Point (`LoadHighPointAwardsAsync` → `LoadHighPointRowsAsync`, выбор правила, `BuildLegacyHighPointAwards` ИЛИ `BuildRuleHighPointAwardsAsync` с `LoadAgeRecordIndexAsync`/`RecordStatusOf`), Top clubs (`LoadAppliedClubRulesAsync`, `LoadClubPointsVerifiedAsync`, `IsClubPointsUnofficialAsync`, `LoadClubPointsMismatchNoteAsync`), новые рекорды (`DetectNewRecordsAsync`). Помощники только обзора переехали туда же (`GetIsraelRecordsAsync`, `CloneWithGender`, `SameNote`, `MedalRow`, `ShortDay`/`IsoDay`/`ParseDayDate`). Анонимный тип строк High Point → `HighPointSourceRow` (init-свойства, типы сверены с сущностями). Порядок запросов к базе прежний. Единственное поведенческое отличие: legacy-HP больше не считается впустую, когда правило есть (чистый расчёт в памяти). Попутно на место встал doc-комментарий `ParseCompetitionDate`, оказавшийся над `SameNote` | (1) **ответ** — `server/tools/refactor/overview_dump.cs` (репозиторий напрямую, кэш выключен) на 26 вариантах: 18 соревнований (правило HP с бонусом за рекорд — 57, «только финалы» — 1555, мастерс 63/1482, пара-очки 64, «mismatch»/«official»/«accepted» 1501/1497/57, Unofficial 1515, очки выключены 1540, ненаградное 61, помеченные строки 1512/1694, эстафеты 1482/1565, общий финал 1696, самое большое 1665) + `combined` у 1482/1565, события 1/7/10 с `combined` и без, `last`, несуществующее. Эталон детерминирован (два прогона «до» равны), «до» (снимок HEAD) и «после» (снимок + два файла) — **побайтно одинаково**; (2) **текст** — остаток `ResultRepository.cs` = исходник минус перенесённые диапазоны (равенство), каждая строка перенесённого найдена в новом файле, кроме объявлений и комментариев, ставших doc-комментариями; (3) полный прогон тестов на снимке — 2889 зелёных, 1 известное падение (`TrainingResults_ReadOnly…`, сканер вне дерева). Псевдоним `--data-checks` проверен живьём (прогон #264, вывод `--check-data`) |
| 6 | **`JsonImportService.ImportAsync` 1100 строк → вложенный класс `ImportRun`** (тем же приёмом, что шаг 7): состояние импорта — поля с именами бывших локальных, `RunAsync` → `ReadItemsAsync`, `LoadReferenceCachesAsync`, стратегия → `RunAttemptAsync` (транзакция: `AttachToEventAsync`, цикл `ImportRowAsync`, `RollBackOnRowErrorsAsync`, `UpsertAsync`, `SaveBatchAsync`, `RemoveOrphanSwimmersAsync`, `UpdateEventDatesAsync`, `MarkSplitsAsync`, `StampOrgCompIdAsync`, `WriteImportHistoryAsync`, коммит, восемь шагов-приборов после коммита, `FinishAsync`). Строка файла — второй уровень: `ResolveStyleAsync` → `ResolveCountryAsync` → `ResolveCompetitionAsync` (отказ «дубль») → `CountExpectedRow` → `ResolveClubAsync` → `ResolveSwimmerAsync` → `BuildRelayAsync` → `BuildGallery` → `AddRecord`; значения строки — поля, каждый шаг присваивает своё до чтения. ⚠ Execution strategy ПОВТОРЯЕТ лямбду транзакции: что было объявлено внутри неё (`targetEvent`, счётчики upsert, итоги сверки и проверок), переприсваивается на прежнем месте, объявленное до неё — общее для попыток, как и было. `style`/`competition`/`club` — `[NotNull] T?`: внутри своего шага бывают null (промах кэша, `club ??= …`), дальше читаются как ненулевые — новых предупреждений 0. Самый большой метод — `ResolveCompetitionAsync` (~125, половина — комментарии) | (1) **текст** — построчная сверка старого метода и нового класса: отличия только объявления→присваивания, два вызова сервиса через `_svc.`, каркас стратегии/транзакции, сигнатуры и вызовы; (2) 921 тест импорта/склейки/соревнований; (3) **сквозная сверка на копиях базы**: два `pg_restore` одного дампа, в A — сборка «до» (снимок HEAD), в B — «после», одна последовательность `--repull 228` (upsert 3362), `--repull 23` (upsert 160), переименование #1608 + снятие штампа, снова `--repull 23` (новое соревнование: категории, история, штамп, источник стартового протокола) → `pg_dump --data-only` обеих, маскировка меток времени (49 231) → **побайтно одинаково**, после обоих уровней деления |
| 7 | **`IsrOrgCompetitionParser.ParseLines` 1350 строк → вложенный класс `LineParser`.** Бывшие локальные переменные — его поля (с теми же именами, чтобы тела переехали без правок), цикл по страницам — `Run`, строка — `ParseLine`: цепочка из 15 методов `Try…` (`true` — строка разобрана, бывший `continue`; порядок цепочки = порядок проверок, менять нельзя). Заголовки HE (265 строк) разделены ещё на 8 подметодов. `yield return` → `Emit` в список (все вызывающие и так материализуют). Самые большие методы теперь: `TryEnglishHeaders` (~165, подблоки делят `heNormLine`/`mAgeEn` — механически не режется), `TryResultLine` (~135), `TryEnglishRelayTeam` (~140), `TryHebrewRelayTeam` (~125). Подблоки — `server/tools/refactor/split_try_chain.py`; первый уровень делался одноразовым скриптом (приём — в README там же) | Три независимые проверки: (1) **текст** — каждое тело метода = исходный блок с точностью до отступа и двух механических замен (`continue` основного цикла → `return true`, найдены компилятором как CS0139 — 29 штук, все `continue`; `yield return current` → `Emit(current)`), 0 отличий на 23 методах; (2) **эталоны** `ParserGoldenTests` — зелёные без пересоздания; (3) **журнал разбора** `GetDebugLog()` по всем 11 протоколам до и после — побайтно одинаковый (ловит и сдвиг порядка проверок). 784 теста парсеров и импорта зелёные |

## 3. Что дальше (порядок — польза на единицу риска)

| # | Что | Размер | Как | Проверка |
|---|---|---|---|---|
| 8 | JS из `.cshtml` админки | `Competitions/Index` — 1780 строк JS (файл 2843), `Import` — 1073, `Index` — 996 | в `wwwroot/js/admin/*.js` | ⚠ Tailwind сканирует только `.cshtml`/`.cs` — добавить `@source` для `.js`, иначе классы молча выпадут из `admin.min.css` (`../pre-push-rules.md`); значения `@Model…` — через data-атрибуты; смотреть глазами |
| 9 | Клиент | `my-groups-panel.tsx` 1257 (~16 компонентов), `swimmer-panels.tsx` 1245 (~15) — разнести по файлам; `filter-data-source-ddl.tsx` 1255 (1 компонент, 27 хуков), `my-media.tsx` (45 хуков), `popup-content-normative.tsx` 907 — логику в хуки | — | браузер |

## 4. Как делать безопасно (выработано на шагах 1–7)

- **Механический перенос — скриптом, а не руками**, и сразу скриптом же сверить перенесённое с
  исходником (нормализовать отступы и переименования, сравнить построчно). Копия исходника до
  правки — в scratchpad.
- Перед переносом проверить: нет ли многострочных строковых литералов (отступ — часть
  содержимого), какие общие переменные блок берёт снаружи, все ли ветки заканчиваются `return`.
- Порядок проверок/команд, если он значим, — сохранить явно и написать об этом в коде.
- Лишние `using` — пробными сборками `--no-dependencies` по одному (IDE0005 в этой сборке не
  включается).
- **Сквозная сверка поведения на копиях базы** (шаг 6) — когда переносится код, который ПИШЕТ:
  `pg_dump -Fc` → два `createdb` + `pg_restore` (`swimm_e2e_a/b`); сборка «до» из `git archive HEAD
  server`, «после» — тот же снимок + новый файл (не рабочая копия: в ней чужие правки); CLI с
  `ConnectionStrings__{Default,Migration,Admin,Read}Connection` на копию; одинаковая
  последовательность команд; `pg_dump --data-only` обеих, метки времени и токен `\restrict`
  замаскировать — дифф должен быть пустым. Потом `dropdb` обе. Путь «новое соревнование» без
  удаления: переименовать соревнование и снять `OrgCompId` — импорт его не узнает.
- **Код, который только ЧИТАЕТ** (шаг 5), сверять дешевле: file-based скрипт зовёт репозиторий
  напрямую на локальной базе с кэшем-заглушкой и пишет ответы в JSON (`overview_dump.cs`); «до» —
  из снимка HEAD, «после» — снимок + свои файлы, `diff -r`. Сначала два прогона «до» — эталон
  должен быть детерминирован. Варианты подбирать SQL-запросом по признакам (правила, флаги,
  эстафеты, помеченные строки), а не наугад.
- **Python в Git Bash** с 09.10 резолвится в заглушку Microsoft Store — звать полным путём
  `C:/Users/Vlad/AppData/Local/Python/pythoncore-3.14-64/python.exe`.
- Сборка и тесты — на сборке ИНДЕКСА + своих файлах (`data-health-handoff.md` §5), не на рабочей
  копии: в ней чужие правки.
- Рабочая копия общая с Владом и параллельной сессией: делить файлы, которые прямо сейчас правит
  кто-то ещё, — гарантированный конфликт; смотреть `git status` перед стартом.
