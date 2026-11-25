using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Domain.Entities;

namespace Swimm.Infrastructure.Repositories;

// Р126: сухой расчёт «что изменится в Overview, если пловцу проставить пол». Карточки считает ТОТ
// ЖЕ код, что и для сайта (BuildGenderCardsAsync), — на строках в памяти, дважды: как есть и с
// полом. Мимо кэша: читается свежая выборка, в кэш ничего не кладётся.
public partial class ResultRepository : IGenderOverviewPreview
{
    /// <summary>Карточки Overview, зависящие от пола строки или пловца, — одна сборка на сайт и на сухой расчёт.</summary>
    private async Task<OverviewGenderCards> BuildGenderCardsAsync(
        OverviewData data, ResultFilter filter, IReadOnlyList<OverviewDayDto> days, List<PointRuleClubs> clubRules)
    {
        var rows = data.Rows;

        // ♂/♀ (design_handoff вариант 4) — тот же выбор с фильтром по полу.
        var bestSwim = BestSwimOf(data, rows);
        var bestSwimMale = BestSwimOf(data, rows.Where(r => r.Gender == "male"));
        var bestSwimFemale = BestSwimOf(data, rows.Where(r => r.Gender == "female"));

        var medalRows = BuildMedalRows(data, filter);
        var topMedalists = BuildMedalists(medalRows, null);
        var topMedalistsMale = BuildMedalists(medalRows, "male");
        var topMedalistsFemale = BuildMedalists(medalRows, "female");

        var highPointAwards = await LoadHighPointAwardsAsync(data, filter);
        var officialAwards = await LoadOfficialAwardsAsync(data, filter, days);

        // Клубный зачёт — тот же расчёт, что у /api/club-summary (BuildClubSummary), по общим
        // строкам; по полу — подвыборка строк. Значения в Results.Gender —
        // "male"/"female" (плюс "mixed" у эстафет и "none"), НЕ "M"/"F"; у Swimmer.Gender
        // те же "male"/"female" (сверено по базе 10.10.2026).
        var (topClubs, topClubsMen, topClubsWomen) = await BuildOverviewClubSummariesAsync(data, filter, clubRules);

        var newRecords = await DetectNewRecordsAsync(data);

        return new OverviewGenderCards(bestSwim, bestSwimMale, bestSwimFemale, topMedalists, topMedalistsMale,
            topMedalistsFemale, highPointAwards, officialAwards, topClubs, topClubsMen, topClubsWomen, newRecords);
    }

    private sealed record OverviewGenderCards(
        OverviewBestSwimDto? BestSwim,
        OverviewBestSwimDto? BestSwimMale,
        OverviewBestSwimDto? BestSwimFemale,
        IReadOnlyList<OverviewMedalistDto> TopMedalists,
        IReadOnlyList<OverviewMedalistDto> TopMedalistsMale,
        IReadOnlyList<OverviewMedalistDto> TopMedalistsFemale,
        List<OverviewHighPointDto> HighPointAwards,
        List<OverviewOfficialAwardsDayDto> OfficialAwards,
        List<ClubSummaryDto> TopClubs,
        List<ClubSummaryDto> TopClubsMen,
        List<ClubSummaryDto> TopClubsWomen,
        List<OverviewRecordDto> Records)
    {
        public GenderOverviewCards ToPreview() => new(BestSwimMale, BestSwimFemale, TopMedalists, TopMedalistsMale,
            TopMedalistsFemale, HighPointAwards, OfficialAwards, TopClubs, TopClubsMen, TopClubsWomen, Records);
    }

    public async Task<GenderOverviewPreview?> PreviewAsync(ResultFilter filter, int swimmerId, string gender)
    {
        var query = await BuildFilteredQueryAsync(filter);
        if (query is null) return null;

        var data = await LoadOverviewDataAsync(query);
        var days = BuildOverviewDays(data);
        var clubRules = await LoadClubRulesAsync();

        var before = (await BuildGenderCardsAsync(data, filter, days, clubRules)).ToPreview();

        // То же, что сделает запись: пол — личным строкам пловца БЕЗ пола и самому пловцу
        // (его пол читают ноги эстафет в Most decorated).
        foreach (var row in data.Rows.Where(r => r.SwimmerId == swimmerId && r.RelayId == null
                                                 && r.Gender is null or "" or "none"))
            row.Gender = gender;
        foreach (var leg in data.RelayMembers.SelectMany(g => g).Where(m => m.SwimmerId == swimmerId
                                                                            && string.IsNullOrEmpty(m.Gender)))
            leg.Gender = gender;

        var after = (await BuildGenderCardsAsync(data, filter, days, clubRules)).ToPreview();
        return new GenderOverviewPreview(before, after);
    }
}
