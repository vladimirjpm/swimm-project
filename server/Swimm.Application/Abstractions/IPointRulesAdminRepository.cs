using Swimm.Application.Dtos;

namespace Swimm.Application.Abstractions;

/// <summary>
/// Порт админского CRUD правил начисления очков (Admin/PointsRules) — клубных и пловца.
/// Оба вида ходят через один порт: набор операций у них одинаков, различаются только поля
/// правила (см. <see cref="PointRuleInputDto"/>).
///
/// Удаление запрещено, пока на правило ссылается хотя бы одно соревнование (FK RESTRICT
/// всё равно оборвал бы — отдаём понятную ошибку заранее, как в Admin/Styles).
/// </summary>
public interface IPointRulesAdminRepository
{
    /// <summary>Все правила вида, свежие сверху, с числом привязанных соревнований.</summary>
    Task<IReadOnlyList<PointRuleRowDto>> GetAllAsync(PointRuleKind kind);

    /// <summary>Данные для формы Edit вместе со шкалой. null — не найдено.</summary>
    Task<PointRuleEditDto?> GetByIdAsync(PointRuleKind kind, int id);

    /// <summary>Создать правило со шкалой. Ошибка — при пустой/занятой версии и кривых полях.</summary>
    Task<PointRuleSaveResult> CreateAsync(PointRuleKind kind, PointRuleInputDto input);

    /// <summary>Обновить правило; шкала перезаписывается целиком.</summary>
    Task<PointRuleSaveResult> UpdateAsync(PointRuleKind kind, int id, PointRuleInputDto input);

    /// <summary>
    /// Соревнования с ЯВНОЙ привязкой к правилу (многодневные — одной строкой), свежие сверху.
    /// Подобранные автоподбором сюда не попадают: панель правит только то, что реально записано
    /// в FK, и её длина совпадает со счётчиком в списке правил.
    /// </summary>
    Task<IReadOnlyList<PointRuleCompetitionRowDto>> GetCompetitionsAsync(PointRuleKind kind, int ruleId);

    /// <summary>
    /// Сменить правило нужного вида у перечисленных соревнований (у многодневных — всем дням).
    /// В Id результата — число реально изменённых логических соревнований (0 — правок не было).
    /// </summary>
    Task<PointRuleSaveResult> ReassignCompetitionsAsync(
        PointRuleKind kind, IReadOnlyList<PointRuleReassignItem> items);

    /// <summary>
    /// Поставить/снять отметку ручной проверки очков у соревнования (у многодневного — всем дням).
    /// <paramref name="verifiedKind"/> — <c>official</c> или <c>accepted</c>
    /// (см. <see cref="Swimm.Domain.Entities.PointsVerifiedKinds"/>); состояния взаимоисключающие,
    /// повторное нажатие того же снимает отметку, нажатие другого — переключает на него.
    /// Отметка своя у каждого вида очков и на расчёт не влияет — это админская памятка.
    /// В Id результата — 1, если отметка теперь стоит, и 0, если снята.
    /// </summary>
    Task<PointRuleSaveResult> ToggleVerifiedAsync(
        PointRuleKind kind, int competitionId, string verifiedKind, string? user);

    /// <summary>Удалить. Отказ — если на правило ссылаются соревнования.</summary>
    Task<PointRuleSaveResult> DeleteAsync(PointRuleKind kind, int id);
}
