using Microsoft.EntityFrameworkCore;
using Npgsql;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Application.Mapping;
using Swimm.Domain;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Services;

/// <inheritdoc cref="IHubGroupUserService"/>
public class HubGroupUserService : IHubGroupUserService
{
    private readonly SwimmDbContext _db;
    private readonly HubGroupCrudCore _core;
    private readonly ISettingsService _settings;

    public HubGroupUserService(SwimmDbContext db, HubGroupCrudCore core, ISettingsService settings)
    {
        _db = db;
        _core = core;
        _settings = settings;
    }

    /// <summary>
    /// Первый ключ транзакционной advisory-блокировки «создание группы владельцем» (второй —
    /// userId). Произвольная константа, лишь бы не совпала с другими блокировками в БД.
    /// </summary>
    private const int CreateLockClass = 0x48474C4D; // "HGLM" — hub group limit

    public async Task<IReadOnlyList<HubGroupAdminRowDto>> GetMineAsync(int userId)
    {
        var rows = await _db.HubGroups.AsNoTracking()
            .Where(g => g.OwnerUserId == userId || g.Admins.Any(m => m.UserId == userId))
            .OrderByDescending(g => g.UpdatedAt)
            .Select(g => new HubGroupAdminRowDto
            {
                Id = g.Id,
                Name = g.Name,
                Slug = g.Slug,
                IconUrl = g.IconUrl,
                ClubName = g.Club != null ? g.Club.Name : null,
                MemberCount = g.Members.Count(m => !m.IsExcluded),
                IsPublic = g.IsPublic,
                IsOfficial = g.IsOfficial,
                UpdatedAt = g.UpdatedAt,
                OwnerUserId = g.OwnerUserId
            })
            .ToListAsync();

        // Клуб подписки и плашка «не в каталоге из-за официальной X» (П4).
        await HubGroupCatalog.FillCatalogStatusAsync(_db, rows);
        return rows;
    }

    public async Task<HubGroupCreateEligibilityDto> GetCreateEligibilityAsync(int userId, bool isAdmin, bool isCoach)
    {
        // Считаем владение, а не админство: официальные группы тоже в счёт (решение 10.09.2026).
        var owned = await _db.HubGroups.CountAsync(g => g.OwnerUserId == userId);
        var personalLimit = await _db.AppUsers
            .Where(u => u.Id == userId)
            .Select(u => u.HubGroupLimit)
            .FirstOrDefaultAsync();

        return HubGroupCreationRules.Evaluate(_settings, isAdmin, isCoach, personalLimit, owned);
    }

    public async Task<HubGroupSaveResult> CreateAsync(HubGroupInputDto input, int ownerUserId, bool isAdmin, bool isCoach)
    {
        // «Посчитать → вставить» — одна транзакция под блокировкой владельца. Без неё два
        // параллельных запроса оба видят owned < limit и оба вставляют: лимит превышен на
        // единицу. Execution strategy обязательна: ручная транзакция при retry-стратегии
        // AdminConnection иначе бросает исключение (см. JsonImportService).
        var strategy = _db.Database.CreateExecutionStrategy();
        var result = await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync();
            await LockOwnerAsync(ownerUserId);

            var eligibility = await GetCreateEligibilityAsync(ownerUserId, isAdmin, isCoach);
            if (!eligibility.CanCreate)
                return HubGroupSaveResult.Fail(eligibility.Reason ?? "Creating a group is not available.");

            var slug = await _core.ResolveSlugAsync(input, excludeId: null);
            var error = await _core.ValidateAsync(input, slug, excludeId: null);
            if (error != null) return HubGroupSaveResult.Fail(error);

            // allowClubChange:false — пользователь создаёт только свободную группу (как favorites);
            // привязка к клубу — через заявку и одобрение админа (8.7), не через ввод.
            var group = new HubGroup { OwnerUserId = ownerUserId };
            HubGroupCrudCore.Apply(group, input, slug, allowClubChange: false);
            await _core.ApplyCountryAsync(group, input.Country);
            _db.HubGroups.Add(group);

            var saveError = await _core.TrySaveChangesAsync(group);
            if (saveError != null) return HubGroupSaveResult.Fail(saveError);

            await tx.CommitAsync();
            return HubGroupSaveResult.Ok(group.Id);
        });

        // Кэш — после коммита: до него параллельный публичный запрос закэшировал бы список без группы.
        if (result.Success) await _core.InvalidateCacheAsync();
        return result;
    }

    /// <summary>
    /// Транзакционная advisory-блокировка по владельцу: снимается сама на commit/rollback,
    /// чужих пользователей не задерживает. На InMemory (тесты) блокировок нет — там один поток.
    /// </summary>
    private async Task LockOwnerAsync(int userId)
    {
        if (!_db.Database.IsNpgsql()) return;
        await _db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({CreateLockClass}, {userId})");
    }

    public async Task<HubGroupSaveResult> UpdateAsync(int id, HubGroupInputDto input)
    {
        var group = await _db.HubGroups.FindAsync(id);
        if (group == null) return HubGroupSaveResult.Fail($"Группа #{id} не найдена");

        var slug = await _core.ResolveSlugAsync(input, excludeId: id);
        var error = await _core.ValidateAsync(input, slug, excludeId: id);
        if (error != null) return HubGroupSaveResult.Fail(error);

        // allowClubChange:false — сохраняем текущий ClubId (в т.ч. у официальной группы),
        // ввод клуба игнорируем: сменить/снять клуб может только админ.
        HubGroupCrudCore.Apply(group, input, slug, allowClubChange: false);
        await _core.ApplyCountryAsync(group, input.Country);
        return await _core.SaveAsync(group);
    }

    public async Task<IReadOnlyList<HubGroupAdminMemberDto>> GetAdminsAsync(int hubGroupId)
    {
        return await _db.HubGroupAdmins.AsNoTracking()
            .Where(m => m.HubGroupId == hubGroupId)
            .Include(m => m.User)
            .OrderBy(m => m.CreatedAt)
            .Select(m => new HubGroupAdminMemberDto
            {
                UserId = m.UserId,
                DisplayName = m.User!.DisplayName,
                Email = m.User.Email,
                CreatedAt = m.CreatedAt
            })
            .ToListAsync();
    }

    public async Task<HubGroupMemberSaveResult> AddAdminAsync(int hubGroupId, string email, int grantedByUserId)
    {
        email = (email ?? "").Trim();
        if (email.Length == 0) return HubGroupMemberSaveResult.Fail("Email обязателен");

        var groupExists = await _db.HubGroups.AnyAsync(g => g.Id == hubGroupId);
        if (!groupExists) return HubGroupMemberSaveResult.Fail($"Группа #{hubGroupId} не найдена");

        var user = await _db.AppUsers.FirstOrDefaultAsync(u => u.Email == email);
        if (user == null) return HubGroupMemberSaveResult.Fail("Пользователь с таким email не найден");

        var dup = await _db.HubGroupAdmins.AnyAsync(m => m.HubGroupId == hubGroupId && m.UserId == user.Id);
        if (dup) return HubGroupMemberSaveResult.Fail("Этот пользователь уже админ группы");

        _db.HubGroupAdmins.Add(new HubGroupAdmin
        {
            HubGroupId = hubGroupId,
            UserId = user.Id,
            GrantedByUserId = grantedByUserId
        });

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "23505" })
        {
            // 23505 = unique_violation по (HubGroupId, UserId) — гонка добавления того же юзера.
            return HubGroupMemberSaveResult.Fail("Этот пользователь уже админ группы");
        }
        catch (DbUpdateException)
        {
            // Прочие ошибки записи (напр. 23503 FK на невалидный GrantedByUserId) — не выдавать
            // за дубликат, это вводит в заблуждение (см. dev-bypass GrantedByUserId=0).
            return HubGroupMemberSaveResult.Fail("Не удалось назначить админа группы");
        }

        await _core.InvalidateCacheAsync();
        return HubGroupMemberSaveResult.Ok();
    }

    public async Task<HubGroupMemberSaveResult> RemoveAdminAsync(int hubGroupId, int adminUserId)
    {
        var admin = await _db.HubGroupAdmins
            .FirstOrDefaultAsync(m => m.HubGroupId == hubGroupId && m.UserId == adminUserId);
        if (admin == null) return HubGroupMemberSaveResult.Fail("Админ группы не найден");

        _db.HubGroupAdmins.Remove(admin);
        await _db.SaveChangesAsync();
        await _core.InvalidateCacheAsync();
        return HubGroupMemberSaveResult.Ok();
    }

    // ── Участники-аккаунты ───────────────────────────────────────────────────
    // Приватный список (Sys_-таблица) — публичная страница его не видит, кэш не трогаем.

    public async Task<HubGroupMemberSaveResult> AddUserMemberAsync(int hubGroupId, string email, int addedByUserId, int? swimmerId = null, string? note = null)
    {
        email = (email ?? "").Trim();
        if (email.Length == 0) return HubGroupMemberSaveResult.Fail("Email обязателен");

        var groupExists = await _db.HubGroups.AnyAsync(g => g.Id == hubGroupId);
        if (!groupExists) return HubGroupMemberSaveResult.Fail($"Группа #{hubGroupId} не найдена");

        var user = await _db.AppUsers.FirstOrDefaultAsync(u => u.Email == email);
        if (user == null) return HubGroupMemberSaveResult.Fail("Пользователь с таким email не найден");

        if (swimmerId != null && !await _db.Swimmers.AnyAsync(s => s.Id == swimmerId))
            return HubGroupMemberSaveResult.Fail("Пловец не найден");

        return await InsertUserMemberAsync(hubGroupId, user.Id, addedByUserId, swimmerId: swimmerId, note: NormalizeNote(note));
    }

    public async Task<HubGroupMemberSaveResult> SetUserMemberLabelAsync(int hubGroupId, int userId, int? swimmerId, string? note)
    {
        var member = await _db.HubGroupUserMembers
            .FirstOrDefaultAsync(m => m.HubGroupId == hubGroupId && m.UserId == userId);
        if (member == null) return HubGroupMemberSaveResult.Fail("Участник не найден");

        if (swimmerId != null && !await _db.Swimmers.AnyAsync(s => s.Id == swimmerId))
            return HubGroupMemberSaveResult.Fail("Пловец не найден");

        member.SwimmerId = swimmerId;
        member.Note = NormalizeNote(note);
        await _db.SaveChangesAsync();
        return HubGroupMemberSaveResult.Ok();
    }

    private static string? NormalizeNote(string? note)
    {
        note = note?.Trim();
        return string.IsNullOrEmpty(note) ? null : note;
    }

    public async Task<HubGroupMemberSaveResult> RemoveUserMemberAsync(int hubGroupId, int userId)
    {
        var member = await _db.HubGroupUserMembers
            .FirstOrDefaultAsync(m => m.HubGroupId == hubGroupId && m.UserId == userId);
        if (member == null) return HubGroupMemberSaveResult.Fail("Участник не найден");

        _db.HubGroupUserMembers.Remove(member);
        await _db.SaveChangesAsync();
        return HubGroupMemberSaveResult.Ok();
    }

    public async Task<HubGroupMemberSaveResult> JoinAsync(int hubGroupId, int userId)
    {
        var group = await _db.HubGroups.AsNoTracking()
            .Where(g => g.Id == hubGroupId)
            .Select(g => new { g.Id, g.IsPublic, g.JoinPolicy })
            .FirstOrDefaultAsync();
        if (group == null) return HubGroupMemberSaveResult.Fail($"Группа #{hubGroupId} не найдена");

        // Вступить можно только в публично видимую группу — тот же критерий, что у публичного
        // списка (HubGroupVisibility: private → никуда, perGroup → только IsPublic, public → любые).
        var visibility = _settings.GetValue("HubGroupVisibility", "public");
        var joinable = visibility switch
        {
            "private" => false,
            "perGroup" => group.IsPublic,
            _ => true,
        };
        if (!joinable) return HubGroupMemberSaveResult.Fail("В эту группу нельзя вступить");

        // Гейт members-контента: при approval самозапись создаёт заявку (pending),
        // активирует владелец/админ группы через ApproveUserMemberAsync.
        var status = group.JoinPolicy == HubGroupJoinPolicy.Approval
            ? HubGroupUserMemberStatus.Pending
            : HubGroupUserMemberStatus.Active;
        return await InsertUserMemberAsync(hubGroupId, userId, addedByUserId: null, status);
    }

    public async Task<HubGroupMemberSaveResult> ApproveUserMemberAsync(int hubGroupId, int userId)
    {
        var member = await _db.HubGroupUserMembers
            .FirstOrDefaultAsync(m => m.HubGroupId == hubGroupId && m.UserId == userId);
        if (member == null) return HubGroupMemberSaveResult.Fail("Участник не найден");
        if (member.Status == HubGroupUserMemberStatus.Active)
            return HubGroupMemberSaveResult.Fail("Участник уже активен");

        member.Status = HubGroupUserMemberStatus.Active;
        await _db.SaveChangesAsync();
        return HubGroupMemberSaveResult.Ok();
    }

    public async Task<HubGroupMemberSaveResult> SetJoinPolicyAsync(int hubGroupId, string policy)
    {
        if (policy != HubGroupJoinPolicy.Open && policy != HubGroupJoinPolicy.Approval)
            return HubGroupMemberSaveResult.Fail("Политика вступления: допустимо open или approval");

        var group = await _db.HubGroups.FirstOrDefaultAsync(g => g.Id == hubGroupId);
        if (group == null) return HubGroupMemberSaveResult.Fail($"Группа #{hubGroupId} не найдена");
        if (group.JoinPolicy == policy) return HubGroupMemberSaveResult.Ok();

        // Уже вступивших переключение НЕ трогает: approval — это дверь для новых, а не
        // ретроактивный пересмотр состава. Кого пустили, того выгоняют руками.
        group.JoinPolicy = policy;
        group.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        // Публичная страница группы кэшируется вместе с политикой (кнопка «Join» / «Request to
        // join» читает её из того же payload) — без сброса тумблер минуту не виден снаружи.
        await _core.InvalidateCacheAsync();
        return HubGroupMemberSaveResult.Ok();
    }

    public async Task<HubGroupMemberSaveResult> SetTrainingScheduleAsync(
        int hubGroupId, GroupTrainingScheduleDto? schedule)
    {
        var group = await _db.HubGroups.FirstOrDefaultAsync(g => g.Id == hubGroupId);
        if (group == null) return HubGroupMemberSaveResult.Fail($"Группа #{hubGroupId} не найдена");

        var model = new GroupTrainingSchedule
        {
            Slots = (schedule?.Slots ?? [])
                .Select(s => new GroupTrainingSlot
                {
                    Day = s.Day,
                    Start = s.Start?.Trim() ?? "",
                    End = string.IsNullOrWhiteSpace(s.End) ? null : s.End.Trim(),
                })
                .ToList(),
            Place = Clean(schedule?.Place),
            PoolType = Clean(schedule?.PoolType),
            Note = Clean(schedule?.Note),
        };

        // Битые слоты не сохраняем: расписание — витрина, и «Ср :» в шапке хуже пустоты.
        // Валидность считает сам домен (день 1..7 + разбор HH:mm), второго мнения тут нет.
        // Текст ошибки ПО-АНГЛИЙСКИ: он показывается в карточке редактора как есть, а
        // видимый UI у нас английский (правило проекта). Соседние сообщения этого сервиса
        // русские — это долг, новый в него не добавляем.
        var invalid = model.Slots.Count(s => !s.IsValid);
        if (invalid > 0)
            return HubGroupMemberSaveResult.Fail(invalid == 1
                ? "One training slot has an invalid day or time (use HH:mm)"
                : $"{invalid} training slots have an invalid day or time (use HH:mm)");

        // Пустое расписание храним как NULL, а не как «{}»: пусто — это отсутствие, и
        // читателю (в т.ч. глазами в psql) незачем гадать, чем «{} » отличается от null.
        group.TrainingSchedule = model.Slots.Count > 0 ? model.ToJson() : null;
        group.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        // Расписание едет в кэшируемом payload страницы группы — без сброса правка минуту
        // не видна (та же ловушка, что у политики вступления).
        await _core.InvalidateCacheAsync();
        return HubGroupMemberSaveResult.Ok();
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public async Task<HubGroupMemberSaveResult> LeaveAsync(int hubGroupId, int userId) =>
        await RemoveUserMemberAsync(hubGroupId, userId);

    public async Task<IReadOnlyList<JoinedHubGroupDto>> GetJoinedAsync(int userId)
    {
        return await _db.HubGroupUserMembers.AsNoTracking()
            .Where(m => m.UserId == userId)
            .OrderByDescending(m => m.JoinedAt)
            .Select(m => new JoinedHubGroupDto
            {
                Id = m.HubGroup!.Id,
                Name = m.HubGroup.Name,
                Slug = m.HubGroup.Slug,
                IconUrl = m.HubGroup.IconUrl,
                Status = m.Status,
                JoinedAt = m.JoinedAt
            })
            .ToListAsync();
    }

    /// <summary>Общая вставка участника-аккаунта: dedup + обработка гонки (23505).</summary>
    private async Task<HubGroupMemberSaveResult> InsertUserMemberAsync(
        int hubGroupId, int userId, int? addedByUserId, string status = HubGroupUserMemberStatus.Active,
        int? swimmerId = null, string? note = null)
    {
        var existing = await _db.HubGroupUserMembers.AsNoTracking()
            .Where(m => m.HubGroupId == hubGroupId && m.UserId == userId)
            .Select(m => m.Status)
            .FirstOrDefaultAsync();
        if (existing != null)
            return HubGroupMemberSaveResult.Fail(existing == HubGroupUserMemberStatus.Pending
                ? "Заявка уже отправлена и ждёт одобрения"
                : "Этот пользователь уже участник группы");

        _db.HubGroupUserMembers.Add(new HubGroupUserMember
        {
            HubGroupId = hubGroupId,
            UserId = userId,
            AddedByUserId = addedByUserId,
            Status = status,
            SwimmerId = swimmerId,
            Note = note
        });

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "23505" })
        {
            return HubGroupMemberSaveResult.Fail("Этот пользователь уже участник группы");
        }
        catch (DbUpdateException)
        {
            return HubGroupMemberSaveResult.Fail("Не удалось добавить участника");
        }

        return HubGroupMemberSaveResult.Ok();
    }

    public async Task<MyHubGroupClubRequestDto?> GetClubRequestAsync(int hubGroupId)
    {
        return await _db.HubGroupClubRequests.AsNoTracking()
            .Where(r => r.HubGroupId == hubGroupId)
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new MyHubGroupClubRequestDto
            {
                Id = r.Id,
                ClubId = r.ClubId,
                ClubName = r.Club!.Name,
                Message = r.Message,
                Status = r.Status,
                CreatedAt = r.CreatedAt,
                DecidedAt = r.DecidedAt
            })
            .FirstOrDefaultAsync();
    }

    public async Task<HubGroupMemberSaveResult> SubmitClubRequestAsync(int hubGroupId, int userId, HubGroupClubRequestInputDto input)
    {
        var group = await _db.HubGroups.FindAsync(hubGroupId);
        if (group == null) return HubGroupMemberSaveResult.Fail($"Группа #{hubGroupId} не найдена");
        if (group.IsOfficial) return HubGroupMemberSaveResult.Fail("Группа уже официальная");

        var clubExists = await _db.Clubs.AnyAsync(c => c.Id == input.ClubId);
        if (!clubExists) return HubGroupMemberSaveResult.Fail("Клуб не найден");

        var hasPending = await _db.HubGroupClubRequests
            .AnyAsync(r => r.HubGroupId == hubGroupId && r.Status == HubGroupClubRequestStatus.Pending);
        if (hasPending) return HubGroupMemberSaveResult.Fail("Заявка на эту группу уже подана и ожидает решения");

        var clubTaken = await _db.HubGroups.AnyAsync(g => g.ClubId == input.ClubId && g.IsOfficial);
        if (clubTaken) return HubGroupMemberSaveResult.Fail("У этого клуба уже есть официальная группа");

        _db.HubGroupClubRequests.Add(new HubGroupClubRequest
        {
            HubGroupId = hubGroupId,
            UserId = userId,
            ClubId = input.ClubId,
            Message = string.IsNullOrWhiteSpace(input.Message) ? null : input.Message.Trim(),
        });

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            return HubGroupMemberSaveResult.Fail("Заявка на эту группу уже подана и ожидает решения");
        }

        return HubGroupMemberSaveResult.Ok();
    }
}
