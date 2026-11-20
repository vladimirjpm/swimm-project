using Swimm.Application.Dtos;

namespace Swimm.Application.Abstractions;

/// <summary>
/// Строки results-auto «ждёт тебя» (правило <c>ResultsAutoWaiting</c>) — для счётчика и панели
/// «Требует внимания» мониторинга /Admin/Jobs. Дешёвый: опрашивается раз в 30 с, в том числе из
/// ProdAdmin против прод-базы — фильтр по вердикту и окну в SQL, матч с соревнованиями только когда
/// кандидаты есть.
/// </summary>
public interface IResultsAutoWaitingQuery
{
    Task<IReadOnlyList<ResultsAutoWaitingRow>> ListAsync(DateTime now, CancellationToken ct = default);
}
