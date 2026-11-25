using Swimm.Application.Dtos;
using Swimm.Application.Mapping;

namespace Swimm.Application.Abstractions;

/// <summary>
/// Р126: что изменится на сайте, если пловцу без пола проставить пол, — «всухую», до записи.
/// Ничего не пишет. Есть хоть одно серьёзное последствие — пол автоматически не пишется, случай
/// уходит человеку (находка <c>swimmers.gender-from-loglig-review</c>).
/// </summary>
public interface IGenderImpactService
{
    /// <param name="gender"><c>male</c> / <c>female</c>.</param>
    Task<GenderImpact> AssessAsync(int swimmerId, string gender, CancellationToken ct = default);
}

/// <summary>
/// Сухой расчёт карточек Overview соревнования с проставленным полом — тем же кодом, что строит
/// сами карточки (<c>ResultRepository</c>), на копии строк в памяти.
/// </summary>
public interface IGenderOverviewPreview
{
    /// <summary>null — выборка пуста (соревнования нет).</summary>
    Task<GenderOverviewPreview?> PreviewAsync(ResultFilter filter, int swimmerId, string gender);
}

/// <summary>Карточки Overview, которые зависят от пола.</summary>
public sealed record GenderOverviewCards(
    OverviewBestSwimDto? BestSwimMale,
    OverviewBestSwimDto? BestSwimFemale,
    IReadOnlyList<OverviewMedalistDto> TopMedalists,
    IReadOnlyList<OverviewMedalistDto> TopMedalistsMale,
    IReadOnlyList<OverviewMedalistDto> TopMedalistsFemale,
    IReadOnlyList<OverviewHighPointDto> HighPointAwards,
    IReadOnlyList<OverviewOfficialAwardsDayDto> OfficialAwards,
    IReadOnlyList<ClubSummaryDto> TopClubs,
    IReadOnlyList<ClubSummaryDto> TopClubsMen,
    IReadOnlyList<ClubSummaryDto> TopClubsWomen,
    IReadOnlyList<OverviewRecordDto> Records);

public sealed record GenderOverviewPreview(GenderOverviewCards Before, GenderOverviewCards After);
