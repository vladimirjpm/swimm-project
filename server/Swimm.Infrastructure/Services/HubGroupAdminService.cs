using Microsoft.EntityFrameworkCore;
using Swimm.Application.Abstractions;
using Swimm.Application.Constants;
using Swimm.Application.Dtos;
using Swimm.Application.Mapping;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Services;

/// <summary>
/// Админский CRUD групп (см. <see cref="IHubGroupAdminService"/>). Пишет через owner-контекст
/// <see cref="SwimmDbContext"/>. После мутаций инвалидирует общий кэш — публичные
/// /api/hub-groups* (фаза 3) кэшируются через ICacheService, как остальные публичные GET.
/// Write-логика (slug/валидация/PostgresException/участники) — общая с пользовательским
/// CRUD (8.6) в <see cref="HubGroupCrudCore"/>.
/// </summary>
public class HubGroupAdminService : IHubGroupAdminService
{
    private readonly SwimmDbContext _db;
    private readonly HubGroupCrudCore _core;
    private readonly IAdminAuditService _audit;
    private readonly ISettingsService _settings;

    public HubGroupAdminService(SwimmDbContext db, HubGroupCrudCore core, IAdminAuditService audit, ISettingsService settings)
    {
        _db = db;
        _core = core;
        _audit = audit;
        _settings = settings;
    }

    public async Task<IReadOnlyList<HubGroupAdminRowDto>> GetAllAsync()
    {
        var rows = await _db.HubGroups.AsNoTracking()
            .OrderByDescending(g => g.UpdatedAt)
            .Select(g => new HubGroupAdminRowDto
            {
                Id = g.Id,
                Name = g.Name,
                Slug = g.Slug,
                IconUrl = g.IconUrl,
                ClubName = g.Club != null ? g.Club.Name : null,
                // Видимый состав: скрытые клубные пловцы — служебные строки пересборки.
                MemberCount = g.Members.Count(m => !m.IsExcluded),
                IsPublic = g.IsPublic,
                IsOfficial = g.IsOfficial,
                IsTest = g.IsTest,
                IsTrusted = g.IsTrusted,
                UpdatedAt = g.UpdatedAt,
                OwnerUserId = g.OwnerUserId
            })
            .ToListAsync();

        await HubGroupCatalog.FillCatalogStatusAsync(_db, rows);
        return rows;
    }

    public async Task<HubGroupEditDto?> GetByIdAsync(int id)
    {
        var g = await _db.HubGroups.AsNoTracking()
            .Include(x => x.Owner)
            .Include(x => x.Country)
            .FirstOrDefaultAsync(x => x.Id == id);
        if (g == null) return null;

        var members = await _db.HubGroupMembers.AsNoTracking()
            .Where(m => m.HubGroupId == id)
            .Include(m => m.Swimmer).ThenInclude(s => s!.Club)
            .OrderBy(m => m.SortOrder)
            .Select(m => new HubGroupMemberRowDto
            {
                Id = m.Id,
                SwimmerId = m.SwimmerId,
                SwimmerName = (m.Swimmer!.LastName + " " + m.Swimmer.FirstName).Trim(),
                SwimmerNameEn = (m.Swimmer.LastNameEn + " " + m.Swimmer.FirstNameEn).Trim(),
                BirthYear = m.Swimmer.BirthYear,
                ClubName = m.Swimmer.Club != null ? m.Swimmer.Club.Name : null,
                Role = m.Role,
                SortOrder = m.SortOrder,
                // Панель управления получает и скрытых — там их возвращают.
                Source = m.Source,
                IsExcluded = m.IsExcluded,
                IsPrivate = m.Swimmer.PrivateHubGroupId == id
            })
            .ToListAsync();

        var clubSubscription = await _db.HubGroupClubSubscriptions.AsNoTracking()
            .Where(s => s.HubGroupId == id)
            .Select(HubGroupClubSubscriptionService.ToDto)
            .FirstOrDefaultAsync();

        var userMembers = await _db.HubGroupUserMembers.AsNoTracking()
            .Where(m => m.HubGroupId == id)
            .Include(m => m.User)
            .Include(m => m.Swimmer)
            .OrderBy(m => m.JoinedAt)
            .Select(m => new HubGroupUserMemberRowDto
            {
                UserId = m.UserId,
                DisplayName = m.User!.DisplayName,
                Email = m.User.Email,
                Status = m.Status,
                SelfJoined = m.AddedByUserId == null,
                JoinedAt = m.JoinedAt,
                SwimmerId = m.SwimmerId,
                SwimmerName = m.Swimmer != null ? (m.Swimmer.LastName + " " + m.Swimmer.FirstName) : null,
                Note = m.Note
            })
            .ToListAsync();

        return new HubGroupEditDto
        {
            Id = g.Id,
            Name = g.Name,
            NameEn = g.NameEn,
            Slug = g.Slug,
            Description = g.Description,
            IconUrl = g.IconUrl,
            CoverImageUrl = g.CoverImageUrl,
            Location = g.Location,
            Country = g.Country?.CountryCode,
            ClubId = g.ClubId,
            OwnerUserId = g.OwnerUserId,
            OwnerDisplayName = g.Owner?.DisplayName ?? $"#{g.OwnerUserId}",
            IsPublic = g.IsPublic,
            IsOfficial = g.IsOfficial,
            IsTest = g.IsTest,
            IsTrusted = g.IsTrusted,
            JoinPolicy = g.JoinPolicy,
            Links = HubGroupCrudCore.ParseLinks(g.Links),
            Members = members,
            UserMembers = userMembers,
            ClubSubscription = clubSubscription
        };
    }

    public async Task<IReadOnlyList<ClubOptionDto>> GetClubOptionsAsync()
    {
        return await _db.Clubs.AsNoTracking()
            .Where(c => c.MergedIntoId == null)   // склеенные в выпадашку не предлагаем
            .OrderBy(c => c.Name)
            .Select(c => new ClubOptionDto { Id = c.Id, Name = c.Name })
            .ToListAsync();
    }

    public async Task<HubGroupSaveResult> CreateAsync(HubGroupInputDto input, int ownerUserId)
    {
        var resolvedOwnerId = await ResolveOwnerIdAsync(ownerUserId);
        if (resolvedOwnerId == null)
            return HubGroupSaveResult.Fail("Не найден пользователь с ролью Admin для назначения владельцем группы.");

        var slug = await _core.ResolveSlugAsync(input, excludeId: null);
        var error = await _core.ValidateAsync(input, slug, excludeId: null);
        if (error != null) return HubGroupSaveResult.Fail(error);

        var group = new HubGroup { OwnerUserId = resolvedOwnerId.Value, IsTest = input.IsTest, IsTrusted = input.IsTrusted };
        HubGroupCrudCore.Apply(group, input, slug);
        await _core.ApplyCountryAsync(group, input.Country);
        _db.HubGroups.Add(group);
        var created = await _core.SaveAsync(group);
        if (created.Success && group.IsTrusted) await LogTrustAsync(group);
        return created;
    }

    public async Task<HubGroupSaveResult> UpdateAsync(int id, HubGroupInputDto input)
    {
        var group = await _db.HubGroups.FindAsync(id);
        if (group == null) return HubGroupSaveResult.Fail($"Группа #{id} не найдена");

        var slug = await _core.ResolveSlugAsync(input, excludeId: id);
        var error = await _core.ValidateAsync(input, slug, excludeId: id);
        if (error != null) return HubGroupSaveResult.Fail(error);
        // Иначе упрёмся в CK_HubGroups_TestNotOfficial исключением на сохранении.
        if (input.IsTest && group.IsOfficial)
            return HubGroupSaveResult.Fail("Официальную группу нельзя сделать тестовой — сначала снимите официальный статус.");

        HubGroupCrudCore.Apply(group, input, slug);
        group.IsTest = input.IsTest;
        var trustChanged = group.IsTrusted != input.IsTrusted;
        group.IsTrusted = input.IsTrusted;
        await _core.ApplyCountryAsync(group, input.Country);
        var saved = await _core.SaveAsync(group);
        if (saved.Success && trustChanged) await LogTrustAsync(group);
        return saved;
    }

    /// <summary>
    /// Доверие выдаётся источнику один раз (И15) — поэтому «кто и когда выдал/снял» обязано
    /// остаться: от флага зависит, что группа выводит на чужие карточки пловцов.
    /// </summary>
    private Task LogTrustAsync(HubGroup group) =>
        _audit.LogAsync(group.IsTrusted ? "hubgroup.trust" : "hubgroup.untrust", "HubGroup", group.Id.ToString(),
            group.IsTrusted
                ? $"Группе «{group.Name}» выдан флаг Trusted: её public-медиа видны всем в протоколе и на карточке пловца"
                : $"С группы «{group.Name}» снят флаг Trusted: её public-медиа вне страницы группы видят только участники");

    public async Task<HubGroupSaveResult> DeleteAsync(int id)
    {
        // Перечень потерь снимаем ДО удаления: после каскада считать уже нечего.
        var impact = await GetDeleteImpactAsync(id);
        var group = await _db.HubGroups.FindAsync(id);
        if (group == null || impact == null) return HubGroupSaveResult.Fail($"Группа #{id} не найдена");

        // Сессии тренировок — явно и РАНЬШЕ группы. Каскад от группы унёс бы их и так, но пловцы
        // группы (Р71) уходят тем же каскадом, а Sys_TrainingResults держит пловца RESTRICT: в
        // одном DELETE порядок каскадов не гарантирован, и удаление падало. EF удаляет сессии
        // (их результаты — каскадом в БД) отдельной командой до группы.
        _db.TrainingSessions.RemoveRange(await _db.TrainingSessions.Where(s => s.HubGroupId == id).ToListAsync());
        _db.HubGroups.Remove(group);
        await _db.SaveChangesAsync();

        // Аудит здесь, а не в вызывающих: путей удаления три, и группа уходит необратимо —
        // «кто и когда удалил, что пропало» должно остаться при любом из них. Актор — из
        // HTTP-контекста: владелец из панели «My groups» или админ сайта.
        await _audit.LogAsync("hubgroup.delete", "HubGroup", id.ToString(), DeleteAuditSummary(impact), impact);
        return HubGroupSaveResult.Ok(id);
    }

    public async Task<HubGroupDeleteImpactDto?> GetDeleteImpactAsync(int id)
    {
        // «Действует» — как HubGroupBreakRules.IsActive: день по Израилю, не по UTC.
        var today = DateOnly.FromDateTime(IsraelTime.ToLocal(DateTime.UtcNow));
        return await _db.HubGroups.AsNoTracking()
            .Where(g => g.Id == id)
            .Select(g => new HubGroupDeleteImpactDto
            {
                Id = g.Id,
                Name = g.Name,
                NameEn = g.NameEn,
                IsOfficial = g.IsOfficial,
                ClubName = g.Club != null ? g.Club.Name : null,
                Swimmers = g.Members.Count(m => !m.IsExcluded),
                AccountMembers = g.UserMembers.Count,
                Admins = g.Admins.Count,
                TrainingSessions = _db.TrainingSessions.Count(s => s.HubGroupId == g.Id),
                TrainingResults = _db.TrainingResults.Count(r => r.Session!.HubGroupId == g.Id),
                Media = _db.HubGroupMedia.Count(m => m.HubGroupId == g.Id),
                MediaPublications = _db.UserMediaPublications.Count(p => p.HubGroupId == g.Id),
                LeveledSwimmers = _db.HubGroupSwimmerLevels.Count(l => l.HubGroupId == g.Id),
                LanePlans = _db.LanePlans.Count(p => p.HubGroupId == g.Id),
                TrainingRsvps = _db.HubGroupTrainingRsvps.Count(r => r.HubGroupId == g.Id),
                LeveledAccounts = _db.HubGroupAccountLevels.Count(l => l.HubGroupId == g.Id),
                PrivateSwimmers = _db.Swimmers.Count(s => s.PrivateHubGroupId == g.Id),
                ActiveBreaks = _db.HubGroupBreaks.Count(b =>
                    b.HubGroupId == g.Id && b.EndedAt == null && (b.Until == null || b.Until >= today)),
                HasPendingClubRequest = _db.HubGroupClubRequests.Any(r =>
                    r.HubGroupId == g.Id && r.Status == HubGroupClubRequestStatus.Pending)
            })
            .FirstOrDefaultAsync();
    }

    /// <summary>Строка аудита: имя и только ненулевые потери — читать глазами в /Admin/Audit.</summary>
    private static string DeleteAuditSummary(HubGroupDeleteImpactDto i)
    {
        var parts = new List<string>();
        if (i.Swimmers > 0) parts.Add($"пловцов в составе {i.Swimmers}");
        if (i.PrivateSwimmers > 0) parts.Add($"из них пловцов группы (удалены насовсем) {i.PrivateSwimmers}");
        if (i.AccountMembers > 0) parts.Add($"аккаунтов {i.AccountMembers}");
        if (i.Admins > 0) parts.Add($"админов группы {i.Admins}");
        if (i.TrainingSessions > 0) parts.Add($"тренировок {i.TrainingSessions} (результатов {i.TrainingResults})");
        if (i.Media > 0) parts.Add($"медиа {i.Media}");
        if (i.MediaPublications > 0) parts.Add($"публикаций медиа {i.MediaPublications}");
        if (i.LeveledSwimmers > 0) parts.Add($"уровней пловцов {i.LeveledSwimmers}");
        if (i.LeveledAccounts > 0) parts.Add($"уровней аккаунтов {i.LeveledAccounts}");
        if (i.LanePlans > 0) parts.Add($"планов дорожек {i.LanePlans}");
        if (i.TrainingRsvps > 0) parts.Add($"ответов на тренировки {i.TrainingRsvps}");
        if (i.ActiveBreaks > 0) parts.Add($"действующих перерывов {i.ActiveBreaks}");
        if (i.IsOfficial) parts.Add($"официальная группа клуба «{i.ClubName}»");
        if (i.HasPendingClubRequest) parts.Add("заявка на официальный статус");

        var lost = parts.Count > 0 ? string.Join(", ", parts) : "пустая";
        return $"Удалена группа «{i.Name}» (#{i.Id}): {lost}";
    }

    public async Task<IReadOnlyList<SwimmerSearchResultDto>> SearchSwimmersAsync(string query)
    {
        query = (query ?? "").Trim();
        if (query.Length == 0) return [];

        // Разбиваем на слова: запрос может быть "фамилия имя" целиком, а совпадение нужно
        // искать по обоим полям вместе (каждое слово запроса — в любом из четырёх полей имени).
        var words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length == 0) return [];

        // Пловец чужой группы (Р71) в другую группу не добавляется — и в поиске его нет. Своих
        // пловцов группы тренер видит в составе, искать их не нужно.
        var swimmersQuery = _db.Swimmers.AsNoTracking().Include(s => s.Club)
            .Where(s => s.PrivateHubGroupId == null);

        foreach (var word in words)
        {
            var pattern = $"%{word}%";
            swimmersQuery = swimmersQuery.Where(s =>
                EF.Functions.ILike(s.LastName, pattern) ||
                EF.Functions.ILike(s.FirstName, pattern) ||
                EF.Functions.ILike(s.LastNameEn, pattern) ||
                EF.Functions.ILike(s.FirstNameEn, pattern));
        }

        return await swimmersQuery
            .Where(s =>
                // Эстафетные заплывы иногда попадали в парсер как "спортсмен" с составным
                // именем — список через запятую (баг парсинга relay-строк). Это не реальные
                // пловцы, добавлять их в группу нельзя — отсекаем по запятой в имени/фамилии.
                !EF.Functions.ILike(s.LastName, "%,%") && !EF.Functions.ILike(s.FirstName, "%,%"))
            .OrderBy(s => s.LastName)
            .Take(20)
            .Select(s => new SwimmerSearchResultDto
            {
                Id = s.Id,
                Name = (s.LastName + " " + s.FirstName).Trim(),
                NameEn = (s.LastNameEn + " " + s.FirstNameEn).Trim(),
                BirthYear = s.BirthYear,
                ClubName = s.Club != null ? s.Club.Name : null
            })
            .ToListAsync();
    }

    public async Task<IReadOnlyList<SwimmerSearchResultDto>> GetClubSwimmersAsync(int clubId)
    {
        return await _db.Swimmers.AsNoTracking()
            .Where(s => s.ClubId == clubId && s.PrivateHubGroupId == null)
            .Include(s => s.Club)
            .OrderBy(s => s.LastName)
            .Take(200)
            .Select(s => new SwimmerSearchResultDto
            {
                Id = s.Id,
                Name = (s.LastName + " " + s.FirstName).Trim(),
                NameEn = (s.LastNameEn + " " + s.FirstNameEn).Trim(),
                BirthYear = s.BirthYear,
                ClubName = s.Club != null ? s.Club.Name : null
            })
            .ToListAsync();
    }

    public Task<HubGroupMemberSaveResult> AddMemberAsync(int hubGroupId, int swimmerId, string role) =>
        _core.AddMemberAsync(hubGroupId, swimmerId, role,
            maxManual: HubGroupQuotaRules.Limit(_settings, HubGroupQuotaRules.MaxManualSwimmersKey));

    public Task<PrivateSwimmerSaveResult> AddPrivateSwimmerAsync(int hubGroupId, AddPrivateSwimmerRequest input) =>
        _core.AddPrivateSwimmerAsync(hubGroupId, input.FirstName, input.LastName, input.Gender, input.BirthYear,
            maxManual: HubGroupQuotaRules.Limit(_settings, HubGroupQuotaRules.MaxManualSwimmersKey));

    public Task<HubGroupMemberSaveResult> UpdateMemberAsync(int hubGroupId, int memberId, string role, int sortOrder) =>
        _core.UpdateMemberAsync(hubGroupId, memberId, role, sortOrder);

    public Task<HubGroupMemberSaveResult> RemoveMemberAsync(int hubGroupId, int memberId) =>
        _core.RemoveMemberAsync(hubGroupId, memberId);

    /// <summary>
    /// DevAdminBypass (Security/DevAdminBypass.cs) на пустой БД падает в фоллбек —
    /// синтетический NameIdentifier="0", которого нет в Sys_AppUsers, и прямая вставка такого
    /// OwnerUserId падает на FK. В этом случае (и вообще если запрошенный id не существует) подставляем
    /// первого пользователя с ролью Admin. Дев-костыль, специфичный для админки — в
    /// пользовательском CRUD (8.6) не переиспользуется: там OwnerUserId — реальный вошедший.
    /// </summary>
    private async Task<int?> ResolveOwnerIdAsync(int requestedUserId)
    {
        if (requestedUserId > 0 && await _db.AppUsers.AnyAsync(u => u.Id == requestedUserId))
            return requestedUserId;

        return await _db.AppUserRoles
            .Where(ur => ur.Role.Name == "Admin")
            .OrderBy(ur => ur.UserId)
            .Select(ur => (int?)ur.UserId)
            .FirstOrDefaultAsync();
    }
}
