using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Флаги соревнования из превью затягивания (Awards, 🏆 чемпионат, Masters, «зачёт не
/// ведётся», длина бассейна) — они едут в импорт опциями и должны доезжать до записи.
///
/// Раньше их проставляли РУКАМИ после импорта, в панели строки, и про половину забывали.
/// </summary>
public class ImportCompetitionFlagsTests
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

    private static object Item(string poolType = "25m") => new
    {
        country = "ISR",
        competition = "Meet",
        date = "01/06/2026",
        event_style_name = "Freestyle",
        event_style_len = "50",
        event_style_gender = "male",
        pool_type = poolType,
        position = 1,
        heat = 1,
        lane = 1,
        last_name = "Cohen",
        first_name = "Tal",
        birth_year = 2012,
        club = "בני הרצליה",
        time = "00:30.00"
    };

    private static Stream ToStream(object[] items) =>
        new MemoryStream(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(items)));

    [Fact]
    public async Task AppliesFlagsToNewCompetition()
    {
        await using var db = CreateDb(nameof(AppliesFlagsToNewCompetition));

        await new JsonImportService(db, new NullCache()).ImportAsync(
            ToStream([Item()]), "file.json", null,
            new ImportEventOptions(null, null, IsAward: true, IsChampionship: true,
                IsMasters: true, ClubPointsDisabled: true));

        var comp = Assert.Single(await db.Competitions.ToListAsync());
        Assert.True(comp.IsAward);
        Assert.True(comp.IsChampionship);
        Assert.True(comp.IsMasters);
        Assert.True(comp.ClubPointsDisabled);
    }

    [Fact]
    public async Task PoolTypeFromPreview_WinsOverParsedOne()
    {
        // Бассейн входит в ключ дедупа (Name|Date|PoolType): подменять его надо ДО ключа,
        // иначе правка «потом» заводит второй Competition с тем же именем и датой.
        await using var db = CreateDb(nameof(PoolTypeFromPreview_WinsOverParsedOne));

        await new JsonImportService(db, new NullCache()).ImportAsync(
            ToStream([Item(poolType: "25m")]), "file.json", null,
            new ImportEventOptions(null, null, PoolType: "50m"));

        var comp = Assert.Single(await db.Competitions.ToListAsync());
        Assert.Equal("50m", comp.PoolType);
    }

    [Fact]
    public async Task UnsetFlagsAreLeftAlone()
    {
        // null = «не трогать»: превью не обязано знать про поля, которых в нём нет.
        await using var db = CreateDb(nameof(UnsetFlagsAreLeftAlone));

        await new JsonImportService(db, new NullCache()).ImportAsync(
            ToStream([Item()]), "file.json", null, new ImportEventOptions(null, null));

        var comp = Assert.Single(await db.Competitions.ToListAsync());
        Assert.False(comp.IsChampionship);
        Assert.False(comp.IsAward);
        Assert.Equal("25m", comp.PoolType);
    }

    [Fact]
    public async Task RepullUpdatesFlagsOfExistingCompetition()
    {
        // Перезатягивание: свежее решение админа должно побеждать то, что стояло раньше.
        await using var db = CreateDb(nameof(RepullUpdatesFlagsOfExistingCompetition));
        db.Competitions.Add(new Competition
        {
            Id = 1, Name = "Meet", Date = "01/06/2026", PoolType = "25m",
            IsAward = false, IsChampionship = false
        });
        await db.SaveChangesAsync();

        await new JsonImportService(db, new NullCache()).ImportAsync(
            ToStream([Item()]), "file.json", null,
            new ImportEventOptions(null, null, OverwriteExisting: true,
                IsAward: true, IsChampionship: true));

        var comp = Assert.Single(await db.Competitions.ToListAsync());
        Assert.True(comp.IsAward);
        Assert.True(comp.IsChampionship);
    }
}
