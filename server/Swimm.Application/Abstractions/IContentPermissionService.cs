using Swimm.Application.Dtos;
using Swimm.Application.Mapping;

namespace Swimm.Application.Abstractions;

/// <summary>
/// Права пользователя на контент (docs/plans/content-permissions-plan.md): читает его
/// исключения и политику по умолчанию, решает — <see cref="ContentPermissionRules"/>.
/// Эндпоинты записи медиа спрашивают его первым делом и отвечают 403 с текстом причины.
/// </summary>
public interface IContentPermissionService
{
    /// <summary>Все три права разом (для <c>/auth/me</c>). Нет пользователя — всё запрещено.</summary>
    Task<ContentPermissionsDto> GetAsync(int userId, bool isAdmin);

    /// <summary>
    /// null — можно; иначе текст отказа для ответа 403. Проверяются ВСЕ переданные действия,
    /// отказ — по первому запрещённому.
    /// </summary>
    Task<string?> DenyReasonAsync(int userId, bool isAdmin, params ContentAction[] actions);
}
