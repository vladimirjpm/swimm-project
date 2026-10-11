using Swimm.Domain.Entities;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Карточка «Best swim» (Overview → Champions): лучший заплыв по FINA-очкам, общий и ♂/♀.
/// Тай-брейки, TimeFail, пара-очки и помеченные строки — в ResultRepositoryTests
/// (<c>Overview_BestSwim_*</c>); здесь — разбивка по полу, эстафета и ссылка на клуб.
/// Правила — docs/competition-overview-cards.md, «Champions → Best swim».
/// </summary>
public class OverviewBestSwimCardTests
{
    [Fact]
    public async Task GenderPanels_AreIndependent_OverallIsTheMax()
    {
        await using var kit = await OverviewTestKit.CreateAsync(nameof(GenderPanels_AreIndependent_OverallIsTheMax));
        var comp = await kit.AddCompetitionAsync();
        var man = await kit.AddSwimmerAsync("Man", "male");
        var slowMan = await kit.AddSwimmerAsync("SlowMan", "male");
        var woman = await kit.AddSwimmerAsync("Woman", "female");
        await kit.AddRowsAsync(
            kit.Row(comp, man, points: 700),
            kit.Row(comp, slowMan, points: 690),
            kit.Row(comp, woman, points: 650));

        var overview = await kit.OverviewAsync(comp);

        Assert.Equal(man.Id, overview.BestSwim!.SwimmerId);
        Assert.Equal(man.Id, overview.BestSwimMale!.SwimmerId);
        // Женская панель — свой максимум, а не «следующий после мужчин».
        Assert.Equal(woman.Id, overview.BestSwimFemale!.SwimmerId);
        Assert.Equal(650, overview.BestSwimFemale.Points);
    }

    [Fact]
    public async Task NoWomen_FemalePanelIsNull()
    {
        await using var kit = await OverviewTestKit.CreateAsync(nameof(NoWomen_FemalePanelIsNull));
        var comp = await kit.AddCompetitionAsync();
        var man = await kit.AddSwimmerAsync("Man", "male");
        await kit.AddRowsAsync(kit.Row(comp, man, points: 700));

        var overview = await kit.OverviewAsync(comp);

        Assert.NotNull(overview.BestSwimMale);
        Assert.Null(overview.BestSwimFemale);
    }

    /// <summary>
    /// Эстафета может стать лучшим заплывом — тогда карточка показывает команду, а не
    /// владельца строки (он лишь одна из ног). Смешанная эстафета (Gender = mixed) в
    /// общий зачёт идёт, а в панели ♂/♀ — нет: её пол ни тот, ни другой.
    /// </summary>
    [Fact]
    public async Task MixedRelay_IsOverallBestWithTeamName_ButInNeitherGenderPanel()
    {
        await using var kit = await OverviewTestKit.CreateAsync(nameof(MixedRelay_IsOverallBestWithTeamName_ButInNeitherGenderPanel));
        var comp = await kit.AddCompetitionAsync();
        var anchor = await kit.AddSwimmerAsync("Anchor", "male");
        var man = await kit.AddSwimmerAsync("Man", "male");
        var relay = new Relay { TeamName = "Alpha Mixed", SwimmersName = "Anchor, …" };
        kit.Db.Add(relay);
        await kit.Db.SaveChangesAsync();

        var relayRow = kit.Row(comp, anchor, points: 900, gender: "mixed");
        relayRow.RelayId = relay.Id;
        relayRow.Distance = "4x100";
        await kit.AddRowsAsync(relayRow, kit.Row(comp, man, points: 700));

        var overview = await kit.OverviewAsync(comp);

        Assert.True(overview.BestSwim!.IsRelay);
        Assert.Equal("Alpha Mixed", overview.BestSwim.RelayTeamName);
        Assert.Equal(man.Id, overview.BestSwimMale!.SwimmerId);
        Assert.Null(overview.BestSwimFemale);
    }

    /// <summary>Эмблема клуба в карточке — ссылка на /clubs/{id}: без id её не построить.</summary>
    [Fact]
    public async Task CarriesClubIdAndDay_ForLinks()
    {
        await using var kit = await OverviewTestKit.CreateAsync(nameof(CarriesClubIdAndDay_ForLinks));
        var comp = await kit.AddCompetitionAsync(c => c.DayNumber = 2);
        var club = await kit.AddClubAsync("Beta");
        var man = await kit.AddSwimmerAsync("Man");
        await kit.AddRowsAsync(kit.Row(comp, man, points: 700, club: club));

        var best = (await kit.OverviewAsync(comp)).BestSwim!;

        Assert.Equal(club.Id, best.ClubId);
        Assert.Equal("Beta", best.Club);
        Assert.Equal(2, best.DayNumber);
        Assert.Equal(comp.Id, best.CompetitionId);
    }
}
