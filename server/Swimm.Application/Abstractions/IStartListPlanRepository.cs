using Swimm.Application.Dtos;

namespace Swimm.Application.Abstractions;

/// <summary>
/// Персональный план на соревнование (docs/plans/start-list-ticket-plan.md, шаг Т3) —
/// хранилище залогиненного пользователя. У гостя ту же роль играет localStorage браузера,
/// и разницу прячет один клиентский шов <c>useStartListPlan</c>.
///
/// Данные приватные: план говорит, где будет ребёнок и придёт ли родитель. Ходит через
/// <c>SwimmDbContext</c> (роль swimm_rw), таблица <c>Sys_UserStartListPlans</c> без гранта
/// публичной роли.
/// </summary>
public interface IStartListPlanRepository
{
    /// <summary>
    /// План пользователя на это соревнование. <b>null — плана нет вовсе</b>, и это НЕ то же
    /// самое, что план с пустым составом: в первом случае витрина подставляет избранных, во
    /// втором — уважает то, что человек всё снял сам.
    /// </summary>
    Task<StartListPlanDto?> GetAsync(int userId, int orgCompId, CancellationToken ct = default);

    /// <summary>Все планы пользователя — для будущих экранов «мои старты»; порядок по свежести.</summary>
    Task<IReadOnlyList<StartListPlanDto>> GetAllAsync(int userId, CancellationToken ct = default);

    /// <summary>Сохранить состав целиком (создать или переписать) и вернуть, что получилось.</summary>
    Task<StartListPlanDto> SaveAsync(
        int userId, int orgCompId, StartListPlanSaveRequest request, CancellationToken ct = default);

    /// <summary>Забыть план на это соревнование. false — его и не было.</summary>
    Task<bool> DeleteAsync(int userId, int orgCompId, CancellationToken ct = default);
}
