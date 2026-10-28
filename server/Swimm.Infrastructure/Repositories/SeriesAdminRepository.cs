using Microsoft.EntityFrameworkCore;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Application.Mapping;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Repositories;

/// <summary>
/// Админка серий турниров (см. <see cref="ISeriesAdminRepository"/>), owner-контекст. Кэш
/// Overview сбрасывается сам: карточка «Official awards» собрана из этих таблиц, и сохранение
/// через EF роняет их метки (перехватчик К4).
/// </summary>
public class SeriesAdminRepository(SwimmDbContext db) : ISeriesAdminRepository
{
    public async Task<IReadOnlyList<SeriesRowDto>> GetAllAsync()
    {
        var days = await db.Competitions.AsNoTracking()
            .Where(c => c.SeriesPartId != null)
            .GroupBy(c => c.SeriesPart!.SeriesId)
            .Select(g => new { SeriesId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.SeriesId, x => x.Count);

        var rows = await db.CompetitionSeries.AsNoTracking()
            .OrderBy(s => s.Name)
            .Select(s => new { s.Id, s.Name, s.NameEn, s.MatchPhrases, Parts = s.Parts.Count, Regs = s.Regulations.Count })
            .ToListAsync();

        return rows.Select(s => new SeriesRowDto(s.Id, s.Name, s.NameEn, s.MatchPhrases, s.Parts, s.Regs,
            days.GetValueOrDefault(s.Id))).ToList();
    }

    public async Task<SeriesEditDto?> GetByIdAsync(int id)
    {
        var series = await db.CompetitionSeries.AsNoTracking()
            .Include(s => s.Parts)
            .Include(s => s.Regulations).ThenInclude(r => r.Awards).ThenInclude(a => a.Bands)
            .Include(s => s.Regulations).ThenInclude(r => r.Awards).ThenInclude(a => a.Prizes)
            .AsSplitQuery()
            .FirstOrDefaultAsync(s => s.Id == id);
        if (series is null) return null;

        var partIds = series.Parts.Select(p => p.Id).ToList();
        var days = await db.Competitions.AsNoTracking()
            .Where(c => c.SeriesPartId != null && partIds.Contains(c.SeriesPartId.Value))
            .Select(c => new
            {
                c.Id, c.Name, EventName = c.Event != null ? c.Event.Name : null, c.Date, c.DayNumber, c.EventId,
                PartId = c.SeriesPartId!.Value, c.SeriesRegulationCheckedAt, c.SeriesRegulationCheckedBy
            })
            .ToListAsync();

        var partName = series.Parts.ToDictionary(p => p.Id, p => p.Name);
        return new SeriesEditDto
        {
            Id = series.Id,
            Name = series.Name,
            NameEn = series.NameEn,
            MatchPhrases = series.MatchPhrases,
            SuggestedPhrase = SeriesMatch.CommonPhrase(days.Select(d => d.EventName ?? d.Name)),
            Parts = series.Parts
                .OrderBy(p => p.SortOrder).ThenBy(p => p.Id)
                .Select(p => new SeriesPartDto(p.Id, p.Name, p.NameEn, p.SortOrder, p.MinSeasonAge, p.MaxSeasonAge))
                .ToList(),
            Regulations = series.Regulations
                .OrderByDescending(r => r.EffectiveFrom)
                .Select(r => new SeriesRegulationDto
                {
                    Id = r.Id,
                    EffectiveFrom = r.EffectiveFrom,
                    SourceUrl = r.SourceUrl,
                    Description = r.Description,
                    Awards = r.Awards
                        .OrderBy(a => a.SortOrder).ThenBy(a => a.Id)
                        .Select(a => new SeriesAwardDto
                        {
                            Id = a.Id,
                            PartId = a.PartId,
                            PartName = partName.GetValueOrDefault(a.PartId, "?"),
                            Kind = a.Kind,
                            Title = a.Title,
                            CountBest = a.CountBest,
                            SplitByGender = a.SplitByGender,
                            Places = a.Places,
                            SortOrder = a.SortOrder,
                            Bands = a.Bands.OrderBy(b => b.Id)
                                .Select(b => new SeriesAwardBandDto(b.Gender, b.MinSeasonAge, b.MaxSeasonAge)).ToList(),
                            Prizes = a.Prizes.OrderBy(p => p.Place)
                                .Select(p => new SeriesAwardPrizeDto(p.Place, p.Text)).ToList()
                        })
                        .ToList()
                })
                .ToList(),
            EditionDays = days
                .OrderByDescending(d => ParseDate(d.Date)).ThenBy(d => d.DayNumber)
                .Select(d => new SeriesEditionDayDto(d.Id, d.EventName ?? d.Name, d.Date, d.DayNumber, d.EventId,
                    d.PartId, partName[d.PartId], d.SeriesRegulationCheckedAt, d.SeriesRegulationCheckedBy))
                .ToList()
        };
    }

    // ── Серия ─────────────────────────────────────────────────────────────────

    public async Task<SeriesSaveResult> CreateSeriesAsync(SeriesInputDto input)
    {
        if (await ValidateSeriesAsync(input, null) is { } error) return SeriesSaveResult.Fail(error);
        var series = new CompetitionSeries();
        Apply(series, input);
        db.CompetitionSeries.Add(series);
        await db.SaveChangesAsync();
        return SeriesSaveResult.Ok(series.Id);
    }

    public async Task<SeriesSaveResult> UpdateSeriesAsync(int id, SeriesInputDto input)
    {
        var series = await db.CompetitionSeries.FindAsync(id);
        if (series is null) return SeriesSaveResult.Fail($"Серия #{id} не найдена");
        if (await ValidateSeriesAsync(input, id) is { } error) return SeriesSaveResult.Fail(error);
        Apply(series, input);
        await db.SaveChangesAsync();
        return SeriesSaveResult.Ok(id);
    }

    public async Task<SeriesSaveResult> DeleteSeriesAsync(int id)
    {
        var series = await db.CompetitionSeries.FindAsync(id);
        if (series is null) return SeriesSaveResult.Fail($"Серия #{id} не найдена");
        // Дни отвязывает FK SET NULL, но трекер его не видит — снимаем явно, чтобы сбросить и кэш.
        await UnbindDaysAsync(db.Competitions.Where(c => c.SeriesPart!.SeriesId == id));
        db.CompetitionSeries.Remove(series);
        await db.SaveChangesAsync();
        return SeriesSaveResult.Ok(id);
    }

    private async Task<string?> ValidateSeriesAsync(SeriesInputDto input, int? id)
    {
        var name = input.Name?.Trim() ?? "";
        if (name.Length == 0) return "Имя серии обязательно";
        if (await db.CompetitionSeries.AnyAsync(s => s.Name == name && s.Id != id))
            return $"Серия «{name}» уже есть";
        return null;
    }

    private static void Apply(CompetitionSeries s, SeriesInputDto input)
    {
        s.Name = input.Name.Trim();
        s.NameEn = Blank(input.NameEn);
        // Фразы храним как ввели, но без пустых и пробелов по краям: «;» — разделитель.
        var phrases = (input.MatchPhrases ?? "").Split([';', '\n', '\r'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        s.MatchPhrases = phrases.Length == 0 ? null : string.Join("; ", phrases);
    }

    // ── Части ─────────────────────────────────────────────────────────────────

    public async Task<SeriesSaveResult> AddPartAsync(int seriesId, SeriesPartInputDto input)
    {
        if (!await db.CompetitionSeries.AnyAsync(s => s.Id == seriesId))
            return SeriesSaveResult.Fail($"Серия #{seriesId} не найдена");
        if (await ValidatePartAsync(seriesId, input, null) is { } error) return SeriesSaveResult.Fail(error);
        var part = new CompetitionSeriesPart { SeriesId = seriesId };
        Apply(part, input);
        db.CompetitionSeriesParts.Add(part);
        await db.SaveChangesAsync();
        return SeriesSaveResult.Ok(part.Id);
    }

    public async Task<SeriesSaveResult> UpdatePartAsync(int partId, SeriesPartInputDto input)
    {
        var part = await db.CompetitionSeriesParts.FindAsync(partId);
        if (part is null) return SeriesSaveResult.Fail($"Часть #{partId} не найдена");
        if (await ValidatePartAsync(part.SeriesId, input, partId) is { } error) return SeriesSaveResult.Fail(error);
        Apply(part, input);
        await db.SaveChangesAsync();
        return SeriesSaveResult.Ok(partId);
    }

    public async Task<SeriesSaveResult> DeletePartAsync(int partId)
    {
        var part = await db.CompetitionSeriesParts.FindAsync(partId);
        if (part is null) return SeriesSaveResult.Fail($"Часть #{partId} не найдена");
        await UnbindDaysAsync(db.Competitions.Where(c => c.SeriesPartId == partId));
        db.CompetitionSeriesParts.Remove(part);
        await db.SaveChangesAsync();
        return SeriesSaveResult.Ok(partId);
    }

    private async Task<string?> ValidatePartAsync(int seriesId, SeriesPartInputDto input, int? partId)
    {
        var name = input.Name?.Trim() ?? "";
        if (name.Length == 0) return "Имя части обязательно";
        if (input.MinSeasonAge is int a && input.MaxSeasonAge is int b && a > b)
            return "Возраст «с» больше возраста «по»";
        if (await db.CompetitionSeriesParts.AnyAsync(p => p.SeriesId == seriesId && p.Name == name && p.Id != partId))
            return $"Часть «{name}» у серии уже есть";
        return null;
    }

    private static void Apply(CompetitionSeriesPart p, SeriesPartInputDto input)
    {
        p.Name = input.Name.Trim();
        p.NameEn = Blank(input.NameEn);
        p.SortOrder = input.SortOrder;
        p.MinSeasonAge = input.MinSeasonAge;
        p.MaxSeasonAge = input.MaxSeasonAge;
    }

    // ── Регламенты ────────────────────────────────────────────────────────────

    public async Task<SeriesSaveResult> AddRegulationAsync(int seriesId, SeriesRegulationInputDto input)
    {
        if (!await db.CompetitionSeries.AnyAsync(s => s.Id == seriesId))
            return SeriesSaveResult.Fail($"Серия #{seriesId} не найдена");
        if (await ValidateRegulationAsync(seriesId, input, null) is { } error) return SeriesSaveResult.Fail(error);
        var reg = new SeriesRegulation { SeriesId = seriesId };
        Apply(reg, input);
        db.SeriesRegulations.Add(reg);
        await db.SaveChangesAsync();
        return SeriesSaveResult.Ok(reg.Id);
    }

    public async Task<SeriesSaveResult> UpdateRegulationAsync(int regulationId, SeriesRegulationInputDto input)
    {
        var reg = await db.SeriesRegulations.FindAsync(regulationId);
        if (reg is null) return SeriesSaveResult.Fail($"Регламент #{regulationId} не найден");
        if (await ValidateRegulationAsync(reg.SeriesId, input, regulationId) is { } error) return SeriesSaveResult.Fail(error);
        Apply(reg, input);
        await db.SaveChangesAsync();
        return SeriesSaveResult.Ok(regulationId);
    }

    public async Task<SeriesSaveResult> CloneRegulationAsync(int regulationId, SeriesRegulationInputDto input)
    {
        var source = await db.SeriesRegulations.AsNoTracking()
            .Include(r => r.Awards).ThenInclude(a => a.Bands)
            .Include(r => r.Awards).ThenInclude(a => a.Prizes)
            .AsSplitQuery()
            .FirstOrDefaultAsync(r => r.Id == regulationId);
        if (source is null) return SeriesSaveResult.Fail($"Регламент #{regulationId} не найден");
        if (await ValidateRegulationAsync(source.SeriesId, input, null) is { } error) return SeriesSaveResult.Fail(error);

        var copy = new SeriesRegulation
        {
            SeriesId = source.SeriesId,
            Awards = source.Awards.Select(a => new SeriesAward
            {
                PartId = a.PartId, Kind = a.Kind, Title = a.Title, CountBest = a.CountBest,
                SplitByGender = a.SplitByGender, Places = a.Places, SortOrder = a.SortOrder,
                Bands = a.Bands.Select(b => new SeriesAwardBand
                    { Gender = b.Gender, MinSeasonAge = b.MinSeasonAge, MaxSeasonAge = b.MaxSeasonAge }).ToList(),
                Prizes = a.Prizes.Select(p => new SeriesAwardPrize { Place = p.Place, Text = p.Text }).ToList()
            }).ToList()
        };
        Apply(copy, input);
        db.SeriesRegulations.Add(copy);
        await db.SaveChangesAsync();
        return SeriesSaveResult.Ok(copy.Id);
    }

    public async Task<SeriesSaveResult> DeleteRegulationAsync(int regulationId)
    {
        var reg = await db.SeriesRegulations.FindAsync(regulationId);
        if (reg is null) return SeriesSaveResult.Fail($"Регламент #{regulationId} не найден");
        db.SeriesRegulations.Remove(reg);
        await db.SaveChangesAsync();
        return SeriesSaveResult.Ok(regulationId);
    }

    private async Task<string?> ValidateRegulationAsync(int seriesId, SeriesRegulationInputDto input, int? regulationId)
    {
        if (input.EffectiveFrom == default) return "Дата вступления обязательна";
        if (await db.SeriesRegulations.AnyAsync(r =>
                r.SeriesId == seriesId && r.EffectiveFrom == input.EffectiveFrom && r.Id != regulationId))
            return $"Версия регламента с {input.EffectiveFrom:dd.MM.yyyy} уже есть";
        if (Blank(input.SourceUrl) is { } url && !Uri.TryCreate(url, UriKind.Absolute, out _))
            return "Ссылка на регламент — полный адрес (https://…)";
        return null;
    }

    private static void Apply(SeriesRegulation r, SeriesRegulationInputDto input)
    {
        r.EffectiveFrom = input.EffectiveFrom;
        r.SourceUrl = Blank(input.SourceUrl);
        r.Description = Blank(input.Description);
    }

    // ── Награды ───────────────────────────────────────────────────────────────

    public async Task<SeriesSaveResult> AddAwardAsync(int regulationId, SeriesAwardInputDto input)
    {
        var reg = await db.SeriesRegulations.FindAsync(regulationId);
        if (reg is null) return SeriesSaveResult.Fail($"Регламент #{regulationId} не найден");
        if (await ValidateAwardAsync(reg.SeriesId, input) is { } error) return SeriesSaveResult.Fail(error);
        var award = new SeriesAward { RegulationId = regulationId };
        Apply(award, input);
        db.SeriesAwards.Add(award);
        await db.SaveChangesAsync();
        return SeriesSaveResult.Ok(award.Id);
    }

    public async Task<SeriesSaveResult> UpdateAwardAsync(int awardId, SeriesAwardInputDto input)
    {
        var award = await db.SeriesAwards
            .Include(a => a.Regulation).Include(a => a.Bands).Include(a => a.Prizes)
            .FirstOrDefaultAsync(a => a.Id == awardId);
        if (award is null) return SeriesSaveResult.Fail($"Награда #{awardId} не найдена");
        if (await ValidateAwardAsync(award.Regulation.SeriesId, input) is { } error) return SeriesSaveResult.Fail(error);
        db.SeriesAwardBands.RemoveRange(award.Bands);
        db.SeriesAwardPrizes.RemoveRange(award.Prizes);
        Apply(award, input);
        await db.SaveChangesAsync();
        return SeriesSaveResult.Ok(awardId);
    }

    public async Task<SeriesSaveResult> DeleteAwardAsync(int awardId)
    {
        var award = await db.SeriesAwards.FindAsync(awardId);
        if (award is null) return SeriesSaveResult.Fail($"Награда #{awardId} не найдена");
        db.SeriesAwards.Remove(award);
        await db.SaveChangesAsync();
        return SeriesSaveResult.Ok(awardId);
    }

    private async Task<string?> ValidateAwardAsync(int seriesId, SeriesAwardInputDto input)
    {
        if (string.IsNullOrWhiteSpace(input.Title)) return "Заголовок награды обязателен";
        if (input.Places < 1) return "Призёров должно быть не меньше одного";
        if (input.CountBest is < 1) return "«Лучших стартов» — не меньше одного (или пусто — все)";
        if (input.Prizes.Any(p => p.Place > input.Places))
            return $"Приз за место больше числа призёров ({input.Places})";
        // Часть и регламент — из одной серии: иначе награда висела бы на чужом турнире.
        if (!await db.CompetitionSeriesParts.AnyAsync(p => p.Id == input.PartId && p.SeriesId == seriesId))
            return "Часть не из этой серии";
        return null;
    }

    private static void Apply(SeriesAward a, SeriesAwardInputDto input)
    {
        a.PartId = input.PartId;
        a.Kind = SeriesAwardKinds.PointsSum;
        a.Title = input.Title.Trim();
        a.CountBest = input.CountBest;
        a.SplitByGender = input.SplitByGender;
        a.Places = input.Places;
        a.SortOrder = input.SortOrder;
        a.Bands = input.Bands.Select(b => new SeriesAwardBand
            { Gender = b.Gender, MinSeasonAge = b.MinSeasonAge, MaxSeasonAge = b.MaxSeasonAge }).ToList();
        a.Prizes = input.Prizes.Select(p => new SeriesAwardPrize { Place = p.Place, Text = p.Text.Trim() }).ToList();
    }

    // ── Привязка дней ─────────────────────────────────────────────────────────

    public async Task<SeriesSaveResult> SetCompetitionPartAsync(int competitionId, int? partId)
    {
        var comp = await db.Competitions.FindAsync(competitionId);
        if (comp is null) return SeriesSaveResult.Fail($"Соревнование #{competitionId} не найдено");
        if (partId is int p && !await db.CompetitionSeriesParts.AnyAsync(x => x.Id == p))
            return SeriesSaveResult.Fail($"Часть #{p} не найдена");
        if (comp.SeriesPartId == partId) return SeriesSaveResult.Ok(competitionId);

        comp.SeriesPartId = partId;
        comp.SeriesRegulationCheckedAt = null;
        comp.SeriesRegulationCheckedBy = null;
        await db.SaveChangesAsync();
        return SeriesSaveResult.Ok(competitionId);
    }

    public async Task<SeriesSaveResult> ToggleRegulationCheckedAsync(int competitionId, string? user)
    {
        var head = await db.Competitions.AsNoTracking()
            .Where(c => c.Id == competitionId)
            .Select(c => new { c.Id, c.EventId, SeriesId = c.SeriesPart != null ? (int?)c.SeriesPart.SeriesId : null })
            .FirstOrDefaultAsync();
        if (head is null) return SeriesSaveResult.Fail($"Соревнование #{competitionId} не найдено");
        if (head.SeriesId is not int seriesId) return SeriesSaveResult.Fail("День не привязан к серии");

        // Регламент подтверждают у выпуска целиком — всем дням события этой серии.
        var days = head.EventId is int ev
            ? await db.Competitions.Where(c => c.EventId == ev && c.SeriesPart!.SeriesId == seriesId).ToListAsync()
            : await db.Competitions.Where(c => c.Id == head.Id).ToListAsync();

        var clearing = days.Any(d => d.SeriesRegulationCheckedAt != null);
        foreach (var d in days)
        {
            d.SeriesRegulationCheckedAt = clearing ? null : DateTime.UtcNow;
            d.SeriesRegulationCheckedBy = clearing ? null : user;
        }
        await db.SaveChangesAsync();
        return SeriesSaveResult.Ok(clearing ? 0 : 1);
    }

    public async Task<IReadOnlyList<SeriesPartOptionDto>> GetPartOptionsAsync() =>
        await db.CompetitionSeriesParts.AsNoTracking()
            .OrderBy(p => p.Series.Name).ThenBy(p => p.SortOrder)
            .Select(p => new SeriesPartOptionDto(p.Id, p.Series.Name, p.Name))
            .ToListAsync();

    public Task<int?> GetCompetitionPartIdAsync(int competitionId) =>
        db.Competitions.AsNoTracking().Where(c => c.Id == competitionId).Select(c => c.SeriesPartId).FirstOrDefaultAsync();

    private async Task UnbindDaysAsync(IQueryable<Competition> days)
    {
        foreach (var d in await days.ToListAsync())
        {
            d.SeriesPartId = null;
            d.SeriesRegulationCheckedAt = null;
            d.SeriesRegulationCheckedBy = null;
        }
    }

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static DateTime ParseDate(string date) =>
        DateTime.TryParseExact(date, "dd/MM/yyyy", System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var d) ? d : DateTime.MinValue;
}
