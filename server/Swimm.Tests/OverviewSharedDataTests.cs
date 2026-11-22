using System.Text.Json;
using Swimm.Application.Dtos;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Repositories;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Overview считает карточки по ОДНОЙ загрузке строк в памяти (22.11.2026, было ~30 SQL на
/// вызов). Здесь — места, где перенос из SQL в память легко меняет ответ:
/// порядок NULL в сортировке, зачёт по полу против /api/club-summary с тем же полом,
/// строки с SuspectReason в суммах (Р76). Байтовая сверка ответа до/после на всей локальной
/// базе — в описании PR; правила карточек — docs/competition-overview-cards.md.
/// </summary>
public class OverviewSharedDataTests
{
    /// <summary>
    /// Ничья по очкам: прежний SQL сортировал ORDER BY TimeMillisecond ASC, а Postgres ставит
    /// NULL в конец. LINQ ставит null в начало — без явного правила «лучшим» стал бы заплыв
    /// без времени.
    /// </summary>
    [Fact]
    public async Task BestSwim_TieOnPoints_RowWithoutTimeLosesToTimedRow()
    {
        await using var kit = await OverviewTestKit.CreateAsync(nameof(BestSwim_TieOnPoints_RowWithoutTimeLosesToTimedRow));
        var comp = await kit.AddCompetitionAsync();
        var noTime = await kit.AddSwimmerAsync("NoTime");
        var timed = await kit.AddSwimmerAsync("Timed");
        await kit.AddRowsAsync(
            kit.Row(comp, noTime, points: 700, timeMs: null),
            kit.Row(comp, timed, points: 700, timeMs: 60_000));

        var overview = await kit.OverviewAsync(comp);

        Assert.Equal(timed.Id, overview.BestSwim!.SwimmerId);
    }

    /// <summary>
    /// Top clubs ♂/♀ в Overview — подвыборка общих строк, а /api/club-summary с полом —
    /// отдельный запрос с WHERE Gender. Ответы обязаны совпадать целиком, включая прежние
    /// названия клуба (их теперь подтягивает один запрос на три зачёта).
    /// </summary>
    [Fact]
    public async Task TopClubsByGender_SameAsClubSummaryWithGender()
    {
        await using var kit = await OverviewTestKit.CreateAsync(nameof(TopClubsByGender_SameAsClubSummaryWithGender));
        var comp = await kit.AddCompetitionAsync(c => c.IsAward = true);
        var beta = await kit.AddClubAsync("Beta");
        kit.Db.Add(new ClubName { ClubId = beta.Id, Name = "Old Beta", NameEn = "Old Beta EN" });
        await kit.Db.SaveChangesAsync();

        var m1 = await kit.AddSwimmerAsync("M1", "male");
        var m2 = await kit.AddSwimmerAsync("M2", "male");
        var w1 = await kit.AddSwimmerAsync("W1", "female");
        var w2 = await kit.AddSwimmerAsync("W2", "female");
        await kit.AddRowsAsync(
            kit.Row(comp, m1, position: 1, points: 600),
            kit.Row(comp, m2, position: 2, points: 590, club: beta),
            kit.Row(comp, w1, position: 1, points: 650, club: beta),
            kit.Row(comp, w2, position: 2, points: 640));

        var overview = await kit.OverviewAsync(comp);
        var repo = new ResultRepository(kit.Db, new NoCache());
        var men = await repo.GetClubSummaryAsync(new ResultFilter { CompetitionId = comp.Id, Gender = "male" });
        var women = await repo.GetClubSummaryAsync(new ResultFilter { CompetitionId = comp.Id, Gender = "female" });
        var all = await repo.GetClubSummaryAsync(new ResultFilter { CompetitionId = comp.Id });

        Assert.Equal(Json(men.Take(3)), Json(overview.TopClubsMen));
        Assert.Equal(Json(women.Take(3)), Json(overview.TopClubsWomen));
        Assert.Equal(Json(all.Take(10)), Json(overview.TopClubs));
        Assert.Contains("Old Beta", overview.TopClubsWomen.Single(c => c.ClubId == beta.Id).FormerNames);
    }

    /// <summary>
    /// Р76: у строки с SuspectReason цифры протокола видны и входят в суммы — счётчики, медали,
    /// клубные очки. Не даём ей только наши выводы (Best swim, High Point). Общая загрузка не
    /// должна отфильтровать её «заодно» для всех карточек.
    /// </summary>
    [Fact]
    public async Task SuspectRow_CountsInTotalsAndMedals_ButNotInBestSwim()
    {
        await using var kit = await OverviewTestKit.CreateAsync(nameof(SuspectRow_CountsInTotalsAndMedals_ButNotInBestSwim));
        var comp = await kit.AddCompetitionAsync(c => c.IsAward = true);
        var suspect = await kit.AddSwimmerAsync("Suspect");
        var clean = await kit.AddSwimmerAsync("Clean");
        var flagged = kit.Row(comp, suspect, position: 1, points: 999);
        flagged.SuspectReason = "time-order";
        await kit.AddRowsAsync(flagged, kit.Row(comp, clean, position: 2, points: 500));

        var overview = await kit.OverviewAsync(comp);

        Assert.Equal(2, overview.Summary.ResultCount);
        Assert.Equal(2, overview.Summary.SwimmerCount);
        Assert.Equal(suspect.Id, overview.TopMedalists.Single().SwimmerId);
        Assert.Equal(1, overview.TopClubs.Single().Gold);
        Assert.Equal(clean.Id, overview.BestSwim!.SwimmerId);
    }

    private static string Json<T>(IEnumerable<T> items) => JsonSerializer.Serialize(items.ToList());

    private sealed class NoCache : Swimm.Application.Abstractions.ICacheService
    {
        public Task<T?> GetAsync<T>(string key) => Task.FromResult<T?>(default);
        public Task SetAsync<T>(string key, T value, TimeSpan ttl) => Task.CompletedTask;
        public Task RemoveAsync(string key) => Task.CompletedTask;
        public Task InvalidateAllAsync() => Task.CompletedTask;
    }
}
