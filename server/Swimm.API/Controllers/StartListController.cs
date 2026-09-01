using Microsoft.AspNetCore.Mvc;
using Swimm.API.Http;
using Swimm.Application.Abstractions;

namespace Swimm.API.Controllers;

/// <summary>
/// Публичный стартовый протокол (docs/plans/start-list-plan.md, шаг С6): «когда и на какой
/// дорожке плывёт мой ребёнок».
///
/// Логина НЕ требует — ссылку пересылают в родительский чат клуба, и она обязана открываться
/// у любого. Данные те же открытые протоколы федерации, что и результаты.
///
/// Идентификатор соревнования здесь — <c>orgCompId</c> (compID на isr.org.il), а НЕ наш
/// <c>Competitions.Id</c>: у предстоящего старта справочной строки ещё нет, и заводить её
/// заранее нельзя (§3.1 плана).
///
/// Кэш нарочно короткий: посев меняют до последнего дня, а механизма дожать изменение до
/// уже открытой страницы в проекте нет. По той же причине каждый ответ несёт
/// <c>updatedAt</c> — витрина показывает «обновлено в HH:MM» и даёт кнопку обновить.
/// </summary>
[ApiController]
[Route("api/start-list")]
public class StartListController : ControllerBase
{
    private const string CacheControlValue = "public, max-age=30";
    private static readonly TimeSpan PayloadTtl = TimeSpan.FromSeconds(60);

    private const int DefaultUpcomingDays = 21;

    /// <summary>Окно секции «Upcoming»: календарь федерации публикуют помесячно.</summary>
    private const int DefaultUpcomingCompetitionDays = 60;
    private const int MaxSwimmerIds = 50;

    /// <summary>Потолок источников в одном запросе: у составного чемпионата их единицы.</summary>
    private const int MaxOrgCompIds = 20;

    private readonly IStartListPublicRepository _startList;
    private readonly ICacheService _cache;

    public StartListController(IStartListPublicRepository startList, ICacheService cache)
    {
        _startList = startList;
        _cache = cache;
    }

    /// <summary>
    /// Предстоящие соревнования — секция «Upcoming» общего списка `/competitions`
    /// (решение В9 от 2026-08-27).
    ///
    /// Список строится по заявкам, а не по «Входящим»: приватную <c>Sys_DiscoveredCompetitions</c>
    /// публичный путь не видит, да и показывать имеет смысл только те старты, для которых
    /// стартовый протокол уже затянут. Соревнование опознаётся <c>org_comp_id</c> — своего
    /// <c>Competitions.Id</c> у него до импорта протокола нет.
    /// </summary>
    [HttpGet("competitions")]
    public async Task<IActionResult> GetUpcomingCompetitions(
        [FromQuery] int days = DefaultUpcomingCompetitionDays, CancellationToken ct = default)
    {
        // Дата в ключе: без неё вчерашняя выдача «предстоящих» пережила бы полночь.
        var from = DateTime.UtcNow;
        return await this.CachedJson(_cache,
            $"http:start-list:competitions:{from:yyyy-MM-dd}:{days}",
            () => _startList.GetUpcomingCompetitionsAsync(from, days, ct),
            PayloadTtl, CacheControlValue);
    }

    /// <summary>Программа соревнования по времени (зум 1) — умолчание таба.</summary>
    [HttpGet("{orgCompId:int}")]
    public async Task<IActionResult> GetProgramme(int orgCompId, CancellationToken ct)
    {
        if (!await _startList.ExistsAsync(orgCompId, ct: ct)) return NotFound();

        return await this.CachedJson(_cache,
            $"http:start-list:{orgCompId}:programme",
            () => _startList.GetProgrammeAsync(orgCompId, ct),
            PayloadTtl, CacheControlValue);
    }

    /// <summary>Дисциплина с дорожками (зум 2) — «с кем плывёт мой».</summary>
    [HttpGet("{orgCompId:int}/events/{orgDisciplineId:int}")]
    public async Task<IActionResult> GetEvent(int orgCompId, int orgDisciplineId, CancellationToken ct)
    {
        if (!await _startList.ExistsAsync(orgCompId, orgDisciplineId: orgDisciplineId, ct: ct)) return NotFound();

        return await this.CachedJson(_cache,
            $"http:start-list:{orgCompId}:event:{orgDisciplineId}",
            () => _startList.GetEventAsync(orgCompId, orgDisciplineId, ct),
            PayloadTtl, CacheControlValue);
    }

    /// <summary>Карточка пловца (зум 3) — то, ради чего фича и делается.</summary>
    [HttpGet("{orgCompId:int}/swimmers/{swimmerId:int}")]
    public async Task<IActionResult> GetSwimmer(int orgCompId, int swimmerId, CancellationToken ct)
    {
        if (!await _startList.ExistsAsync(orgCompId, swimmerId: swimmerId, ct: ct)) return NotFound();

        return await this.CachedJson(_cache,
            $"http:start-list:{orgCompId}:swimmer:{swimmerId}",
            () => _startList.GetSwimmerAsync(orgCompId, swimmerId, ct),
            PayloadTtl, CacheControlValue);
    }

    /// <summary>
    /// Клубы соревнования со счётчиками — секция «follow a whole club» пикера (шаг Т2):
    /// «מכבי חיפה · 42 swimmers · 96 entries».
    /// </summary>
    [HttpGet("{orgCompId:int}/clubs")]
    public async Task<IActionResult> GetClubs(int orgCompId, CancellationToken ct) =>
        await this.CachedJson(_cache,
            $"http:start-list:{orgCompId}:clubs",
            () => _startList.GetClubsAsync([orgCompId], ct),
            PayloadTtl, CacheControlValue);

    /// <summary>
    /// То же по нескольким источникам сразу — у составного старта (окружные протоколы)
    /// клубный список один на все compID, а не по одному на каждый. Форма запроса та же,
    /// что у поиска: повторяемый <c>orgCompId</c>.
    /// </summary>
    [HttpGet("clubs")]
    public async Task<IActionResult> GetClubsAcross(
        [FromQuery] int[] orgCompId, CancellationToken ct = default)
    {
        var ids = orgCompId.Distinct().Take(MaxOrgCompIds).ToArray();
        if (ids.Length == 0) return BadRequest("Укажите хотя бы один orgCompId.");

        return await this.CachedJson(_cache,
            $"http:start-list:clubs:{string.Join(',', ids.Order())}",
            () => _startList.GetClubsAsync(ids, ct),
            PayloadTtl, CacheControlValue);
    }

    /// <summary>Кто из клуба плывёт на этом старте.</summary>
    [HttpGet("{orgCompId:int}/clubs/{clubId:int}")]
    public async Task<IActionResult> GetClub(int orgCompId, int clubId, CancellationToken ct) =>
        await this.CachedJson(_cache,
            $"http:start-list:{orgCompId}:club:{clubId}",
            () => _startList.GetClubSwimsAsync(orgCompId, clubId, ct),
            PayloadTtl, CacheControlValue);

    /// <summary>
    /// Ближайшие старты нескольких пловцов — основа блока «мои избранные» (шаг С8).
    /// Список id приходит от клиента (свои favorites он уже знает), поэтому эндпоинт
    /// остаётся анонимным: он ничего не рассказывает сверх того, что и так публично.
    /// </summary>
    [HttpGet("upcoming")]
    public async Task<IActionResult> GetUpcoming(
        [FromQuery] int[] swimmerId, [FromQuery] int days = DefaultUpcomingDays,
        CancellationToken ct = default)
    {
        var ids = swimmerId.Distinct().Take(MaxSwimmerIds).ToArray();
        if (ids.Length == 0) return BadRequest("Укажите хотя бы один swimmerId.");

        // Дата в ключе кэша: без неё вчерашняя выдача «ближайших» пережила бы полночь.
        var from = DateTime.UtcNow;
        return await this.CachedJson(_cache,
            $"http:start-list:upcoming:{from:yyyy-MM-dd}:{days}:{string.Join(',', ids.Order())}",
            () => _startList.GetUpcomingAsync(ids, from, days, ct),
            PayloadTtl, CacheControlValue);
    }

    /// <summary>
    /// Поиск пловца по имени внутри соревнования: «когда плывёт мой», если его нет в
    /// избранных. Источников (compID) может быть несколько — окружные протоколы одного
    /// чемпионата, — поэтому параметр повторяемый, как swimmerId у «ближайших».
    /// </summary>
    [HttpGet("search")]
    public async Task<IActionResult> Search(
        [FromQuery] int[] orgCompId, [FromQuery] string q = "", [FromQuery] int limit = 20,
        CancellationToken ct = default)
    {
        var ids = orgCompId.Distinct().Take(MaxOrgCompIds).ToArray();
        if (ids.Length == 0) return BadRequest("Укажите хотя бы один orgCompId.");

        var query = (q ?? string.Empty).Trim();
        // Короткий запрос — не ошибка, а «ещё печатают»: отдаём пустой список, чтобы поле
        // ввода не показывало ошибку на первом же символе.
        if (query.Length < 2) return Ok(Array.Empty<object>());

        return await this.CachedJson(_cache,
            $"http:start-list:search:{string.Join(',', ids.Order())}:{query.ToLowerInvariant()}:{limit}",
            () => _startList.SearchSwimmersAsync(ids, query, limit, ct),
            PayloadTtl, CacheControlValue);
    }

    /// <summary>
    /// Карточка пловца сразу по нескольким источникам — та же выдача, что
    /// <c>{orgCompId}/swimmers/{id}</c>, но для соревнования из нескольких протоколов:
    /// заплывы всех дней в одном календаре.
    /// </summary>
    [HttpGet("swimmers/{swimmerId:int}")]
    public async Task<IActionResult> GetSwimmerAcross(
        int swimmerId, [FromQuery] int[] orgCompId, CancellationToken ct = default)
    {
        var ids = orgCompId.Distinct().Take(MaxOrgCompIds).ToArray();
        if (ids.Length == 0) return BadRequest("Укажите хотя бы один orgCompId.");

        var payload = await _startList.GetSwimmerAcrossAsync(ids, swimmerId, ct);
        if (payload is null) return NotFound();

        Response.Headers.CacheControl = CacheControlValue;
        return Ok(payload);
    }
}
