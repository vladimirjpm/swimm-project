using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Repositories;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Смена флага «Combine All Results» или привязки правила клубных очков в админке
/// запускает пересчёт материализованных величин. Combine: включили задним числом — строки
/// остались бы с пустым CombinedPlace и тоггл на клиенте показал бы пустую таблицу;
/// выключили — значения обязаны обнулиться. Правило: клубный зачёт материализован в
/// ClubCompetitionStandings — без пересчёта витрина остаётся на очках старого правила
/// (Маккаби-2026 #1565: показывал шкалу автоподбора вместо привязанной вручную).
/// </summary>
public class CompetitionRecalcOnFlagTests
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

    private sealed class RecalcSpy : ICompetitionRecalculationService
    {
        public List<int> Calls { get; } = [];
        public bool Throw { get; init; }

        public Task<int> RecalculateCompetitionAsync(int competitionId, CancellationToken ct = default)
        {
            Calls.Add(competitionId);
            if (Throw) throw new InvalidOperationException("boom");
            return Task.FromResult(3);
        }

        public Task<int> RecalculateAllCombinedAsync(CancellationToken ct = default) => Task.FromResult(0);
    }

    private static async Task<Competition> SeedAsync(SwimmDbContext db, bool showCombine)
    {
        var comp = new Competition
        {
            Name = "Meet",
            Country = new Country { CountryCode = "ISR", CountryName = "ISR" },
            Date = "01/06/2026",
            PoolType = "50m",
            ShowCombineAllResults = showCombine
        };
        db.Add(comp);
        await db.SaveChangesAsync();
        return comp;
    }

    private static CompetitionInputDto Input(Competition comp, bool showCombine) => new()
    {
        Name = comp.Name,
        Date = comp.Date,
        PoolType = comp.PoolType,
        Country = "ISR",
        ShowCombineAllResults = showCombine,
        CategoryKeys = []
    };

    [Fact]
    public async Task TurningFlagOn_TriggersRecalculation()
    {
        await using var db = CreateDb(nameof(TurningFlagOn_TriggersRecalculation));
        var comp = await SeedAsync(db, showCombine: false);
        var spy = new RecalcSpy();

        var res = await new CompetitionAdminRepository(db, new NullCache(), spy)
            .UpdateAsync(comp.Id, Input(comp, showCombine: true));

        Assert.True(res.Success);
        Assert.Equal([comp.Id], spy.Calls);
    }

    [Fact]
    public async Task TurningFlagOff_TriggersRecalculation_ToClearStaleValues()
    {
        await using var db = CreateDb(nameof(TurningFlagOff_TriggersRecalculation_ToClearStaleValues));
        var comp = await SeedAsync(db, showCombine: true);
        var spy = new RecalcSpy();

        await new CompetitionAdminRepository(db, new NullCache(), spy)
            .UpdateAsync(comp.Id, Input(comp, showCombine: false));

        Assert.Equal([comp.Id], spy.Calls);
    }

    [Fact]
    public async Task EditWithoutFlagChange_DoesNotRecalculate()
    {
        await using var db = CreateDb(nameof(EditWithoutFlagChange_DoesNotRecalculate));
        var comp = await SeedAsync(db, showCombine: true);
        var spy = new RecalcSpy();

        var input = Input(comp, showCombine: true);
        input.Name = "Meet renamed";
        await new CompetitionAdminRepository(db, new NullCache(), spy).UpdateAsync(comp.Id, input);

        Assert.Empty(spy.Calls); // пересчёт по всему событию — не для каждой правки формы
    }

    [Fact]
    public async Task RecalculationFailure_DoesNotFailTheEdit()
    {
        await using var db = CreateDb(nameof(RecalculationFailure_DoesNotFailTheEdit));
        var comp = await SeedAsync(db, showCombine: false);

        var res = await new CompetitionAdminRepository(db, new NullCache(), new RecalcSpy { Throw = true })
            .UpdateAsync(comp.Id, Input(comp, showCombine: true));

        Assert.True(res.Success);
        Assert.True((await db.Competitions.FindAsync(comp.Id))!.ShowCombineAllResults);
    }

    // ── смена привязки правила клубных очков ─────────────────────────────────

    private static PointRuleClubs ClubsRule(int id) => new()
    {
        Id = id,
        Version = $"test.{id}",
        Scope = "all",
        EffectiveFrom = new DateOnly(2000, 1, 1),
        Entries = [new PointRuleClubsEntry { Place = 1, Points = 9 }]
    };

    [Fact]
    public async Task ClubsRuleRebind_TriggersRecalculation()
    {
        await using var db = CreateDb(nameof(ClubsRuleRebind_TriggersRecalculation));
        db.Add(ClubsRule(4));
        var comp = await SeedAsync(db, showCombine: false);
        var spy = new RecalcSpy();

        var input = Input(comp, showCombine: false);
        input.PointRuleClubsId = 4; // было null (авто) → привязали вручную
        var res = await new CompetitionAdminRepository(db, new NullCache(), spy)
            .UpdateAsync(comp.Id, input);

        Assert.True(res.Success);
        Assert.Equal([comp.Id], spy.Calls);
        Assert.Equal(4, (await db.Competitions.FindAsync(comp.Id))!.PointRuleClubsId);
    }

    [Fact]
    public async Task SwimmersRuleRebind_DoesNotRecalculate()
    {
        // Очки пловцов (High Point) не материализованы — считаются на лету (Э6),
        // пересчёт зачёта при их перепривязке был бы пустой работой.
        await using var db = CreateDb(nameof(SwimmersRuleRebind_DoesNotRecalculate));
        db.Add(new PointRuleSwimmers
        {
            Id = 7,
            Version = "test.7",
            Scope = "all",
            EffectiveFrom = new DateOnly(2000, 1, 1)
        });
        var comp = await SeedAsync(db, showCombine: false);
        var spy = new RecalcSpy();

        var input = Input(comp, showCombine: false);
        input.PointRuleSwimmersId = 7;
        await new CompetitionAdminRepository(db, new NullCache(), spy).UpdateAsync(comp.Id, input);

        Assert.Empty(spy.Calls);
    }

    [Fact]
    public async Task AssignRules_ClubsRuleChange_RecalculatesOncePerEvent()
    {
        await using var db = CreateDb(nameof(AssignRules_ClubsRuleChange_RecalculatesOncePerEvent));
        db.Add(ClubsRule(4));
        var ev = new CompetitionEvent { Name = "Событие" };
        db.Add(ev);
        await db.SaveChangesAsync();
        var day1 = new Competition { Name = "День 1", Date = "01/07/2026", PoolType = "25m", EventId = ev.Id, DayNumber = 1 };
        var day2 = new Competition { Name = "День 2", Date = "02/07/2026", PoolType = "25m", EventId = ev.Id, DayNumber = 2 };
        var single = new Competition { Name = "Однодневка", Date = "03/07/2026", PoolType = "25m" };
        db.AddRange(day1, day2, single);
        await db.SaveChangesAsync();
        var spy = new RecalcSpy();

        var res = await new CompetitionAdminRepository(db, new NullCache(), spy)
            .AssignRulesAsync(new CompetitionRuleAssignmentDto
            {
                CompetitionIds = [day1.Id, day2.Id, single.Id],
                SetClubs = true,
                ClubsRuleId = 4
            });

        Assert.True(res.Success);
        // Дни одного события — одна зачётная единица: пересчёт по разу на событие + одиночка.
        Assert.Equal(2, spy.Calls.Count);
        Assert.Contains(single.Id, spy.Calls);
        Assert.Contains(spy.Calls, id => id == day1.Id || id == day2.Id);
    }

    [Fact]
    public async Task AssignRules_SameClubsRule_DoesNotRecalculate()
    {
        await using var db = CreateDb(nameof(AssignRules_SameClubsRule_DoesNotRecalculate));
        db.Add(ClubsRule(4));
        var comp = await SeedAsync(db, showCombine: false);
        comp.PointRuleClubsId = 4;
        await db.SaveChangesAsync();
        var spy = new RecalcSpy();

        var res = await new CompetitionAdminRepository(db, new NullCache(), spy)
            .AssignRulesAsync(new CompetitionRuleAssignmentDto
            {
                CompetitionIds = [comp.Id],
                SetClubs = true,
                ClubsRuleId = 4 // то же правило — зачёт не менялся
            });

        Assert.True(res.Success);
        Assert.Empty(spy.Calls);
    }
}
