using Swimm.Application.Dtos;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Repositories;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// «Как напечатано» у клуба на экранах соревнования (Р97; план swimmer-club-change §5 Н9,
/// решение Влада 18.11.2026 «Да, как напечатано»). Клуб с тех пор переименован, строка помнит
/// прежнее название (<c>Results.ClubNameId</c>): протокол, зачёт клубов и карточки Overview
/// пишут напечатанное, текущее — подсказкой «now: …» (<c>club_now</c> у строки и зачёта,
/// карта <c>club_now</c> у Overview). Фильтр клуба принимает оба названия.
///
/// Места, где у строки соревнования выводится клуб, и чем они покрыты:
/// протокол и Combine All Results (одна выдача /api/results) — <see cref="ProtocolRow_ShowsPrintedName_WithCurrentAsHint"/>;
/// фильтр клуба — <see cref="ClubFilter_MatchesPrintedAndCurrentName"/>;
/// зачёт клубов (таб Clubs, Top clubs, подсказки фильтра) — <see cref="ClubSummary_KeyedByPrintedName_WithClubNow"/>;
/// Best swim, Most decorated, High Point, карта club_now — <see cref="OverviewCards_ShowPrintedName_AndMapCurrentNames"/>;
/// New records — <see cref="NewRecordsCard_ShowsClubAsPrinted"/>; Official awards — <c>OverviewOfficialAwardsTests.Place_ShowsClubAsPrinted</c>.
/// Клиент: подсказка и карта — <c>club-now-model.test.mjs</c>, подпись фильтра — <c>filter-club-match.test.mjs</c>.
/// </summary>
public class ProtocolPrintedClubNameTests
{
    private const string Former = "הפועל עירוני הוד השרון";

    /// <summary>Клуб Alpha (текущее название) с прежним названием; строки 1 — под прежним, 2 — под текущим.</summary>
    private static async Task<(OverviewTestKit Kit, Competition Comp, ClubName Name)> SeedAsync(
        string name, bool bothNames = true)
    {
        var kit = await OverviewTestKit.CreateAsync($"{nameof(ProtocolPrintedClubNameTests)}.{name}");
        var former = new ClubName { ClubId = kit.Club.Id, Name = Former, NameEn = "Hapoel", Kind = ClubNameKinds.Former };
        kit.Db.Add(former);
        await kit.Db.SaveChangesAsync();

        var comp = await kit.AddCompetitionAsync();
        var old = await kit.AddSwimmerAsync("Old", birthYear: 2010);
        var oldRow = kit.Row(comp, old, position: 1, points: 600, timeMs: 60000);
        oldRow.ClubNameId = former.Id;
        if (bothNames)
        {
            var cur = await kit.AddSwimmerAsync("Cur", birthYear: 2010);
            await kit.AddRowsAsync(oldRow, kit.Row(comp, cur, position: 2, points: 500, timeMs: 61000));
        }
        else
        {
            await kit.AddRowsAsync(oldRow);
        }
        return (kit, comp, former);
    }

    [Fact]
    public async Task ProtocolRow_ShowsPrintedName_WithCurrentAsHint()
    {
        var (kit, comp, _) = await SeedAsync(nameof(ProtocolRow_ShowsPrintedName_WithCurrentAsHint));
        await using var _ = kit;

        var (items, _, _) = await new ResultRepository(kit.Db, new NullCache())
            .GetPagedAsync(new ResultFilter { CompetitionId = comp.Id }, 1, 50);

        var printed = items.Single(i => i.Position == 1);
        Assert.Equal(Former, printed.ClubName);
        Assert.Equal("Hapoel", printed.ClubNameEn);
        Assert.Equal("Alpha", printed.ClubNameNow);
        Assert.Equal(kit.Club.Id, printed.ClubId);   // ссылка — на живой клуб

        var current = items.Single(i => i.Position == 2);
        Assert.Equal("Alpha", current.ClubName);
        Assert.Null(current.ClubNameNow);              // напечатано текущее — подсказки нет
    }

    [Fact]
    public async Task ClubFilter_MatchesPrintedAndCurrentName()
    {
        var (kit, comp, _) = await SeedAsync(nameof(ClubFilter_MatchesPrintedAndCurrentName));
        await using var _ = kit;
        var repo = new ResultRepository(kit.Db, new NullCache());

        var byPrinted = await repo.GetPagedAsync(new ResultFilter { CompetitionId = comp.Id, Club = "הפועל עירוני" }, 1, 50);
        var byCurrent = await repo.GetPagedAsync(new ResultFilter { CompetitionId = comp.Id, Club = "Alpha" }, 1, 50);

        Assert.Single(byPrinted.Items);   // фильтр сводки клубов шлёт напечатанное
        Assert.Equal(2, byCurrent.Items.Count); // по текущему — весь клуб
    }

    [Fact]
    public async Task ClubSummary_KeyedByPrintedName_WithClubNow()
    {
        var (kit, comp, _) = await SeedAsync(nameof(ClubSummary_KeyedByPrintedName_WithClubNow), bothNames: false);
        await using var _ = kit;

        var summary = await new ResultRepository(kit.Db, new NullCache())
            .GetClubSummaryAsync(new ResultFilter { CompetitionId = comp.Id });

        var row = Assert.Single(summary);
        Assert.Equal(Former, row.Club);
        Assert.Equal("Alpha", row.ClubNow);
        Assert.Equal(kit.Club.Id, row.ClubId);
        Assert.Contains(Former, row.FormerNames!);
    }

    [Fact]
    public async Task ClubSummary_ClubUnderCurrentName_HasNoClubNow()
    {
        await using var kit = await OverviewTestKit.CreateAsync(nameof(ClubSummary_ClubUnderCurrentName_HasNoClubNow));
        var comp = await kit.AddCompetitionAsync();
        await kit.AddRowsAsync(kit.Row(comp, await kit.AddSwimmerAsync("A"), position: 1));

        var row = Assert.Single(await new ResultRepository(kit.Db, new NullCache())
            .GetClubSummaryAsync(new ResultFilter { CompetitionId = comp.Id }));
        Assert.Equal("Alpha", row.Club);
        Assert.Null(row.ClubNow);
    }

    [Fact]
    public async Task OverviewCards_ShowPrintedName_AndMapCurrentNames()
    {
        var (kit, comp, _) = await SeedAsync(nameof(OverviewCards_ShowPrintedName_AndMapCurrentNames), bothNames: false);
        await using var _ = kit;

        var overview = await kit.OverviewAsync(comp);

        Assert.Equal(Former, overview.BestSwim!.Club);                 // Best swim
        Assert.Equal(Former, Assert.Single(overview.TopMedalists).Club); // Most decorated
        Assert.NotEmpty(overview.HighPointAwards);                       // High Point
        Assert.All(overview.HighPointAwards, a => Assert.Equal(Former, a.Club));
        var top = Assert.Single(overview.TopClubs);                      // Top clubs
        Assert.Equal(Former, top.Club);
        Assert.Equal("Alpha", top.ClubNow);
        Assert.Equal("Alpha", overview.ClubNow[kit.Club.Id]);            // «now: …» для всех карточек
    }

    [Fact]
    public async Task NewRecordsCard_ShowsClubAsPrinted()
    {
        await using var kit = await OverviewTestKit.CreateAsync(nameof(NewRecordsCard_ShowsClubAsPrinted));
        kit.Db.Add(new Swimm.Domain.Entities.Record
        {
            RegionType = "country", RegionCode = "ISR", Category = "open", AgeKey = "",
            Gender = "male", PoolType = "50m", Style = "freestyle", Distance = "100m", Time = "0:50.00",
        });
        var former = new ClubName { ClubId = kit.Club.Id, Name = Former, Kind = ClubNameKinds.Former };
        kit.Db.Add(former);
        await kit.Db.SaveChangesAsync();
        var comp = await kit.AddCompetitionAsync(c => c.DayNumber = 1);
        var row = kit.Row(comp, await kit.AddSwimmerAsync("Fast"), position: 1, timeMs: 49_500);
        row.TimeOriginal = "0:49.50";
        row.ClubNameId = former.Id;
        await kit.AddRowsAsync(row);

        var record = Assert.Single((await kit.OverviewAsync(comp)).Records);

        Assert.Equal(Former, record.Club);
    }

    [Fact]
    public async Task Overview_NoRenamedClubs_EmptyMap()
    {
        await using var kit = await OverviewTestKit.CreateAsync(nameof(Overview_NoRenamedClubs_EmptyMap));
        var comp = await kit.AddCompetitionAsync();
        await kit.AddRowsAsync(kit.Row(comp, await kit.AddSwimmerAsync("A"), position: 1, points: 500, timeMs: 60000));

        var overview = await kit.OverviewAsync(comp);

        Assert.Empty(overview.ClubNow);
        Assert.Equal("Alpha", overview.BestSwim!.Club);
    }

    private sealed class NullCache : Swimm.Application.Abstractions.ICacheService
    {
        public Task<T?> GetAsync<T>(string key) => Task.FromResult<T?>(default);
        public Task SetAsync<T>(string key, T value, TimeSpan ttl) => Task.CompletedTask;
        public Task RemoveAsync(string key) => Task.CompletedTask;
        public Task InvalidateAllAsync() => Task.CompletedTask;
    }
}
