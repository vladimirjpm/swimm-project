using System.Text.Json.Serialization;

namespace Swimm.Application.Dtos;

// snake_case, как у всей выдачи витрины (StartListDtos, FavoriteDto): типы клиента
// зеркалят эти записи один в один.

/// <summary>
/// Персональный план пользователя на соревнование (шаг Т3): за кем он следит в табе
/// Start list и две галочки.
/// </summary>
/// <param name="SwimmerIds">Выбранные пловцы. Пустой список — «выбрано никого» И ЭТО НЕ ТО ЖЕ,
/// что отсутствие плана: у плана, которого нет, витрина подставляет избранных, а пустой
/// сохранённый план значит «я всё снял сам» (§Т5).</param>
/// <param name="NotifyMe">Кнопка «Notify me when it's out» нажата. Рассылки за этим пока нет.</param>
public sealed record StartListPlanDto(
    [property: JsonPropertyName("org_comp_id")] int OrgCompId,
    [property: JsonPropertyName("swimmer_ids")] IReadOnlyList<int> SwimmerIds,
    [property: JsonPropertyName("club_ids")] IReadOnlyList<int> ClubIds,
    [property: JsonPropertyName("im_coming")] bool ImComing,
    [property: JsonPropertyName("notify_me")] bool NotifyMe,
    [property: JsonPropertyName("updated_at")] DateTime UpdatedAt);

/// <summary>
/// Что клиент присылает на сохранение. Состав приходит ЦЕЛИКОМ (а не «добавь одного»):
/// пикер — экран множественного выбора, и частичные операции здесь только плодили бы
/// расхождение между тем, что видно, и тем, что сохранено.
/// </summary>
public sealed record StartListPlanSaveRequest(
    [property: JsonPropertyName("swimmer_ids")] IReadOnlyList<int>? SwimmerIds,
    [property: JsonPropertyName("club_ids")] IReadOnlyList<int>? ClubIds,
    [property: JsonPropertyName("im_coming")] bool ImComing,
    [property: JsonPropertyName("notify_me")] bool NotifyMe);
