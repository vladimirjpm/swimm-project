using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Domain;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Repositories;

/// <summary>
/// Национальный season best (см. <see cref="ISeasonBestRepository"/>). Читает через
/// <see cref="SwimmReadDbContext"/> (роль swimm_ro), как остальные публичные репозитории.
/// </summary>
public class SeasonBestRepository : ISeasonBestRepository
{
    private readonly SwimmReadDbContext _read;

    public SeasonBestRepository(SwimmReadDbContext read)
    {
        _read = read;
    }

    /// <summary>Строка-кандидат: всё, что нужно и для отбора лидера, и для ответа.</summary>
    private sealed record Row(
        string Gender, int? BirthYear, int? TimeMs, string? TimeOriginal, int SwimmerId,
        string Name, string? NameEn, string? Club, string? PoolType, string? Competition,
        DateTime Date, int? Points);

    public async Task<SeasonBestNationalDto> GetNationalSeasonBestAsync(
        string style, string distance, string? poolType, int? season, CancellationToken ct = default)
    {
        var seasonYear = season ?? SeasonMath.CurrentStartYear();
        var (start, endExclusive) = SeasonMath.RangeOf(seasonYear);

        var query = _read.Results.AsNoTracking()
            .Where(r => r.TimeMillisecond != null
                        && !r.TimeFail
                        && r.RelayId == null
                        // Помеченные ошибки протокола не должны становиться «лучшим временем
                        // сезона» — тот же фильтр, что у клубного season best.
                        && r.SuspectReason == null
                        // Masters исключены целиком (решение Влада 2026-08-22): таб стоит рядом
                        // с детскими возрастными рекордами, и взрослые старты там не к месту.
                        && !r.Competition.IsMasters
                        && r.Style.Name == style
                        && r.Distance == distance
                        && r.CompetitionDate >= start
                        && r.CompetitionDate < endExclusive);

        // ⚠ 25m и 50m — разные времена. Без фильтра оба бассейна попадают в одну выборку
        // (витрина так и делает при pool_type=all), и бассейн виден у каждой записи.
        if (!string.IsNullOrWhiteSpace(poolType))
            query = query.Where(r => r.Competition.PoolType == poolType);

        // Возраст нужен на строку (он сезонный, а не хранимый) — лидера выбираем в памяти,
        // как в клубном season best. Ответ кэшируется на сутки, выборка узкая (одна дистанция).
        var rows = await query
            .Select(r => new Row(
                // Пол берём у ПЛОВЦА, а не из строки: пол человека живёт в карточке, а
                // Results.Gender — пол зачёта заплыва, и он бывает ошибочным (одна кривая
                // шапка протокола уводила пловца в чужую колонку витрины).
                r.Swimmer.Gender ?? r.Gender,
                r.Swimmer.BirthYear,
                r.TimeMillisecond,
                r.TimeOriginal,
                r.SwimmerId,
                (r.Swimmer.LastName + " " + r.Swimmer.FirstName).Trim(),
                (r.Swimmer.LastNameEn + " " + r.Swimmer.FirstNameEn).Trim(),
                r.Club.Name,
                r.Competition.PoolType,
                r.Competition.Name,
                r.CompetitionDate,
                r.InternationalPoints))
            .ToListAsync(ct);

        var meets = rows.Select(r => (r.Competition ?? "") + "|" + r.Date.ToString("yyyy-MM-dd")).Distinct().Count();

        var items = rows
            .Select(r => new
            {
                Row = r,
                Gender = NormalizeGender(r.Gender),
                Age = r.BirthYear is int by ? SeasonMath.AgeInSeason(seasonYear, by) : null,
            })
            // Без года рождения ступени нет — такие заплывы просто выпадают (в клубной
            // карточке для них есть корзина «n/a», здесь таблица строго по возрастам).
            .Where(x => x.Age != null && x.Gender != null)
            .GroupBy(x => new { x.Gender, Age = x.Age!.Value })
            .Select(g => g
                .OrderBy(x => x.Row.TimeMs)
                // При равенстве времени лидер — тот, кто проплыл раньше.
                .ThenBy(x => x.Row.Date)
                .First())
            .Select(x => new SeasonBestNationalItemDto
            {
                Gender = x.Gender!,
                Age = x.Age!.Value,
                Time = x.Row.TimeOriginal ?? "",
                TimeMs = x.Row.TimeMs,
                SwimmerId = x.Row.SwimmerId,
                Name = x.Row.Name,
                NameEn = string.IsNullOrWhiteSpace(x.Row.NameEn) ? null : x.Row.NameEn,
                Club = x.Row.Club,
                PoolType = x.Row.PoolType,
                Competition = x.Row.Competition,
                Date = x.Row.Date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
                Points = x.Row.Points,
            })
            .OrderBy(i => i.Age)
            .ThenBy(i => i.Gender)
            .ToList();

        return new SeasonBestNationalDto
        {
            Season = seasonYear,
            SeasonLabel = SeasonMath.Label(seasonYear),
            Style = style,
            Distance = distance,
            PoolType = string.IsNullOrWhiteSpace(poolType) ? null : poolType,
            Meets = meets,
            Data = items,
        };
    }

    /// <summary>
    /// Results.Gender живёт в двух написаниях («male»/«female» и «M»/«F», как и у пловцов) —
    /// наружу отдаём одно, иначе витрина делит один и тот же пол на две колонки.
    /// </summary>
    private static string? NormalizeGender(string? gender)
    {
        var g = gender?.Trim().ToLowerInvariant();
        return g switch
        {
            "male" or "m" => "male",
            "female" or "f" => "female",
            _ => null,
        };
    }
}
