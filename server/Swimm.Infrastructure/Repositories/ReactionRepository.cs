using Microsoft.EntityFrameworkCore;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Services;

namespace Swimm.Infrastructure.Repositories;

/// <summary>
/// Реакции (Sys_UserReactions). Лайк доступен только на видимое пользователю медиа:
/// своё, либо с approved-публикацией по общему правилу аудитории
/// (<see cref="MediaPublicationAudience"/>: public — всем, members — активным участникам,
/// владельцу, админам группы и админу сайта). Поздравление — на любой существующий заплыв
/// (результаты публичны).
/// Гонка двойного POST гасится partial unique индексом (UX_UserReactions_*).
/// </summary>
public class ReactionRepository : IReactionRepository
{
    private readonly SwimmDbContext _db;

    public ReactionRepository(SwimmDbContext db)
    {
        _db = db;
    }

    public async Task<ReactionStateDto?> SetLikeAsync(int userId, int mediaId, bool on, bool isSiteAdmin)
    {
        // Своё — всегда; чужое — только если хоть одна одобренная публикация видна этому
        // пользователю. Раньше здесь была своя копия правила, и она пускала участника с
        // висящей заявкой (pending) лайкнуть members-видео, которого он не видит.
        var visible = await _db.UserMedia.AsNoTracking().AnyAsync(m => m.Id == mediaId && m.UserId == userId)
            || await _db.UserMediaPublications.AsNoTracking()
                .Where(p => p.UserMediaId == mediaId && p.Status == UserMediaPublicationStatus.Approved)
                .Where(MediaPublicationAudience.CanSee(_db, userId, isSiteAdmin))
                .AnyAsync();
        if (!visible) return null;

        await ToggleAsync(userId, on,
            r => r.Kind == "like" && r.MediaId == mediaId,
            () => new UserReaction { UserId = userId, Kind = "like", MediaId = mediaId });

        return new ReactionStateDto
        {
            Count = await _db.UserReactions.CountAsync(r => r.Kind == "like" && r.MediaId == mediaId),
            Mine = on,
        };
    }

    public async Task<ReactionStateDto?> SetCheerAsync(int userId, long resultId, bool on)
    {
        var exists = await _db.Results.AsNoTracking().AnyAsync(r => r.Id == resultId);
        if (!exists) return null;

        await ToggleAsync(userId, on,
            r => r.Kind == "congrats" && r.ResultId == resultId,
            () => new UserReaction { UserId = userId, Kind = "congrats", ResultId = resultId });

        return new ReactionStateDto
        {
            Count = await _db.UserReactions.CountAsync(r => r.Kind == "congrats" && r.ResultId == resultId),
            Mine = on,
        };
    }

    private async Task ToggleAsync(
        int userId, bool on,
        System.Linq.Expressions.Expression<Func<UserReaction, bool>> target,
        Func<UserReaction> create)
    {
        var existing = await _db.UserReactions
            .Where(target)
            .FirstOrDefaultAsync(r => r.UserId == userId);

        if (on)
        {
            if (existing != null) return; // уже стоит — идемпотентно
            _db.UserReactions.Add(create());
            try
            {
                await _db.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                // Гонка двойного POST: уникальный индекс уже вставил такую же строку — ок.
            }
        }
        else
        {
            if (existing == null) return;
            _db.UserReactions.Remove(existing);
            await _db.SaveChangesAsync();
        }
    }
}
