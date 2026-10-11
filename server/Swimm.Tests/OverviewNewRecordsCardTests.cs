using Swimm.Domain.Entities;
using Xunit;
using Record = Swimm.Domain.Entities.Record;

namespace Swimm.Tests;

/// <summary>
/// Карточка «New records» (Overview): заплывы соревнования, побившие рекорд Израиля.
/// Оси и категории сравнения — в CompetitionRecordsDetectorTests; здесь — что обзор
/// подаёт детектору: какие строки вообще кандидаты.
/// Правила — docs/competition-overview-cards.md, «New records».
/// </summary>
public class OverviewNewRecordsCardTests
{
    /// <summary>Национальный рекорд (category open): 100 в/с, 50-метровый бассейн, 50.00.</summary>
    private static Record OpenRecord() => new()
    {
        RegionType = "country", RegionCode = "ISR", Category = "open", AgeKey = "",
        Gender = "male", PoolType = "50m", Style = "freestyle", Distance = "100m", Time = "0:50.00"
    };

    private static async Task<(OverviewTestKit Kit, Competition Comp)> SeedAsync(string name)
    {
        var kit = await OverviewTestKit.CreateAsync(name);
        kit.Db.Add(OpenRecord());
        await kit.Db.SaveChangesAsync();
        return (kit, await kit.AddCompetitionAsync(c => c.DayNumber = 1));
    }

    private static ResultRecord Swim(OverviewTestKit kit, Competition comp, Swimmer s, int ms, string time)
    {
        var row = kit.Row(comp, s, position: 1, timeMs: ms);
        row.TimeOriginal = time;
        return row;
    }

    [Fact]
    public async Task FasterThanRecord_IsListed_WithHolderAndLink()
    {
        var (kit, comp) = await SeedAsync(nameof(FasterThanRecord_IsListed_WithHolderAndLink));
        await using var _ = kit;
        var fast = await kit.AddSwimmerAsync("Fast");
        var slow = await kit.AddSwimmerAsync("Slow");
        var row = Swim(kit, comp, fast, 49_500, "0:49.50");
        await kit.AddRowsAsync(row, Swim(kit, comp, slow, 50_500, "0:50.50"));

        var record = Assert.Single((await kit.OverviewAsync(comp)).Records);

        Assert.Equal(fast.Id, record.SwimmerId);
        Assert.Equal("0:49.50", record.Time);
        Assert.Equal(row.Id, record.ResultId);
        Assert.Equal(1, record.DayNumber);
        Assert.Equal("Open record", record.Kind);
    }

    /// <summary>
    /// Помеченная ошибка протокола (32.59 на 100 батт. у Маккабиады) рекорд не бьёт, и
    /// DSQ — тоже: время в протоколе есть, но заплыв не засчитан.
    /// </summary>
    [Fact]
    public async Task SuspectAndTimeFailRows_DoNotBreakRecords()
    {
        var (kit, comp) = await SeedAsync(nameof(SuspectAndTimeFailRows_DoNotBreakRecords));
        await using var _ = kit;
        var typo = await kit.AddSwimmerAsync("Typo");
        var dsq = await kit.AddSwimmerAsync("Dsq");
        var suspect = Swim(kit, comp, typo, 32_590, "0:32.59");
        suspect.SuspectReason = "time_vs_distance";
        var failed = Swim(kit, comp, dsq, 49_000, "0:49.00");
        failed.TimeFail = true;
        await kit.AddRowsAsync(suspect, failed);

        Assert.Empty((await kit.OverviewAsync(comp)).Records);
    }

    /// <summary>Строка без времени в мс (не распарсилось) — не кандидат, а не «рекорд за 0».</summary>
    [Fact]
    public async Task RowWithoutTime_IsNotACandidate()
    {
        var (kit, comp) = await SeedAsync(nameof(RowWithoutTime_IsNotACandidate));
        await using var _ = kit;
        var s = await kit.AddSwimmerAsync("NoTime");
        await kit.AddRowsAsync(kit.Row(comp, s, position: 1));

        Assert.Empty((await kit.OverviewAsync(comp)).Records);
    }
}
