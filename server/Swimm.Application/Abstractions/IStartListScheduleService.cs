using Swimm.Application.Dtos;

namespace Swimm.Application.Abstractions;

/// <summary>
/// Плановый обход стартовых протоколов (docs/plans/start-list-plan.md, шаг С10):
/// дочитать <c>logligId</c> будущим стартам, у которых его ещё нет, и затянуть у них
/// стартовый протокол.
///
/// Живёт в Application/Infrastructure, а не внутри <c>BackgroundService</c>, сознательно:
/// фоновому сервису в слое API положено быть расписанием, а не бизнес-логикой. Иначе он
/// начинает сам ходить в <c>SwimmDbContext</c> — а это ровно то, что запрещает правило
/// проекта «API инжектит только интерфейсы Application» (CLAUDE.md). Побочная выгода —
/// обход тестируется без ссылки тестов на веб-проект.
/// </summary>
public interface IStartListScheduleService
{
    /// <summary>
    /// Один тик по окну <paramref name="daysAhead"/> дней вперёд: тянет только тех, кому по
    /// правилу частоты (<c>StartListSweepRules</c>, П3) пора. Ошибка на одном соревновании не
    /// роняет обход.
    /// </summary>
    /// <param name="previousRunAt">
    /// Начало прошлого успешного прогона задачи (<c>Sys_JobRuns</c>): по нему решается, нужна ли в
    /// этот тик суточная догрузка loglig-id. null — нужна.
    /// </param>
    Task<StartListSweepReport> RunAsync(int daysAhead, DateTime? previousRunAt = null, CancellationToken ct = default);
}
