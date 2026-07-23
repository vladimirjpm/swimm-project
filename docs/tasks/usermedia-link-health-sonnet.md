# Задание Sonnet — здоровье ссылок UserMedia (хвост фазы 7.5)

## Контекст

Фаза 7 (админка) закрыта, кроме одного пункта роадмапа 7.5: **дашборд `/Admin` должен
показывать «битые ссылки UserMedia»**. `Sys_UserMedia` хранит внешние ссылки на медиа
(YouTube/Vimeo/картинки). Со временем часть ссылок умирает (видео удалено → 404). Нужен
**on-demand чек живости** ссылок + счётчик на дашборде + страница со списком битых.

«Битое» = **реально мёртвая ссылка** (404 / недоступно), а не формат — формат уже
валидируется при вставке (`MediaUrlValidator`).

## Решения (зафиксированы, НЕ пересматривать)

- Проверка **по кнопке** (синхронно, on-demand). Фоновой/периодической проверки НЕ делаем
  (UserMedia сейчас мало; если разрастётся — вынесем в BackgroundService отдельной задачей).
- Логика проверки по `SourceType`:
  - `youtube` → GET `https://www.youtube.com/oembed?format=json&url=<urlencoded>`: **200 = жив**,
    **404/400 = битый** (oEmbed 404 у удалённого/приватного видео).
  - `vimeo` → GET `https://vimeo.com/api/oembed.json?url=<urlencoded>`: 200 = жив, 404 = битый.
  - `other` (в т.ч. `image`) → GET самой ссылки с `HttpCompletionOption.ResponseHeadersRead`
    (не качаем тело): **2xx/3xx = жив**, **4xx = битый**, 5xx/сетевая ошибка/таймаут = битый
    с причиной в `LinkError`.
- Named `HttpClient` `"media-link-check"`: `Timeout` 10 c, заголовок User-Agent
  `"SwimmLinkCheck/1.0"`, `AllowAutoRedirect = true`.
- Параллелизм ограничить `SemaphoreSlim(6)`. Каждый элемент — свой `try/catch`, падение
  одного не рушит прогон. По завершении — один `SaveChanges` (или батчами).
- `LinkCheckedAt` ставится у КАЖДОЙ проверенной строки (даже живой). `LinkOk` = true/false.
- Инвалидация кэша НЕ нужна: поля служебные, в публичные выдачи не идут.
- UI сайта — **только English** (страница `/Admin/Media`, лейблы карточки). Комментарии/тексты
  тостов — по стилю файла (RU-проза, EN-идентификаторы).

## Что уже готово (НЕ переделывать, использовать как есть)

- **Сущность** `server/Swimm.Domain/Entities/UserMedia.cs` → таблица `Sys_UserMedia`
  (маппинг в `SwimmDbContext.OnModelCreating`, секция `Sys_UserMedia`). Поля: `Id`, `UserId`
  (→ `AppUser`), `SwimmerId` (→ `Swimmer`), `Url`, `MediaType` (image/video),
  `SourceType` (youtube/vimeo/other), `Visibility`, `CreatedAt`.
- **Дашборд-конвейер**: `IDashboardStatusService` → `DashboardStatusService`
  (`server/Swimm.Infrastructure/Services/DashboardStatusService.cs`) →
  `DashboardStatusSummary` (`server/Swimm.Application/Dtos/DashboardStatusDtos.cs`).
  Отдаётся через `GET /api/admin/dashboard/status` (`DashboardAdminController`).
  Рендер карточек — JS в `server/Swimm.API/Pages/Admin/Index.cshtml`, функция
  `statusCard(href, value, label, sublines, color)` + `loadStatus()`.
- **AdminController** (`server/Swimm.API/Controllers/AdminController.cs`) уже инжектит
  `IAdminAuditService _audit` и `ICacheService _cacheService`; `[Authorize(Roles="Admin")]`
  + `[AutoValidateAntiforgeryToken]`. Аудит: `await _audit.LogAsync(action, entityType,
  entityId, summary, detailsObj)` — см. существующие вызовы (`cache.invalidate`, `setting.update`).
- **Named HttpClient паттерн** — `server/Swimm.Infrastructure/DependencyInjection.cs`,
  блок `services.AddHttpClient("loglig")…` и `"serper"`. Регистрируй `"media-link-check"` рядом.
- **Эталон admin-list страницы** — `server/Swimm.API/Pages/Admin/Styles/Index.cshtml(.cs)`
  (простой список + toolbar-кнопка). Общий партиал пагинации — `Shared/_Pagination.cshtml`
  (если понадобится; для битых ссылок пагинация вряд ли нужна — их мало).
- **Nav** — `server/Swimm.API/Pages/Admin/Shared/AdminUi.cs`, массив `AdminNav.Items`
  (`new AdminNavItem(Title, Icon, Url, Section)`). Иконки: см. `Shared/_Icon.cshtml`
  (есть `history`, `download`, `radio`, `database`, `shield`, `users`, `award`, `trophy`,
  `tag`, `waves`, `gauge`, `inbox`, `settings`, `home`, `log-out`, `menu`, `x`,
  `alert-triangle`). Для Media возьми `radio` или `alert-triangle` (что уместнее).
- **Миграции** — ДВА DbContext, каждая команда с `--context SwimmDbContext`. Пример в
  корневом `CLAUDE.md`. `Sys_`-таблица → grant `swimm_ro` НЕ нужен.

## Шаги

### 1. Миграция: колонки живости в Sys_UserMedia
В `UserMedia.cs` добавь:
```csharp
public DateTime? LinkCheckedAt { get; set; }   // null = ещё не проверяли
public bool? LinkOk { get; set; }              // true=жив, false=битый, null=не проверяли
public int? LinkStatusCode { get; set; }       // последний HTTP-код (или null)
[MaxLength(200)] public string? LinkError { get; set; }  // краткая причина, если битая
```
Скаффолд:
```
dotnet ef migrations add AddUserMediaLinkHealth \
  --project server/Swimm.Infrastructure --startup-project server/Swimm.API --context SwimmDbContext
```
Проверь, что миграция только ADD COLUMN (4 nullable колонки), без побочных изменений.
Применение: `dotnet ef database update … --context SwimmDbContext` (при закрытом VS).

### 2. Чекер: IUserMediaLinkChecker + реализация
`server/Swimm.Application/Abstractions/IUserMediaLinkChecker.cs`:
```csharp
public interface IUserMediaLinkChecker
{
    Task<UserMediaLinkCheckReport> CheckAllAsync(CancellationToken ct = default);
}
```
DTO в `server/Swimm.Application/Dtos/UserMediaLinkDtos.cs`:
```csharp
public sealed record UserMediaLinkCheckReport(int Checked, int Ok, int Broken);

public sealed record BrokenMediaRowDto(
    int Id, string Url, string OwnerEmail, string SwimmerName,
    string MediaType, string SourceType,
    DateTime? LinkCheckedAt, int? LinkStatusCode, string? LinkError);
```
Реализация `server/Swimm.Infrastructure/Services/UserMediaLinkChecker.cs`
(конструктор: `SwimmDbContext db, IHttpClientFactory httpFactory, ILogger<UserMediaLinkChecker> logger`):
- Загрузи все `db.UserMedia` (tracked).
- `SemaphoreSlim(6)`; для каждой строки — определить check-URL по SourceType (oEmbed для
  youtube/vimeo, иначе сама Url); `client.GetAsync(checkUrl, ResponseHeadersRead, ct)`.
- Живость по правилам выше. На исключение (`HttpRequestException`/`TaskCanceledException`/
  таймаут) — `LinkOk=false`, `LinkStatusCode=null`, `LinkError` = тип+краткое сообщение (обрезать 200).
- Проставь `LinkCheckedAt=DateTime.UtcNow`, `LinkOk`, `LinkStatusCode`, `LinkError` (у живой — null).
- Один `SaveChangesAsync`. Верни `UserMediaLinkCheckReport`.
- **Важно:** обновление tracked-полей делай ПОСЛЕ параллельных проверок (собери результаты в
  список, потом присвой в цикле, потом SaveChanges) — чтобы не гонять DbContext из нескольких
  потоков (EF DbContext не потокобезопасен).

Метод списка битых — добавь сюда же в интерфейс:
```csharp
Task<IReadOnlyList<BrokenMediaRowDto>> GetBrokenAsync(CancellationToken ct = default);
```
Реализация: `db.UserMedia.AsNoTracking().Where(m => m.LinkOk == false)` + Include User/Swimmer,
проекция в `BrokenMediaRowDto` (`OwnerEmail = m.User.Email`,
`SwimmerName = m.Swimmer.LastName + " " + m.Swimmer.FirstName`), сортировка по `LinkCheckedAt desc`.

DI: `server/Swimm.Infrastructure/DependencyInjection.cs` —
`services.AddHttpClient("media-link-check", c => { c.Timeout = TimeSpan.FromSeconds(10);
c.DefaultRequestHeaders.UserAgent.ParseAdd("SwimmLinkCheck/1.0"); });` и
`services.AddScoped<IUserMediaLinkChecker, UserMediaLinkChecker>();`.

### 3. Endpoint запуска проверки (AdminController)
Инжектни `IUserMediaLinkChecker _linkChecker` (добавь поле + параметр ctor + присваивание —
как сделаны `_audit`, `_cacheService`). Добавь:
```csharp
[HttpPost("media/check-links")]
public async Task<IActionResult> CheckMediaLinks(CancellationToken ct)
{
    var report = await _linkChecker.CheckAllAsync(ct);
    await _audit.LogAsync("media.link-check", "UserMedia", null,
        $"Проверка ссылок медиа: всего {report.Checked}, битых {report.Broken}", report);
    return Ok(report);
}
```

### 4. Карточка на дашборде
- В `DashboardStatusDtos.cs`: новый рекорд
  `public sealed record DashboardMediaStatus(int Total, int Broken, int Unchecked);` и добавь
  5-м позиционным полем в `DashboardStatusSummary(..., DashboardMediaStatus Media)`.
- В `DashboardStatusService.GetStatusAsync`: посчитай
  `Total = await db.UserMedia.CountAsync(ct)`,
  `Broken = await db.UserMedia.CountAsync(m => m.LinkOk == false, ct)`,
  `Unchecked = await db.UserMedia.CountAsync(m => m.LinkCheckedAt == null, ct)` и передай
  `new DashboardMediaStatus(...)` в конструктор summary.
- В `Index.cshtml` (`loadStatus()`): добавь блок-карточку (по образцу Discovery) →
  `statusCard('/Admin/Media', m.broken, 'Broken media links', sub, m.broken > 0 ? '#ffa726' : '#66bb6a')`,
  где `sub` показывает `total: N` и, если `m.unchecked>0`, `unchecked: N`.

### 5. Страница /Admin/Media (список битых + кнопка проверки)
`server/Swimm.API/Pages/Admin/Media/Index.cshtml(.cs)` (`[Authorize(Roles="Admin")]`,
namespace `Swimm.API.Pages.Admin.Media`):
- `.cs`: инжект `IUserMediaLinkChecker`; `OnGetAsync` → `Broken = await _checker.GetBrokenAsync()`.
- `.cshtml`: заголовок; кнопка **Check links now** (JS `fetch('/api/admin/media/check-links',
  {method:'POST', headers:{'X-XSRF-TOKEN': _csrfToken}})` → тост с отчётом → перезагрузка страницы);
  таблица битых: Url (ссылка, `target="_blank"`), Owner (email), Swimmer, Type
  (`mediaType/sourceType`), Checked (LinkCheckedAt), Status (LinkStatusCode/LinkError). Пусто →
  «No broken media links.». Стиль — как `Styles/Index.cshtml` (admin-card/admin-table/admin-btn).
- Nav: добавь `new AdminNavItem("Media", "radio", "/Admin/Media", "System")` в `AdminUi.cs`.

### 6. Тесты (обязательно)
`server/Swimm.Tests/UserMediaLinkCheckerTests.cs` — по образцу существующих (InMemory db +
фейковый HttpMessageHandler). Дай фейк `IHttpClientFactory`, возвращающий `HttpClient` с
кастомным `HttpMessageHandler`, который отдаёт заданный `HttpStatusCode` по URL. Кейсы:
- `other` ссылка, ответ 200 → `LinkOk == true`, `LinkStatusCode == 200`, `LinkError == null`.
- `other` ссылка, ответ 404 → `LinkOk == false`, `LinkStatusCode == 404`.
- сетевая ошибка (handler бросает `HttpRequestException`) → `LinkOk == false`,
  `LinkError != null`, `LinkCheckedAt != null`.
- `youtube` ссылка: проверь, что чекер дёргает `youtube.com/oembed` (handler ассертит host),
  200 → жив.
- `GetBrokenAsync` возвращает только строки с `LinkOk == false` с корректными Owner/Swimmer.
Образец InMemory-контекста и фейков — `server/Swimm.Tests/ResultAdminRepositoryTests.cs`,
`SerperCandidateSearchProviderTests.cs` (там уже есть фейковый HttpMessageHandler — переиспользуй паттерн).

### 7. Документация
- `docs/admin-pages/media.md` (новая): что делает страница, файлы, эндпоинт, правила проверки.
- Строка в таблице `docs/admin-pages/README.md`:
  `| /Admin/Media | [media.md](media.md) | Проверка живости ссылок UserMedia (битые 404) |`.

## Проверка (Definition of Done)
```
dotnet build server/Swimm.sln -c Release
dotnet test server/Swimm.Tests/Swimm.Tests.csproj
```
Оба зелёные, новые тесты проходят. Если VS держит билд-лок — работай в `--configuration Release`,
API поднимай на :5079. Живьём (по возможности): `/Admin/Media` открывается, кнопка «Check links
now» отрабатывает, дашборд показывает карточку «Broken media links».

## Footguns
- **Два DbContext** — все `dotnet ef` с `--context SwimmDbContext`.
- **Build-lock от VS**: `MSB3021/3027 … locked by .NET Host` → VS держит `Swimm.API.dll`.
  Собирать/мигрировать при закрытом VS, либо `-c Release` + API на :5079.
- **`Sys_UserMedia`** — приватная таблица, grant `swimm_ro` НЕ добавлять.
- **EF DbContext не потокобезопасен** — параллелить только HTTP-запросы, запись полей и
  SaveChanges делать в одном потоке после сбора результатов.
- Аудит `LogAsync` вызывать ПОСЛЕ мутации (у чекера — после SaveChanges; в контроллере — после
  `CheckAllAsync`).

## Вне скоупа (НЕ делать)
- Фоновая/периодическая проверка, расписание, инкрементальная («только непроверенные») —
  только полный прогон по кнопке.
- Удаление/правка медиа из этой страницы (только показ битых + ссылка наружу).
- Ретраи, backoff, обход rate-limit, кэш результатов проверки.
- Клиентская (публичная) часть — только админка.
