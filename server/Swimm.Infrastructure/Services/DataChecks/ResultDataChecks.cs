using Microsoft.EntityFrameworkCore;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Services.DataChecks;

/// <summary>
/// Проверки по результатам (docs/data-integrity.md, фаза Д3). Тонкие адаптеры поверх
/// <see cref="IDataQualityService"/> — сама логика выборок остаётся там, в одном месте.
/// </summary>
public sealed class ExactDuplicateCheck(IDataQualityService quality) : IDataCheck
{
    public string Id => "results.exact-duplicate";
    public string Title => "Точные дубликаты результатов";
    public string Description =>
        "Совпадает всё: соревнование, пловец, дисциплина, заплыв, дорожка, время. " +
        "Так не бывает — дорожку в заплыве занимает один пловец один раз. Лечится переимпортом с «удалять лишние».";
    public DataCheckSeverity Severity => DataCheckSeverity.Error;

    public async Task<DataCheckOutcome> RunAsync(CancellationToken ct = default)
    {
        var res = (await quality.GetResultAnomaliesAsync(ct)).ExactDuplicates;
        return new DataCheckOutcome(res.Total, res.Items
            .Select(r => new DataCheckItem(
                "Result", (int)r.ResultId,
                $"{r.SwimmerName} · {r.Distance} {r.StyleName} · {r.Time} — копий {r.Copies}",
                $"{r.CompetitionName} (#{r.CompetitionId}), заплыв {r.Heat}, дорожка {r.Lane}",
                $"/Admin/Results/Edit?id={r.ResultId}",
                PublicRoutes.Competition(r.CompetitionId)))
            .ToList());
    }
}

/// <summary>
/// Инвариант И4: дистанция вида <c>4X50</c> бывает только у строк с <c>RelayId</c>.
/// Нарушение = личные результаты уехали в эстафетный заплыв (инцидент И-1).
/// </summary>
public sealed class RelayDistanceWithoutRelayCheck(SwimmDbContext db) : IDataCheck
{
    public string Id => "results.relay-distance-without-relay";
    public string Title => "Эстафетная дистанция у личного результата";
    public string Description =>
        "У строки дистанция вида 4X50, но она не привязана к эстафете. Обычно это личные " +
        "результаты, уехавшие в чужой заплыв из-за нераспознанного заголовка (инцидент И-1).";
    public DataCheckSeverity Severity => DataCheckSeverity.Error;

    public async Task<DataCheckOutcome> RunAsync(CancellationToken ct = default)
    {
        var q = db.Results.AsNoTracking()
            .Where(r => r.RelayId == null && r.Distance.ToUpper().Contains("X"));

        var total = await q.CountAsync(ct);
        var items = await q.OrderBy(r => r.Id).Take(50)
            .Select(r => new
            {
                r.Id, r.Distance, r.CompetitionId,
                CompetitionName = r.Competition != null ? r.Competition.Name : "",
                SwimmerName = r.Swimmer != null ? (r.Swimmer.LastName + " " + r.Swimmer.FirstName).Trim() : ""
            })
            .ToListAsync(ct);

        return new DataCheckOutcome(total, items
            .Select(r => new DataCheckItem(
                "Result", (int)r.Id,
                $"{r.SwimmerName} · дистанция {r.Distance} без эстафеты",
                $"{r.CompetitionName} (#{r.CompetitionId})",
                $"/Admin/Results/Edit?id={r.Id}",
                PublicRoutes.Competition(r.CompetitionId)))
            .ToList());
    }
}

/// <summary>Личные результаты без пола (И2): в шапке протокола его не было, у пловца тоже.</summary>
public sealed class NoGenderCheck(IDataQualityService quality) : IDataCheck
{
    public string Id => "results.no-gender";
    public string Title => "Результаты без пола";
    public string Description =>
        "Смешанный заплыв («שומרי שבת») не даёт пола в шапке, а у пловца он неизвестен. " +
        "Проставь пол пловцу и переимпортируй протокол.";
    public DataCheckSeverity Severity => DataCheckSeverity.Warning;

    public async Task<DataCheckOutcome> RunAsync(CancellationToken ct = default)
    {
        var res = (await quality.GetResultAnomaliesAsync(ct)).NoGender;
        return new DataCheckOutcome(res.Total, res.Items
            .Select(r => new DataCheckItem(
                "Result", (int)r.ResultId,
                $"{r.SwimmerName} · {r.Distance} {r.StyleName}",
                $"{r.CompetitionName} (#{r.CompetitionId})",
                $"/Admin/Swimmers/Edit?id={r.SwimmerId}",
                PublicRoutes.Competition(r.CompetitionId)))
            .ToList());
    }
}

/// <summary>FK-аномалии: результат ссылается на несуществующего пловца или клуб.</summary>
public sealed class FkAnomalyCheck(IDataQualityService quality) : IDataCheck
{
    public string Id => "results.fk-anomaly";
    public string Title => "Битые ссылки результатов";
    public string Description => "Результат ссылается на несуществующего пловца или клуб. В проде ожидаемо пусто — целостность держит FK.";
    public DataCheckSeverity Severity => DataCheckSeverity.Error;

    public async Task<DataCheckOutcome> RunAsync(CancellationToken ct = default)
    {
        var res = (await quality.GetResultAnomaliesAsync(ct)).FkAnomalies;
        return new DataCheckOutcome(res.Total, res.Items
            .Select(r => new DataCheckItem(
                "Result", (int)r.ResultId,
                $"результат #{r.ResultId}: пловец {r.SwimmerId}, клуб {r.ClubId}",
                $"соревнование #{r.CompetitionId}",
                $"/Admin/Results/Edit?id={r.ResultId}",
                PublicRoutes.Competition(r.CompetitionId)))
            .ToList());
    }
}

/// <summary>Эстафеты без единого участника — состав потерян при разборе.</summary>
public sealed class EmptyRelayCheck(IDataQualityService quality) : IDataCheck
{
    public string Id => "relays.empty";
    public string Title => "Эстафеты без состава";
    public string Description => "У эстафеты нет ни одной ноги (RelayMembers). Состав не разобрался — заплыв виден только «владельцу» строки.";
    public DataCheckSeverity Severity => DataCheckSeverity.Warning;

    public async Task<DataCheckOutcome> RunAsync(CancellationToken ct = default)
    {
        var res = (await quality.GetResultAnomaliesAsync(ct)).EmptyRelays;
        return new DataCheckOutcome(res.Total, res.Items
            .Select(r => new DataCheckItem(
                "Relay", r.RelayId,
                $"эстафета #{r.RelayId} без состава",
                $"соревнование #{r.CompetitionId}",
                null,
                PublicRoutes.Competition(r.CompetitionId)))
            .ToList());
    }
}
