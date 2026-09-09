using Swimm.Application.Dtos;

namespace Swimm.Application.Abstractions;

/// <summary>
/// Запись настроек отображения страницы коллектива (клуб или группа).
///
/// Права здесь НЕ проверяются — это делает контроллер: у клуба и группы они разные
/// (у клуба владельцев не существует, решает админ сайта; у группы — владелец/админ группы
/// или админ сайта). Репозиторий только пишет.
/// </summary>
public interface IEntityDisplayRepository
{
    /// <summary>Настройки клуба. false — клуба нет.</summary>
    Task<bool> UpdateClubAsync(int clubId, EntityDisplayInputDto input, CancellationToken ct = default);

    /// <summary>Настройки группы. false — группы нет.</summary>
    Task<bool> UpdateGroupAsync(int hubGroupId, EntityDisplayInputDto input, CancellationToken ct = default);
}
