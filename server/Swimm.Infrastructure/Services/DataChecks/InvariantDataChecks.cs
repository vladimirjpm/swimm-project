using Microsoft.EntityFrameworkCore;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Services.DataChecks;

/// <summary>
/// Инвариант И8: строки должны быть РАЗЛИЧИМЫ для переимпорта.
///
/// ⚠ Калибровка (важная): ключ upsert сам по себе (<c>ResultMatcher.KeyOfPersisted</c> —
/// соревнование, стиль, дистанция, пол, заплыв, дорожка, эстафета?) совпадает МАССОВО и это
/// норма: заплывы нумеруются внутри возрастной ступени, поэтому «100 комплекс, заплыв 1,
/// дорожка 4» законно встречается в ступенях 8, 9-10, 11-12 у разных пловцов. На живой базе
/// таких групп 8071 — если бы проверка ловила их, реестр кричал бы волком.
///
/// Матчер не зря берёт вторым дискриминатором SwimmerId, поэтому и проверка смотрит на
/// ключ ВМЕСТЕ с пловцом: неразличимы лишь строки одного и того же человека. На живой базе
/// таких 16 — и там есть на что посмотреть (одна строка NS, вторая с временем).
/// </summary>
public sealed class UpsertKeyCollisionCheck(SwimmDbContext db) : IDataCheck
{
    public string Id => "results.upsert-key-collision";
    public string Title => "Строки неразличимы для переимпорта";
    public string Description =>
        "У одного пловца две строки с одинаковыми соревнованием, дисциплиной, полом, заплывом и " +
        "дорожкой — переимпорт не может сопоставить их с файлом по отдельности. Бывает законно " +
        "(протокол печатает и снятие, и результат), но при «удалять лишние» такая пара ведёт " +
        "себя непредсказуемо. Ключ upsert не различает и программы (EventCategory).";
    public DataCheckSeverity Severity => DataCheckSeverity.Warning;

    public async Task<DataCheckOutcome> RunAsync(CancellationToken ct = default)
    {
        var groups = db.Results.AsNoTracking()
            .GroupBy(r => new
            {
                r.CompetitionId, r.StyleId, r.Distance, r.Gender,
                r.Heat, r.Lane, IsRelay = r.RelayId != null, r.SwimmerId
            })
            .Where(g => g.Count() > 1);

        var total = await groups.CountAsync(ct);
        if (total == 0) return DataCheckOutcome.Empty;

        var keys = await groups
            .Select(g => new { FirstId = g.Min(x => x.Id), Copies = g.Count() })
            .OrderBy(x => x.FirstId).Take(50).ToListAsync(ct);

        var ids = keys.Select(k => k.FirstId).ToList();
        var rows = await db.Results.AsNoTracking()
            .Where(r => ids.Contains(r.Id))
            .Select(r => new
            {
                r.Id, r.CompetitionId,
                CompetitionName = r.Competition != null ? r.Competition.Name : "",
                SwimmerName = r.Swimmer != null ? (r.Swimmer.LastName + " " + r.Swimmer.FirstName).Trim() : "",
                StyleName = r.Style != null ? r.Style.Name : "",
                r.Distance, r.Heat, r.Lane
            })
            .ToListAsync(ct);

        var copies = keys.ToDictionary(k => k.FirstId, k => k.Copies);
        return new DataCheckOutcome(total, rows
            .OrderBy(r => r.Id)
            .Select(r => new DataCheckItem(
                "Result", (int)r.Id,
                $"{r.SwimmerName} · {r.Distance} {r.StyleName} · заплыв {r.Heat}/{r.Lane} — строк {copies.GetValueOrDefault(r.Id)}",
                $"{r.CompetitionName} (#{r.CompetitionId})",
                $"/Admin/Results/Edit?id={r.Id}",
                PublicRoutes.Competition(r.CompetitionId)))
            .ToList());
    }
}

/// <summary>
/// Инвариант И3: у эстафетной строки пол — это пол ЗАПЛЫВА (в миксе <c>none</c>), а не пол
/// первой ноги. Признак нарушения: в составе есть и мужчины, и женщины, а строка помечена
/// male/female. Именно так выглядел бы возврат бага, из-за которого переимпорт однажды
/// наплодил дубликаты эстафет (инцидент И-4).
/// </summary>
public sealed class RelayGenderFromLegCheck(SwimmDbContext db) : IDataCheck
{
    public string Id => "relays.gender-conflict";
    public string Title => "Состав эстафеты противоречит полу заплыва";
    public string Description =>
        "Заплыв помечен конкретным полом, а в составе есть и мужчины, и женщины. Две причины: " +
        "ноги привязались к однофамильцам другого пола (частое при разборе EN-протоколов) или " +
        "пол взят с первой ноги вместо пола заплыва — а Gender входит в ключ upsert, и такая " +
        "подмена однажды превратила переимпорт в генератор дубликатов (инцидент И-4).";
    public DataCheckSeverity Severity => DataCheckSeverity.Warning;

    public async Task<DataCheckOutcome> RunAsync(CancellationToken ct = default)
    {
        var suspicious = await db.Results.AsNoTracking()
            .Where(r => r.RelayId != null && (r.Gender == "male" || r.Gender == "female"))
            .Where(r => db.RelayMembers
                .Where(m => m.RelayId == r.RelayId)
                .Select(m => m.Swimmer!.Gender)
                .Distinct()
                .Count(g => g == "male" || g == "female") > 1)
            .Select(r => new
            {
                r.Id, r.Gender, r.Distance, r.CompetitionId,
                CompetitionName = r.Competition != null ? r.Competition.Name : "",
                ClubName = r.Club != null ? r.Club.Name : ""
            })
            .Take(50)
            .ToListAsync(ct);

        return new DataCheckOutcome(suspicious.Count, suspicious
            .Select(r => new DataCheckItem(
                "Result", (int)r.Id,
                $"{r.ClubName} · {r.Distance} — помечена {r.Gender}, состав смешанный",
                $"{r.CompetitionName} (#{r.CompetitionId})",
                $"/Admin/Results/Edit?id={r.Id}",
                PublicRoutes.Competition(r.CompetitionId)))
            .ToList());
    }
}

/// <summary>
/// Два дня одного события с одинаковой датой — почти всегда след дубля: тот же протокол
/// заехал дважды (инцидент И-3). Внутри события даты дней уникальны по смыслу, на этом
/// же стоит идентичность Д2.
/// </summary>
public sealed class DuplicateEventDayCheck(SwimmDbContext db) : IDataCheck
{
    public string Id => "competitions.duplicate-day";
    public string Title => "Два дня события на одну дату";
    public string Description =>
        "У многодневного события два соревнования с одной и той же датой. Обычно это дубль " +
        "после переимпорта. На уникальности даты внутри события стоит сопоставление по compID (Д2).";
    public DataCheckSeverity Severity => DataCheckSeverity.Warning;

    public async Task<DataCheckOutcome> RunAsync(CancellationToken ct = default)
    {
        var dupes = await db.Competitions.AsNoTracking()
            .Where(c => c.EventId != null)
            .GroupBy(c => new { c.EventId, c.Date })
            .Where(g => g.Count() > 1)
            .Select(g => new { g.Key.EventId, g.Key.Date, Ids = g.Select(x => x.Id).ToList() })
            .Take(50)
            .ToListAsync(ct);

        return new DataCheckOutcome(dupes.Count, dupes
            .Select(d => new DataCheckItem(
                "CompetitionEvent", d.EventId,
                $"событие #{d.EventId}: дата {d.Date} у соревнований {string.Join(", ", d.Ids)}",
                null,
                "/Admin/Competitions",
                // На публичной стороне события нет — ведём на первый из спорных дней.
                PublicRoutes.Competition(d.Ids.First())))
            .ToList());
    }
}

/// <summary>Соревнование без единого результата — пустая карточка в справочнике.</summary>
public sealed class EmptyCompetitionCheck(SwimmDbContext db) : IDataCheck
{
    public string Id => "competitions.empty";
    public string Title => "Соревнования без результатов";
    public string Description =>
        "Карточка есть, результатов нет: импорт не дошёл или соревнование заведено вручную. " +
        "Либо затянуть протокол, либо удалить карточку.";
    public DataCheckSeverity Severity => DataCheckSeverity.Info;

    public async Task<DataCheckOutcome> RunAsync(CancellationToken ct = default)
    {
        var empty = await db.Competitions.AsNoTracking()
            .Where(c => !db.Results.Any(r => r.CompetitionId == c.Id))
            .OrderBy(c => c.Id)
            .Select(c => new { c.Id, c.Name, c.Date })
            .Take(50)
            .ToListAsync(ct);

        return new DataCheckOutcome(empty.Count, empty
            .Select(c => new DataCheckItem(
                "Competition", c.Id, $"{c.Name} · {c.Date}", null, $"/Admin/Competitions/Edit?id={c.Id}",
                PublicRoutes.Competition(c.Id)))
            .ToList());
    }
}
