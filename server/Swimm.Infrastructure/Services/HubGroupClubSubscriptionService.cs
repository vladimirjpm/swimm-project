using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Application.Mapping;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Services;

/// <inheritdoc cref="IHubGroupClubSubscriptionService"/>
/// <remarks>
/// Состав из подписки МАТЕРИАЛИЗУЕТСЯ в <c>HubGroupMembers</c> (Source = club), а не собирается
/// на лету: иначе пришлось бы переписать всех читателей состава (страница, зачёт, ростер
/// соревнований, медиа) — план §2. Цена — пересборка по событиям: подписка/отписка, импорт
/// (JsonImportService, по клубам импорта), склейка клубов, CLI <c>--hubgroup-club-sync</c>.
///
/// Каждая пересборка группы — «прочитать → сравнить → записать» в транзакции под
/// advisory-lock группы: без неё импорт и клик «подписаться» в одну секунду вставили бы
/// одного пловца дважды и упали бы на уникальном (HubGroupId, SwimmerId).
/// </remarks>
public class HubGroupClubSubscriptionService : IHubGroupClubSubscriptionService
{
    private readonly SwimmDbContext _db;
    private readonly HubGroupCrudCore _core;

    public HubGroupClubSubscriptionService(SwimmDbContext db, HubGroupCrudCore core)
    {
        _db = db;
        _core = core;
    }

    /// <summary>
    /// Первый ключ транзакционной advisory-блокировки «состав группы» (второй — id группы).
    /// Произвольная константа, лишь бы не совпала с другими блокировками в БД.
    /// </summary>
    private const int SyncLockClass = 0x48474353; // "HGCS" — hub group club sync

    /// <summary>
    /// Проекция подписки в DTO — одна на этот сервис и форму группы (HubGroupAdminService):
    /// в ней правило имени клуба (иврит по умолчанию, латиница фоллбеком).
    /// </summary>
    internal static readonly Expression<Func<HubGroupClubSubscription, HubGroupClubSubscriptionDto>> ToDto =
        s => new HubGroupClubSubscriptionDto
        {
            ClubId = s.ClubId,
            ClubName = s.Club!.Name.Length > 0 ? s.Club.Name : s.Club.NameEn,
            ClubNameEn = s.Club.NameEn.Length > 0 ? s.Club.NameEn : null,
            CreatedAt = s.CreatedAt
        };

    public async Task<HubGroupClubSubscriptionDto?> GetAsync(int hubGroupId)
    {
        return await _db.HubGroupClubSubscriptions.AsNoTracking()
            .Where(s => s.HubGroupId == hubGroupId)
            .Select(ToDto)
            .FirstOrDefaultAsync();
    }

    public async Task<HubGroupClubSubscriptionPreviewDto?> PreviewAsync(int hubGroupId, int clubId)
    {
        var requested = await _db.Clubs.AsNoTracking()
            .Where(c => c.Id == clubId)
            .Select(c => new { c.Id, c.MergedIntoId })
            .FirstOrDefaultAsync();
        if (requested == null) return null;

        // Тот же канон, что при подписке: предпросмотр обязан показать ровно то, что случится.
        var targetClubId = requested.MergedIntoId ?? requested.Id;
        var club = await _db.Clubs.AsNoTracking()
            .Where(c => c.Id == targetClubId)
            .Select(c => new { c.Name, c.NameEn })
            .FirstAsync();

        var swimmerCount = await HubGroupClubRoster
            .SwimmerIds(_db, targetClubId, HubGroupClubRules.ActivitySince(DateTime.UtcNow))
            .CountAsync();

        var official = await _db.HubGroups.AsNoTracking()
            .Where(g => g.IsOfficial && g.ClubId == targetClubId)
            .Select(g => new HubGroupRefDto
            {
                Id = g.Id, Slug = g.Slug, Name = g.Name, MemberCount = g.Members.Count(m => !m.IsExcluded)
            })
            .FirstOrDefaultAsync();

        var following = await _db.HubGroups.AsNoTracking()
            .Where(g => g.Id != hubGroupId && g.ClubSubscriptions.Any(s => s.ClubId == targetClubId))
            .OrderByDescending(g => g.Members.Count(m => !m.IsExcluded))
            .Select(g => new HubGroupRefDto
            {
                Id = g.Id, Slug = g.Slug, Name = g.Name, MemberCount = g.Members.Count(m => !m.IsExcluded)
            })
            .ToListAsync();

        var isOwnOfficial = official?.Id == hubGroupId;
        var otherOfficial = isOwnOfficial ? null : official;

        // Звать — в официальную, если она есть (это «лицо клуба»), иначе в самую населённую из
        // подписанных. Самой официальной группе звать некуда.
        var hintGroup = isOwnOfficial ? null : otherOfficial ?? following.FirstOrDefault();

        return new HubGroupClubSubscriptionPreviewDto
        {
            ClubId = targetClubId,
            ClubName = club.Name.Length > 0 ? club.Name : club.NameEn,
            ClubNameEn = club.NameEn.Length > 0 ? club.NameEn : null,
            SwimmerCount = swimmerCount,
            IsOwnOfficialClub = isOwnOfficial,
            OfficialGroup = otherOfficial,
            FollowingGroups = following,
            Warning = isOwnOfficial ? null : HubGroupClubRules.SubscribeWarning(otherOfficial?.Name),
            Hint = hintGroup == null ? null : HubGroupClubRules.JoinInsteadHint(hintGroup.Name, hintGroup == otherOfficial),
            HintGroup = hintGroup
        };
    }

    public async Task<HubGroupClubSubscribeResult> SubscribeAsync(int hubGroupId, int clubId, int? userId)
    {
        if (!await _db.HubGroups.AnyAsync(g => g.Id == hubGroupId))
            return HubGroupClubSubscribeResult.Fail("Group not found.");

        var club = await _db.Clubs.AsNoTracking()
            .Where(c => c.Id == clubId)
            .Select(c => new { c.Id, c.MergedIntoId })
            .FirstOrDefaultAsync();
        if (club == null) return HubGroupClubSubscribeResult.Fail("Club not found.");

        // Склеенный клуб пуст — его результаты уже у канонического (ClubMergeService). Цепочек
        // склеек не бывает (Guard 0 там же), поэтому одного шага достаточно.
        var targetClubId = club.MergedIntoId ?? club.Id;

        var sync = await InGroupTransactionAsync(hubGroupId, async () =>
        {
            var subscription = await _db.HubGroupClubSubscriptions
                .FirstOrDefaultAsync(s => s.HubGroupId == hubGroupId);

            if (subscription == null)
            {
                _db.HubGroupClubSubscriptions.Add(new HubGroupClubSubscription
                {
                    HubGroupId = hubGroupId,
                    ClubId = targetClubId,
                    CreatedByUserId = userId
                });
            }
            else if (subscription.ClubId != targetClubId)
            {
                // Одна подписка на группу: другой клуб ЗАМЕНЯЕТ прежний. Строки прежнего
                // клуба уйдут в пересборке ниже — они вне нового набора.
                subscription.ClubId = targetClubId;
                subscription.CreatedAt = DateTime.UtcNow;
                subscription.CreatedByUserId = userId;
            }

            await _db.SaveChangesAsync();
            return await SyncLockedAsync(hubGroupId, targetClubId);
        });

        await _core.InvalidateCacheAsync();
        return HubGroupClubSubscribeResult.Ok((await GetAsync(hubGroupId))!, sync);
    }

    public async Task<HubGroupClubSyncResult?> UnsubscribeAsync(int hubGroupId)
    {
        var result = await InGroupTransactionAsync(hubGroupId, async () =>
        {
            var subscriptions = await _db.HubGroupClubSubscriptions
                .Where(s => s.HubGroupId == hubGroupId)
                .ToListAsync();
            if (subscriptions.Count == 0) return null;

            _db.HubGroupClubSubscriptions.RemoveRange(subscriptions);
            await _db.SaveChangesAsync();

            // Пустой набор: уходят все клубные строки, и скрытые тоже; ручные остаются.
            return await SyncLockedAsync(hubGroupId, clubId: null);
        });

        if (result != null) await _core.InvalidateCacheAsync();
        return result;
    }

    public async Task<HubGroupClubSyncResult> SyncGroupAsync(int hubGroupId)
    {
        var result = await SyncOneAsync(hubGroupId);
        if (result.Added + result.Removed > 0) await _core.InvalidateCacheAsync();
        return result;
    }

    public async Task<HubGroupClubSyncResult> SyncClubsAsync(IReadOnlyCollection<int> clubIds)
    {
        if (clubIds.Count == 0) return HubGroupClubSyncResult.Empty;

        var groupIds = await _db.HubGroupClubSubscriptions.AsNoTracking()
            .Where(s => clubIds.Contains(s.ClubId))
            .Select(s => s.HubGroupId)
            .Distinct()
            .ToListAsync();

        return await SyncManyAsync(groupIds);
    }

    public async Task<HubGroupClubSyncResult> SyncAllAsync()
    {
        var groupIds = await _db.HubGroupClubSubscriptions.AsNoTracking()
            .Select(s => s.HubGroupId)
            .Distinct()
            .ToListAsync();

        return await SyncManyAsync(groupIds);
    }

    public async Task<HubGroupMemberSaveResult> SetExcludedAsync(int hubGroupId, int memberId, bool excluded)
    {
        var member = await _db.HubGroupMembers.FindAsync(memberId);
        if (member == null || member.HubGroupId != hubGroupId)
            return HubGroupMemberSaveResult.Fail("Member not found.");

        // Ручного не прячут — его удаляют: «ручной и скрытый» запрещён и в БД
        // (CK_HubGroupMembers_ExcludedOnlyClub).
        if (member.Source != HubGroupMemberSource.Club)
            return HubGroupMemberSaveResult.Fail("Only swimmers from the club subscription can be hidden — remove this swimmer instead.");

        if (member.IsExcluded == excluded) return HubGroupMemberSaveResult.Ok();

        member.IsExcluded = excluded;
        await _db.SaveChangesAsync();
        await _core.TouchGroupAsync(hubGroupId); // UpdatedAt + сброс кэша
        return HubGroupMemberSaveResult.Ok();
    }

    // ── Пересборка ──────────────────────────────────────────────────────────

    private async Task<HubGroupClubSyncResult> SyncManyAsync(IReadOnlyList<int> groupIds)
    {
        var total = HubGroupClubSyncResult.Empty;
        foreach (var groupId in groupIds)
            total = total.Plus(await SyncOneAsync(groupId));

        // Кэш — один раз на весь проход, и только если состав где-то сдвинулся.
        if (total.Added + total.Removed > 0) await _core.InvalidateCacheAsync();
        return total;
    }

    /// <summary>Пересборка одной группы по её ТЕКУЩЕЙ подписке (без сброса кэша).</summary>
    private Task<HubGroupClubSyncResult> SyncOneAsync(int hubGroupId) =>
        InGroupTransactionAsync(hubGroupId, async () =>
        {
            // Подписку читаем уже под блокировкой: параллельная отписка/смена клуба не
            // проскочит между чтением и записью.
            var clubId = await _db.HubGroupClubSubscriptions.AsNoTracking()
                .Where(s => s.HubGroupId == hubGroupId)
                .Select(s => (int?)s.ClubId)
                .FirstOrDefaultAsync();

            return await SyncLockedAsync(hubGroupId, clubId);
        });

    /// <summary>
    /// Привести состав к набору пловцов клуба. Вызывать ТОЛЬКО под блокировкой группы
    /// (<see cref="InGroupTransactionAsync{T}"/>). <paramref name="clubId"/> null — подписки
    /// нет, клубные строки уходят все.
    /// </summary>
    private async Task<HubGroupClubSyncResult> SyncLockedAsync(int hubGroupId, int? clubId)
    {
        var target = clubId == null
            ? []
            : await HubGroupClubRoster
                .SwimmerIds(_db, clubId.Value, HubGroupClubRules.ActivitySince(DateTime.UtcNow))
                .ToListAsync();

        var rows = await _db.HubGroupMembers
            .Where(m => m.HubGroupId == hubGroupId)
            .ToListAsync();

        var plan = HubGroupClubRules.PlanSync(
            rows.Select(r => new HubGroupClubRules.MemberRow(r.Id, r.SwimmerId, r.Source, r.IsExcluded)),
            target);
        if (plan.IsEmpty) return new HubGroupClubSyncResult(1, 0, 0);

        var toDelete = plan.DeleteRowIds.ToHashSet();
        _db.HubGroupMembers.RemoveRange(rows.Where(r => toDelete.Contains(r.Id)));

        if (plan.InsertSwimmerIds.Count > 0)
        {
            // Новые — в конец состава, по алфавиту: ростер из 160 человек в порядке id
            // читать невозможно. Имя — иврит по умолчанию, латиница фоллбеком (правило имён).
            var swimmers = await _db.Swimmers.AsNoTracking()
                .Where(s => plan.InsertSwimmerIds.Contains(s.Id))
                .Select(s => new
                {
                    s.Id,
                    Last = s.LastName.Length > 0 ? s.LastName : s.LastNameEn,
                    First = s.FirstName.Length > 0 ? s.FirstName : s.FirstNameEn
                })
                .ToListAsync();

            var sortOrder = rows.Where(r => !toDelete.Contains(r.Id))
                .Select(r => r.SortOrder)
                .DefaultIfEmpty(0)
                .Max();
            var now = DateTime.UtcNow;

            foreach (var s in swimmers.OrderBy(s => s.Last, StringComparer.Ordinal).ThenBy(s => s.First, StringComparer.Ordinal))
            {
                _db.HubGroupMembers.Add(new HubGroupMember
                {
                    HubGroupId = hubGroupId,
                    SwimmerId = s.Id,
                    Role = "member",
                    SortOrder = ++sortOrder,
                    JoinedAt = now,
                    Source = HubGroupMemberSource.Club
                });
            }
        }

        var group = await _db.HubGroups.FindAsync(hubGroupId);
        if (group != null) group.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return new HubGroupClubSyncResult(1, plan.InsertSwimmerIds.Count, toDelete.Count);
    }

    /// <summary>
    /// Работа над составом группы в транзакции под её advisory-lock. Если транзакция уже идёт
    /// (вызвали изнутри чужой), встаём в неё и берём только блокировку. Execution strategy
    /// обязательна: ручная транзакция при retry-стратегии иначе бросает исключение.
    /// </summary>
    private async Task<T> InGroupTransactionAsync<T>(int hubGroupId, Func<Task<T>> work)
    {
        if (_db.Database.CurrentTransaction != null)
        {
            await LockGroupAsync(hubGroupId);
            return await work();
        }

        var strategy = _db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync();
            await LockGroupAsync(hubGroupId);
            var result = await work();
            await tx.CommitAsync();
            return result;
        });
    }

    /// <summary>Снимается сама на commit/rollback. На InMemory (тесты) блокировок нет — там один поток.</summary>
    private async Task LockGroupAsync(int hubGroupId)
    {
        if (!_db.Database.IsNpgsql()) return;
        await _db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({SyncLockClass}, {hubGroupId})");
    }
}
