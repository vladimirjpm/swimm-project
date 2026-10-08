using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Swimm.Application.Abstractions;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Services;

/// <inheritdoc cref="ICompetitionDayJoinService"/>
/// <remarks>
/// Те же решения, что у деления (multiday-split-plan §6): имя дня = имя события, исходное
/// название — в <c>SubName</c> (Р-сп-1); compID остаётся на своём дне и поднимается на событие
/// (Р-сп-2, Д2). Только через EF: сохранение само сбрасывает метки кэша (перехватчик К4).
/// </remarks>
public sealed class CompetitionDayJoinService(
    SwimmDbContext db,
    ICompetitionRecalculationService recalc) : ICompetitionDayJoinService
{
    private const string DateFormat = "dd/MM/yyyy";

    public async Task<CompetitionDayJoinReport> JoinAsync(
        IReadOnlyList<int> competitionIds, bool apply, CancellationToken ct = default)
    {
        var ids = competitionIds.Distinct().ToList();
        if (ids.Count < 2)
            return CompetitionDayJoinReport.Refused("", "нужно минимум два соревнования");

        var comps = await db.Competitions.AsNoTracking().Where(c => ids.Contains(c.Id)).ToListAsync(ct);
        var name = comps.FirstOrDefault()?.Name ?? "";
        var missing = ids.Except(comps.Select(c => c.Id)).ToList();
        if (missing.Count > 0)
            return CompetitionDayJoinReport.Refused(name, $"не найдены: {string.Join(", ", missing.Select(id => $"#{id}"))}");

        if (comps.FirstOrDefault(c => c.EventId != null) is { } inEvent)
            return CompetitionDayJoinReport.Refused(name, $"#{inEvent.Id} уже день события #{inEvent.EventId}");

        // Один старт — одно имя и один бассейн; иначе это разные соревнования, и собирать их
        // в событие — решение не для команды.
        if (comps.Select(c => c.Name.Trim()).Distinct().Count() > 1)
            return CompetitionDayJoinReport.Refused(name, "у соревнований разные имена: " +
                string.Join(" / ", comps.Select(c => $"#{c.Id} «{c.Name}»")));
        if (comps.Select(c => c.PoolType).Distinct().Count() > 1)
            return CompetitionDayJoinReport.Refused(name, "у соревнований разный бассейн: " +
                string.Join(", ", comps.Select(c => $"#{c.Id} {c.PoolType}")));

        var dated = comps.Select(c => (Comp: c, Date: ParseDate(c.Date))).ToList();
        if (dated.FirstOrDefault(d => d.Date == null) is { Comp: { } undated })
            return CompetitionDayJoinReport.Refused(name, $"у #{undated.Id} дата «{undated.Date}» не читается");
        if (dated.GroupBy(d => d.Date).FirstOrDefault(g => g.Count() > 1) is { } sameDay)
            return CompetitionDayJoinReport.Refused(name, $"два соревнования на {Format(sameDay.Key!.Value)} — " +
                "день события должен быть один на дату");

        // compID у события один (Д2): два штампа — два разных старта сайта.
        var orgIds = comps.Where(c => c.OrgCompId != null).Select(c => c.OrgCompId!.Value).Distinct().ToList();
        if (orgIds.Count > 1)
            return CompetitionDayJoinReport.Refused(name, $"у дней разные compID ({string.Join(", ", orgIds)}) — это разные старты");
        int? orgCompId = orgIds.Count == 1 ? orgIds[0] : null;
        if (orgCompId is int org && await db.CompetitionEvents.AnyAsync(e => e.OrgCompId == org, ct))
            return CompetitionDayJoinReport.Refused(name, $"событие с compID {org} уже есть");

        // Строки не двигаются, поэтому дата строки обязана уже совпадать с датой её дня
        // (инвариант InvariantDataChecks) — иначе сначала разобраться, что лежит не там.
        var rowStats = await db.Results.AsNoTracking()
            .Where(r => ids.Contains(r.CompetitionId))
            .GroupBy(r => new { r.CompetitionId, r.CompetitionDate })
            .Select(g => new { g.Key.CompetitionId, g.Key.CompetitionDate, Count = g.Count() })
            .ToListAsync(ct);
        foreach (var (comp, date) in dated)
        {
            var foreign = rowStats.Where(s => s.CompetitionId == comp.Id
                                              && DateOnly.FromDateTime(s.CompetitionDate) != date).Sum(s => s.Count);
            if (foreign > 0)
                return CompetitionDayJoinReport.Refused(name,
                    $"у #{comp.Id} ({comp.Date}) {foreign} строк с другой датой — строки команда не двигает");
        }

        var days = dated
            .OrderBy(d => d.Date)
            .Select((d, i) => new CompetitionDayJoinDay(d.Comp.Id, d.Date!.Value, i + 1, d.Comp.OrgCompId,
                rowStats.Where(s => s.CompetitionId == d.Comp.Id).Sum(s => s.Count)))
            .ToList();
        var differences = await DifferencesAsync(comps, ct);

        if (!apply)
            return new CompetitionDayJoinReport(name, orgCompId, days, differences, null, false, null);

        var eventId = await ApplyAsync(name, orgCompId, days, ct);

        // Зачёт события строится по всем дням и ложится на первый; объединённые места и
        // пара-очки пересчёт берёт по событию сам.
        foreach (var day in days)
            await recalc.RecalculateCompetitionAsync(day.CompetitionId, ct);

        return new CompetitionDayJoinReport(name, orgCompId, days, differences, null, true, eventId);
    }

    private async Task<int> ApplyAsync(string name, int? orgCompId, IReadOnlyList<CompetitionDayJoinDay> days, CancellationToken ct)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            var ev = new CompetitionEvent
            {
                Name = name,
                OrgCompId = orgCompId,
                StartDate = days[0].Date,
                EndDate = days[^1].Date,
            };
            db.CompetitionEvents.Add(ev);
            await db.SaveChangesAsync(ct);

            var ids = days.Select(d => d.CompetitionId).ToArray();
            var comps = await db.Competitions.Where(c => ids.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct);
            foreach (var day in days)
            {
                var comp = comps[day.CompetitionId];
                comp.EventId = ev.Id;
                comp.DayNumber = day.DayNumber;
                comp.SubName ??= comp.Name;
            }
            await db.SaveChangesAsync(ct);

            await tx.CommitAsync(ct);
            return ev.Id;
        });
    }

    /// <summary>
    /// Настройки, которые у дней расходятся. Сбор их не трогает: правило очков ставится на
    /// день (admin-pages/import.md), а зачёт события суммирует дни — каждый по своему правилу.
    /// </summary>
    private async Task<List<string>> DifferencesAsync(List<Competition> comps, CancellationToken ct)
    {
        var ids = comps.Select(c => c.Id).ToArray();
        var cats = await db.CategoryCompetitions.AsNoTracking()
            .Where(cc => ids.Contains(cc.CompetitionId))
            .ToListAsync(ct);

        var fields = new (string Name, Func<Competition, string> Value)[]
        {
            ("правило клубных очков", c => c.PointRuleClubsId?.ToString() ?? "—"),
            ("правило личных очков", c => c.PointRuleSwimmersId?.ToString() ?? "—"),
            ("зачёт не ведётся", c => c.ClubPointsDisabled ? "да" : "нет"),
            ("вид зачёта", c => c.StandingKindOverride ?? "—"),
            ("мастерс", c => c.IsMasters ? "да" : "нет"),
            ("чемпионат", c => c.IsChampionship ? "да" : "нет"),
            ("медали", c => c.IsAward ? "да" : "нет"),
            ("промежуточные", c => c.HasSplits ? "да" : "нет"),
            ("дисциплина", c => c.Discipline),
            ("категории", c => string.Join(",", cats.Where(cc => cc.CompetitionId == c.Id)
                .Select(cc => cc.CategoryId).Order()) is { Length: > 0 } s ? s : "—"),
        };

        var ordered = comps.OrderBy(c => ParseDate(c.Date)).ToList();
        return fields
            .Where(f => ordered.Select(f.Value).Distinct().Count() > 1)
            .Select(f => $"{f.Name}: " + string.Join(", ", ordered.Select(c => $"#{c.Id} {f.Value(c)}")))
            .ToList();
    }

    private static string Format(DateOnly date) => date.ToString(DateFormat, CultureInfo.InvariantCulture);

    private static DateOnly? ParseDate(string? date) =>
        DateOnly.TryParseExact(date?.Trim(), DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d
            : null;
}
