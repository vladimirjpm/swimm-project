using Swimm.Application.Dtos;

namespace Swimm.Application.Abstractions;

/// <summary>
/// Забор стартового протокола соревнования (docs/plans/start-list-plan.md, шаг С4):
/// программа дня + все заплывы → заявки в <c>CompetitionEntries</c>.
/// </summary>
public interface IStartListPullService
{
    /// <summary>
    /// Затянуть стартовый протокол соревнования по его <paramref name="orgCompId"/>
    /// (compID на isr.org.il — идентичность по И7).
    ///
    /// Не бросает на ожидаемых состояниях источника: «посев ещё не сделан» и «у соревнования
    /// нет loglig-id» возвращаются статусом <c>empty</c>, а не исключением — до старта это
    /// нормальная жизнь, а не сбой, и админка должна показывать её спокойно.
    /// </summary>
    Task<StartListPullReport> PullAsync(int orgCompId, CancellationToken ct = default);
}
