# Задание (Sonnet): Loglig ID — провайдер поиска кандидатов (шаг 4 плана, Serper вместо CSE)

Контекст: план `docs/loglig-id-plan.md`, шаг 4. Шаги 1–3 уже готовы (поля у Swimmer,
`LogligClient`, `LogligMatchService`). Исходно кандидатов должен был искать Google CSE,
но **Custom Search JSON API закрыт для новых клиентов с 01.2026** — вместо него
используем serper.dev (SERP-прокси, отдаёт настоящую Google-выдачу). Проверено вживую
2026-07-19: запрос `site:loglig.com "ברנצב סבינה"` возвращает карточку
`Players/Details/304199` первым результатом при обоих порядках имени.

Твоя работа: нейтральный интерфейс провайдера кандидатов + реализация через Serper +
тесты. Никаких UI, эндпоинтов и записи в БД.

## Решения (зафиксированы, не пересматривать)

- Интерфейс называется `ICandidateSearchProvider` (нейтрально — провайдер сменный,
  Serper может быть заменён на Brave и т.п. одним классом).
- Из выдачи берём ТОЛЬКО ссылки с паттерном `Players/Details/{id}` (regex
  `Players/Details/(\d+)`), остальные (StartList, RegisteredCompetitionAthletes,
  Activity/Form…) игнорируем. Возвращаем **уникальные** ID в порядке появления,
  максимум 5 (как в плане: «топ-5 URL с паттерном, не первый результат»).
- Запросы на одного пловца (в одном вызове провайдера, останавливаемся, как только
  набрали ≥1 кандидата после очередного запроса):
  1. `site:loglig.com "<фамилия> <имя>"` (иврит),
  2. `site:loglig.com "<имя> <фамилия>"`,
  3. фолбэк: `site:loglig.com "<фамилия>"`.
  Кандидаты запросов аккумулируются (union, порядок появления). Имена приходят
  параметрами `lastNameHe`, `firstNameHe` — провайдер их не нормализует и не
  транслитерирует; если оба пустые — сразу пустой результат.
- Конфиг — секция `CandidateSearch` (`appsettings.Development.json` уже содержит её:
  `Provider` = `"serper"`, `ApiKey`). **Graceful**: пустой/отсутствующий ApiKey →
  провайдер возвращает пустой список и пишет один warning в лог (не исключение,
  не спам на каждый вызов — доп. состояние «уже предупреждал» допустимо).
- Serper API: `POST https://google.serper.dev/search`, заголовки `X-API-KEY: <ключ>`,
  `Content-Type: application/json`, тело `{"q":"<запрос>","num":10}`. Ответ: массив
  `organic[]`, у элемента поле `link`. Не-2xx / сетевые ошибки / кривой JSON →
  пустой список + warning (как в `LogligClient.GetPlayerCardAsync`).
- Анти-SSRF-принцип тот же, что в шагах 1–3: из ссылок выдачи извлекаем только
  числовой ID; никакие URL из выдачи не фетчатся (фетч карточек делает `LogligClient`
  по int — не в этом таске).
- Парсинг ответа — `System.Text.Json` (`JsonDocument` или DTO-record), без новых
  NuGet-пакетов.

## Что уже готово (не переделывать)

- Секция конфига в `appsettings.Development.json` (не коммитится) — ключ там уже
  лежит у Влада. В `appsettings.json` секцию НЕ добавлять (отсутствие = отключено).
- **Фикстура реального ответа Serper**:
  `server/Swimm.Tests/Fixtures/Loglig/serper-search-branzev-sabina.json`
  (запрос `site:loglig.com "ברנצב סבינה"`, 10 organic-результатов; первый —
  `https://loglig.com:2053/Players/Details/304199?seasonId=1715`, остальные —
  StartList/RegisteredCompetitionAthletes и прочий мусор без playerId).
  `Fixtures\**\*` уже копируется в output.
- `AddHttpClient`-паттерн и graceful-обработка ошибок: `Swimm.Infrastructure/Services/LogligClient.cs`.
- Регистрация сервисов: `Swimm.Infrastructure/DependencyInjection.cs` (блок «Loglig ID»).

## Шаги

### 1. Интерфейс и DTO

`Swimm.Application/Abstractions/ICandidateSearchProvider.cs`:
```csharp
/// <summary>Поиск кандидатов Loglig ID по имени пловца (сменный провайдер: serper и т.п.).</summary>
public interface ICandidateSearchProvider
{
    /// <summary>true — провайдер сконфигурирован (есть ключ); false — поиск отключён.</summary>
    bool IsConfigured { get; }

    /// <summary>До 5 уникальных loglig ID (Players/Details/{id}) из поисковой выдачи. Пустой список — ничего не нашли/поиск отключён/ошибка.</summary>
    Task<IReadOnlyList<int>> FindCandidatesAsync(string lastNameHe, string firstNameHe, CancellationToken ct = default);
}
```

### 2. Реализация Serper

`Swimm.Infrastructure/Services/SerperCandidateSearchProvider.cs` (`public partial class`):
- конфиг читается из `IConfiguration` секции `CandidateSearch` (ApiKey; Provider пока
  не ветвится — реализация одна, но имя клиента/класса не «Google» и не «Cse»);
- статический разбор, тестируемый без сети:
  `internal static IReadOnlyList<int> ExtractPlayerIds(string serperJson, int max = 5)` —
  парсит JSON выдачи, вынимает уникальные ID по regex `Players/Details/(\d+)`;
- составление списка запросов для пары имён —
  `internal static IReadOnlyList<string> BuildQueries(string lastNameHe, string firstNameHe)`
  (пустые/пробельные части пропускаются: только фамилия → один запрос);
- именованный HttpClient `"serper"` (обычный, без кук) в DependencyInjection +
  `services.AddScoped<ICandidateSearchProvider, SerperCandidateSearchProvider>()` —
  в тот же блок «Loglig ID»;
- лог: debug на каждый запрос (текст запроса + сколько кандидатов), warning на ошибки.

### 3. Тесты (обязательно, xUnit в Swimm.Tests)

`SerperCandidateSearchProviderTests.cs` (образец работы с фикстурой — `LogligClientParseTests.cs`):
- `ExtractPlayerIds` на фикстуре: ровно `[304199]` (остальные ссылки выдачи — не карточки);
- синтетический JSON с 7 ссылками `Players/Details/…` (в т.ч. дубли и loglig.com без
  порта): уникальность, порядок появления, обрезка до 5;
- JSON без `organic` / пустой / битый → пустой список, без исключений;
- `BuildQueries`: оба имени → 3 запроса в правильном порядке; только фамилия → 1;
  оба пустые → 0;
- `IsConfigured == false` (пустой конфиг) → `FindCandidatesAsync` возвращает пустой
  список, HTTP не вызывается (проверка через фейковый `HttpMessageHandler`, который
  падает при любом вызове, — по образцу существующих тестов с HttpClient, если есть;
  иначе простой handler-заглушка в тесте).

## Проверка

- `dotnet build server/Swimm.sln` — 0 ошибок;
- `dotnet test server/Swimm.Tests` — все зелёные;
- клиент не менялся — tsc не нужен.

## Footguns

- Visual Studio может держать Debug-bin (MSB3027) — собирай/гоняй с `--configuration Release`.
- Иврит: файлы в UTF-8; в тестах строки-литералы на иврите — нормально (см. LogligClientParseTests).
- В рабочем дереве есть чужие незакоммиченные правки (client/src/projects/my-media-project,
  HubGroupsController, MediaController) — не трогать.
- Реальный ключ лежит в appsettings.Development.json — НЕ печатать его в логи/отчёт.

## Вне скоупа (не делать)

- Живые запросы к serper.dev (квота платная; проверка уже сделана, фикстура снята).
- Оркестрация «поиск → фетч карточек → сверка → сохранение» (это шаг 5, кнопка в админке).
- UI, контроллеры, эндпоинты, миграции, запись в БД.
- Batch-джоб (шаг 7) и краудсорс (шаг 6).
- Не коммитить.
