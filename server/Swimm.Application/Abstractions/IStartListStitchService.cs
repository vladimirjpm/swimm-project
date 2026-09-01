using Swimm.Application.Dtos;

namespace Swimm.Application.Abstractions;

/// <summary>
/// Сшивка заявок с результатами (docs/plans/start-list-plan.md, шаг С9): когда протокол дня
/// импортирован, каждая заявка получает свой результат либо становится неявкой.
///
/// Это же и есть плата за решение «заявки не стирать»: заявка без результата — не мусор,
/// а единственный ответ на вопрос «почему в протоколе меньше, чем заявлено».
/// </summary>
public interface IStartListStitchService
{
    /// <summary>Сшить заявки одного соревнования по его <c>orgCompId</c> (compID на isr.org.il).</summary>
    Task<StartListStitchReport> StitchAsync(int orgCompId, CancellationToken ct = default);

    /// <summary>
    /// Сшить всё, что относится к этим дням справочника, — точка вызова из конца импорта.
    /// Дни превращаются в <c>orgCompId</c> внутри: у многодневки штамп стоит на событии,
    /// у однодневного — на самом дне.
    /// </summary>
    Task<IReadOnlyList<StartListStitchReport>> StitchCompetitionsAsync(
        IReadOnlyCollection<int> competitionIds, CancellationToken ct = default);
}
