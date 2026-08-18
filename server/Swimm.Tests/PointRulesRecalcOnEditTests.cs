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
/// Правка правила в /Admin/PointsRules пересчитывает материализованный клубный зачёт.
///
/// Парный случай к <see cref="CompetitionRecalcOnFlagTests"/>: там чинили привязку
/// (соревнование → другое правило), здесь — саму ШКАЛУ. Раньше репозиторий только сбрасывал
/// кэш, и после правки хвоста шкалы витрина молча оставалась на старых очках
/// (docs/points-rules-per-competition-plan.md §10.5).
///
/// Пересчёт затрагивает и соревнования БЕЗ явной привязки: они считаются по автоподбору,
/// и правка правила-по-умолчанию меняет их очки ровно так же.
/// </summary>
public class PointRulesRecalcOnEditTests
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
            return Task.FromResult(1);
        }

        public Task<int> RecalculateAllCombinedAsync(CancellationToken ct = default) => Task.FromResult(0);
    }

    private static PointRuleClubs Rule(int id, string version, DateOnly? from = null, params int[] points) => new()
    {
        Id = id,
        Version = version,
        Scope = "all",
        EffectiveFrom = from ?? new DateOnly(2025, 1, 1),
        Entries = points.Select((p, i) => new PointRuleClubsEntry { Place = i + 1, Points = p }).ToList()
    };

    private static PointRuleInputDto Input(PointRuleClubs r, params int[] points) => new()
    {
        Version = r.Version,
        EffectiveFrom = r.EffectiveFrom,
        Scope = r.Scope,
        DefaultPoints = r.DefaultPoints,
        MaxScoringPlace = r.MaxScoringPlace,
        ManualOnly = r.ManualOnly,
        RelayMultiplier = r.RelayMultiplier,
        Entries = points.Select((p, i) => new PointRuleEntryDto { Place = i + 1, Points = p }).ToList()
    };

    private static Competition Comp(int id, string date, int? clubsRuleId, int? eventId = null) => new()
    {
        Id = id, Name = $"Meet {id}", Date = date, PoolType = "50m",
        EventId = eventId, PointRuleClubsId = clubsRuleId
    };

    /// <summary>Материализованный зачёт: без него соревнованию нечего пересчитывать.</summary>
    private static ClubCompetitionStanding Standing(int competitionId, int clubId = 1) => new()
    {
        CompetitionId = competitionId, ClubId = clubId, Rank = 1, Points = 30,
        SwimmerCount = 1, ScoringSwims = 1, SwimCount = 1, ComputedAt = DateTime.UtcNow
    };

    // ── правка шкалы ──────────────────────────────────────────────────────────

    [Fact]
    public async Task ScaleEdit_RebuildsStandingsOfBoundCompetition()
    {
        await using var db = CreateDb(nameof(ScaleEdit_RebuildsStandingsOfBoundCompetition));
        var rule = Rule(1, "v1", null, 30, 28, 26);
        db.Add(rule);
        db.Add(Comp(10, "10/01/2026", clubsRuleId: 1));
        db.Add(Standing(10));
        await db.SaveChangesAsync();
        var spy = new RecalcSpy();

        var res = await new PointRulesAdminRepository(db, new NullCache(), spy)
            .UpdateAsync(PointRuleKind.Clubs, 1, Input(rule, 30, 28, 25)); // хвост шкалы поправлен

        Assert.True(res.Success);
        Assert.Equal([10], spy.Calls);
    }

    [Fact]
    public async Task ScaleEdit_AlsoRebuildsAutoPickedCompetition()
    {
        // У соревнования привязки нет — правило подобрано по дате. Правка шкалы меняет
        // его очки точно так же, как у явно привязанного.
        await using var db = CreateDb(nameof(ScaleEdit_AlsoRebuildsAutoPickedCompetition));
        var rule = Rule(1, "v1", null, 30, 28);
        db.Add(rule);
        db.Add(Comp(10, "10/01/2026", clubsRuleId: null));
        db.Add(Standing(10));
        await db.SaveChangesAsync();
        var spy = new RecalcSpy();

        await new PointRulesAdminRepository(db, new NullCache(), spy)
            .UpdateAsync(PointRuleKind.Clubs, 1, Input(rule, 30, 26));

        Assert.Equal([10], spy.Calls);
    }

    [Fact]
    public async Task EffectiveFromShift_RebuildsCompetitionThatLeftTheRule()
    {
        // Правило перестало действовать на дату соревнования: после сохранения оно уже не
        // в выборке, но его очки посчитаны по старой шкале — пересчитать обязаны.
        await using var db = CreateDb(nameof(EffectiveFromShift_RebuildsCompetitionThatLeftTheRule));
        var rule = Rule(1, "v1", new DateOnly(2025, 1, 1), 30, 28);
        db.Add(rule);
        db.Add(Comp(10, "10/01/2026", clubsRuleId: null));
        db.Add(Standing(10));
        await db.SaveChangesAsync();
        var spy = new RecalcSpy();

        var input = Input(rule, 30, 28);
        input.EffectiveFrom = new DateOnly(2027, 1, 1);
        await new PointRulesAdminRepository(db, new NullCache(), spy)
            .UpdateAsync(PointRuleKind.Clubs, 1, input);

        Assert.Equal([10], spy.Calls);
    }

    [Fact]
    public async Task RenameOnly_DoesNotRebuild()
    {
        await using var db = CreateDb(nameof(RenameOnly_DoesNotRebuild));
        var rule = Rule(1, "v1", null, 30, 28);
        db.Add(rule);
        db.Add(Comp(10, "10/01/2026", clubsRuleId: 1));
        db.Add(Standing(10));
        await db.SaveChangesAsync();
        var spy = new RecalcSpy();

        var input = Input(rule, 30, 28);
        input.Version = "v1-renamed";
        input.Description = "поправили описание";
        await new PointRulesAdminRepository(db, new NullCache(), spy)
            .UpdateAsync(PointRuleKind.Clubs, 1, input);

        Assert.Empty(spy.Calls);
    }

    [Fact]
    public async Task CompetitionWithoutStandings_IsNotRebuilt()
    {
        // Зачёт не материализован — устаревать нечему; иначе правка правила 1 запускала бы
        // пересчёт шестисот соревнований прямо в админском POST.
        await using var db = CreateDb(nameof(CompetitionWithoutStandings_IsNotRebuilt));
        var rule = Rule(1, "v1", null, 30, 28);
        db.Add(rule);
        db.Add(Comp(10, "10/01/2026", clubsRuleId: 1));
        await db.SaveChangesAsync();
        var spy = new RecalcSpy();

        await new PointRulesAdminRepository(db, new NullCache(), spy)
            .UpdateAsync(PointRuleKind.Clubs, 1, Input(rule, 30, 26));

        Assert.Empty(spy.Calls);
    }

    [Fact]
    public async Task EventDays_RebuiltOncePerScoringUnit()
    {
        await using var db = CreateDb(nameof(EventDays_RebuiltOncePerScoringUnit));
        var rule = Rule(1, "v1", null, 30, 28);
        db.Add(rule);
        db.Add(new CompetitionEvent { Id = 7, Name = "Champs" });
        db.Add(Comp(10, "10/01/2026", clubsRuleId: 1, eventId: 7));
        db.Add(Comp(11, "11/01/2026", clubsRuleId: 1, eventId: 7));
        db.Add(Comp(20, "05/02/2026", clubsRuleId: 1));
        // Строка зачёта многодневки живёт на первом дне, но искать надо по всей единице.
        db.Add(Standing(10));
        db.Add(Standing(20));
        await db.SaveChangesAsync();
        var spy = new RecalcSpy();

        await new PointRulesAdminRepository(db, new NullCache(), spy)
            .UpdateAsync(PointRuleKind.Clubs, 1, Input(rule, 30, 26));

        Assert.Equal(2, spy.Calls.Count);
        Assert.Contains(10, spy.Calls);
        Assert.Contains(20, spy.Calls);
    }

    [Fact]
    public async Task SwimmersRuleEdit_DoesNotRebuild()
    {
        // Очки пловца не материализованы (Э6) — им хватает сброса кэша.
        await using var db = CreateDb(nameof(SwimmersRuleEdit_DoesNotRebuild));
        db.Add(new PointRuleSwimmers
        {
            Id = 3, Version = "hp", Scope = "all", EffectiveFrom = new DateOnly(2025, 1, 1),
            Entries = [new PointRuleSwimmersEntry { Place = 1, Points = 5 }]
        });
        var comp = Comp(10, "10/01/2026", clubsRuleId: null);
        comp.PointRuleSwimmersId = 3;
        db.Add(comp);
        db.Add(Standing(10));
        await db.SaveChangesAsync();
        var spy = new RecalcSpy();

        await new PointRulesAdminRepository(db, new NullCache(), spy).UpdateAsync(
            PointRuleKind.Swimmers, 3,
            new PointRuleInputDto
            {
                Version = "hp", Scope = "all", EffectiveFrom = new DateOnly(2025, 1, 1),
                Entries = [new PointRuleEntryDto { Place = 1, Points = 9 }]
            });

        Assert.Empty(spy.Calls);
    }

    [Fact]
    public async Task RebuildFailure_DoesNotFailTheEdit()
    {
        await using var db = CreateDb(nameof(RebuildFailure_DoesNotFailTheEdit));
        var rule = Rule(1, "v1", null, 30, 28);
        db.Add(rule);
        db.Add(Comp(10, "10/01/2026", clubsRuleId: 1));
        db.Add(Standing(10));
        await db.SaveChangesAsync();

        var res = await new PointRulesAdminRepository(db, new NullCache(), new RecalcSpy { Throw = true })
            .UpdateAsync(PointRuleKind.Clubs, 1, Input(rule, 30, 26));

        Assert.True(res.Success);
        var saved = await db.PointRulesClubs.Include(r => r.Entries).FirstAsync(r => r.Id == 1);
        Assert.Equal(26, saved.Entries.Single(e => e.Place == 2).Points);
    }

    // ── создание и удаление правила ───────────────────────────────────────────

    [Fact]
    public async Task CreatingDefaultRule_RebuildsCompetitionsItIntercepts()
    {
        // Новое НЕ-ManualOnly правило свежее прежнего → соревнования без привязки уезжают
        // на него, хотя их никто не трогал.
        await using var db = CreateDb(nameof(CreatingDefaultRule_RebuildsCompetitionsItIntercepts));
        db.Add(Rule(1, "old", new DateOnly(2025, 1, 1), 30, 28));
        db.Add(Comp(10, "10/06/2026", clubsRuleId: null));
        db.Add(Standing(10));
        await db.SaveChangesAsync();
        var spy = new RecalcSpy();

        var res = await new PointRulesAdminRepository(db, new NullCache(), spy).CreateAsync(
            PointRuleKind.Clubs,
            new PointRuleInputDto
            {
                Version = "new-default", Scope = "all", EffectiveFrom = new DateOnly(2026, 1, 1),
                Entries = [new PointRuleEntryDto { Place = 1, Points = 25 }]
            });

        Assert.True(res.Success);
        Assert.Equal([10], spy.Calls);
    }

    [Fact]
    public async Task CreatingManualOnlyRule_DoesNotRebuild()
    {
        // ManualOnly в автоподбор не входит — пока его не привязали руками, ничего не меняет.
        await using var db = CreateDb(nameof(CreatingManualOnlyRule_DoesNotRebuild));
        db.Add(Rule(1, "old", new DateOnly(2025, 1, 1), 30, 28));
        db.Add(Comp(10, "10/06/2026", clubsRuleId: null));
        db.Add(Standing(10));
        await db.SaveChangesAsync();
        var spy = new RecalcSpy();

        await new PointRulesAdminRepository(db, new NullCache(), spy).CreateAsync(
            PointRuleKind.Clubs,
            new PointRuleInputDto
            {
                Version = "hapoel", Scope = "all", EffectiveFrom = new DateOnly(2026, 1, 1),
                ManualOnly = true,
                Entries = [new PointRuleEntryDto { Place = 1, Points = 30 }]
            });

        Assert.Empty(spy.Calls);
    }

    [Fact]
    public async Task DeletingRule_RebuildsCompetitionsThatAutoPickedIt()
    {
        await using var db = CreateDb(nameof(DeletingRule_RebuildsCompetitionsThatAutoPickedIt));
        db.Add(Rule(1, "v1", new DateOnly(2025, 1, 1), 30, 28));
        db.Add(Comp(10, "10/01/2026", clubsRuleId: null)); // без FK — гард удаления не сработает
        db.Add(Standing(10));
        await db.SaveChangesAsync();
        var spy = new RecalcSpy();

        var res = await new PointRulesAdminRepository(db, new NullCache(), spy)
            .DeleteAsync(PointRuleKind.Clubs, 1);

        Assert.True(res.Success);
        Assert.Equal([10], spy.Calls);
    }

    // ── перепривязка со страницы правила ──────────────────────────────────────

    [Fact]
    public async Task Reassign_FromRulePage_RebuildsChangedCompetition()
    {
        await using var db = CreateDb(nameof(Reassign_FromRulePage_RebuildsChangedCompetition));
        db.Add(Rule(1, "v1", null, 30, 28));
        db.Add(Rule(9, "v9", null, 25, 22));
        db.Add(Comp(10, "10/01/2026", clubsRuleId: 1));
        db.Add(Standing(10));
        await db.SaveChangesAsync();
        var spy = new RecalcSpy();

        var res = await new PointRulesAdminRepository(db, new NullCache(), spy)
            .ReassignCompetitionsAsync(PointRuleKind.Clubs, [new PointRuleReassignItem(10, 9)]);

        Assert.True(res.Success);
        Assert.Equal([10], spy.Calls);
    }

    [Fact]
    public async Task Reassign_SameRule_DoesNotRebuild()
    {
        await using var db = CreateDb(nameof(Reassign_SameRule_DoesNotRebuild));
        db.Add(Rule(1, "v1", null, 30, 28));
        db.Add(Comp(10, "10/01/2026", clubsRuleId: 1));
        db.Add(Standing(10));
        await db.SaveChangesAsync();
        var spy = new RecalcSpy();

        await new PointRulesAdminRepository(db, new NullCache(), spy)
            .ReassignCompetitionsAsync(PointRuleKind.Clubs, [new PointRuleReassignItem(10, 1)]);

        Assert.Empty(spy.Calls);
    }
}
