using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Domain.Entities;

namespace Swimm.Application.Mapping;

/// <summary>
/// Прогон расписания <c>records-check</c>: проверить все источники, а упавшие — ещё раз после
/// паузы (16.11.2026). Первый боевой прогон на проде уронил три источника World Aquatics из
/// пяти (504 шлюза, разовый сбой DNS), а через полчаса два из них прошли. Без повтора джоб
/// считает такой прогон успешным и ждёт сутки — «проверено» у этих источников не сдвигается.
///
/// Повтор — одна дополнительная проверка каждого упавшего источника; в журнале
/// <c>Sys_RecordSourceChecks</c> остаются обе строки (failed, затем исход повтора), свежесть
/// берёт последнюю. Повтор самого файла внутри источника — <c>WorldAquaticsDownload</c>; этот
/// уровень страхует всё остальное (резолвер страницы мастерсов, isr.org.il).
/// </summary>
public static class RecordsCheckRun
{
    /// <summary>Пауза перед повтором упавших: шлюзу и DNS нужна хотя бы минута.</summary>
    public static readonly TimeSpan RetryDelay = TimeSpan.FromMinutes(1);

    /// <returns>Итог по каждому источнику: у упавших — результат повтора.</returns>
    public static async Task<IReadOnlyList<RecordSourceCheckResultDto>> RunAsync(
        IRecordSourceCheckService checks, TimeSpan retryDelay, CancellationToken ct)
    {
        var results = (await checks.CheckAllAsync(ct)).ToList();
        var failed = results
            .Select((r, i) => (r, i))
            .Where(x => x.r.Outcome == RecordSourceCheckOutcomes.Failed)
            .ToList();
        if (failed.Count == 0) return results;

        await Task.Delay(retryDelay, ct);
        foreach (var (r, i) in failed)
            results[i] = await checks.CheckAsync(r.Source, ct: ct);
        return results;
    }
}
