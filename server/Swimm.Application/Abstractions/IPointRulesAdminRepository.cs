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

    /// <summary>Удалить. Отказ — если на правило ссылаются соревнования.</summary>
    Task<PointRuleSaveResult> DeleteAsync(PointRuleKind kind, int id);
}
