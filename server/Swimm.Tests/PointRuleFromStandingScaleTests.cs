using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Repositories;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Правило клубных очков, заведённое по шкале официального зачёта (кнопка «Завести правило»
/// в превью затягивания). Сам HTTP-эндпоинт тонкий — здесь проверяется то, что он собирает:
/// какие поля обязаны получиться у нового правила и почему.
///
/// Живой случай — «גביע האביב 2026» (loglig 14535): зачёт есть, шкала 9,7,6,5,4,3,2,1 не
/// совпала ни с одним из шести правил, и до этой кнопки её пришлось бы заводить руками в
/// другом разделе, а потом затягивать соревнование заново.
/// </summary>
public class PointRuleFromStandingScaleTests
{
    private static SwimmDbContext CreateDb(string name) =>
        new(new DbContextOptionsBuilder<SwimmDbContext>()
            .UseInMemoryDatabase(name)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private sealed class NullCache : ICacheService
    {
        public Task<T?> GetAsync<T>(string key) => Task.FromResult<T?>(default);
        public Task SetAsync<T>(string key, T value, TimeSpan ttl) => Task.CompletedTask;
        public Task RemoveAsync(string key) => Task.CompletedTask;
        public Task InvalidateAllAsync() => Task.CompletedTask;
    }

    /// <summary>Ровно то, что собирает эндпоинт из снятой шкалы.</summary>
    private static PointRuleInputDto InputFromScale(IReadOnlyList<(int Place, int Points)> scale, string version) => new()
    {
        Version = version,
        EffectiveFrom = new DateOnly(2026, 1, 1),
        Scope = "all",
        DefaultPoints = 0,
        MaxScoringPlace = scale[^1].Place,
        ManualOnly = true,
        RelayMultiplier = 2,
        Entries = scale.Select(p => new PointRuleEntryDto { Place = p.Place, Points = p.Points }).ToList()
    };

    private static readonly (int Place, int Points)[] SpringCupScale =
        [(1, 9), (2, 7), (3, 6), (4, 5), (5, 4), (6, 3), (7, 2), (8, 1)];

    [Fact]
    public async Task CreatedRule_IsManualOnly_SoItDoesNotHijackAutoPick()
    {
        // Не-ManualOnly правило со свежей датой перехватывает ВСЕ соревнования без явной
        // привязки — этот баг мы уже ловили, поэтому кнопка обязана ставить флаг.
        await using var db = CreateDb(nameof(CreatedRule_IsManualOnly_SoItDoesNotHijackAutoPick));
        var repo = new PointRulesAdminRepository(db, new NullCache());

        var res = await repo.CreateAsync(PointRuleKind.Clubs, InputFromScale(SpringCupScale, "9pt.8pl.2026"));

        Assert.True(res.Success);
        var rule = await db.PointRulesClubs.Include(r => r.Entries).SingleAsync();
        Assert.True(rule.ManualOnly);
        Assert.Equal(8, rule.MaxScoringPlace);
        Assert.Equal(2, rule.RelayMultiplier);
        Assert.Equal(0, rule.DefaultPoints);
        Assert.Equal([9, 7, 6, 5, 4, 3, 2, 1], rule.Entries.OrderBy(e => e.Place).Select(e => e.Points));
    }

    [Fact]
    public async Task CreatedRule_MatchesTheStandingItWasBuiltFrom()
    {
        // Смысл всей кнопки: после заведения та же шкала должна опознаваться этим правилом,
        // иначе превью продолжит предлагать завести ещё одно.
        await using var db = CreateDb(nameof(CreatedRule_MatchesTheStandingItWasBuiltFrom));
        var repo = new PointRulesAdminRepository(db, new NullCache());
        await repo.CreateAsync(PointRuleKind.Clubs, InputFromScale(SpringCupScale, "9pt.8pl.2026"));

        var rules = await db.PointRulesClubs.Include(r => r.Entries).ToListAsync();
        var observed = SpringCupScale.ToDictionary(p => p.Place, p => p.Points);

        Assert.Equal("9pt.8pl.2026", PointRuleScaleMatcher.Match(observed, rules)?.Version);
    }

    [Fact]
    public async Task DuplicateVersion_IsRejected_NotSilentlyDoubled()
    {
        await using var db = CreateDb(nameof(DuplicateVersion_IsRejected_NotSilentlyDoubled));
        var repo = new PointRulesAdminRepository(db, new NullCache());
        await repo.CreateAsync(PointRuleKind.Clubs, InputFromScale(SpringCupScale, "9pt.8pl.2026"));

        var second = await repo.CreateAsync(PointRuleKind.Clubs, InputFromScale(SpringCupScale, "9pt.8pl.2026"));

        Assert.False(second.Success);
        Assert.Single(await db.PointRulesClubs.ToListAsync());
    }

    [Fact]
    public async Task CreatedRule_ScoresLikeTheOfficialTable()
    {
        // Проверка «по делу»: заведённое правило должно давать те же очки, что стоят в
        // протоколе loglig — 1-е место 9, 8-е 1, вне восьмёрки ноль, эстафета вдвое.
        await using var db = CreateDb(nameof(CreatedRule_ScoresLikeTheOfficialTable));
        var repo = new PointRulesAdminRepository(db, new NullCache());
        await repo.CreateAsync(PointRuleKind.Clubs, InputFromScale(SpringCupScale, "9pt.8pl.2026"));
        var rule = await db.PointRulesClubs.Include(r => r.Entries).SingleAsync();

        Assert.Equal(9, PointRulesClubsScoring.RelayPointsFor(rule, position: 1, timeFail: false, isRelay: false));
        Assert.Equal(1, PointRulesClubsScoring.RelayPointsFor(rule, position: 8, timeFail: false, isRelay: false));
        Assert.Equal(0, PointRulesClubsScoring.RelayPointsFor(rule, position: 9, timeFail: false, isRelay: false));
        Assert.Equal(18, PointRulesClubsScoring.RelayPointsFor(rule, position: 1, timeFail: false, isRelay: true));
    }
}
