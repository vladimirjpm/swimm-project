using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;

namespace Swimm.Infrastructure.Services.DataChecks;

// Серии турниров и официальное награждение (docs/plans/official-awards-plan.md §5.8, шаг 3):
// страховка автопривязки при импорте. Диагноз — ISeriesBinder, тот же, что привязывает.

/// <summary>Название подходит под фразу серии, а день к ней не привязан.</summary>
public sealed class SeriesUnboundMatchCheck(ISeriesBinder binder) : IDataCheck
{
    public string Id => "series.unbound-match";
    public string Title => "Выпуск серии не привязан";
    public string Description => "Название дня содержит ключевую фразу серии турнира, но день не привязан к её части — официальных наград на витрине нет. Обычно часть не определилась по возрастам или день импортирован до заведения серии. Привязывается на /Admin/Series.";
    public DataCheckSeverity Severity => DataCheckSeverity.Warning;

    public async Task<DataCheckOutcome> RunAsync(CancellationToken ct = default)
    {
        var rows = (await binder.EvaluateAsync(ct: ct)).Where(b => b.CurrentPartId is null).ToList();
        return new DataCheckOutcome(rows.Count, rows
            .Take(50)
            .Select(b => new DataCheckItem(
                "Competition", b.CompetitionId,
                $"#{b.CompetitionId} {b.Date} — «{b.SeriesName ?? "?"}»" +
                    (b.PickedPartName is { } p ? $", часть по возрастам: {p}" : ""),
                b.Reason,
                b.SeriesId is int sid ? $"/Admin/Series/Edit?id={sid}" : "/Admin/Series",
                PublicRoutes.Competition(b.CompetitionId),
                SubjectName: b.CompetitionName))
            .ToList());
    }
}

/// <summary>День привязан к части, а возраста его пловцов на неё не похожи.</summary>
public sealed class SeriesPartMismatchCheck(ISeriesBinder binder) : IDataCheck
{
    public string Id => "series.part-mismatch";
    public string Title => "Часть серии не по возрастам дня";
    public string Description => "День привязан к части серии, но меньше 80% его пловцов попадают в её возраста — скорее всего, перепутаны дни («צעירים» и «נוער ובוגרים»), и награды считаются не по тому регламенту. Проверьте привязку на /Admin/Series.";
    public DataCheckSeverity Severity => DataCheckSeverity.Warning;

    public async Task<DataCheckOutcome> RunAsync(CancellationToken ct = default)
    {
        var rows = (await binder.EvaluateAsync(ct: ct))
            .Where(b => b.CurrentPartShare is double share && share < Application.Mapping.SeriesMatch.PartShareThreshold)
            .ToList();
        return new DataCheckOutcome(rows.Count, rows
            .Take(50)
            .Select(b => new DataCheckItem(
                "Competition", b.CompetitionId,
                $"#{b.CompetitionId} {b.Date} — часть «{b.CurrentPartName}»: {b.CurrentPartShare:P0} пловцов дня в её возрастах",
                b.PickedPartName is { } p ? $"по возрастам подходит «{p}» ({b.Share:P0})" : b.Reason,
                $"/Admin/Series/Edit?id={b.SeriesId}",
                PublicRoutes.Competition(b.CompetitionId),
                SubjectName: b.CompetitionName))
            .ToList());
    }
}

/// <summary>
/// Выпуск привязан, а регламент для него не подтверждён (§5.8, шаг 4): привязка доказывает, что
/// турнир тот же, но не что регламент не поменялся — витрина может показывать прошлогодние призы.
/// </summary>
public sealed class SeriesRegulationUncheckedCheck(ISeriesBinder binder) : IDataCheck
{
    public string Id => "series.regulation-unchecked";
    public string Title => "Регламент выпуска не подтверждён";
    public string Description => "День привязан к серии и витрина уже показывает «Official awards» по действующей версии регламента, но никто не сверил её с PDF этого выпуска. Сверьте и нажмите «Регламент подтверждён» на /Admin/Series (или заведите новую версию).";
    public DataCheckSeverity Severity => DataCheckSeverity.Info;

    public async Task<DataCheckOutcome> RunAsync(CancellationToken ct = default)
    {
        var rows = (await binder.EvaluateAsync(ct: ct))
            .Where(b => b.CurrentPartId is not null && b.HasRegulation && b.RegulationCheckedAt is null)
            .ToList();
        return new DataCheckOutcome(rows.Count, rows
            .Take(50)
            .Select(b => new DataCheckItem(
                "Competition", b.CompetitionId,
                $"#{b.CompetitionId} {b.Date} — «{b.SeriesName}» · {b.CurrentPartName}",
                null,
                $"/Admin/Series/Edit?id={b.SeriesId}",
                PublicRoutes.Competition(b.CompetitionId),
                SubjectName: b.CompetitionName))
            .ToList());
    }
}
