# Задание (Sonnet 5): дашборд «здоровье данных» — T1, бэкенд-счётчики

Контекст: редизайн главной админки `/Admin` (план `docs/plans/admin-dashboard-health-2-plan.md`,
дизайн-хендофф `!design_handoff/design_handoff_data_health_dashboard/README.md`). Работа разбита
на T1–T5; это **T1 — только бэкенд**: расширить сводку `GET /api/admin/dashboard/status` всеми
счётчиками из плана, добавить кэш, покрыть тестами. Вёрстка (T2), deep-link фильтры (T3) и
кнопка «Проверить апдейты» рекордов (T4) — НЕ твои, их делают отдельно.

## Решения (зафиксированы, не пересматривать)

- **DTO перегруппировать по блокам плана** (пловцы/клубы/соревнования/результаты/рекорды/медиа/
  юзеры-группы/система) — форма ниже. Это ломает текущий JSON — поэтому в шаге 4 минимально
  правишь пути полей в JS текущего `Index.cshtml`, чтобы существующие 5 карточек не сломались.
  Новую вёрстку НЕ делать.
- **Кэш**: весь `DashboardStatusSummary` целиком в `IMemoryCache`, ключ `"dashboard:status"`,
  absolute expiration **2 минуты**. Инвалидация не нужна (свежесть 2 мин достаточна).
- «Синтетика» = `SwimmerOrgId LIKE 'SYNTH-%'` (как в `SwimmerDedupService`/`dedup-report.sql`).
- «Пловец без результатов» = нет ни одной строки в `Results` (`ResultRecord.SwimmerId`) И ни
  одной в `RelayMembers`. Синтетику исключать.
- «Клуб без пловцов» = нет `Swimmer.ClubId == club.Id` И нет `ResultRecord.ClubId == club.Id`.
  Псевдоклубы (`IsPseudo`) и SYNTH-клубы исключать.
- «Клуб без страны» = `CountryId == null && !IsPseudo`.
- «FK-аномалии результатов» = строки `Results`, у которых `SwimmerId` отсутствует в `Swimmers`
  ИЛИ `ClubId` отсутствует в `Clubs` (в проде БД держит FK, ожидаемо 0 — карточка-сторож).
- «Эстафета без участников» = `Relay`, на который нет ни одной `RelayMember`-строки.
- «Наборы рекордов» = группировка `Records` по `(RegionType, RegionCode)`; каждому — `Count`
  и `LastUpdatedAt = max(UpdatedAt)`. Порог «устарело» сервер НЕ считает — это фронт (T2).
- «Аудит за 7 дней» = количество строк `AdminAudit` с `CreatedAt >= UtcNow-7d` (уровня
  «ошибка» в аудите нет — считаем все действия; так и назвать `Actions7d`).
- Активный пользователь = `AppUser.LastSeenAt >= UtcNow-7d`.
- Все счётчики — `AsNoTracking`/`CountAsync` по `SwimmDbContext` (сервис уже на нём); дедуп —
  через существующие `ISwimmerDedupService`/`IClubDedupService`, как сейчас.

## Целевая форма DTO (`Swimm.Application/Dtos/DashboardStatusDtos.cs`)

```csharp
public sealed record DashboardStatusSummary(
    DashboardSwimmerStatus Swimmers,
    DashboardClubStatus Clubs,
    DashboardCompetitionStatus Competitions,
    DashboardResultStatus Results,
    IReadOnlyList<DashboardRecordSetStatus> RecordSets,
    DashboardMediaStatus Media,
    DashboardUsersGroupsStatus UsersGroups,
    DashboardSystemStatus System);

public sealed record DashboardSwimmerStatus(
    int Total, int OriginIsr, int OriginLocal, int Synthetic,
    int SureCandidates, int UnsureCandidates, int Orphans,
    int NoOrgId,        // Origin == "isr" && SwimmerOrgId == null
    int NoResults,      // см. решения
    DashboardLogligStatus Loglig);   // существующий record, вложить сюда

public sealed record DashboardClubStatus(
    int Total, int Pseudo,           // Real = Total - Pseudo, отдельного поля не надо
    int SureCandidates, int UnsureCandidates,
    int NoSwimmers, int NoCountry,
    int ClubRequestsPending);        // HubGroupClubRequests.Status == "pending"

public sealed record DashboardCompetitionStatus(
    int Total, int WithResults,      // Empty = Total - WithResults
    int DiscoveryImported, int DiscoveryNew, int DiscoveryIgnored,
    int DiscoveryErrors,             // DiscoveredCompetition.LastError != null
    int NoOrgCompId);                // Competition.OrgCompId == null

public sealed record DashboardResultStatus(
    int Total, int TimeFail,         // TimeFail == true
    int FkAnomalies, int EmptyRelays);

public sealed record DashboardRecordSetStatus(
    string RegionType, string RegionCode, int Count, DateTime LastUpdatedAt);

public sealed record DashboardMediaStatus(
    int Total, int Video, int Photo, // по MediaType; прочие типы = Total - Video - Photo
    int Broken, int Unchecked,
    int ModerationPending);          // UserMediaPublications.Status == "pending"

public sealed record DashboardUsersGroupsStatus(
    int UsersTotal, int Active7d, int Deactivated,
    int GroupsTotal, int GroupsOfficial,
    int JoinRequestsPending);        // HubGroupUserMembers.Status == "pending"

public sealed record DashboardSystemStatus(
    DateTime? LastImportAt, bool? LastImportApproved,   // последняя строка Sys_ImportHistory
    DateTime? LastMediaCheckAt,                          // max(UserMedia.LinkCheckedAt)
    DateTime? LastDiscoverySeenAt,                       // max(DiscoveredCompetition.LastSeenAt)
    int AuditActions7d);
```

`DashboardLogligStatus` и `DashboardDiscoveryStatus` — второй упраздняется (его поля переезжают
в `DashboardCompetitionStatus`), логика матчинга Discovery остаётся как есть (см. текущий сервис
и коммент про `DiscoveryCompetitionMatcher`).

`MediaType`: проверь фактические значения в `UserMedia.MediaType` (константы/комменты в
`server/Swimm.Domain/Entities/UserMedia.cs`) и считай Video/Photo по ним.

## Что уже готово (не переделывать)

- `server/Swimm.Infrastructure/Services/DashboardStatusService.cs` — текущие блоки: дедуп через
  сервисы, loglig `GroupBy(LogligIdStatus)`, discovery через `DiscoveryCompetitionMatcher`,
  медиа Broken/Unchecked. Расширяй, не переписывай работающее.
- `ISwimmerDedupService.FindCandidatesAsync` — в отчёте уже есть `Orphans` (пловцы без
  результатов/групп/медиа/тренировок/аккаунта) — брать оттуда, НЕ считать заново.
- Сущности: `UserMediaPublication` (Status pending/approved/rejected),
  `HubGroupUserMember` (Status active/pending), `HubGroupClubRequest` (Status pending/…),
  `AdminAudit.CreatedAt`, `ImportHistory` (`Sys_ImportHistory`: ImportDate, Approved),
  `Record` (RegionType/RegionCode/UpdatedAt), `AppUser` (IsActive, LastSeenAt),
  `Swimmer` (Origin "isr"/"local", SwimmerOrgId), `Club` (IsPseudo, CountryId),
  `Relay`/`RelayMember`, `ResultRecord` (SwimmerId, ClubId, TimeFail).
- `DashboardAdminController` — эндпоинт `/api/admin/dashboard/status`; контракт метода
  `IDashboardStatusService.GetStatusAsync` не меняется.

## Шаги

1. **DTO**: переписать `DashboardStatusDtos.cs` в форму выше (XML-комменты на русском, как там).
2. **Сервис**: расширить `DashboardStatusService` новыми счётчиками. Кэш: инжект `IMemoryCache`,
   `GetOrCreateAsync("dashboard:status", AbsoluteExpirationRelativeToNow = 2 мин)`. Проверь, что
   `AddMemoryCache()` есть в DI (Program.cs / DependencyInjection) — если нет, добавь.
   Счётчики писать отдельными `CountAsync`-запросами (читаемость важнее одного мегазапроса);
   для NoResults/NoSwimmers/FkAnomalies/EmptyRelays — `Where(... !Any ...)`-подзапросы, EF
   их транслирует в NOT EXISTS.
3. **Никакой новой вёрстки**, но: в `Pages/Admin/Index.cshtml` (inline JS, функция рендера
   статус-карточек) поправить пути полей под новый JSON, чтобы существующие 5 карточек
   продолжали показывать те же числа (`swimmers.unsureCandidates`, `clubs.…`,
   `swimmers.loglig.…`, `competitions.discoveryNew`/`discoveryImported`, `media.…`).
   JSON-имена — camelCase (System.Text.Json по умолчанию).
4. **Тесты** (`server/Swimm.Tests/DashboardStatusServiceTests.cs`, xUnit + InMemory, Moq нет —
   стабы уже в файле): существующие 4 теста адаптировать к новой форме DTO + добавить:
   - пустая БД → все нули, `RecordSets` пуст, `System.*At` = null;
   - пловцы: Total/OriginIsr/OriginLocal/Synthetic/NoOrgId/NoResults (синтетика исключена
     из NoResults; пловец только с RelayMember НЕ попадает в NoResults);
   - клубы: NoSwimmers (клуб с результатом, но без Swimmer.ClubId — НЕ в NoSwimmers;
     IsPseudo исключён), NoCountry (IsPseudo исключён), ClubRequestsPending;
   - соревнования: WithResults, DiscoveryErrors, NoOrgCompId;
   - результаты: TimeFail, FkAnomalies (результат с несуществующим SwimmerId), EmptyRelays;
   - рекорды: две группы (world,"") и (country,"ISR") → 2 набора с корректными Count/max;
   - медиа: Video/Photo/ModerationPending (pending считается, approved/rejected нет);
   - юзеры/группы: Active7d (LastSeenAt 8 дней назад НЕ считается), Deactivated,
     GroupsOfficial, JoinRequestsPending (active не считается);
   - система: LastImportAt+Approved из последней строки, AuditActions7d (запись 8 дней
     назад не считается);
   - кэш: два вызова подряд → данные из кэша (изменить БД между вызовами, число не
     изменилось). InMemoryCache бери реальный `MemoryCache(new MemoryCacheOptions())`.

## Проверка

- `dotnet build server/Swimm.sln --configuration Release` — 0 ошибок.
- `dotnet test server/Swimm.Tests --configuration Release` — все зелёные (сейчас 588).
- Вживую: подними API, `curl -s http://localhost:5078/api/admin/dashboard/status` под
  админ-кукой ЛИБО просто открой /Admin и убедись, что 5 существующих карточек рендерятся
  с числами (не NaN/undefined). Останови процесс API после проверки.

## Footguns

- Build-lock MSB3027 (`Swimm.API.dll` locked): жив старый `dotnet run` — убей процесс
  (`Get-CimInstance Win32_Process -Filter "Name='Swimm.API.exe'" | % { Stop-Process -Id $_.ProcessId -Force }`).
  Если :5078 держит Visual Studio — работай в `--configuration Release` и поднимай API на :5079.
- Два DbContext: сервис работает через `SwimmDbContext` (полный доступ) — так и оставить,
  `SwimmReadDbContext` сюда не тащить (нужны Sys_-таблицы: аудит, публикации, юзеры).
- InMemory-провайдер не проверяет FK — для теста FkAnomalies это удобно (можно вставить
  результат с несуществующим SwimmerId), но НЕ пиши логику, которая полагается на FK.
- `DiscoveredCompetition.Status` — строковые константы `DiscoveredCompetitionStatus`;
  статусы публикаций/членств тоже строки — используй константы классов, не литералы.
- EF InMemory иногда не транслирует сложные подзапросы так же, как Npgsql — если
  `!Any`-подзапрос падает на InMemory, разбей на два запроса (список Id + Except) внутри
  сервиса, но сначала попробуй прямой вариант.

## Вне скоупа (не делать)

- Новая вёрстка дашборда (вариант 1c) — T2.
- Query-фильтры на целевых страницах — T3.
- Кнопка/эндпоинты «Проверить апдейты» рекордов и порог устаревания — T4.
- Снапшоты/тренды метрик, кастом-карточки.
- Миграции схемы — их тут нет и не должно появиться.
