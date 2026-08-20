using System.Globalization;
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
                PublicRoutes.Competition(r.CompetitionId),
                // Имя отдельно — его копируют, чтобы найти пловца в интернете; плюс якорь
                // для кнопки «поставить пол» прямо из списка находок.
                SubjectName: r.SwimmerName,
                FixKind: DataCheckFixKinds.SwimmerGender,
                FixEntityId: r.SwimmerId))
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

/// <summary>
/// Сессии одного заплыва склеены в одно событие: пловец встречается в дисциплине ДВАЖДЫ,
/// и обе строки — полноценные результаты (место + время).
///
/// Откуда берётся. Чемпионаты формата «мокдамот и финал» (регламент, например loglig doc 3311)
/// разыгрывают ДВА самостоятельных зачёта: утренние заплывы по возрастным группам и вечерний
/// финал. Официально оба дают медали и клубные очки — в live-зачёте loglig это два разных
/// события (<c>AthleticsDisciplineResults/{id}</c>). А PDF-экспорт, из которого мы импортируем,
/// печатает их ОДНИМ списком, пересортированным по времени, и слова «מוקדמות»/«גמר» в файле
/// нет. Из-за этого финалист занимает в нашей таблице сразу два места подряд (1-е и 2-е),
/// а очки и медали считаются один раз вместо двух: у соревнования 1581 зачёт 29 200 против
/// официальных 40 575.
///
/// ⚠ Калибровка. Требуем у ОБЕИХ строк место и время — иначе проверка ловила бы законное
/// «протокол напечатал и снятие (NS/DQ), и результат», которым занимается
/// <see cref="UpsertKeyCollisionCheck"/>. С этим условием на живой базе находятся ровно два
/// соревнования: 1581 (747 заплывов) и 1526 (2 строки с ОДИНАКОВЫМ временем на разных
/// дорожках — другая болезнь, но тоже требует человека).
///
/// Находка — на СОРЕВНОВАНИЕ, а не на пловца: чинится оно целиком (перетягиванием из
/// другого источника), и 747 отдельных находок только утопили бы /Admin/Health.
/// Проверка ставит диагноз, лечение — отдельное осознанное действие.
/// </summary>
public sealed class MergedSessionsCheck(SwimmDbContext db) : IDataCheck
{
    public string Id => "results.merged-sessions";
    public string Title => "Сессии склеены: пловец дважды в одной дисциплине";
    public string Description =>
        "В одной дисциплине у пловца две строки, и обе — с местом и временем. Так выглядит " +
        "чемпионат «мокдамот и финал», у которого PDF-экспорт слил утреннюю и вечернюю сессии " +
        "в один список: официально это два зачёта с отдельными медалями и очками, а у нас один. " +
        "Места в таблице идут подряд у одного человека, клубный зачёт занижен. Лечится " +
        "перетягиванием соревнования из пособытийного источника loglig.";
    public DataCheckSeverity Severity => DataCheckSeverity.Error;

    public async Task<DataCheckOutcome> RunAsync(CancellationToken ct = default)
    {
        // Разметки сессий нет ни в каком виде: HeatType (наш вывод об отборе) и Round
        // (раунд из источника) пусты. Если хоть одно проставлено — строки различимы, и это
        // не наш случай. Эстафеты исключены: там «дважды» законно (две команды клуба).
        var dupes = await db.Results.AsNoTracking()
            .Where(r => r.RelayId == null && r.HeatType == null && r.Round == null
                        && r.Position != null && r.TimeMillisecond != null)
            .GroupBy(r => new
            {
                r.CompetitionId, r.StyleId, r.Distance, r.Gender, r.EventStyleAge, r.SwimmerId
            })
            .Where(g => g.Count() > 1)
            .Select(g => new { g.Key.CompetitionId, Rows = g.Count() })
            .ToListAsync(ct);

        if (dupes.Count == 0) return DataCheckOutcome.Empty;

        var byComp = dupes
            .GroupBy(d => d.CompetitionId)
            .Select(g => new { CompetitionId = g.Key, Swims = g.Count(), Rows = g.Sum(x => x.Rows) })
            .OrderByDescending(x => x.Swims)
            .ToList();

        var ids = byComp.Select(c => c.CompetitionId).Take(50).ToList();
        var comps = await db.Competitions.AsNoTracking()
            .Where(c => ids.Contains(c.Id))
            .Select(c => new { c.Id, c.Name, c.Date })
            .ToDictionaryAsync(c => c.Id, ct);

        return new DataCheckOutcome(byComp.Count, byComp
            .Take(50)
            .Select(c => new DataCheckItem(
                "Competition", c.CompetitionId,
                $"{comps.GetValueOrDefault(c.CompetitionId)?.Name ?? $"#{c.CompetitionId}"} — " +
                $"заплывов с дублем {c.Swims}, строк {c.Rows}",
                comps.GetValueOrDefault(c.CompetitionId)?.Date ?? string.Empty,
                $"/Admin/Competitions?q={c.CompetitionId}",
                PublicRoutes.Competition(c.CompetitionId)))
            .ToList());
    }
}

/// <summary>
/// Наш клубный зачёт разошёлся с ОФИЦИАЛЬНЫМ построчно.
///
/// У соревнований, затянутых из пособытийного источника loglig, рядом с каждым заплывом
/// лежит <c>Results.OfficialClubPoints</c> — сколько очков за него начислила сама федерация.
/// Проверка считает наши очки тем же движком, что и витрина
/// (<see cref="PointRulesClubsScoring.RelayPointsFor"/> по привязанному правилу), и сравнивает.
///
/// Зачем. Раньше расхождение было видно только суммой («39 562 против 40 575»), и разбирать
/// его приходилось раскопками. Сверка 1581 нашла −131 очко, размазанные по 223 строкам В ОБЕ
/// СТОРОНЫ: организатор решает, кто получает очки, по СЕКЦИИ протокола, а не по раунду —
/// у взрослых 19-99 платят мокдамот, секция «כללי» не платит вовсе (docs/data-integrity.md §10).
/// Правилами это не выводится, поэтому эталон и хранится построчно.
///
/// Находка — на СОРЕВНОВАНИЕ: чинится оно целиком (правилом, привязкой или решением
/// «расходится, принято»), а сотни строк утопили бы /Admin/Health.
/// </summary>
public sealed class OfficialClubPointsMismatchCheck(SwimmDbContext db) : IDataCheck
{
    public string Id => "results.official-club-points-mismatch";
    public string Title => "Клубные очки расходятся с официальными";
    public string Description =>
        "У соревнования есть официальные клубные очки построчно (пособытийный источник loglig), " +
        "и наш расчёт по правилу с ними не совпал. Обычная причина — регламентная тонкость, " +
        "которую движок правил не выводит из места: какие секции протокола вообще оплачиваются. " +
        "Сравниваются только строки С эталоном — эстафеты сюда не входят, их пособытийный " +
        "источник не несёт. Лечится правкой правила, привязки — либо решением «расходится, " +
        "принято как есть».";
    public DataCheckSeverity Severity => DataCheckSeverity.Warning;

    public async Task<DataCheckOutcome> RunAsync(CancellationToken ct = default)
    {
        var rows = await db.Results.AsNoTracking()
            .Where(r => r.OfficialClubPoints != null)
            .Select(r => new
            {
                r.CompetitionId,
                CompetitionName = r.Competition!.Name,
                r.Competition.Date,
                r.Competition.IsMasters,
                RuleId = r.Competition.PointRuleClubsId,
                r.Position,
                r.HeatType,
                r.TimeFail,
                IsRelay = r.RelayId != null,
                Official = r.OfficialClubPoints!.Value
            })
            .ToListAsync(ct);

        if (rows.Count == 0) return DataCheckOutcome.Empty;

        var rules = await db.PointRulesClubs.AsNoTracking().Include(r => r.Entries).ToListAsync(ct);

        var findings = new List<(int CompetitionId, string Name, string Date, int Ours, int Official, int Rows)>();
        foreach (var group in rows.GroupBy(r => r.CompetitionId))
        {
            var head = group.First();
            var rule = CompetitionRuleResolver.Resolve(
                rules, head.RuleId, head.IsMasters, ParseDate(head.Date));

            var ours = 0;
            var official = 0;
            var mismatched = 0;
            foreach (var r in group)
            {
                // Место prelim-заплыва очков не приносит (Р34) — ровно как на витрине.
                var mine = PointRulesClubsScoring.RelayPointsFor(
                    rule, r.HeatType == "prelim" ? null : r.Position, r.TimeFail, r.IsRelay);
                ours += mine;
                official += r.Official;
                if (mine != r.Official) mismatched++;
            }

            if (mismatched > 0)
                findings.Add((group.Key, head.CompetitionName, head.Date, ours, official, mismatched));
        }

        if (findings.Count == 0) return DataCheckOutcome.Empty;

        return new DataCheckOutcome(findings.Count, findings
            .OrderByDescending(f => Math.Abs(f.Ours - f.Official))
            .Take(50)
            .Select(f => new DataCheckItem(
                "Competition", f.CompetitionId,
                $"{f.Name}: наши {f.Ours}, официальные {f.Official} " +
                $"({(f.Ours - f.Official > 0 ? "+" : string.Empty)}{f.Ours - f.Official}), " +
                $"строк с расхождением {f.Rows}",
                f.Date,
                $"/Admin/Competitions?q={f.CompetitionId}",
                PublicRoutes.Competition(f.CompetitionId)))
            .ToList());
    }

    /// <summary>Дата соревнования хранится строкой «dd/MM/yyyy»; неразобранная — минимальная.</summary>
    private static DateOnly ParseDate(string date) =>
        DateOnly.TryParseExact(date, "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d
            : DateOnly.MinValue;
}
