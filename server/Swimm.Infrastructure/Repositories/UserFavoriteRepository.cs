using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Application.Mapping;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Repositories;

public class UserFavoriteRepository : IUserFavoriteRepository
{
    private readonly SwimmDbContext _db;
    private readonly ISettingsService _settings;

    public UserFavoriteRepository(SwimmDbContext db, ISettingsService settings)
    {
        _db = db;
        _settings = settings;
    }

    /// <summary>
    /// Первый ключ транзакционной advisory-блокировки «добавление в избранное» (второй —
    /// userId). Произвольная константа, лишь бы не совпала с другими блокировками в БД.
    /// </summary>
    private const int AddLockClass = 0x46415653; // "FAVS"

    /// <summary>
    /// Проекция строки избранного в DTO — одна на чтение списка и ответ на добавление: в ней
    /// правило имён, и двум копиям разъехаться нельзя.
    /// </summary>
    private static readonly Expression<Func<UserFavorite, FavoriteDto>> ToDto = f => new FavoriteDto
    {
        Id = f.Id,
        TargetType = f.TargetType,
        SwimmerId = f.SwimmerId,
        // Имя пловца — ИВРИТСКОЕ по умолчанию (правило Влада от 28.08.2026):
        // имена показываются так, как напечатаны в протоколе федерации. Английское —
        // фоллбек, когда ивритского нет. Правило «UI только English» этому не
        // противоречит: оно про строки интерфейса, а имя человека — данные.
        SwimmerName = f.Swimmer == null
            ? null
            : (f.Swimmer.LastName.Length > 0 || f.Swimmer.FirstName.Length > 0)
                ? (f.Swimmer.LastName + " " + f.Swimmer.FirstName).Trim()
                : (f.Swimmer.LastNameEn + " " + f.Swimmer.FirstNameEn).Trim(),
        ClubId = f.ClubId,
        // Клуб — по тому же правилу, что имя: иврит по умолчанию, EN фоллбеком.
        ClubName = f.Club == null
            ? null
            : (f.Club.Name.Length > 0 ? f.Club.Name : f.Club.NameEn),
        IsPrimary = f.IsPrimary,
        SortOrder = f.SortOrder,
        CreatedAt = f.CreatedAt
    };

    public async Task<List<FavoriteDto>> GetForUserAsync(int userId)
    {
        return await _db.UserFavorites
            .AsNoTracking()
            .Where(f => f.UserId == userId)
            .OrderBy(f => f.SortOrder)
            .ThenBy(f => f.Id)
            .Select(ToDto)
            .ToListAsync();
    }

    public async Task<AddFavoriteResult> AddAsync(int userId, AddFavoriteRequest request)
    {
        // «Посчитать → вставить» — одна транзакция под блокировкой пользователя (как у лимита
        // групп в HubGroupUserService): без неё две вкладки на 29-м пловце обе видят «есть
        // место» и обе вставляют. Execution strategy обязательна: ручная транзакция при
        // retry-стратегии иначе бросает исключение.
        var strategy = _db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync();
            await LockUserAsync(userId);

            // Дубль — раньше лимита: «уже в избранном» остаётся 409 и на пределе, иначе
            // повторный клик по уже горящему сердечку объявил бы, что места нет.
            var sameTarget = request.TargetType == FavoritesRules.TargetClub
                ? _db.UserFavorites.Where(f => f.UserId == userId
                    && f.TargetType == FavoritesRules.TargetClub && f.ClubId == request.ClubId)
                : _db.UserFavorites.Where(f => f.UserId == userId
                    && f.TargetType == FavoritesRules.TargetSwimmer && f.SwimmerId == request.SwimmerId);
            if (await sameTarget.AnyAsync())
                return AddFavoriteResult.Duplicate();

            // Счёт по типу: primary («это я») — такой же пловец и идёт в счёт пловцов. Кто
            // уже выше лимита (лимит снизили), ничего не теряет — только не добавляет.
            var limit = FavoritesRules.LimitFor(_settings, request.TargetType);
            var count = await _db.UserFavorites
                .CountAsync(f => f.UserId == userId && f.TargetType == request.TargetType);
            if (count >= limit)
                return AddFavoriteResult.LimitReached(limit, FavoritesRules.FullHint(request.TargetType, limit));

            var fav = new UserFavorite
            {
                UserId = userId,
                TargetType = request.TargetType,
                SwimmerId = request.SwimmerId,
                ClubId = request.ClubId,
                IsPrimary = false,
                SortOrder = 0,
                CreatedAt = DateTime.UtcNow
            };

            _db.UserFavorites.Add(fav);
            try
            {
                await _db.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                // Нарушение unique-constraint (гонка мимо проверки выше или несуществующий
                // пловец/клуб) → как и раньше, 409 на уровне контроллера. Транзакция
                // откатится при выходе из using.
                _db.Entry(fav).State = EntityState.Detached;
                return AddFavoriteResult.Duplicate();
            }

            // Читаем до коммита: своя вставка в своей транзакции видна наверняка.
            var dto = await _db.UserFavorites
                .AsNoTracking()
                .Where(f => f.Id == fav.Id)
                .Select(ToDto)
                .FirstAsync();

            await tx.CommitAsync();
            return AddFavoriteResult.Added(dto);
        });
    }

    /// <summary>
    /// Транзакционная advisory-блокировка по пользователю: снимается сама на commit/rollback,
    /// чужих пользователей не задерживает. На InMemory (тесты) блокировок нет — там один поток.
    /// </summary>
    private async Task LockUserAsync(int userId)
    {
        if (!_db.Database.IsNpgsql()) return;
        await _db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({AddLockClass}, {userId})");
    }

    public async Task<bool> RemoveAsync(int userId, int favoriteId)
    {
        // IDOR: фильтруем по userId — нельзя удалить чужой фаворит.
        var fav = await _db.UserFavorites
            .FirstOrDefaultAsync(f => f.Id == favoriteId && f.UserId == userId);

        if (fav == null) return false;

        _db.UserFavorites.Remove(fav);
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> SetPrimaryAsync(int userId, int favoriteId)
    {
        // IDOR: проверяем, что фаворит принадлежит текущему пользователю и является swimmer.
        var target = await _db.UserFavorites
            .FirstOrDefaultAsync(f => f.Id == favoriteId && f.UserId == userId && f.TargetType == "swimmer");

        if (target == null) return false;

        // Clear СНАЧАЛА своим SaveChangesAsync, потом set — раздельно, а не одним вызовом.
        // Partial unique index UX_UserFav_OnePrimary проверяется immediately (не deferred):
        // если бы EF отправил UPDATE "target → true" раньше UPDATE "old → false" в одном
        // SaveChanges (порядок операторов внутри одного SaveChanges не гарантирован), это
        // временно давало бы два primary одновременно → нарушение индекса. Раздельные вызовы
        // убирают эту гонку с самим собой полностью (не просто глушат исключение).
        var currentPrimaries = await _db.UserFavorites
            .Where(f => f.UserId == userId && f.IsPrimary && f.TargetType == "swimmer" && f.Id != favoriteId)
            .ToListAsync();

        if (currentPrimaries.Count > 0)
        {
            foreach (var f in currentPrimaries)
                f.IsPrimary = false;
            await _db.SaveChangesAsync();
        }

        target.IsPrimary = true;
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Гонка с ДРУГИМ конкурентным запросом (не с самим собой, та убрана выше) → no-op, не 500.
        }
        return true;
    }

    public async Task<bool> UnsetPrimaryAsync(int userId, int favoriteId)
    {
        // IDOR: only the owner's swimmer favorite can be cleared.
        var fav = await _db.UserFavorites
            .FirstOrDefaultAsync(f => f.Id == favoriteId && f.UserId == userId && f.TargetType == "swimmer");

        if (fav == null) return false;

        fav.IsPrimary = false;
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> ReorderAsync(int userId, List<ReorderItem> items)
    {
        foreach (var item in items)
        {
            await _db.UserFavorites
                .Where(f => f.Id == item.Id && f.UserId == userId)
                .ExecuteUpdateAsync(s => s.SetProperty(f => f.SortOrder, item.SortOrder));
        }
        return true;
    }
}
