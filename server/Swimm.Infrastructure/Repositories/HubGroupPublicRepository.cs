using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Swimm.Application.Constants;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Application.Mapping;
using Swimm.Domain;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Services;

namespace Swimm.Infrastructure.Repositories;

/// <summary>
/// Публичный read-путь групп (см. <see cref="IHubGroupPublicRepository"/>).
///
/// Два контекста намеренно: публичные группы читаются через <see cref="SwimmReadDbContext"/>
/// (роль swimm_ro), а виртуальная группа «Моё избранное» — через <see cref="SwimmDbContext"/>,
/// потому что Sys_UserFavorites роли swimm_ro недоступна (личные данные, см. setup-roles.sql).
/// </summary>
public class HubGroupPublicRepository : IHubGroupPublicRepository
{
    /// <summary>Сколько последних заплывов отдаём на страницу группы.</summary>
    private const int RecentResultsLimit = 25;

    /// <summary>Сколько лучших заплывов последнего старта показывает карточка Overview.</summary>
    private const int LastStartRowsLimit = 5;

    private readonly SwimmReadDbContext _read;
    private readonly SwimmDbContext _rw;
    private readonly ISettingsService _settings;

    public HubGroupPublicRepository(SwimmReadDbContext read, SwimmDbContext rw, ISettingsService settings)
    {
        _read = read;
        _rw = rw;
        _settings = settings;
    }

    private string Visibility => HubGroupVisibilityRules.Current(_settings);

    public async Task<IReadOnlyList<HubGroupListItemDto>> GetGroupsAsync()
    {
        var visibility = Visibility;
        // Приватные в каталоге не показываем НИКОМУ: список общий и кэшируется, а участники
        // приходят к своей группе по ссылке или из «My groups».
        if (visibility == HubGroupVisibilityRules.Private) return [];

        // Официальная группа — главная (П4): копии, подписанные на клуб с официальной группой,
        // в каталоге не показываем — по ссылке они работают (GetBySlugAsync их не фильтрует).
        var query = _read.HubGroups.AsNoTracking().Where(HubGroupCatalog.ListedInCatalog(_read));
        if (visibility == HubGroupVisibilityRules.PerGroup)
            query = query.Where(g => g.IsPublic);

        return await query
            .OrderBy(g => g.Name)
            .Select(g => new HubGroupListItemDto
            {
                Slug = g.Slug,
                Name = g.Name,
                NameEn = g.NameEn,
                Description = g.Description,
                IconUrl = g.IconUrl,
                Location = g.Location,
                Country = g.Country != null ? g.Country.CountryCode : null,
                ClubName = g.Club != null ? g.Club.Name : null,
                IsOfficial = g.IsOfficial,
                // Скрытых владельцем клубных пловцов (IsExcluded) не видит НИ ОДИН читатель
                // состава — ни счётчик, ни страница, ни ростер соревнований.
                MemberCount = g.Members.Count(m => !m.IsExcluded)
            })
            .ToListAsync();
    }

    public async Task<HubGroupDetailsDto?> GetBySlugAsync(string slug)
    {
        // Приватную тоже отдаём целиком: смотреть ли её этому зрителю, решает контроллер по
        // GetAccessAsync — участнику страница, остальным заглушка (решение §6-6, 11.09.2026).
        var group = await _read.HubGroups.AsNoTracking()
            .Include(g => g.Club)
            .Include(g => g.Country)
            .FirstOrDefaultAsync(g => g.Slug == slug);
        if (group == null) return null;

        var members = await _read.HubGroupMembers.AsNoTracking()
            .Where(m => m.HubGroupId == group.Id && !m.IsExcluded)
            .OrderBy(m => m.SortOrder)
            .Select(m => new HubGroupPublicMemberDto
            {
                SwimmerId = m.SwimmerId,
                Name = (m.Swimmer!.LastName + " " + m.Swimmer.FirstName).Trim(),
                NameEn = (m.Swimmer.LastNameEn + " " + m.Swimmer.FirstNameEn).Trim(),
                BirthYear = m.Swimmer.BirthYear,
                ClubName = m.Swimmer.Club != null ? m.Swimmer.Club.Name : null,
                Role = m.Role
            })
            .ToListAsync();

        var dto = new HubGroupDetailsDto
        {
            Id = group.Id,
            Slug = group.Slug,
            Name = group.Name,
            NameEn = group.NameEn,
            Description = group.Description,
            IconUrl = group.IconUrl,
            CoverImageUrl = group.CoverImageUrl,
            Location = group.Location,
            Country = group.Country?.CountryCode,
            ClubName = group.Club?.Name,
            IsOfficial = group.IsOfficial,
            JoinPolicy = group.JoinPolicy,
            IsPrivate = HubGroupVisibilityRules.IsPrivate(Visibility, group.IsPublic),
            Links = ParseLinks(group.Links),
            IsVirtual = false,
            Members = members
        };

        // Подписка на клуб и официальная группа этого клуба (если это не мы) — для шапки: копию
        // клуба открыли по ссылке мимо каталога, и она должна показать, где «лицо клуба» (П4).
        var followed = await _read.HubGroupClubSubscriptions.AsNoTracking()
            .Where(s => s.HubGroupId == group.Id)
            .Select(s => new
            {
                s.ClubId,
                ClubName = s.Club!.Name.Length > 0 ? s.Club.Name : s.Club.NameEn,
                Official = _read.HubGroups
                    .Where(o => o.IsOfficial && o.ClubId == s.ClubId && o.Id != group.Id)
                    .Select(o => new { o.Slug, o.Name })
                    .FirstOrDefault()
            })
            .FirstOrDefaultAsync();
        if (followed != null)
        {
            dto.FollowedClubId = followed.ClubId;
            dto.FollowedClubName = followed.ClubName;
            dto.OfficialGroupSlug = followed.Official?.Slug;
            dto.OfficialGroupName = followed.Official?.Name;
        }

        // Настройки отображения: показ блока фото и указатель «взять из медиа». Сам URL
        // указателя доразрешает контроллер — там уже собрана лента `Gallery`, в которой
        // лежат и свои медиа, и одобренные публикации. Здесь — фоллбек на обложку.
        var display = EntityDisplaySettings.Parse(group.DisplaySettings);
        dto.ShowHeroImage = display.Hero.Show;
        dto.HeroMediaId = display.Hero.MediaId;
        dto.HeroImageUrl = group.CoverImageUrl;

        FillTrainingSchedule(dto, group.TrainingSchedule);

        await FillAggregatesAsync(_read, dto, members.Select(m => m.SwimmerId).ToList());
        return dto;
    }

    /// <summary>
    /// Расписание и ближайшее занятие. Считаем ЗДЕСЬ, а не на клиенте: «сегодня» должно быть
    /// израильским независимо от часов зрителя, и логику так покрывают тесты
    /// (GroupTrainingScheduleTests). Пустое расписание → оба поля null, слоты шапки скрыты.
    /// </summary>
    private static void FillTrainingSchedule(HubGroupDetailsDto dto, string? json)
    {
        var schedule = GroupTrainingSchedule.Parse(json);
        if (!schedule.HasSlots) return;

        dto.TrainingSchedule = new GroupTrainingScheduleDto
        {
            Slots = schedule.Slots
                .Where(s => s.IsValid)
                .OrderBy(s => s.Day).ThenBy(s => s.StartTime!.Value)
                .Select(s => new GroupTrainingSlotDto { Day = s.Day, Start = s.Start, End = s.End })
                .ToList(),
            Place = schedule.Place,
            PoolType = schedule.PoolType,
            Note = schedule.Note,
        };

        var next = schedule.NextOccurrence(IsraelTime.ToLocal(DateTime.UtcNow));
        if (next == null) return;

        dto.NextTraining = new NextTrainingDto
        {
            Date = next.Value.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            Start = next.Value.Slot.Start,
            End = next.Value.Slot.End,
            Place = schedule.Place,
            PoolType = schedule.PoolType,
        };
    }

    public async Task<HubGroupDetailsDto> GetFavoritesGroupAsync(int userId)
    {
        // Sys_UserFavorites читается только владельцем через rw-контекст; клубы из избранного
        // здесь не участвуют — «группа» состоит из пловцов.
        var members = await _rw.UserFavorites.AsNoTracking()
            .Where(f => f.UserId == userId && f.SwimmerId != null)
            .OrderBy(f => f.SortOrder)
            .Select(f => new HubGroupPublicMemberDto
            {
                SwimmerId = f.SwimmerId!.Value,
                Name = (f.Swimmer!.LastName + " " + f.Swimmer.FirstName).Trim(),
                NameEn = (f.Swimmer.LastNameEn + " " + f.Swimmer.FirstNameEn).Trim(),
                BirthYear = f.Swimmer.BirthYear,
                ClubName = f.Swimmer.Club != null ? f.Swimmer.Club.Name : null,
                Role = "member"
            })
            .ToListAsync();

        var dto = new HubGroupDetailsDto
        {
            Slug = "favorites",
            Name = "Моё избранное",
            NameEn = "My favorites",
            IsVirtual = true,
            Members = members
        };

        await FillAggregatesAsync(_rw, dto, members.Select(m => m.SwimmerId).ToList());
        return dto;
    }

    public async Task<List<int>?> GetRosterSwimmerIdsAsync(string slug)
    {
        var groupId = await _read.HubGroups.AsNoTracking()
            .Where(g => g.Slug == slug)
            .Select(g => (int?)g.Id)
            .FirstOrDefaultAsync();
        if (groupId == null) return null;

        return await _read.HubGroupMembers.AsNoTracking()
            .Where(m => m.HubGroupId == groupId && !m.IsExcluded)
            .Select(m => m.SwimmerId)
            .ToListAsync();
    }

    public async Task<HubGroupAccessDto?> GetAccessAsync(string slug, int? userId, bool isSiteAdmin)
    {
        // Через rw-контекст: членство и админы групп — Sys_-таблицы, роли swimm_ro их не видно.
        var group = await _rw.HubGroups.AsNoTracking()
            .Where(g => g.Slug == slug)
            .Select(g => new { g.Id, g.IsPublic, g.OwnerUserId })
            .FirstOrDefaultAsync();
        if (group == null) return null;

        var isPrivate = HubGroupVisibilityRules.IsPrivate(Visibility, group.IsPublic);
        if (!isPrivate || isSiteAdmin) return new HubGroupAccessDto(group.Id, isPrivate, CanView: true);
        if (userId is not int uid) return new HubGroupAccessDto(group.Id, isPrivate, CanView: false);

        // Та же аудитория, что у тренировок и members-медиа: управляющий ИЛИ активный участник.
        // Заявка (pending) доступа не даёт — иначе «вступление по заявке» пускало бы до решения.
        var canView = group.OwnerUserId == uid
            || await _rw.HubGroupAdmins.AnyAsync(a => a.HubGroupId == group.Id && a.UserId == uid)
            || await _rw.HubGroupUserMembers.AnyAsync(m => m.HubGroupId == group.Id && m.UserId == uid
                && m.Status == HubGroupUserMemberStatus.Active);

        return new HubGroupAccessDto(group.Id, isPrivate, canView);
    }

    public async Task<HubGroupDetailsDto?> GetMembersOnlyStubAsync(string slug)
    {
        // Только то, что нужно странице «вступите, чтобы увидеть»: кто это и как вступить.
        // Состав, результаты, медиа, расписание и описание — данные группы, их тут нет.
        return await _read.HubGroups.AsNoTracking()
            .Where(g => g.Slug == slug)
            .Select(g => new HubGroupDetailsDto
            {
                Id = g.Id,
                Slug = g.Slug,
                Name = g.Name,
                NameEn = g.NameEn,
                IconUrl = g.IconUrl,
                Country = g.Country != null ? g.Country.CountryCode : null,
                IsOfficial = g.IsOfficial,
                ClubName = g.Club != null ? g.Club.Name : null,
                // В приватную вступают только заявкой — кнопка должна говорить «Request to join».
                JoinPolicy = HubGroupJoinPolicy.Approval,
                IsPrivate = true,
                MembersOnly = true,
                ShowHeroImage = false,
                IsVirtual = false
            })
            .FirstOrDefaultAsync();
    }

    /// <summary>Общие агрегаты страницы: последние заплывы, рекорды группы и сезонный зачёт.</summary>
    private static async Task FillAggregatesAsync(SwimmDbContext db, HubGroupDetailsDto dto, List<int> swimmerIds)
    {
        // Сезон = израильский плавательный (1 сен – 31 авг); общий календарь — SeasonMath.
        var seasonStartYear = SeasonMath.StartYearOf(DateTime.Today);
        var seasonStart = SeasonMath.StartOf(seasonStartYear);
        dto.SeasonLabel = SeasonMath.Label(seasonStartYear);

        if (swimmerIds.Count == 0) return;

        // Двухшаговый fetch «последних заплывов» — намеренно. Одним запросом (фильтр по
        // пловцам + ORDER BY дата DESC + LIMIT + широкая проекция ToDto с джойнами)
        // планировщик на большой таблице попадает в LIMIT-ловушку: пятится по индексу даты
        // через ВСЮ таблицу в надежде быстро набрать 25 строк (16 с на 3 млн синтетики).
        // Узкий id-запрос покрывается index-only сканом IX_Results_SwimmerId_CompetitionDate_Id
        // (миграция AddResultsSwimmerRecentIndex), а полные DTO добираются по 25 конкретным id.
        var recentIds = await db.Results.AsNoTracking()
            .Where(r => swimmerIds.Contains(r.SwimmerId))
            .OrderByDescending(r => r.CompetitionDate)
            .ThenByDescending(r => r.Id)
            .Take(RecentResultsLimit)
            .Select(r => r.Id)
            .ToListAsync();

        dto.RecentResults = await db.Results.AsNoTracking()
            .Where(r => recentIds.Contains(r.Id))
            .OrderByDescending(r => r.CompetitionDate)
            .ThenByDescending(r => r.Id)
            .Select(ResultMapping.ToDto)
            .ToListAsync();

        if (recentIds.Count > 0)
            dto.LastStart = await BuildLastStartAsync(db, swimmerIds, recentIds[0]);

        // «Рекорды группы»: лучшее время по каждой оси стиль+дистанция+бассейн+пол.
        // Эстафеты и незачтённые времена (DSQ/DNS) не участвуют.
        dto.Bests = await db.Results.AsNoTracking()
            .Where(r => swimmerIds.Contains(r.SwimmerId)
                        && r.TimeMillisecond != null
                        && !r.TimeFail
                        && r.RelayId == null)
            .GroupBy(r => new { StyleName = r.Style.Name, r.Distance, r.Competition.PoolType, r.Gender })
            .Select(g => g
                .OrderBy(r => r.TimeMillisecond)
                .ThenBy(r => r.CompetitionDate)
                .Select(r => new HubGroupBestDto
                {
                    StyleName = g.Key.StyleName,
                    Distance = g.Key.Distance,
                    PoolType = g.Key.PoolType,
                    Gender = g.Key.Gender,
                    TimeOriginal = r.TimeOriginal,
                    SuspectReason = r.SuspectReason,
                    TimeMillisecond = r.TimeMillisecond,
                    SwimmerId = r.SwimmerId,
                    SwimmerName = (r.Swimmer.LastName + " " + r.Swimmer.FirstName).Trim(),
                    SwimmerNameEn = (r.Swimmer.LastNameEn + " " + r.Swimmer.FirstNameEn).Trim(),
                    CompetitionName = r.Competition.Name,
                    Date = r.Competition.Date,
                    Points = r.InternationalPoints
                })
                .First())
            .ToListAsync();

        dto.Bests = dto.Bests
            .OrderBy(b => b.StyleName)
            .ThenBy(b => b.Distance.Length)
            .ThenBy(b => b.Distance)
            .ThenBy(b => b.Gender)
            .ToList();

        await FillStandingsAsync(db, dto, swimmerIds, seasonStart);
    }

    /// <summary>
    /// «Последний старт» ростера целиком: турнир самого свежего заплыва ленты.
    ///
    /// Многодневка — несколько <c>Competitions</c> под одним <c>EventId</c>, и старт здесь весь
    /// турнир, а не день: иначе карточка показала бы треть чемпионата. Эстафеты входят по
    /// членству в <c>RelayMembers</c> — строка эстафеты принадлежит одной ноге, а плыла вся
    /// команда (docs/relays.md, «Золотое правило»).
    /// </summary>
    private static async Task<HubGroupLastStartDto?> BuildLastStartAsync(
        SwimmDbContext db, List<int> swimmerIds, long latestResultId)
    {
        var anchor = await db.Results.AsNoTracking()
            .Where(r => r.Id == latestResultId)
            .Select(r => new
            {
                r.CompetitionId,
                r.Competition.EventId,
                Name = r.Competition.Event != null ? r.Competition.Event.Name : r.Competition.Name,
            })
            .FirstOrDefaultAsync();
        if (anchor == null) return null;

        var dayIds = anchor.EventId is int eventId
            ? await db.Competitions.AsNoTracking().Where(c => c.EventId == eventId).Select(c => c.Id).ToListAsync()
            : [anchor.CompetitionId];

        var rows = await db.Results.AsNoTracking()
            .Where(r => dayIds.Contains(r.CompetitionId)
                        && (swimmerIds.Contains(r.SwimmerId)
                            || (r.RelayId != null && db.RelayMembers.Any(m =>
                                    m.RelayId == r.RelayId && swimmerIds.Contains(m.SwimmerId)))))
            .Select(r => new LastStartRow
            {
                Id = r.Id,
                CompetitionDate = r.CompetitionDate,
                Position = r.Position,
                TimeFail = r.TimeFail,
                HeatType = r.HeatType,
                Round = r.Round,
                IsAward = r.Competition.IsAward,
            })
            .ToListAsync();
        if (rows.Count == 0) return null;

        // Строки карточки: медали, затем остальные места по возрастанию, без места и снятые —
        // в конце. Одно место — свежий день выше. Раньше карточка брала пять последних по id,
        // и на чемпионате с шестью золотами показывала 12-е, 11-е и 10-е места.
        var topIds = rows
            .OrderBy(r => r.IsMedal ? 0 : r.Position is > 0 && !r.TimeFail ? 1 : 2)
            .ThenBy(r => r.Position ?? int.MaxValue)
            .ThenByDescending(r => r.CompetitionDate)
            .ThenByDescending(r => r.Id)
            .Take(LastStartRowsLimit)
            .Select(r => r.Id)
            .ToList();

        var dtos = await db.Results.AsNoTracking()
            .Where(r => topIds.Contains(r.Id))
            .Select(ResultMapping.ToDto)
            .ToListAsync();
        var order = topIds.Select((id, i) => (id, i)).ToDictionary(x => x.id, x => x.i);

        var medals = rows.Where(r => r.IsMedal).Select(r => r.Position).ToList();
        return new HubGroupLastStartDto
        {
            CompetitionId = anchor.CompetitionId,
            EventId = anchor.EventId,
            Name = anchor.Name,
            DateFrom = rows.Min(r => r.CompetitionDate).ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
            DateTo = rows.Max(r => r.CompetitionDate).ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
            Swims = rows.Count,
            Golds = medals.Count(p => p == 1),
            Silvers = medals.Count(p => p == 2),
            Bronzes = medals.Count(p => p == 3),
            Rows = dtos.OrderBy(d => order[d.Id]).ToList(),
        };
    }

    /// <summary>Проекция заплыва последнего старта (in-memory).</summary>
    private sealed class LastStartRow
    {
        public long Id { get; init; }
        public DateTime CompetitionDate { get; init; }
        public int? Position { get; init; }
        public bool TimeFail { get; init; }
        public string? HeatType { get; init; }
        public string? Round { get; init; }
        public bool IsAward { get; init; }

        /// <summary>
        /// Медаль — по единому правилу продукта, зеркало клиентского
        /// <c>HelperResults.isMedalPlace</c>: соревнование награждаемое, время зачтено, место
        /// не из предварительного или дополнительного заплыва (Р34) и не из общей секции
        /// «כללי» (Р43). Само место правило не трогает — его показываем как в протоколе.
        /// </summary>
        public bool IsMedal =>
            IsAward && !TimeFail && HeatTypes.GivesOfficialPlace(HeatType)
            && Round != ResultRounds.FinalOpen && Position is >= 1 and <= 3;
    }

    /// <summary>Сезонный зачёт: очки за место по правилам PointRuleClubs + медали за сезон.</summary>
    private static async Task FillStandingsAsync(
        SwimmDbContext db, HubGroupDetailsDto dto, List<int> swimmerIds, DateTime seasonStart)
    {
        // Сезонные заплывы участников (без эстафет) — минимальная проекция для зачёта.
        var season = await db.Results.AsNoTracking()
            .Where(r => swimmerIds.Contains(r.SwimmerId)
                        && r.RelayId == null
                        && r.CompetitionDate >= seasonStart)
            .Select(r => new SeasonResultRow
            {
                SwimmerId = r.SwimmerId,
                // Место prelim-заплыва — ранжир сессии, не награда; общий финал «כללי»
                // (Round=final-open) тоже не зачётный: у организатора единица зачёта —
                // возрастная ступень, там пловец очки и получает (Р34, Р43).
                Position = r.HeatType == "prelim" || r.HeatType == "extra" || r.Round == ResultRounds.FinalOpen
                    ? null : r.Position,
                TimeFail = r.TimeFail,
                InternationalPoints = r.InternationalPoints,
                CompetitionDate = r.CompetitionDate,
                IsMasters = r.Competition.IsMasters,
                RuleId = r.Competition.PointRuleClubsId
            })
            .ToListAsync();

        // Правила очков грузим целиком (их единицы) и применяем в памяти.
        var rules = await db.PointRulesClubs.AsNoTracking()
            .Include(r => r.Entries)
            .ToListAsync();

        var bySwimmer = season.GroupBy(r => r.SwimmerId).ToDictionary(g => g.Key, g => g.ToList());

        // Участники без заплывов за сезон (в т.ч. тренер) — в зачёте с нулями.
        dto.Standings = dto.Members
            .Select(m =>
            {
                bySwimmer.TryGetValue(m.SwimmerId, out var rows);
                rows ??= [];
                return new HubGroupStandingDto
                {
                    SwimmerId = m.SwimmerId,
                    Name = m.Name,
                    NameEn = m.NameEn,
                    Role = m.Role,
                    Swims = rows.Count,
                    // Медали — только по зачтённым заплывам: DSQ/незачтённое время (TimeFail)
                    // не медаль (согласовано с очками, которые тоже исключают TimeFail).
                    Golds = rows.Count(r => !r.TimeFail && r.Position == 1),
                    Silvers = rows.Count(r => !r.TimeFail && r.Position == 2),
                    Bronzes = rows.Count(r => !r.TimeFail && r.Position == 3),
                    // Правило: привязка соревнования важнее подбора по дате (CompetitionRuleResolver).
                    ClubPoints = rows.Sum(r =>
                        PointRulesClubsScoring.PointsFor(
                            CompetitionRuleResolver.Resolve(
                                rules, r.RuleId, r.IsMasters, DateOnly.FromDateTime(r.CompetitionDate)),
                            r.Position, r.TimeFail)),
                    BestFina = rows.Count > 0 ? rows.Max(r => r.InternationalPoints) : 0
                };
            })
            .OrderByDescending(s => s.ClubPoints)
            .ThenByDescending(s => s.Swims)
            .ToList();
    }

    /// <summary>Проекция сезонного результата для зачёта (in-memory).</summary>
    private sealed class SeasonResultRow
    {
        public int SwimmerId { get; init; }
        public int? Position { get; init; }
        public bool TimeFail { get; init; }
        public int InternationalPoints { get; init; }
        public DateTime CompetitionDate { get; init; }
        public bool IsMasters { get; init; }
        /// <summary>Правило клубных очков, привязанное к соревнованию; null — подбор по дате.</summary>
        public int? RuleId { get; init; }
    }

    private static List<HubGroupPublicLinkDto> ParseLinks(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            // В БД ссылки лежат в формате админского HubGroupLinkDto (PascalCase-ключи) —
            // десериализуем его же, наружу отдаём snake_case-вариант.
            var stored = System.Text.Json.JsonSerializer.Deserialize<List<HubGroupLinkDto>>(json) ?? [];
            return stored.Select(l => new HubGroupPublicLinkDto { Kind = l.Kind, Url = l.Url }).ToList();
        }
        catch (System.Text.Json.JsonException)
        {
            return [];
        }
    }
}
