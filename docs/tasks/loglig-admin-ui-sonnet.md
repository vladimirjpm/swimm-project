# Задание (Sonnet): Loglig ID — админ-страница привязки (шаг 5 плана)

Контекст: план `docs/loglig-id-plan.md`, шаг 5. Готовы шаги 1–3 (поля Swimmer,
`LogligClient`, `LogligMatchService`) и шаг 4 (`ICandidateSearchProvider` /
`SerperCandidateSearchProvider` — поиск кандидатов по имени в Google-выдаче).
Твоя работа — MVP «на кнопке»: страница в админке, где админ ищет пловцу его
loglig-профиль (автоматически через поиск + верификацию) или привязывает вручную
ссылкой, плюс оркестрирующий сервис и его тесты. Краудсорс и ночной джоб (шаг 6) —
НЕ твоя часть.

## Решения (зафиксированы, не пересматривать)

- **Анти-SSRF** (раздел «Безопасность» плана): сервер принимает только `int logligId`.
  Из вставленной админом ссылки числовой ID извлекает **клиентский JS** (regex
  `Players/Details/(\d+)`; если вставили просто число — тоже ок), на сервер уходит int.
  Никаких строк-URL в API.
- Статусы: успешная привязка здесь — сразу `Verified` (`LogligIdStatus="Verified"`,
  `LogligIdVerifiedAt=DateTime.UtcNow`); `LogligIdSource` = `"auto"` (нашёл пайплайн)
  или `"admin"` (ручная). `LogligIdSuggestedByUserId/At` здесь НЕ трогаем (это для
  краудсорса, шаг 6).
- Автопривязка: среди кандидатов ровно один с решением `AutoVerify` → сохраняем.
  Ноль или больше одного `AutoVerify`, либо только `Candidate` → ничего не сохраняем,
  возвращаем список кандидатов с деталями — админ выбирает и жмёт «Привязать» у строки
  (та же ручная привязка `SetManual`, но UI подставляет ID сам).
- `Verified`-привязка залочена: `Find`/`SetManual` по пловцу с `Verified` возвращают
  ошибку «уже привязан» — сначала «Отвязать» (Unlink). Unlink чистит ВСЕ шесть
  Loglig-полей (в т.ч. Suggested*). Это и есть «снятие/смена только здесь» из плана.
- Уникальность: `LogligId` уникален по всем Swimmer. Перед сохранением проверяем
  занятость (`AnyAsync`), при гонке ловим `DbUpdateException` — в обоих случаях
  дружелюбная ошибка «loglig ID {id} уже привязан к пловцу {имя} #{id}».
- Ручная привязка тоже тянет карточку (`LogligClient`): карточка недоступна → ошибка
  (привязывать несуществующий профиль нельзя); сверку с результатами при ручной
  привязке НЕ требуем (админ сам решил), но в ответе возвращаем отчёт сверки
  (`LogligMatchService`) как информацию.
- Сервис — `ILogligLinkService` (Swimm.Application/Abstractions), реализация в
  Swimm.Infrastructure/Services с `SwimmDbContext` напрямую (по образцу
  `SwimmerDedupService(SwimmDbContext db)`).
- Наши результаты для сверки: `ResultRecord` пловца, join `Style`, эстафеты не брать
  (у эстафетных строк `RelayId != null`) → `LocalResultKey(CompetitionDate, Distance,
  Style.Name, TimeMillisecond)`.
- Кэш публичных выдач НЕ инвалидируем (LogligId нигде публично не отдаётся).

## Что уже готово (не переделывать)

- `ICandidateSearchProvider.FindCandidatesAsync(lastNameHe, firstNameHe)` → до 5 ID;
  `IsConfigured == false` → пустой список (тогда в UI авто-поиск недоступен, ручная
  привязка работает — сервис различает «поиск отключён» и «не нашли»).
- `ILogligClient.GetPlayerCardAsync(int)` → `LogligPlayerCard?` (имя, год, пол, клуб,
  строки результатов); `ILogligMatchService.Match(card, birthYear, clubName,
  localResults)` → `LogligMatchReport` (Decision/BirthYearMatch/ClubNameMatch/
  MatchedResultCount).
- Поля `Swimmer.Loglig*` + уникальный частичный индекс.
- Образец админ-страницы (вёрстка, apiJson с X-XSRF-TOKEN, toast):
  `server/Swimm.API/Pages/Admin/Swimmers/Index.cshtml` (+ `.cshtml.cs` — минимальный).
- Образец админ-контроллера: `Controllers/SwimmersAdminController.cs`
  (`[Authorize(Roles="Admin")]`, `[AutoValidateAntiforgeryToken]`, record-DTO внутри).
- Навигация: `Pages/Admin/Shared/AdminUi.cs` — массив `AdminNavItem`.

## Шаги

### 1. `ILogligLinkService` + реализация

`Swimm.Application/Abstractions/ILogligLinkService.cs`:
```csharp
public sealed record LogligCandidateInfo(
    int LogligId, string FullName, int? BirthYear, string? ClubName,
    LogligMatchDecision Decision, bool BirthYearMatch, bool ClubNameMatch, int MatchedResultCount);

public sealed record LogligLinkResult(
    bool Linked,                 // true — привязка сохранена
    string? Error,               // человекочитаемая ошибка (уже привязан, карточка недоступна…)
    bool SearchConfigured,       // false — поисковый провайдер без ключа
    IReadOnlyList<LogligCandidateInfo> Candidates); // кандидаты для выбора админом

public sealed record LogligSwimmerRow(
    int SwimmerId, string LastName, string FirstName, int BirthYear, string? ClubName,
    int? LogligId, string? Status, string? Source, DateTime? VerifiedAt);

public interface ILogligLinkService
{
    /// <summary>Пловцы для админ-таблицы: фильтр по подстроке имени (иврит/англ) и статусу ("linked"/"unlinked"/null=все), максимум take.</summary>
    Task<IReadOnlyList<LogligSwimmerRow>> ListAsync(string? query, string? status, int take, CancellationToken ct);

    /// <summary>Пайплайн поиска: кандидаты по имени → карточки → сверка. Ровно один AutoVerify → привязка (auto), иначе кандидаты в ответ.</summary>
    Task<LogligLinkResult> FindAndLinkAsync(int swimmerId, CancellationToken ct);

    /// <summary>Ручная привязка админом (source=admin). Карточка обязана существовать; сверка возвращается как информация.</summary>
    Task<LogligLinkResult> SetManualAsync(int swimmerId, int logligId, CancellationToken ct);

    /// <summary>Снять привязку (обнуляет все Loglig-поля).</summary>
    Task<bool> UnlinkAsync(int swimmerId, CancellationToken ct);
}
```
Реализация `Swimm.Infrastructure/Services/LogligLinkService.cs` — правила из «Решений».
`FindAndLinkAsync`: пловец не найден → `Error`; `Verified` → `Error`; кандидаты (≤5)
→ для каждого `GetPlayerCardAsync` (null-карточки пропустить) + `Match` → решение.
Успешная привязка логируется `LogWarning` («Admin loglig link: swimmer #… ← loglig #…,
source=…») по образцу merge-лога. Регистрация `AddScoped` в DependencyInjection (блок
«Loglig ID»).

### 2. Контроллер

`Controllers/LogligAdminController.cs`, `[Route("api/admin/loglig")]`, атрибуты как у
`SwimmersAdminController`:
- `GET list?query=&status=&take=` (take: default 50, максимум 200) → `ListAsync`;
- `POST find` body `{swimmerId}` → `FindAndLinkAsync`;
- `POST set` body `{swimmerId, logligId}` → `SetManualAsync`;
- `POST unlink` body `{swimmerId}` → `UnlinkAsync` (false → 404).
Ошибки сервиса (`Error != null`) отдавать как `Ok(result)` — это доменный ответ для
UI, не HTTP-ошибка.

### 3. Страница `/Admin/Swimmers/Loglig`

`Pages/Admin/Swimmers/Loglig.cshtml` (+ минимальный `.cshtml.cs` по образцу Index):
- пояснительная плашка сверху (как на Index): что делает кнопка, что привязка —
  обогащение данных и прав не даёт;
- поиск по имени (input + кнопка/Enter), фильтр статуса (select: все/непривязанные/
  привязанные), таблица: имя (dir="rtl"), год, клуб, статус (бейдж: Verified зелёный,
  прочее жёлтое, пусто — серое «—»), LogligId — ссылкой на
  `https://loglig.com:2053/Players/Details/{id}` (target=_blank; ссылку строит JS из
  константы хоста и числа — не из данных сервера);
- у непривязанных: кнопка «Find loglig ID» (зовёт `find`; при `linked` — toast и
  обновление строки; при кандидатах — раскрывающийся блок под строкой со списком
  кандидатов: имя/год/клуб/совпадений, у каждого кнопка «Привязать» → `set`) и поле
  «вставить ссылку» + кнопка «Привязать вручную» (JS извлекает `\d+` из
  `Players/Details/(\d+)` или принимает голое число → `set`);
- у привязанных: кнопка «Отвязать» с `confirm()`;
- `SearchConfigured == false` → у «Find loglig ID» disabled + title «поиск не
  сконфигурирован (CandidateSearch:ApiKey)», ручная привязка доступна;
- JS-стиль, apiJson/esc/toast — как на `Swimmers/Index.cshtml`.
Навигация: в `AdminUi.cs` после пункта Swimmers добавить
`new("Loglig ID", "users", "/Admin/Swimmers/Loglig", "Data")` (иконку можно взять
существующую — посмотри, какие ключи icon поддержаны в _Sidebar).

### 4. Тесты (обязательно, xUnit в Swimm.Tests)

`LogligLinkServiceTests.cs` — InMemory `SwimmDbContext` (образец сетапа —
`SwimmerDedupServiceTests.cs` / `HubGroupCountryTests.cs`) + фейки интерфейсов
(простые классы-стабы в тесте, Moq в проекте нет — не добавлять):
- авто: один кандидат AutoVerify → привязано, Status=Verified, Source=auto, лог не проверяем;
- два AutoVerify → НЕ привязано, оба в Candidates;
- кандидаты есть, но только Candidate-решения → не привязано, список возвращён;
- поиск не сконфигурирован → `SearchConfigured=false`, кандидатов нет, не привязано;
- `SetManual`: карточка недоступна (клиент вернул null) → Error, не привязано;
- `SetManual`: занятый LogligId → Error с именем держателя;
- пловец уже Verified → Find и SetManual возвращают Error;
- `Unlink` чистит все шесть полей; Unlink несуществующего → false;
- `ListAsync`: фильтр "unlinked"/"linked"/подстрока имени.

## Проверка

- `dotnet build server/Swimm.sln` — 0 ошибок;
- `dotnet test server/Swimm.Tests` — все зелёные;
- если добавил НОВЫЕ Tailwind-классы, которых нет в существующих админ-страницах —
  `npm run css:build` (см. корневой CLAUDE.md) и включи обновлённый бандл в результат;
  проще — переиспользуй классы с Swimmers/Index.cshtml, тогда пересборка не нужна.

## Footguns

- Visual Studio может держать Debug-bin (MSB3027) — собирай/гоняй с `--configuration Release`.
- В рабочем дереве чужие незакоммиченные правки (client/src/projects/my-media-project,
  HubGroupsController, MediaController) — не трогать.
- Живые запросы к serper.dev/loglig.com не делать (в тестах — фейки интерфейсов).
- `SwimmDbContext` vs `SwimmReadDbContext`: здесь пишем — только `SwimmDbContext`.
- InMemory-провайдер не проверяет уникальный индекс — тест на занятый LogligId идёт
  через предзаполненного держателя (ветка `AnyAsync`), гонку с `DbUpdateException`
  тестом не покрываем.

## Вне скоупа (не делать)

- Краудсорс-предложения, ночной джоб, rate-limit (шаг 6), батч (шаг 7), обогащение.
- Публичный API/клиентский React — только админка.
- Миграции (схема готова).
- Не коммитить.
