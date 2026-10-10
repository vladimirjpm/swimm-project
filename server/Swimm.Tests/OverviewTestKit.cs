using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Repositories;

namespace Swimm.Tests;

/// <summary>
/// Общая обвязка тестов карточек Overview (<c>Overview*CardTests</c>): InMemory-база, стиль,
/// клуб и строка результата с разумными умолчаниями. Тест задаёт только то, что проверяет, —
/// иначе каждый файл тащил бы сорок строк одинаковой посадки и терял за ними суть.
/// Правила карточек — docs/competition-overview-cards.md.
/// </summary>
internal sealed class OverviewTestKit : IAsyncDisposable
{
    public static readonly DateTime Date = new(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public SwimmReadDbContext Db { get; }
    public Style Style { get; private set; } = null!;
    public Club Club { get; private set; } = null!;
    private Country _country = null!;

    private OverviewTestKit(SwimmReadDbContext db) => Db = db;

    public static async Task<OverviewTestKit> CreateAsync(string name)
    {
        var db = new SwimmReadDbContext(new DbContextOptionsBuilder<SwimmReadDbContext>()
            .UseInMemoryDatabase(name)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);
        var kit = new OverviewTestKit(db)
        {
            Style = new Style { Name = "freestyle" },
            Club = new Club { Name = "Alpha", NameEn = "Alpha" },
            _country = new Country { CountryCode = "ISR", CountryName = "ISR" },
        };
        db.AddRange(kit.Style, kit.Club, kit._country);
        await db.SaveChangesAsync();
        return kit;
    }

    public async Task<Competition> AddCompetitionAsync(Action<Competition>? setup = null)
    {
        var comp = new Competition { Name = "Meet", Country = _country, Date = "01/01/2024", PoolType = "50m" };
        setup?.Invoke(comp);
        Db.Add(comp);
        await Db.SaveChangesAsync();
        return comp;
    }

    public async Task<Club> AddClubAsync(string name)
    {
        var club = new Club { Name = name, NameEn = name };
        Db.Add(club);
        await Db.SaveChangesAsync();
        return club;
    }

    public async Task<Swimmer> AddSwimmerAsync(string tag, string gender = "male", int birthYear = 2000)
    {
        var s = new Swimmer
        {
            LastName = tag, FirstName = tag, LastNameEn = tag, FirstNameEn = tag,
            BirthYear = birthYear, Gender = gender
        };
        Db.Add(s);
        await Db.SaveChangesAsync();
        return s;
    }

    /// <summary>Строка результата: 100 в/с, место и очки — по месту вызова.</summary>
    public ResultRecord Row(Competition comp, Swimmer swimmer, int? position = null, int points = 0,
        string? gender = null, Club? club = null, int? timeMs = null) => new()
    {
        CompetitionId = comp.Id, SwimmerId = swimmer.Id, ClubId = (club ?? Club).Id, StyleId = Style.Id,
        Distance = "100", Gender = gender ?? swimmer.Gender ?? "male", CompetitionDate = Date,
        TimeOriginal = "1:00.00", TimeMillisecond = timeMs, AgeGroup = "Open",
        EventStyleAge = "100 freestyle Open", Position = position, InternationalPoints = points
    };

    public async Task AddRowsAsync(params ResultRecord[] rows)
    {
        Db.Results.AddRange(rows);
        await Db.SaveChangesAsync();
    }

    public Task<CompetitionOverviewDto> OverviewAsync(ResultFilter filter) =>
        new ResultRepository(Db, new NullCache()).GetCompetitionOverviewAsync(filter);

    public Task<CompetitionOverviewDto> OverviewAsync(Competition comp, bool combined = false) =>
        OverviewAsync(new ResultFilter { CompetitionId = comp.Id, Combined = combined });

    public ValueTask DisposeAsync() => Db.DisposeAsync();

    /// <summary>Кэш всегда промахивается — каждый вызов идёт в базу.</summary>
    private sealed class NullCache : ICacheService
    {
        public Task<T?> GetAsync<T>(string key) => Task.FromResult<T?>(default);
        public Task SetAsync<T>(string key, T value, TimeSpan ttl) => Task.CompletedTask;
        public Task RemoveAsync(string key) => Task.CompletedTask;
        public Task InvalidateAllAsync() => Task.CompletedTask;
    }
}
