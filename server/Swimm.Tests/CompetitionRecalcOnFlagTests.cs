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
/// Смена флага «Combine All Results» в админке запускает пересчёт объединённых мест.
/// Включили задним числом — строки остались бы с пустым CombinedPlace и тоггл на клиенте
/// показал бы пустую таблицу; выключили — значения обязаны обнулиться.
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
}
