using Swimm.Domain.Entities;
using Xunit;
using Record = Swimm.Domain.Entities.Record;

namespace Swimm.Tests;

/// <summary>
/// Карточка «High Point» (Overview): лучший по сумме очков в номинации, ♂/♀.
/// Legacy-сумма FINA по возрастам, привязанное правило со шкалой мест и рекордный бонус
/// уже покрыты в ResultRepositoryTests (<c>Overview_HighPointAward_*</c>); арифметика
/// правила — в PointRulesSwimmersScoringTests. Здесь — то, что делает сам обзор: корзины
/// мастерсов, подпись номинации, сноска «только финалы», режим Combine, отбор строк.
/// Правила — docs/competition-overview-cards.md, «High Point».
/// </summary>
public class OverviewHighPointCardTests
{
    private static PointRuleSwimmers PlacementRule(Action<PointRuleSwimmers>? setup = null)
    {
        var rule = new PointRuleSwimmers
        {
            Version = "test-hp", Scope = "all", PointsSource = "placement",
            GroupBy = "age", SplitByGender = true, MaxScoringPlace = 4, DefaultPoints = 0,
            EffectiveFrom = new DateOnly(2020, 1, 1), ManualOnly = true,
            Entries =
            [
                new PointRuleSwimmersEntry { Place = 1, Points = 5 },
                new PointRuleSwimmersEntry { Place = 2, Points = 3 },
                new PointRuleSwimmersEntry { Place = 3, Points = 2 },
                new PointRuleSwimmersEntry { Place = 4, Points = 1 },
            ]
        };
        setup?.Invoke(rule);
        return rule;
    }

    private static async Task<Competition> CompetitionWithRuleAsync(
        OverviewTestKit kit, PointRuleSwimmers rule, Action<Competition>? setup = null)
    {
        kit.Db.Add(rule);
        await kit.Db.SaveChangesAsync();
        return await kit.AddCompetitionAsync(c =>
        {
            c.PointRuleSwimmersId = rule.Id;
            setup?.Invoke(c);
        });
    }

    /// <summary>
    /// Мастерсы без правила (legacy): номинация — возрастная ГРУППА, а не возраст; группы идут
    /// по нижней границе: «90+» без дефиса — после «85-89», «100-104» — в конце (строкой встал бы первым).
    /// </summary>
    [Fact]
    public async Task Legacy_Masters_BucketsByAgeGroup_OrderedByLowerBound()
    {
        await using var kit = await OverviewTestKit.CreateAsync(nameof(Legacy_Masters_BucketsByAgeGroup_OrderedByLowerBound));
        var comp = await kit.AddCompetitionAsync(c => c.IsMasters = true);

        var rows = new List<ResultRecord>();
        foreach (var group in new[] { "90+", "25-29", "100-104", "85-89", "30-34" })
        {
            var s = await kit.AddSwimmerAsync("M" + group, birthYear: 1950);
            var row = kit.Row(comp, s, points: 300);
            row.AgeGroup = group;
            rows.Add(row);
        }
        await kit.AddRowsAsync(rows.ToArray());

        var awards = (await kit.OverviewAsync(comp)).HighPointAwards;

        Assert.Equal(new[] { "25-29", "30-34", "85-89", "90+", "100-104" }, awards.Select(a => a.AgeGroup));
        Assert.All(awards, a =>
        {
            Assert.Equal(0, a.Age);           // у мастерсов номинация — группа, возраст не показывается
            Assert.Null(a.RuleVersion);       // null = legacy-расчёт, а не поломка
        });
    }

    /// <summary>GroupBy=age-group: номинация подаётся группой и подписью, возраст — 0.</summary>
    [Fact]
    public async Task Rule_AgeGroupGrouping_ReturnsGroupLabel()
    {
        await using var kit = await OverviewTestKit.CreateAsync(nameof(Rule_AgeGroupGrouping_ReturnsGroupLabel));
        var comp = await CompetitionWithRuleAsync(kit, PlacementRule(r => r.GroupBy = "age-group"));
        var s = await kit.AddSwimmerAsync("Kid", birthYear: 2013);
        var row = kit.Row(comp, s, position: 1);
        row.AgeGroup = "11-12";
        await kit.AddRowsAsync(row);

        var award = Assert.Single((await kit.OverviewAsync(comp)).HighPointAwards);

        Assert.Equal("11-12", award.AgeGroup);
        Assert.Equal("11-12", award.GroupLabel);
        Assert.Equal(0, award.Age);
        Assert.Equal(5, award.Points);
        Assert.Equal("test-hp", award.RuleVersion);
    }

    /// <summary>Повторённый возрастной рекорд — 8 очков вместо очков за место (§8.A).</summary>
    [Fact]
    public async Task Rule_TiedAgeRecord_GivesTiePoints()
    {
        await using var kit = await OverviewTestKit.CreateAsync(nameof(Rule_TiedAgeRecord_GivesTiePoints));
        kit.Db.Add(new Record
        {
            RegionType = "country", RegionCode = "ISR", Category = "age", AgeKey = "12",
            Gender = "male", PoolType = "50m", Style = "freestyle", Distance = "100m", Time = "1:00.00"
        });
        var comp = await CompetitionWithRuleAsync(kit, PlacementRule(r => { r.RecordPoints = 13; r.RecordTiePoints = 8; }));
        var s = await kit.AddSwimmerAsync("Equal", birthYear: 2012);   // 2024 − 2012 = 12
        await kit.AddRowsAsync(kit.Row(comp, s, position: 2, timeMs: 60_000));

        var award = Assert.Single((await kit.OverviewAsync(comp)).HighPointAwards);

        Assert.Equal(8, award.Points);   // не 3 за второе место и не 13 за новый рекорд
    }

    /// <summary>
    /// «Только финалы»: prelim очков не приносит, финал — по своему месту. Сноска
    /// «по всем заплывам» не нужна: признак заплыва в данных есть.
    /// </summary>
    [Fact]
    public async Task FinalsOnly_PrelimGivesNothing_FootnoteOff()
    {
        await using var kit = await OverviewTestKit.CreateAsync(nameof(FinalsOnly_PrelimGivesNothing_FootnoteOff));
        var comp = await CompetitionWithRuleAsync(kit, PlacementRule(r => r.FinalsOnly = true));
        var morningStar = await kit.AddSwimmerAsync("Morning", birthYear: 2012);
        var finalist = await kit.AddSwimmerAsync("Final", birthYear: 2012);

        var prelim = kit.Row(comp, morningStar, position: 1);
        prelim.HeatType = "prelim";
        var finalA = kit.Row(comp, morningStar, position: 3);
        finalA.HeatType = "final";
        var finalB = kit.Row(comp, finalist, position: 2);
        finalB.HeatType = "final";
        await kit.AddRowsAsync(prelim, finalA, finalB);

        var award = Assert.Single((await kit.OverviewAsync(comp)).HighPointAwards);

        Assert.Equal(finalist.Id, award.SwimmerId);   // 3 за финал против 2 (prelim-золото не в счёт)
        Assert.Equal(3, award.Points);
        Assert.False(award.FinalsOnlyUnavailable);
    }

    /// <summary>
    /// Признака заплыва нет ни у одной строки (данные до HeatType) — «только финалы» не
    /// выполнить: считаем по всем заплывам и честно говорим это сноской.
    /// </summary>
    [Fact]
    public async Task FinalsOnly_NoHeatTypeAnywhere_CountsAllAndRaisesFootnote()
    {
        await using var kit = await OverviewTestKit.CreateAsync(nameof(FinalsOnly_NoHeatTypeAnywhere_CountsAllAndRaisesFootnote));
        var comp = await CompetitionWithRuleAsync(kit, PlacementRule(r => r.FinalsOnly = true));
        var s = await kit.AddSwimmerAsync("Old", birthYear: 2012);
        await kit.AddRowsAsync(kit.Row(comp, s, position: 1), kit.Row(comp, s, position: 2));

        var award = Assert.Single((await kit.OverviewAsync(comp)).HighPointAwards);

        Assert.Equal(8, award.Points);
        Assert.True(award.FinalsOnlyUnavailable);
    }

    /// <summary>
    /// Combine All Results: место — объединённое (как у медалей и клубных очков), иначе
    /// High Point спорил бы с таблицей на том же экране.
    /// </summary>
    [Fact]
    public async Task Rule_Combined_UsesCombinedPlace()
    {
        await using var kit = await OverviewTestKit.CreateAsync(nameof(Rule_Combined_UsesCombinedPlace));
        var comp = await CompetitionWithRuleAsync(kit, PlacementRule(), c => c.ShowCombineAllResults = true);
        var a = await kit.AddSwimmerAsync("A", birthYear: 2012);
        var b = await kit.AddSwimmerAsync("B", birthYear: 2012);
        var rowA = kit.Row(comp, a, position: 1);
        rowA.CombinedPlace = 3;
        var rowB = kit.Row(comp, b, position: 2);
        rowB.CombinedPlace = 1;
        await kit.AddRowsAsync(rowA, rowB);

        var protocol = Assert.Single((await kit.OverviewAsync(comp)).HighPointAwards);
        var combined = Assert.Single((await kit.OverviewAsync(comp, combined: true)).HighPointAwards);

        Assert.Equal(a.Id, protocol.SwimmerId);
        Assert.Equal(b.Id, combined.SwimmerId);
        Assert.Equal(5, combined.Points);
    }

    /// <summary>
    /// Combine All Results: дисциплина зачитывается один раз — по лучшему заплыву
    /// (<c>IsBestResult</c>), иначе повторный старт удваивал бы вклад в сумму FINA.
    /// </summary>
    [Fact]
    public async Task Legacy_Combined_CountsOnlyBestResultOfDiscipline()
    {
        await using var kit = await OverviewTestKit.CreateAsync(nameof(Legacy_Combined_CountsOnlyBestResultOfDiscipline));
        var comp = await kit.AddCompetitionAsync(c => c.ShowCombineAllResults = true);
        var twice = await kit.AddSwimmerAsync("Twice", birthYear: 2012);
        var once = await kit.AddSwimmerAsync("Once", birthYear: 2012);
        var best = kit.Row(comp, twice, points: 500);
        best.IsBestResult = true;
        var repeat = kit.Row(comp, twice, points: 450);
        repeat.IsBestResult = false;
        await kit.AddRowsAsync(best, repeat, kit.Row(comp, once, points: 800));

        var protocol = Assert.Single((await kit.OverviewAsync(comp)).HighPointAwards);
        var combined = Assert.Single((await kit.OverviewAsync(comp, combined: true)).HighPointAwards);

        Assert.Equal(twice.Id, protocol.SwimmerId);   // 950 по протоколу
        Assert.Equal(950, protocol.Points);
        Assert.Equal(once.Id, combined.SwimmerId);    // 800 против 500 — повтор не в счёт
    }

    /// <summary>Пол строки неизвестен («none») — в номинации ♂/♀ она не попадает.</summary>
    [Fact]
    public async Task UnknownGenderRow_IsExcluded()
    {
        await using var kit = await OverviewTestKit.CreateAsync(nameof(UnknownGenderRow_IsExcluded));
        var comp = await kit.AddCompetitionAsync();
        var known = await kit.AddSwimmerAsync("Known", birthYear: 2012);
        var unknown = await kit.AddSwimmerAsync("Unknown", birthYear: 2012);
        await kit.AddRowsAsync(
            kit.Row(comp, known, points: 300),
            kit.Row(comp, unknown, points: 999, gender: "none"));

        var award = Assert.Single((await kit.OverviewAsync(comp)).HighPointAwards);

        Assert.Equal(known.Id, award.SwimmerId);
    }
}
