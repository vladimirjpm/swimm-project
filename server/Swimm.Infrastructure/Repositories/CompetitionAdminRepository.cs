using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Swimm.Application.Abstractions;
using Swimm.Application.Constants;
using Swimm.Application.Dtos;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Services;

namespace Swimm.Infrastructure.Repositories;

/// <summary>
/// Эталонная реализация админского CRUD (см. <see cref="ICompetitionAdminRepository"/>).
/// Пишет через owner-контекст <see cref="SwimmDbContext"/>; после мутаций сбрасывает публичный кэш.
/// </summary>
public class CompetitionAdminRepository : ICompetitionAdminRepository
{
    private readonly SwimmDbContext _db;
    private readonly ICacheService _cache;
    private readonly ICompetitionRecalculationService? _recalc;

    /// <param name="recalc">
    /// Пересчёт материализованных величин (объединённые места) при смене
    /// <c>ShowCombineAllResults</c>. Необязателен: null — пересчёт пропускается (тесты).
    /// </param>
    public CompetitionAdminRepository(SwimmDbContext db, ICacheService cache,
        ICompetitionRecalculationService? recalc = null)
    {
        _db = db;
        _cache = cache;
        _recalc = recalc;
    }

    public async Task<PagedResult<CompetitionRowDto>> GetPagedAsync(string? search, string? categoryKey, int? year, int page, int pageSize, int? orgCompId = null)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 20;

        var heads = FilteredHeads(search, categoryKey, year);

        if (orgCompId is int oc)
        {
            // Сужаем до соревнования с этим OrgCompId (или всего его события) — точный переход
            // с Discovery на нужную строку. OrgCompId у дня события — резолвим к «голове» события.
            var target = await _db.Competitions.AsNoTracking()
                .Where(c => c.OrgCompId == oc)
                .Select(c => new { c.Id, c.EventId })
                .FirstOrDefaultAsync();
            heads = target == null
                ? heads.Where(_ => false)
                : target.EventId is int ev
                    ? heads.Where(c => c.EventId == ev)
                    : heads.Where(c => c.Id == target.Id);
        }

        var total = await heads.CountAsync();

        var headItems = await heads
            .OrderByDescending(c => c.Date)
            .ThenBy(c => c.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(ProjectListItem)
            .ToListAsync();

        var rows = await BuildRowsAsync(headItems);
        return new PagedResult<CompetitionRowDto>(rows, total, page, pageSize);
    }

    public async Task<UnifiedCompetitionList> GetUnifiedAsync(
        string? search, string? categoryKey, int? year, string? stage, bool showSynthetic, int? month, int page, int pageSize,
        string? qualityFilter = null)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 20;

        // 1) БД-сторона: все «головы» под фильтрами (без пагинации — мержим и режем в памяти).
        // На admin-масштабе (~1.5k соревнований) это приемлемо; при росте — вынести в БД-пагинацию.
        var heads = FilteredHeads(search, categoryKey, year);
        // Тестовая синтетика (SYNTH Meet…) по умолчанию скрыта — её сотни и она забивает список.
        if (!showSynthetic)
            heads = heads.Where(c => !c.Name.StartsWith("SYNTH "));
        var headItems = await heads
            .Select(ProjectListItem)
            .ToListAsync();
        var dbRows = await BuildRowsAsync(headItems);

        // Индекс «id соревнования (одиночного или дня события) → строка списка» — чтобы привязать
        // Discovery-оверлей к нужной строке (день события резолвится к «голове»).
        var rowByCompId = new Dictionary<int, CompetitionRowDto>();
        foreach (var r in dbRows)
            foreach (var id in r.Single != null ? [r.Single.Id] : r.Days.Select(d => d.Id))
                rowByCompId[id] = r;

        // 2) Discovery-сторона: все строки + резолв «discovered → соревнование» (OrgCompId
        // приоритетно, иначе имя+дата через общий матчер). OrgCompId соревнований — для приоритета.
        var discovered = await _db.DiscoveredCompetitions.AsNoTracking().ToListAsync();
        var matches = await new DiscoveryCompetitionMatcher(_db).MatchAsync(discovered);
        var compByOrgCompId = await _db.Competitions.AsNoTracking()
            .Where(c => c.OrgCompId != null)
            .Select(c => new { OrgCompId = c.OrgCompId!.Value, c.Id })
            .ToDictionaryAsync(c => c.OrgCompId, c => c.Id);

        var unified = new List<UnifiedCompetitionRowDto>(dbRows.Count + discovered.Count);
        var overlaidRows = new HashSet<CompetitionRowDto>();

        foreach (var d in discovered)
        {
            var site = new UnifiedSiteInfo
            {
                DiscoveredId = d.Id, OrgCompId = d.OrgCompId, Name = d.Name,
                DateStart = d.DateStart, DateEnd = d.DateEnd, Venue = d.Venue,
                Status = d.Status, Languages = d.Languages, LastError = d.LastError, LogligId = d.LogligId
            };

            // Резолв к соревнованию: OrgCompId штампован → он; иначе матч по имени+дате.
            int? compId = compByOrgCompId.TryGetValue(d.OrgCompId, out var byOrg)
                ? byOrg
                : matches.GetValueOrDefault(d.Id)?.CompetitionId;

            if (compId is int cid && rowByCompId.TryGetValue(cid, out var row))
            {
                // Импортировано: вешаем site-оверлей на строку БД (первый выигрывает — для многодневки
                // берём одну репрезентативную discovery-строку; языки/площадка из неё).
                if (overlaidRows.Add(row))
                    unified.Add(new UnifiedCompetitionRowDto
                    {
                        Stage = CompetitionStage.Imported, Db = row, Site = site, SortDate = RowDate(row)
                    });
            }
            else
            {
                // Только на сайте: в БД соревнования нет. Фильтры БД-стороны применяем и здесь:
                // category — исключает (у discovery нет категорий); search/year — по имени/датам.
                if (!string.IsNullOrWhiteSpace(categoryKey)) continue;
                if (!string.IsNullOrWhiteSpace(search) &&
                    d.Name.IndexOf(search.Trim(), StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (year is int yy && d.DateStart.Year != yy && d.DateEnd.Year != yy) continue;

                unified.Add(new UnifiedCompetitionRowDto
                {
                    Stage = d.Status == "ignored" ? CompetitionStage.Ignored : CompetitionStage.OnSite,
                    Site = site, SortDate = d.DateStart
                });
            }
        }

        // Строки БД без discovery-оверлея → «только в БД» (PDF-импорт без OrgCompId).
        foreach (var row in dbRows.Where(r => !overlaidRows.Contains(r)))
            unified.Add(new UnifiedCompetitionRowDto
            {
                Stage = CompetitionStage.DbOnly, Db = row, SortDate = RowDate(row)
            });

        // 3) Фильтр по стадии (если задан).
        if (!string.IsNullOrWhiteSpace(stage) && Enum.TryParse<CompetitionStage>(stage, ignoreCase: true, out var st))
            unified = unified.Where(u => u.Stage == st).ToList();

        // 3b) T3b deep-link: доп. фильтр «здоровье данных» (те же предикаты, что DashboardStatusService).
        // Многодневное событие свёрнуто в одну строку — «нет OrgCompId»/«нет результатов» проверяем
        // по любому дню/сумме дней события, а не только по «голове».
        unified = qualityFilter switch
        {
            "discovery-error" => unified.Where(u => u.Site?.LastError != null).ToList(),
            "no-org-comp-id" => unified.Where(u => u.Db != null && (
                u.Db.Single != null ? u.Db.Single.OrgCompId == null : u.Db.Days.Any(d => d.OrgCompId == null))).ToList(),
            "no-results" => unified.Where(u => u.Db != null && (
                u.Db.Single != null ? u.Db.Single.ResultCount == 0 : u.Db.TotalResultCount == 0)).ToList(),
            _ => unified
        };

        // 4) Счётчики по месяцам (под текущими фильтрами, но ДО фильтра по месяцу — для кнопок).
        var monthCounts = new int[12];
        foreach (var u in unified)
            if (u.SortDate.Year > 1) // непарсибельные даты (MinValue) не считаем
                monthCounts[u.SortDate.Month - 1]++;

        // 5) Фильтр по месяцу (если задан) + единый date-desc порядок + пагинация в памяти.
        if (month is >= 1 and <= 12)
            unified = unified.Where(u => u.SortDate.Year > 1 && u.SortDate.Month == month).ToList();

        unified = unified.OrderByDescending(u => u.SortDate).ToList();
        var total = unified.Count;
        var pageItems = unified.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        return new UnifiedCompetitionList(
            new PagedResult<UnifiedCompetitionRowDto>(pageItems, total, page, pageSize), monthCounts);
    }

    /// <summary>Дата строки для сортировки: одиночное — своя дата, событие — дата первого дня.
    /// Date хранится текстом «дд/ММ/гггг»; непарсибельное → MinValue (уедет вниз списка).</summary>
    private static DateTime RowDate(CompetitionRowDto row)
    {
        var s = row.Single?.Date ?? (row.Days.Count > 0 ? row.Days[0].Date : null);
        return DateTime.TryParseExact(s, "dd/MM/yyyy",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var dt) ? dt : DateTime.MinValue;
    }

    /// <summary>
    /// «Головы» списка = одиночные соревнования ИЛИ многодневные события (свёрнуты в одну строку),
    /// с фильтрами поиск/категория/сезон. «Голова» события — день с минимальным DayNumber
    /// (min, а не ==1, чтобы событие не пропало, если день 1 удалили). Общий шов для
    /// <see cref="GetPagedAsync"/> и <see cref="GetUnifiedAsync"/>.
    /// </summary>
    private IQueryable<Competition> FilteredHeads(string? search, string? categoryKey, int? year)
    {
        var heads = _db.Competitions.AsNoTracking().Where(c =>
            c.EventId == null ||
            c.DayNumber == _db.Competitions.Where(x => x.EventId == c.EventId).Min(x => x.DayNumber));

        if (!string.IsNullOrWhiteSpace(search))
        {
            // Экранируем LIKE-метасимволы (\ % _), иначе поиск по литералу «100%» или «a_b»
            // трактует их как wildcard'ы и матчит лишнее / не находит нужное.
            var s = search.Trim()
                .Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
            // По имени/подзаголовку головы. У события имя общее на всех днях — поиск по названию
            // события работает; по подзаголовку конкретного дня внутри события — нет (приемлемо).
            heads = heads.Where(c =>
                EF.Functions.ILike(c.Name, $"%{s}%", "\\") ||
                (c.SubName != null && EF.Functions.ILike(c.SubName, $"%{s}%", "\\")));
        }

        if (!string.IsNullOrWhiteSpace(categoryKey))
        {
            // Категория может быть проставлена не на самой голове, а на другом дне того же события
            // (обычно одинаково на всех днях, но подстраховываемся) — матчим по всей группе дней.
            var compIdsWithCategory = _db.CategoryCompetitions
                .Where(cc => cc.Category.Key == categoryKey)
                .Select(cc => cc.CompetitionId);
            heads = heads.Where(c =>
                compIdsWithCategory.Contains(c.Id) ||
                (c.EventId != null && _db.Competitions.Any(x =>
                    x.EventId == c.EventId && compIdsWithCategory.Contains(x.Id))));
        }

        if (year is int y)
        {
            // Date хранится текстом «дд/ММ/гггг» — год всегда последние 4 символа.
            var yearStr = y.ToString();
            heads = heads.Where(c =>
                c.Date.EndsWith(yearStr) ||
                (c.EventId != null && _db.Competitions.Any(x =>
                    x.EventId == c.EventId && x.Date.EndsWith(yearStr))));
        }

        return heads;
    }

    /// <summary>
    /// Достраивает проектированные «головы» до строк списка: подтягивает дни событий и категории
    /// (по одному запросу на набор), сворачивает события ≥2 дней. Общий шов для пагинированного
    /// и объединённого списков.
    /// </summary>
    private async Task<List<CompetitionRowDto>> BuildRowsAsync(IReadOnlyList<CompetitionListItemDto> headItems)
    {
        // Дни событий, попавших в набор, — одним запросом.
        var eventIds = headItems.Where(h => h.EventId != null).Select(h => h.EventId!.Value).ToList();
        var daysByEvent = new Dictionary<int, List<CompetitionListItemDto>>();
        if (eventIds.Count > 0)
        {
            var days = await _db.Competitions.AsNoTracking()
                .Where(c => c.EventId != null && eventIds.Contains(c.EventId!.Value))
                .OrderBy(c => c.DayNumber)
                .Select(ProjectListItem)
                .ToListAsync();
            daysByEvent = days
                .GroupBy(d => d.EventId!.Value)
                .ToDictionary(g => g.Key, g => g.ToList());
        }

        var rows = headItems.Select(h =>
        {
            // Событие показываем свёрнутым только если реально ≥2 дней (иначе — обычная строка).
            if (h.EventId is int evId && daysByEvent.TryGetValue(evId, out var days) && days.Count >= 2)
            {
                return new CompetitionRowDto
                {
                    EventId = evId,
                    EventName = h.EventName ?? h.Name,
                    Days = days,
                    DayCount = days.Count,
                    TotalResultCount = days.Sum(d => d.ResultCount),
                    DateRange = FormatDateRange(days),
                    PoolType = days[0].PoolType,
                    Country = days[0].Country,
                    // Флаг на шапке — только если он у всех дней (обычно так и есть).
                    IsMasters = days.All(d => d.IsMasters),
                    IsAward = days.All(d => d.IsAward),
                    ShowCombineAllResults = days.All(d => d.ShowCombineAllResults),
                };
            }
            return new CompetitionRowDto { Single = h };
        }).ToList();

        // Категории для всех соревнований в наборе (одиночные + дни) — одним запросом (бейджи в списке).
        var allItems = rows.SelectMany(r => r.Single != null ? new[] { r.Single } : r.Days.ToArray()).ToList();
        var itemIds = allItems.Select(i => i.Id).ToList();
        if (itemIds.Count > 0)
        {
            var cats = await _db.CategoryCompetitions.AsNoTracking()
                .Where(cc => itemIds.Contains(cc.CompetitionId))
                .Join(_db.Categories, cc => cc.CategoryId, cat => cat.Id,
                    (cc, cat) => new { cc.CompetitionId, cat.Key, cat.Name, cat.Badge, cat.DisplayOrder })
                .OrderBy(x => x.DisplayOrder)
                .ToListAsync();
            var byComp = cats
                .GroupBy(x => x.CompetitionId)
                .ToDictionary(g => g.Key, g => g.Select(x => new CategoryTagDto { Key = x.Key, Name = x.Name, Badge = x.Badge }).ToList());
            foreach (var it in allItems)
                if (byComp.TryGetValue(it.Id, out var list)) it.Categories = list;
        }

        // У шапки события — категории, общие для всех дней (обычно одинаковые).
        foreach (var r in rows.Where(r => r.IsEvent && r.Days.Count > 0))
        {
            r.Categories = r.Days[0].Categories
                .Where(c => r.Days.All(d => d.Categories.Any(x => x.Key == c.Key)))
                .ToList();
        }

        return rows;
    }

    public async Task<IReadOnlyList<CategoryTagDto>> GetAllCategoriesAsync() =>
        await _db.Categories.AsNoTracking()
            .OrderBy(c => c.DisplayOrder)
            .Select(c => new CategoryTagDto { Key = c.Key, Name = c.Name, Badge = c.Badge })
            .ToListAsync();

    public async Task<IReadOnlyList<int>> GetAvailableYearsAsync()
    {
        // Date хранится текстом «дд/ММ/гггг» (длина 10) — год всегда последние 4 символа.
        var years = await _db.Competitions.AsNoTracking()
            .Where(c => c.Date.Length == 10)
            .Select(c => c.Date.Substring(6, 4))
            .Distinct()
            .ToListAsync();
        return years
            .Where(y => int.TryParse(y, out _))
            .Select(int.Parse)
            .OrderByDescending(y => y)
            .ToList();
    }

    /// <summary>Проекция Competition → строка списка. Общая для «голов» и для дней события.</summary>
    private Expression<Func<Competition, CompetitionListItemDto>> ProjectListItem => c => new CompetitionListItemDto
    {
        Id = c.Id,
        Name = c.Name,
        SubName = c.SubName,
        Date = c.Date,
        PoolType = c.PoolType,
        Country = c.Country != null ? c.Country.CountryCode : "",
        OrgCompId = c.OrgCompId,
        IsMasters = c.IsMasters,
        IsAward = c.IsAward,
        ShowCombineAllResults = c.ShowCombineAllResults,
        EventId = c.EventId,
        EventName = c.Event != null ? c.Event.Name : null,
        DayNumber = c.DayNumber,
        ResultCount = _db.Results.Count(r => r.CompetitionId == c.Id),
        ResultUrlCount = c.OrgCompId != null
            ? _db.CompetitionResultUrls.Count(u => u.OrgCompId == c.OrgCompId)
            : 0
    };

    /// <summary>«дд/ММ/гггг – дд/ММ/гггг» по дням события (или один день, если даты совпали).</summary>
    private static string FormatDateRange(IReadOnlyList<CompetitionListItemDto> days)
    {
        var first = days[0].Date;
        var last = days[^1].Date;
        return string.IsNullOrEmpty(last) || last == first ? first : $"{first} – {last}";
    }

    public async Task<CompetitionEditDto?> GetByIdAsync(int id)
    {
        var c = await _db.Competitions
            .AsNoTracking()
            .Include(x => x.Event)
            .Include(x => x.Country)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (c == null) return null;

        var dto = new CompetitionEditDto
        {
            Id = c.Id,
            Name = c.Name,
            SubName = c.SubName,
            Date = c.Date,
            PoolType = c.PoolType,
            Country = c.Country != null ? c.Country.CountryCode : "",
            OrgCompId = c.OrgCompId,
            IsMasters = c.IsMasters,
            IsAward = c.IsAward,
            ShowCombineAllResults = c.ShowCombineAllResults,
            EventId = c.EventId,
            EventName = c.Event?.Name,
            DayNumber = c.DayNumber,
            PointRuleClubsId = c.PointRuleClubsId,
            PointRuleSwimmersId = c.PointRuleSwimmersId,
            EventDayCount = c.EventId == null
                ? 1
                : await _db.Competitions.CountAsync(x => x.EventId == c.EventId),
            ResultCount = await _db.Results.CountAsync(r => r.CompetitionId == c.Id),
            CategoryKeys = await _db.CategoryCompetitions
                .AsNoTracking()
                .Where(cc => cc.CompetitionId == c.Id)
                .Join(_db.Categories, cc => cc.CategoryId, cat => cat.Id, (cc, cat) => cat.Key)
                .ToListAsync()
        };

        if (c.OrgCompId != null)
        {
            dto.ResultUrls = await _db.CompetitionResultUrls
                .AsNoTracking()
                .Where(u => u.OrgCompId == c.OrgCompId)
                .OrderBy(u => u.Culture)
                .Select(u => new CompetitionResultUrlDto
                {
                    Id = u.Id,
                    OrgCompId = u.OrgCompId,
                    Culture = u.Culture,
                    Url = u.Url
                })
                .ToListAsync();
        }

        return dto;
    }

    public async Task<CompetitionSaveResult> CreateAsync(CompetitionInputDto input)
    {
        var error = await ValidateAsync(input, excludeId: null);
        if (error != null) return CompetitionSaveResult.Fail(error);

        var comp = new Competition();
        Apply(comp, input);
        await ApplyCountryAsync(comp, input.Country);
        _db.Competitions.Add(comp);
        var save = await SaveAsync(comp);
        if (save.Success) await SyncCategoriesAsync(comp.Id, input.CategoryKeys);
        return save;
    }

    public async Task<CompetitionSaveResult> UpdateAsync(int id, CompetitionInputDto input)
    {
        var comp = await _db.Competitions.FindAsync(id);
        if (comp == null) return CompetitionSaveResult.Fail($"Соревнование #{id} не найдено");

        var error = await ValidateAsync(input, excludeId: id);
        if (error != null) return CompetitionSaveResult.Fail(error);

        // Смена/очистка OrgCompId, к которому привязаны URL результатов, порвёт FK
        // (CompetitionResultUrls.OrgCompId → Competitions.OrgCompId, только ON DELETE CASCADE,
        // без ON UPDATE). Блокируем с понятным текстом вместо DbUpdateException/500.
        if (comp.OrgCompId != input.OrgCompId && comp.OrgCompId is int oldOrg)
        {
            var urlCount = await _db.CompetitionResultUrls.CountAsync(u => u.OrgCompId == oldOrg);
            if (urlCount > 0)
                return CompetitionSaveResult.Fail(
                    $"К текущему OrgCompId={oldOrg} привязано URL результатов: {urlCount}. " +
                    "Удалите их перед сменой или очисткой OrgCompId.");
        }

        // Объединённые места считаются только у соревнований с этим флагом. Включили задним
        // числом — строки остались с пустым CombinedPlace, и тоггл на клиенте показал бы
        // пустую таблицу; выключили — значения обязаны обнулиться, иначе останутся от
        // прошлой жизни (docs/points-rules-per-competition-plan.md §3.4).
        var combineChanged = comp.ShowCombineAllResults != input.ShowCombineAllResults;

        Apply(comp, input);
        await ApplyCountryAsync(comp, input.Country);
        var save = await SaveAsync(comp);
        if (save.Success) await SyncCategoriesAsync(comp.Id, input.CategoryKeys);

        if (save.Success && combineChanged && _recalc is not null)
        {
            // Соревнование уже сохранено — сбой пересчёта не должен отменять правку формы.
            // Аварийный прогон: `dotnet run -- --recalc-combined`.
            try { await _recalc.RecalculateCompetitionAsync(comp.Id); }
            catch (Exception) { /* лог не нужен: аварийный прогон закрывает случай */ }
            await _cache.InvalidateAllAsync();
        }

        return save;
    }

    /// <summary>SaveChanges + инвалидация кэша с перехватом гонки уникальности (проверка
    /// в <see cref="ValidateAsync"/> не атомарна с записью — параллельный сабмит мог вставить дубль).</summary>
    private async Task<CompetitionSaveResult> SaveAsync(Competition comp)
    {
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            return CompetitionSaveResult.Fail(
                "Не удалось сохранить: нарушено ограничение уникальности " +
                "(название+дата+бассейн или OrgCompId уже заняты).");
        }
        await _cache.InvalidateAllAsync();
        return CompetitionSaveResult.Ok(comp.Id);
    }

    public async Task<CompetitionSaveResult> AddResultUrlAsync(int orgCompId, string culture, string url)
    {
        culture = culture.Trim();
        url = url.Trim();

        if (string.IsNullOrEmpty(culture)) return CompetitionSaveResult.Fail("Culture обязателен");
        if (string.IsNullOrEmpty(url)) return CompetitionSaveResult.Fail("URL обязателен");

        // OrgCompId должен существовать у какого-то соревнования — иначе URL «висит» без связи.
        if (!await _db.Competitions.AnyAsync(c => c.OrgCompId == orgCompId))
            return CompetitionSaveResult.Fail($"Нет соревнования с OrgCompId={orgCompId}");

        if (await _db.CompetitionResultUrls.AnyAsync(u => u.OrgCompId == orgCompId && u.Culture == culture))
            return CompetitionSaveResult.Fail($"URL для culture «{culture}» уже есть");

        _db.CompetitionResultUrls.Add(new CompetitionResultUrl
        {
            OrgCompId = orgCompId,
            Culture = culture,
            Url = url
        });
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Гонка с параллельным добавлением того же (OrgCompId, Culture) — есть UNIQUE-индекс.
            return CompetitionSaveResult.Fail($"URL для culture «{culture}» уже есть");
        }
        await _cache.InvalidateAllAsync();
        return CompetitionSaveResult.Ok(orgCompId);
    }

    public async Task<bool> RemoveResultUrlAsync(int urlId, int orgCompId)
    {
        // Привязка к orgCompId — чтобы со страницы одного соревнования нельзя было удалить
        // URL другого по подделанному urlId.
        var entity = await _db.CompetitionResultUrls
            .FirstOrDefaultAsync(u => u.Id == urlId && u.OrgCompId == orgCompId);
        if (entity == null) return false;

        _db.CompetitionResultUrls.Remove(entity);
        await _db.SaveChangesAsync();
        await _cache.InvalidateAllAsync();
        return true;
    }

    // ── helpers ────────────────────────────────────────────────────────────────

    private static void Apply(Competition comp, CompetitionInputDto input)
    {
        comp.Name = input.Name.Trim();
        comp.SubName = string.IsNullOrWhiteSpace(input.SubName) ? null : input.SubName.Trim();
        comp.Date = (input.Date ?? "").Trim();
        comp.PoolType = (input.PoolType ?? "").Trim();
        comp.OrgCompId = input.OrgCompId;
        // IsMasters — производный от членства в категории Masters (галочки в форме нет).
        comp.IsMasters = input.CategoryKeys.Contains(Category.MastersKey);
        comp.IsAward = input.IsAward;
        comp.ShowCombineAllResults = input.ShowCombineAllResults;
        comp.PointRuleClubsId = input.PointRuleClubsId;
        comp.PointRuleSwimmersId = input.PointRuleSwimmersId;
    }

    /// <summary>
    /// Страна соревнования из ввода: alpha-3 код (ISR…) → FK на Countries, find-or-create
    /// (как в импорте JsonImportService и HubGroupCrudCore.ApplyCountryAsync). Пустой код —
    /// снимает страну (CountryId = null).
    /// </summary>
    private async Task ApplyCountryAsync(Competition comp, string? countryCode)
    {
        var code = (countryCode ?? "").Trim().ToUpperInvariant();
        if (code.Length == 0)
        {
            comp.CountryId = null;
            return;
        }

        var country = await _db.Countries.FirstOrDefaultAsync(c => c.CountryCode == code);
        if (country == null)
        {
            country = new Country { CountryCode = code, CountryName = code };
            _db.Countries.Add(country);
            await _db.SaveChangesAsync();
        }
        comp.CountryId = country.Id;
    }

    /// <summary>Приводит членство соревнования в категориях к заданному набору ключей (add/remove).</summary>
    private async Task SyncCategoriesAsync(int competitionId, IReadOnlyCollection<string> keys)
    {
        var wantedIds = await _db.Categories.AsNoTracking()
            .Where(c => keys.Contains(c.Key))
            .Select(c => c.Id)
            .ToListAsync();

        var existing = await _db.CategoryCompetitions
            .Where(cc => cc.CompetitionId == competitionId)
            .ToListAsync();

        var existingIds = existing.Select(cc => cc.CategoryId).ToHashSet();
        var toRemove = existing.Where(cc => !wantedIds.Contains(cc.CategoryId)).ToList();
        var toAdd = wantedIds.Where(catId => !existingIds.Contains(catId)).ToList();

        if (toRemove.Count == 0 && toAdd.Count == 0) return;

        if (toRemove.Count > 0)
            _db.CategoryCompetitions.RemoveRange(toRemove);

        foreach (var catId in toAdd)
        {
            var nextOrder = (await _db.CategoryCompetitions
                .Where(cc => cc.CategoryId == catId)
                .MaxAsync(cc => (int?)cc.DisplayOrder) ?? 0) + 1;
            _db.CategoryCompetitions.Add(new CategoryCompetition
            {
                CategoryId = catId,
                CompetitionId = competitionId,
                DisplayOrder = nextOrder
            });
        }

        await _db.SaveChangesAsync();
        await _cache.InvalidateAllAsync();
    }

    /// <summary>Проверка уникальности до записи (дружелюбный текст вместо DbUpdateException).</summary>
    private async Task<string?> ValidateAsync(CompetitionInputDto input, int? excludeId)
    {
        if (string.IsNullOrWhiteSpace(input.Name))
            return "Название обязательно";

        var name = input.Name.Trim();
        // Пустое текстовое поле формы биндится в null (не ""), поэтому Date/PoolType/Country
        // (не обязательные, без required-проверки) нормализуем через ?? "" перед Trim().
        var date = (input.Date ?? "").Trim();
        var pool = (input.PoolType ?? "").Trim();

        if (!PoolTypes.IsValid(pool))
            return $"Тип бассейна должен быть одним из: {string.Join(" / ", PoolTypes.All)}";

        // UNIQUE (Name, Date, PoolType)
        var dupKey = await _db.Competitions.AnyAsync(c =>
            c.Id != excludeId &&
            c.Name == name && c.Date == date && c.PoolType == pool);
        if (dupKey)
            return $"Соревнование с таким названием, датой и бассейном уже есть ({name} / {date} / {pool})";

        // UNIQUE OrgCompId (nullable — null не конфликтует)
        if (input.OrgCompId is int org)
        {
            var dupOrg = await _db.Competitions.AnyAsync(c => c.Id != excludeId && c.OrgCompId == org);
            if (dupOrg)
                return $"OrgCompId={org} уже занят другим соревнованием";
        }

        return await ValidateRuleIdsAsync(input.PointRuleClubsId, input.PointRuleSwimmersId);
    }

    /// <summary>Правила должны существовать: FK RESTRICT иначе даст DbUpdateException/500.</summary>
    private async Task<string?> ValidateRuleIdsAsync(int? clubsRuleId, int? swimmersRuleId)
    {
        if (clubsRuleId is int cid && !await _db.PointRulesClubs.AnyAsync(r => r.Id == cid))
            return $"Правило клубных очков #{cid} не найдено";

        if (swimmersRuleId is int sid && !await _db.PointRulesSwimmers.AnyAsync(r => r.Id == sid))
            return $"Правило High Point #{sid} не найдено";

        return null;
    }

    // ── Привязка правил очков (Э4) ─────────────────────────────────────────────

    public async Task<IReadOnlyList<CompetitionRuleRowDto>> GetForRuleAssignmentAsync(
        int? year, string scope, bool onlyUnassigned)
    {
        // Дни, а не свёрнутые события: правило хранится у каждого дня отдельно, и в выборке
        // «без привязки» один непривязанный день события не должен прятаться за головой.
        var q = _db.Competitions.AsNoTracking().Where(c => !c.Name.StartsWith("SYNTH "));

        if (year is int y)
            q = q.Where(c => c.Date.EndsWith(y.ToString()));

        q = scope switch
        {
            "masters" => q.Where(c => c.IsMasters),
            "non-masters" => q.Where(c => !c.IsMasters),
            _ => q
        };

        if (onlyUnassigned)
            q = q.Where(c => c.PointRuleClubsId == null && c.PointRuleSwimmersId == null);

        var rows = await q
            .OrderByDescending(c => c.Date.Substring(6, 4))
            .ThenByDescending(c => c.Date.Substring(3, 2))
            .ThenByDescending(c => c.Date.Substring(0, 2))
            .ThenBy(c => c.Id)
            .Select(c => new CompetitionRuleRowDto
            {
                Id = c.Id,
                Name = c.Name,
                SubName = c.SubName,
                Date = c.Date,
                IsMasters = c.IsMasters,
                ClubsRuleVersion = c.PointRuleClubs != null ? c.PointRuleClubs.Version : null,
                SwimmersRuleVersion = c.PointRuleSwimmers != null ? c.PointRuleSwimmers.Version : null
            })
            .ToListAsync();

        return rows;
    }

    public async Task<CompetitionSaveResult> AssignRulesAsync(CompetitionRuleAssignmentDto assignment)
    {
        if (!assignment.SetClubs && !assignment.SetSwimmers)
            return CompetitionSaveResult.Fail("Не выбрано ни одно правило для простановки");

        var ids = assignment.CompetitionIds.Distinct().ToList();
        if (ids.Count == 0)
            return CompetitionSaveResult.Fail("Не выбрано ни одного соревнования");

        var error = await ValidateRuleIdsAsync(
            assignment.SetClubs ? assignment.ClubsRuleId : null,
            assignment.SetSwimmers ? assignment.SwimmersRuleId : null);
        if (error != null) return CompetitionSaveResult.Fail(error);

        var comps = await _db.Competitions.Where(c => ids.Contains(c.Id)).ToListAsync();
        foreach (var comp in comps)
        {
            if (assignment.SetClubs) comp.PointRuleClubsId = assignment.ClubsRuleId;
            if (assignment.SetSwimmers) comp.PointRuleSwimmersId = assignment.SwimmersRuleId;
        }

        await _db.SaveChangesAsync();
        await _cache.InvalidateAllAsync();
        // Id в результате нет — это массовая операция; число изменённых строк отдаём через Id.
        return CompetitionSaveResult.Ok(comps.Count);
    }

    public async Task<IReadOnlyList<int>> GetEventDayIdsAsync(int eventId) =>
        await _db.Competitions.AsNoTracking()
            .Where(c => c.EventId == eventId)
            .OrderBy(c => c.DayNumber)
            .Select(c => c.Id)
            .ToListAsync();
}
