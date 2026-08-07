using Microsoft.EntityFrameworkCore;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Services.DataChecks;

/// <summary>Пловцы-сироты: ничего на них не ссылается (в т.ч. ноги эстафет — урок И-2).</summary>
public sealed class SwimmerOrphanCheck(ISwimmerDedupService dedup) : IDataCheck
{
    public string Id => "swimmers.orphans";
    public string Title => "Пловцы-сироты";
    public string Description => "Ни результатов, ни ног эстафет, ни групп, ни избранного, ни аккаунта. Удаляются кнопкой на /Admin/Swimmers.";
    public DataCheckSeverity Severity => DataCheckSeverity.Info;

    public async Task<DataCheckOutcome> RunAsync(CancellationToken ct = default)
    {
        var report = await dedup.FindCandidatesAsync(ct);
        return new DataCheckOutcome(report.Orphans.Count, report.Orphans
            .Take(50)
            .Select(o => new DataCheckItem(
                "Swimmer", o.Id, $"{o.Name} ({o.BirthYear})", o.Club, $"/Admin/Swimmers/Edit?id={o.Id}",
                PublicRoutes.Swimmer(o.Id)))
            .ToList());
    }
}

/// <summary>Уверенные дубли пловцов — ждут склейки.</summary>
public sealed class SwimmerDedupCheck(ISwimmerDedupService dedup) : IDataCheck
{
    public string Id => "swimmers.dedup-sure";
    public string Title => "Уверенные дубли пловцов";
    public string Description => "Пары с почти одинаковым именем, годом и полом. Склеиваются на /Admin/Swimmers; перед merge смотри на общий заплыв — близнецы не дубли.";
    public DataCheckSeverity Severity => DataCheckSeverity.Warning;

    public async Task<DataCheckOutcome> RunAsync(CancellationToken ct = default)
    {
        var report = await dedup.FindCandidatesAsync(ct);
        var sure = report.Candidates.Where(c => c.Sure).ToList();
        return new DataCheckOutcome(sure.Count, sure
            .Take(50)
            .Select(c => new DataCheckItem(
                "Swimmer", c.CanonicalId,
                $"{c.CanonicalName} #{c.CanonicalId} ← #{c.DuplicateId} {c.DuplicateName}",
                $"год {c.BirthYear}, расстояние имён {c.Distance}, {c.CanonicalResults} vs {c.DuplicateResults} результатов",
                "/Admin/Swimmers?filter=dedup-sure",
                // Ведём на КАНОН: перед merge смотрят его заплывы, а не дубля.
                PublicRoutes.Swimmer(c.CanonicalId)))
            .ToList());
    }
}

/// <summary>Клубы-дубли с одинаковым именем — след импорта (инцидент И-9).</summary>
public sealed class ClubDedupCheck(IClubDedupService dedup) : IDataCheck
{
    public string Id => "clubs.dedup-sure";
    public string Title => "Уверенные дубли клубов";
    public string Description => "Одно имя у разных Id — обычно след импорта, у канона заполнен NameEn, у дубля нет. Склеивается на /Admin/Clubs.";
    public DataCheckSeverity Severity => DataCheckSeverity.Warning;

    public async Task<DataCheckOutcome> RunAsync(CancellationToken ct = default)
    {
        var report = await dedup.FindCandidatesAsync(ct);
        var sure = report.Candidates.Where(c => c.Sure).ToList();
        return new DataCheckOutcome(sure.Count, sure
            .Take(50)
            .Select(c => new DataCheckItem(
                "Club", c.CanonicalId,
                $"{c.CanonicalName} #{c.CanonicalId} ({c.CanonicalResults}) ← #{c.DuplicateId} ({c.DuplicateResults})",
                $"эвристика {c.Heuristic}",
                "/Admin/Clubs?filter=dedup-sure",
                PublicRoutes.Club(c.CanonicalId)))
            .ToList());
    }
}

/// <summary>Пустые клубы: ни пловцов, ни результатов — мусор парсера.</summary>
public sealed class EmptyClubCheck(IDataQualityService quality) : IDataCheck
{
    public string Id => "clubs.empty";
    public string Title => "Пустые клубы";
    public string Description => "Ни пловцов, ни результатов. Удаляются кнопкой «Удалить все пустые» на /Admin/Clubs.";
    public DataCheckSeverity Severity => DataCheckSeverity.Info;

    public async Task<DataCheckOutcome> RunAsync(CancellationToken ct = default)
    {
        var res = await quality.GetClubQualityAsync("no-swimmers", ct);
        return new DataCheckOutcome(res.Total, res.Items
            .Select(c => new DataCheckItem("Club", c.Id, c.Name, c.NameEn, $"/Admin/Clubs/Edit?id={c.Id}",
                PublicRoutes.Club(c.Id)))
            .ToList());
    }
}

/// <summary>
/// Соревнование с результатами, но без привязанного правила клубных очков
/// (docs/points-rules-per-competition-plan.md, §9.3).
///
/// Молчащий отказ: без FK зачёт уходит на страховочный подбор по дате и scope, а тот
/// «едет» при заведении новой версии правила — цифры прошлого сезона меняются задним
/// числом. Обнаружить это можно только по странным суммам Top Clubs, поэтому индикатор
/// и просился в реестр.
///
/// Правило ПЛОВЦОВ сознательно не проверяем: «не привязано → legacy-расчёт по FINA» —
/// легитимный режим (masters и Маккабиада живут так намеренно), и находка по каждому
/// такому соревнованию была бы ложной тревогой.
/// </summary>
public sealed class CompetitionWithoutClubPointRuleCheck(SwimmDbContext db) : IDataCheck
{
    public string Id => "competitions.no-club-point-rule";
    public string Title => "Соревнования без правила клубных очков";
    public string Description =>
        "У соревнования есть результаты, но не привязано правило клубных очков — зачёт считается " +
        "страховочным подбором по дате, и новая версия правила сдвинет цифры задним числом. " +
        "Проставляется на /Admin/Competitions/AssignRules (массово) или в форме соревнования.";
    public DataCheckSeverity Severity => DataCheckSeverity.Warning;

    public async Task<DataCheckOutcome> RunAsync(CancellationToken ct = default)
    {
        // Пустые соревнования пропускаем: считать там нечего, и про них уже кричит
        // отдельная проверка competitions.empty — две находки на одну причину только шумят.
        var rows = await db.Competitions.AsNoTracking()
            .Where(c => c.PointRuleClubsId == null && db.Results.Any(r => r.CompetitionId == c.Id))
            .Select(c => new
            {
                c.Id, c.Name, c.Date, c.IsMasters,
                Rows = db.Results.Count(r => r.CompetitionId == c.Id)
            })
            .OrderByDescending(c => c.Rows)
            .ToListAsync(ct);

        if (rows.Count == 0) return DataCheckOutcome.Empty;

        return new DataCheckOutcome(rows.Count, rows
            .Take(50)
            .Select(c => new DataCheckItem(
                "Competition", c.Id,
                c.Name,
                $"{c.Date}, строк {c.Rows}" + (c.IsMasters ? ", masters" : ""),
                $"/Admin/Competitions/Edit?id={c.Id}",
                PublicRoutes.Competition(c.Id)))
            .ToList());
    }
}

/// <summary>
/// Сверка импорта не сошлась (фаза Д1): в БД не то число строк, что в файле-протоколе.
/// Берётся ПОСЛЕДНЯЯ сверка по каждому соревнованию — предыдущие уже неактуальны.
/// </summary>
public sealed class ReconciliationMismatchCheck(SwimmDbContext db) : IDataCheck
{
    public string Id => "import.reconciliation-mismatch";
    public string Title => "Импорт разошёлся с протоколом";
    public string Description =>
        "Последняя сверка соревнования показала другое число строк, чем в файле. " +
        "Лечится переимпортом с «удалять лишние»; если расходится дата — сперва разберись с ней (И-8).";
    public DataCheckSeverity Severity => DataCheckSeverity.Error;

    public async Task<DataCheckOutcome> RunAsync(CancellationToken ct = default)
    {
        // Последний прогон сверки по каждому соревнованию: сравниваем итоговые строки
        // (EventKey = "") — по ним видно масштаб, не складывая десятки заплывов.
        var latest = await db.ImportReconciliations.AsNoTracking()
            .Where(r => r.EventKey == "")
            .GroupBy(r => r.CompetitionId)
            .Select(g => g.OrderByDescending(x => x.ImportedAt).ThenByDescending(x => x.Id).First())
            .ToListAsync(ct);

        var bad = latest.Where(r => r.Status == "mismatch").ToList();
        if (bad.Count == 0) return DataCheckOutcome.Empty;

        var names = await db.Competitions.AsNoTracking()
            .Where(c => bad.Select(b => b.CompetitionId).Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Name + " · " + c.Date, ct);

        return new DataCheckOutcome(bad.Count, bad
            .OrderBy(r => r.CompetitionId)
            .Select(r => new DataCheckItem(
                "Competition", r.CompetitionId,
                $"{names.GetValueOrDefault(r.CompetitionId, $"#{r.CompetitionId}")}: файл {r.ExpectedRows}, БД {r.ActualRows}",
                $"сверка от {r.ImportedAt:dd.MM.yyyy HH:mm}, файл {r.ImportFileName}",
                $"/Admin/Competitions?q={r.CompetitionId}",
                PublicRoutes.Competition(r.CompetitionId)))
            .ToList());
    }
}
