# Задание (Sonnet): Loglig ID — миграция + LogligClient + LogligMatchService (шаги 1–3 плана)

Контекст: план `docs/loglig-id-plan.md` (привязка `Swimmer` к профилю на loglig.com,
карточка вида `https://loglig.com:2053/Players/Details/{id}?seasonId={sid}`). Это даёт
надёжный внешний ID (лечит дедуп по имени) и источник обогащения. Твоя часть — шаги 1–3:
поля в схеме, HTTP-клиент с парсером карточки, сервис сверки карточки с нашими
результатами. CSE, админ-UI, user-claim флоу и джобы — НЕ твоя часть.

Прочитай в плане раздел «Безопасность» — требования оттуда зафиксированы ниже.

## Решения (зафиксированы, не пересматривать)

- **Анти-SSRF**: `LogligClient` принимает только `int logligId`. URL собирается из
  константы `https://loglig.com:2053` внутри клиента. Никаких методов, принимающих URL.
- Парсер — **статический метод** `ParseCard(string html)`, регекс-подход в стиле
  `Swimm.Parsing/Discovery/IsrOrgDiscoveryProvider.cs` (`[GeneratedRegex]`, partial class).
  Никаких HTML-библиотек (HtmlAgilityPack и т.п. в проекте нет — не добавлять).
  Парсер только читает значения; сырой HTML никуда не встраивается и не сохраняется.
- Место: интерфейсы в `Swimm.Application/Abstractions`, реализации в
  `Swimm.Infrastructure/Services` (как соседние сервисы).
- Статусы привязки — строки `"Suggested"` / `"Verified"` / `"Rejected"`, `null` = нет
  привязки (enum не заводить, по образцу строковых полей `Swimmer.Origin`).
- Пол в DTO нормализуем: `נקבה` → `"F"`, `זכר` → `"M"`, иначе `null` (формат как в
  `Swimmer.Gender`).
- Маппинг ивритских стилей → имена `Style.Name` из нашей БД:
  `חופשי`→`freestyle`, `גב`→`backstroke`, `חזה`→`breaststroke`, `פרפר`→`butterfly`,
  `מעורב אישי`→`individual_medley`. Строки с `שליחים` или дистанцией вида `4X50` —
  эстафеты: помечаются `IsRelay = true`, в сверке НЕ участвуют.
- Время `MM:SS.ff` → миллисекунды: `(mm*60+ss)*1000 + ff*10`. Дополнительно поддержать
  `HH:MM:SS.ff` (длинные дистанции). Невалидная строка → `null`, не исключение.
- Сверка результата loglig ↔ наш `ResultRecord`: совпадение = та же дата (день),
  та же дистанция, тот же стиль и |Δвремени| ≤ 20 мс.
- Пороги решения (`LogligMatchService`): `AutoVerify` = год рождения совпал **и** ≥2
  совпавших заплывов; `Candidate` = год рождения совпал, но заплывов совпало 0–1, либо
  год не совпал при ≥2 совпавших заплывах; иначе `NoMatch`. Совпадение клуба — только
  бонус-сигнал в отчёте (bool), на решение не влияет.
- `LogligMatchService` — чистая логика без БД: принимает DTO карточки и уже загруженный
  список наших результатов; загрузку из БД делают будущие вызывающие (не ты).

## Что уже готово (не переделывать)

- **Тест-фикстура сохранена**: `server/Swimm.Tests/Fixtures/Loglig/loglig-player-304199.html`
  (снапшот живой карточки от 2026-07-19). Тесты в сеть НЕ ходят. `Fixtures\**\*` уже
  копируется в output (`Swimm.Tests.csproj`).
- Структура карточки (из фикстуры):
  - имя: `<h1>סבינה ברנצב</h1>` внутри `div.pld-hero-top`;
  - мета-чипы `span.pld-chip` с `span.pld-chip-label`: `שנת לידה` → `2017`,
    `מגדר` → `נקבה`, `אגודה` → `הפועל דולפין נתניה`;
  - таблица результатов `table#pld-pb-table` (`data-section="Swimming"`), строки из
    5 `<td>`: событие (`100 חופשי`), длина бассейна (`25`/`50`), время (`01:32.68`),
    дата (`16/06/2026`, формат dd/MM/yyyy), название соревнования. В названиях
    встречаются HTML-энтити (`&quot;`) — декодируй `WebUtility.HtmlDecode`.
- Особенность сайта: карточка отдаётся после self-redirect с кукой `DetailsPageVisited` —
  поэтому клиенту нужен `CookieContainer` + `AllowAutoRedirect`. Для тестов это
  неважно (фикстура уже скачана).
- Образец netto-парсера на регексах и его тестов: `IsrOrgDiscoveryProvider.cs` +
  `Swimm.Tests/IsrOrgDiscoveryProviderTests.cs` (хелпер `Fixture(...)` из
  `AppContext.BaseDirectory`).

## Шаги

### 1. Миграция — поля Loglig у Swimmer

В `Swimm.Domain/Entities/Swimmer.cs` добавить (RU-комментарии, как у соседей):
- `int? LogligId` — ID игрока на loglig.com; уникальный **filtered** индекс
  (`[Index(nameof(LogligId), IsUnique = true)]` — Npgsql сам делает его частичным для
  nullable? НЕТ: явно задать фильтр в `SwimmDbContext.OnModelCreating`:
  `HasIndex(...).IsUnique().HasFilter("\"LogligId\" IS NOT NULL")`);
- `string? LogligIdStatus` `[MaxLength(20)]` — `Suggested`/`Verified`/`Rejected`;
- `string? LogligIdSource` `[MaxLength(20)]` — `admin`/`user-claim`/`auto`;
- `int? LogligIdSuggestedByUserId` — FK на `AppUser` (`[ForeignKey]`-навигация не нужна,
  достаточно int-поля + `OnModelCreating`-FK НЕ заводить — просто колонка для аудита,
  по образцу «недоверенных» ссылок; если рядом в Swimmer уже есть паттерн FK — не важно,
  оставь просто колонку);
- `DateTime? LogligIdSuggestedAt`, `DateTime? LogligIdVerifiedAt` (UTC, timestamptz).

Скаффолдинг: `dotnet ef migrations add AddSwimmerLogligId --project server/Swimm.Infrastructure
--startup-project server/Swimm.API --context SwimmDbContext` (флаг контекста обязателен —
два DbContext). Grant для `swimm_ro` не нужен: таблица Swimmer уже публичная, новые
колонки читаются существующим грантом. Применить: `dotnet ef database update …` (та же
пара project/startup + `--context SwimmDbContext`); если локальная БД недоступна —
не изобретай, зафиксируй в «Отклонениях».

### 2. `LogligClient` + парсер карточки

- `Swimm.Application/Abstractions/ILogligClient.cs`:
  ```csharp
  public interface ILogligClient
  {
      /// <summary>Тянет карточку игрока loglig; null — карточка недоступна (404/500/невалидный HTML).</summary>
      Task<LogligPlayerCard?> GetPlayerCardAsync(int logligId, CancellationToken ct = default);
  }
  ```
- DTO рядом в `Swimm.Application` (папка `Models` или как принято у соседних DTO):
  ```csharp
  public record LogligPlayerCard(
      string FullName, int? BirthYear, string? Gender, string? ClubName,
      IReadOnlyList<LogligResultRow> Results);

  public record LogligResultRow(
      string EventRaw,        // «100 חופשי» как на сайте
      string? Distance,       // «100», «4X50»
      string? StyleName,      // freestyle/... по маппингу; null если стиль не распознан
      bool IsRelay,
      int PoolLength,         // 25/50
      string TimeRaw,         // «01:32.68»
      int? TimeMillisecond,
      DateTime Date,          // из dd/MM/yyyy, DateTimeKind.Utc
      string CompetitionName);
  ```
- `Swimm.Infrastructure/Services/LogligClient.cs` (`public partial class`):
  - статический `public static LogligPlayerCard? ParseCard(string html)` — вся логика
    парсинга, тестируется без сети;
  - `GetPlayerCardAsync(int logligId)`: URL строго
    `$"{BaseUrl}/Players/Details/{logligId}"` (константа `BaseUrl = "https://loglig.com:2053"`),
    `IHttpClientFactory` c именованным клиентом `"loglig"`; не-2xx или пустой парс → `null`
    (лог warning), исключения сети не пробрасывать наружу как есть — вернуть `null` с логом.
  - Регистрация в `Swimm.Infrastructure/DependencyInjection.cs`:
    ```csharp
    services.AddHttpClient("loglig")
        .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
        {
            UseCookies = true,
            CookieContainer = new CookieContainer(),
            AllowAutoRedirect = true,
        });
    services.AddScoped<ILogligClient, LogligClient>();
    ```
    В `Swimm.Infrastructure.csproj` добавить `Microsoft.Extensions.Http` версии `10.0.0`
    (как в `Swimm.Parsing.csproj`).

### 3. `LogligMatchService`

- `Swimm.Application/Abstractions/ILogligMatchService.cs` + входная модель нашего
  результата (не тащить EF-сущность):
  ```csharp
  public record LocalResultKey(DateTime Date, string Distance, string StyleName, int? TimeMillisecond);

  public enum LogligMatchDecision { AutoVerify, Candidate, NoMatch }

  public record LogligMatchReport(
      LogligMatchDecision Decision, bool BirthYearMatch, bool ClubNameMatch,
      int MatchedResultCount, IReadOnlyList<LogligResultRow> MatchedRows);

  public interface ILogligMatchService
  {
      LogligMatchReport Match(LogligPlayerCard card, int swimmerBirthYear,
          string? swimmerClubName, IReadOnlyList<LocalResultKey> localResults);
  }
  ```
- Реализация `Swimm.Infrastructure/Services/LogligMatchService.cs`: правила из раздела
  «Решения» (эстафеты и строки без `TimeMillisecond`/`StyleName` — пропуск; сравнение
  дат по `.Date`; дистанция — строковое сравнение после trim; клуб — точное сравнение
  после trim, это только сигнал). Зарегистрировать `AddScoped` в DI.

### 4. Тесты (обязательно, xUnit в Swimm.Tests)

`LogligClientParseTests.cs` — на фикстуре (образец — `IsrOrgDiscoveryProviderTests`):
- имя `סבינה ברנצב`, год 2017, пол `F`, клуб `הפועל דולפין נתניה`;
- строк результатов ≥ 15; конкретная: `100 חופשי`, бассейн 25, `01:32.68` → 92680 мс,
  дата 2026-06-16, соревнование `אליפות חדרה הפתוחה 2026`;
- эстафета `4X50 חופשי שליחים` → `IsRelay == true`;
- строка с `&quot;` в названии соревнования декодирована (`אליפות ישראל "ארנה"…`);
- `ParseCard("<html></html>")` → `null` (нет `<h1>`), не исключение.

`LogligMatchServiceTests.cs`:
- нормализация времени: `00:41.97` → 41970, `01:32.68` → 92680, `1:02:03.45` → 3723450,
  мусор → `null` (если парс времени — приватный в клиенте, тестируй через `ParseCard`
  или вынеси `internal static` хелпер + `InternalsVisibleTo` уже есть/добавь по образцу);
- маппинг стилей, включая `מעורב אישי` → `individual_medley`;
- `AutoVerify`: год совпал + 2 совпавших заплыва;
- `Candidate`: год совпал + 1 заплыв; год не совпал + 2 заплыва;
- `NoMatch`: ничего не совпало; эстафеты и строки без времени не считаются;
- допуск времени: Δ=20 мс — совпадение, Δ=21 мс — нет.

## Проверка

- `dotnet build server/Swimm.sln` — 0 ошибок;
- `dotnet test server/Swimm.Tests` — все зелёные;
- клиентский код не менялся — `tsc` не нужен.

## Footguns

- Visual Studio может держать Debug-bin (`MSB3027`) — собирай/гоняй с
  `--configuration Release`.
- Два DbContext — каждая EF-команда с `--context SwimmDbContext`.
- Иврит в фикстуре — RTL; сравнивай строки как есть (без reverse), файл в UTF-8.
- Тесты в сеть не ходят. Живой запрос к loglig в рамках задания не делать.

## Вне скоупа (не делать)

- Google CSE (шаг 4), админ-UI (шаг 5), user-claim эндпоинты и ночной джоб (шаг 6),
  батч (шаг 7), обогащение.
- Никаких контроллеров/эндпоинтов и записи привязок в БД — только схема, клиент, матчер.
- Не коммитить (результат ревьюит и коммитит вызывающая сессия).
