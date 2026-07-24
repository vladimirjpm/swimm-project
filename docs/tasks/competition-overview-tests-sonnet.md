# Задание: юнит-тесты на ResultRepository.GetCompetitionOverviewAsync

## Контекст

Добавлен эндпоинт `GET /api/competition-overview?competitionId=|eventId=` — дэшборд
соревнования для нового таба Overview (дизайн: `docs/design-handoff-competition-header.md`).
Реализация: `server/Swimm.Infrastructure/Repositories/ResultRepository.cs`, метод
`GetCompetitionOverviewAsync(ResultFilter filter)`; DTO —
`server/Swimm.Application/Dtos/CompetitionOverviewDto.cs`; интерфейс —
`IResultRepository.cs`. Код написан и собирается; нужно покрыть его юнит-тестами.

## Решения (зафиксированы, не пересматривать)

Семантика метода, которую проверяют тесты:

- **Summary:** `result_count` = все строки под фильтром; `day_count` = число дней
  (distinct Competition); `swimmer_count` = distinct SwimmerId ТОЛЬКО личных заплывов
  (`RelayId == null`); `club_count` = distinct ClubId без псевдоклубов (`Club.IsPseudo`).
- **Days:** по одному элементу на Competition-день, сортировка по `DayNumber`
  (null — в конец), затем по дате `dd/MM/yyyy`; у каждого дня свой `result_count`.
- **BestSwim:** максимум `InternationalPoints` среди `!TimeFail && InternationalPoints > 0`;
  тай-брейк: меньшее `TimeMillisecond`, затем меньший `Id`. Если очков нет ни у кого — `null`.
- **TopMedalist:** только личные заплывы (`RelayId == null`), `!TimeFail`,
  `Position` 1–3. Сортировка: всего медалей ↓, золото ↓, серебро ↓, SwimmerId ↑.
  Нет медалей — `null`.
- **TopClubs / TopClubsMen / TopClubsWomen:** переиспользуют `GetClubSummaryAsync`
  (клубный зачёт уже покрыт тестами — его правила заново НЕ тестировать);
  общий топ = Take(10), по полу = Take(3) с фильтром `Gender = "M"` / `"F"`.
- **Records:** в v1 всегда пустой список — достаточно assert Empty.
- Пустая БД / несуществующий competitionId → DTO с нулевым Summary, пустыми
  списками, null BestSwim/TopMedalist (не исключение).

## Что уже готово (не переделывать)

- Образец тестов и вся инфраструктура: `server/Swimm.Tests/ResultRepositoryTests.cs` —
  хелперы `BuildOptions`/`CreateDb` (EF InMemory на `SwimmReadDbContext`),
  `NullCacheService`/`NoCache()` (кэш always-miss), и club-summary тесты
  (`SeedClubSummaryFixtureAsync`, `GetClubSummary_*`, строки ~396-515) — бери их
  как шаблон сидинга (Style, Club, Competition, Swimmer, ClubPointsRule, ResultRecord).
- Сущности: `ResultRecord` (`server/Swimm.Domain/Entities/ResultRecord.cs`),
  `Competition` (поля `Date` строка dd/MM/yyyy, `DayNumber`, `SubName`, `EventId`).

## Шаги

1. В `server/Swimm.Tests/ResultRepositoryTests.cs` (тот же файл, ниже club-summary
   тестов) добавь регион тестов overview. Свой seed-хелпер
   `SeedOverviewFixtureAsync(SwimmReadDbContext db)` — многодневное событие из 2 дней
   (2 Competition с EventId=1, DayNumber 1/2) + данные, покрывающие кейсы ниже.
2. Тесты (xUnit, `[Fact]`, паттерн `Overview_<Что>`):
   - `Overview_EmptyDb_ReturnsEmptyDto` — пустая БД, фильтр по несуществующему id.
   - `Overview_Summary_CountsSwimmersClubsDaysResults` — эстафетная строка не
     увеличивает swimmer_count; псевдоклуб не увеличивает club_count; day_count=2.
   - `Overview_Days_OrderedWithPerDayCounts`.
   - `Overview_BestSwim_MaxPointsWithTieBreaks` — два результата с равными очками,
     разное время → побеждает быстрое; TimeFail с большими очками игнорируется.
   - `Overview_BestSwim_NullWhenNoPoints` — все InternationalPoints = 0.
   - `Overview_TopMedalist_CountsAndOrdering` — медаль эстафеты не считается;
     TimeFail на 1-м месте не считается; при равенстве всего медалей побеждает
     большее золото.
   - `Overview_TopClubs_SplitByGender` — мужской зачёт видит только Gender="M"
     строки, женский — "F" (по одному клубу-победителю на пол достаточно).
3. Ничего в продакшен-коде не менять. Если тест вскрыл баг реализации — НЕ чини
   молча: опиши в отчёте (раздел «Отклонения») с ожидаемым/фактическим.

## Проверка

```bash
dotnet test server/Swimm.sln --nologo --configuration Release
```

Все тесты зелёные (было 615 до задачи; общее число должно вырасти на твои тесты).

## Footguns

- **Build-lock:** Visual Studio у Влада может держать Debug-сборку → работай в
  `--configuration Release` (и build, и test). Если всё же MSB3027 на Swimm.API.dll —
  запущен API-процесс; см. корневой CLAUDE.md.
- InMemory-провайдер не знает SQL — только LINQ-переводимые конструкции (реализация
  уже такая).
- `Competition.Date` — СТРОКА `dd/MM/yyyy`; `ResultRecord.CompetitionDate` — DateTime;
  сеять согласованно (как в club-summary фикстуре).
- Кэш: используй `NoCache()`, иначе второй тест получит данные первого.

## Вне скоупа (не делать)

- Тесты контроллера/HTTP-слоя, изменения DTO/реализации, клиент, миграции.
- Тесты правил очков клубов (покрыты `ClubPointsScoringTests` и club-summary тестами).
