using Microsoft.EntityFrameworkCore;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Domain;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Repositories;

/// <summary>
/// Публичный read-путь клуба — ростер и клубные рекорды (см. <see cref="IClubPublicRepository"/>).
/// Читает через <see cref="SwimmReadDbContext"/> (роль swimm_ro), как остальные публичные
/// репозитории (HubGroups, Records).
/// </summary>
public class ClubPublicRepository : IClubPublicRepository
{
    private readonly SwimmReadDbContext _read;

    public ClubPublicRepository(SwimmReadDbContext read)
    {
        _read = read;
    }

    public async Task<int?> ResolveClubIdAsync(int clubId)
    {
        var club = await _read.Clubs.AsNoTracking()
            .Where(c => c.Id == clubId)
            .Select(c => new { c.Id, c.IsPseudo, c.MergedIntoId })
            .FirstOrDefaultAsync();
        if (club == null) return null;

        if (club.MergedIntoId != null)
        {
            // Один переход по мягкому merge — редиректа нет (решение Влада), отдаём данные
            // приёмника. Дальше разматывать не нужно: merge второго уровня запрещён guard-ом.
            var target = await _read.Clubs.AsNoTracking()
                .Where(c => c.Id == club.MergedIntoId.Value)
                .Select(c => new { c.Id, c.IsPseudo })
                .FirstOrDefaultAsync();
            return target == null || target.IsPseudo ? null : target.Id;
        }

        return club.IsPseudo ? null : club.Id;
    }

    public async Task<ClubRosterPageDto> GetRosterAsync(
        int resolvedClubId, int page, int pageSize, string? gender, int? ageFrom, int? ageTo, int? season)
    {
        // Возраст в сезоне (НЕ зачётная группа Category) — сезон берём из параметра
        // либо из текущего календарного сезона (SeasonMath).
        var seasonYear = season ?? SeasonMath.CurrentStartYear();

        var query = _read.Swimmers.AsNoTracking().Where(s => s.ClubId == resolvedClubId);

        if (!string.IsNullOrWhiteSpace(gender))
        {
            // ⚠ Swimmer.Gender хранится в ДВУХ форматах: подавляющее большинство строк —
            // "male"/"female" (2475/1484 на 2026-08-01), но есть и "M"/"F" (13/2) от старого
            // импорта; плюс "none" и null. XML-док у поля говорит «M / F» и врёт — сверяться
            // с данными, а не с ним. Фильтр обязан принимать оба написания, иначе роcтер по
            // полу вернул бы полтора десятка пловцов на всю базу.
            var isFemale = gender == "female";
            var word = isFemale ? "female" : "male";
            var letter = isFemale ? "F" : "M";
            query = query.Where(s => s.Gender == word || s.Gender == letter);
        }

        // age = seasonYear - BirthYear ⇒ границы возраста переводим в границы BirthYear,
        // чтобы фильтр остался переводимым в SQL (без клиентской пост-фильтрации).
        if (ageFrom.HasValue) query = query.Where(s => s.BirthYear <= seasonYear - ageFrom.Value);
        if (ageTo.HasValue) query = query.Where(s => s.BirthYear >= seasonYear - ageTo.Value);

        var total = await query.CountAsync();

        var pageRows = await query
            .OrderBy(s => s.LastNameEn).ThenBy(s => s.LastName).ThenBy(s => s.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(s => new
            {
                s.Id,
                s.LastName,
                s.FirstName,
                s.LastNameEn,
                s.FirstNameEn,
                s.BirthYear,
                s.Gender
            })
            .ToListAsync();

        var ids = pageRows.Select(s => s.Id).ToList();

        // Счётчики competitions/swims — по Results.ClubId ЭТОГО клуба (не по всем клубам
        // пловца за карьеру), в границах сезона только если season задан явно.
        DateTime? rangeStart = null, rangeEndExclusive = null;
        if (season.HasValue)
        {
            var range = SeasonMath.RangeOf(season.Value);
            rangeStart = range.Start;
            rangeEndExclusive = range.EndExclusive;
        }

        var countsById = new Dictionary<int, (int Competitions, int Swims)>();
        if (ids.Count > 0)
        {
            var countsQuery = _read.Results.AsNoTracking()
                .Where(r => r.ClubId == resolvedClubId && ids.Contains(r.SwimmerId));
            if (rangeStart.HasValue)
                countsQuery = countsQuery.Where(r => r.CompetitionDate >= rangeStart.Value && r.CompetitionDate < rangeEndExclusive!.Value);

            var counts = await countsQuery
                .GroupBy(r => r.SwimmerId)
                .Select(g => new
                {
                    SwimmerId = g.Key,
                    Competitions = g.Select(r => r.CompetitionId).Distinct().Count(),
                    Swims = g.Count()
                })
                .ToListAsync();

            foreach (var c in counts)
                countsById[c.SwimmerId] = (c.Competitions, c.Swims);
        }

        var data = pageRows.Select(s =>
        {
            countsById.TryGetValue(s.Id, out var c);
            return new ClubRosterItemDto
            {
                SwimmerId = s.Id,
                LastName = s.LastName,
                FirstName = s.FirstName,
                LastNameEn = s.LastNameEn,
                FirstNameEn = s.FirstNameEn,
                BirthYear = s.BirthYear,
                Age = seasonYear - s.BirthYear,
                Gender = s.Gender == "F" ? "female" : s.Gender == "M" ? "male" : s.Gender,
                Competitions = c.Competitions,
                Swims = c.Swims
            };
        }).ToList();

        return new ClubRosterPageDto
        {
            Page = page,
            PageSize = pageSize,
            Total = total,
            HasMore = (long)page * pageSize < total,
            Data = data
        };
    }

    public async Task<ClubRecordsDto> GetRecordsAsync(int resolvedClubId, string? poolType)
    {
        // Ось и группировка скопированы с «рекордов группы» (HubGroupPublicRepository,
        // FillAggregatesAsync) — фаза 8.3. Отличия: фильтр по ClubId вместо списка пловцов,
        // плюс исключение SuspectReason (помеченные ошибки протокола не должны становиться
        // «рекордом клуба» — у групп этого фильтра нет, это их долг, но здесь делаем сразу верно).
        var query = _read.Results.AsNoTracking()
            .Where(r => r.ClubId == resolvedClubId
                        && r.TimeMillisecond != null
                        && !r.TimeFail
                        && r.RelayId == null
                        && r.SuspectReason == null);

        // ⚠ 25m и 50m — разные рекорды: PoolType всегда часть ключа группировки (ниже),
        // фильтр тут лишь сужает выборку под конкретный бассейн.
        if (!string.IsNullOrWhiteSpace(poolType))
            query = query.Where(r => r.Competition.PoolType == poolType);

        var bests = await query
            .GroupBy(r => new { StyleName = r.Style.Name, r.Distance, r.Competition.PoolType, r.Gender })
            .Select(g => g
                // При равенстве времени — более ранний заплыв.
                .OrderBy(r => r.TimeMillisecond)
                .ThenBy(r => r.CompetitionDate)
                .Select(r => new ClubBestDto
                {
                    StyleName = g.Key.StyleName,
                    Distance = g.Key.Distance,
                    PoolType = g.Key.PoolType,
                    Gender = g.Key.Gender,
                    TimeOriginal = r.TimeOriginal,
                    TimeMs = r.TimeMillisecond,
                    SwimmerId = r.SwimmerId,
                    SwimmerName = (r.Swimmer.LastName + " " + r.Swimmer.FirstName).Trim(),
                    SwimmerNameEn = (r.Swimmer.LastNameEn + " " + r.Swimmer.FirstNameEn).Trim(),
                    CompetitionName = r.Competition.Name,
                    Date = r.Competition.Date,
                    Points = r.InternationalPoints
                })
                .First())
            .ToListAsync();

        var ordered = bests
            .OrderBy(b => b.StyleName)
            .ThenBy(b => b.Distance.Length)
            .ThenBy(b => b.Distance)
            .ThenBy(b => b.Gender)
            .ToList();

        return new ClubRecordsDto { Data = ordered };
    }
}
