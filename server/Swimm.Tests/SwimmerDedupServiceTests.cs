using Microsoft.EntityFrameworkCore;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>Поиск кандидатов-дублей для админ-UI (фаза 7.2) — порт логики dedup-report.sql.</summary>
public class SwimmerDedupServiceTests
{
    private static SwimmDbContext CreateDb(string name) =>
        new(new DbContextOptionsBuilder<SwimmDbContext>().UseInMemoryDatabase(name).Options);

    private static Swimmer S(string last, string first, int year, Club? club = null, string? gender = "male") =>
        new() { LastName = last, FirstName = first, BirthYear = year, Club = club, Gender = gender };

    [Fact]
    public void Normalize_HebrewFinalsAndGeresh()
    {
        Assert.Equal(SwimmerDedupService.Normalize("סמוזיץ' נדב"), SwimmerDedupService.Normalize("סמוזיץ׳ נדב"));
        Assert.Equal("abc def", SwimmerDedupService.Normalize("  ABC   DEF "));
    }

    [Fact]
    public void Normalize_TypographicApostrophe()
    {
        Assert.Equal(SwimmerDedupService.Normalize("O'Brien"), SwimmerDedupService.Normalize("O’Brien"));
    }

    [Fact]
    public void Normalize_DifferentLettersNotEqual()
    {
        Assert.NotEqual(SwimmerDedupService.Normalize("דרזנר דין"), SwimmerDedupService.Normalize("דרזנר שון"));
    }

    [Theory]
    [InlineData("abc", "abc", 0)]
    [InlineData("abc", "abd", 1)]
    [InlineData("abc", "xyz", 3)] // > max → max+1
    public void Levenshtein_WithCutoff(string a, string b, int expected)
        => Assert.Equal(expected, SwimmerDedupService.Levenshtein(a, b, 2));

    [Fact]
    public async Task FindCandidates_GereshVariant_SureAndCanonicalByResults()
    {
        await using var db = CreateDb(nameof(FindCandidates_GereshVariant_SureAndCanonicalByResults));
        var club = new Club { Name = "Club" };
        var a = S("סמוזיץ'", "נדב", 2017, club);
        var b = S("סמוזיץ׳", "נדב", 2017, club);
        var comp = new Competition { Name = "M", Date = "01/06/2026", PoolType = "25m" };
        var style = new Style { Name = "free" };
        db.AddRange(club, a, b, comp, style);
        await db.SaveChangesAsync();
        // у b больше результатов → b канонический
        db.Results.Add(new ResultRecord { Swimmer = b, Competition = comp, Style = style, Club = club, Distance = "50", Gender = "male", CompetitionDate = DateTime.UtcNow });
        await db.SaveChangesAsync();

        var report = await new SwimmerDedupService(db).FindCandidatesAsync();

        var c = Assert.Single(report.Candidates);
        Assert.True(c.Sure);
        Assert.Equal(0, c.Distance);
        Assert.Equal(b.Id, c.CanonicalId);
        Assert.Equal(a.Id, c.DuplicateId);
    }

    [Fact]
    public async Task FindCandidates_DifferentYearOrNoise_NotPaired()
    {
        await using var db = CreateDb(nameof(FindCandidates_DifferentYearOrNoise_NotPaired));
        var c1 = new Club { Name = "A" };
        var c2 = new Club { Name = "B" };
        db.AddRange(c1, c2,
            S("כהן", "נטע", 2015, c1),
            S("כהן", "נטע", 2016, c1),      // другой год — не пара
            S("שמיר", "יובל", 2015, c1),
            S("מדר", "יובל", 2015, c2));    // dist=2, разные клубы — шум, отсекается
        await db.SaveChangesAsync();

        var report = await new SwimmerDedupService(db).FindCandidatesAsync();
        Assert.Empty(report.Candidates);
    }

    [Fact]
    public async Task FindCandidates_SyntheticExcluded_OrphanListed()
    {
        await using var db = CreateDb(nameof(FindCandidates_SyntheticExcluded_OrphanListed));
        var synth = S("Synth", "One", 2000);
        synth.SwimmerOrgId = "SYNTH-1";
        var synth2 = S("Synth", "Two", 2000);
        synth2.SwimmerOrgId = "SYNTH-2";
        var orphan = S("Одинокий", "Пловец", 2010);
        db.AddRange(synth, synth2, orphan);
        await db.SaveChangesAsync();

        var report = await new SwimmerDedupService(db).FindCandidatesAsync();

        Assert.Equal(1, report.RealSwimmers);
        Assert.Empty(report.Candidates);
        var o = Assert.Single(report.Orphans);
        Assert.Equal(orphan.Id, o.Id);
    }

    [Fact]
    public async Task FindCandidates_YearZeroPhantoms_NotSure()
    {
        await using var db = CreateDb(nameof(FindCandidates_YearZeroPhantoms_NotSure));
        var club = new Club { Name = "Дельфин" };
        db.AddRange(club, S("אדם ט", "", 0, club, "M"), S("אדם פ", "", 0, club, "M"));
        await db.SaveChangesAsync();

        var report = await new SwimmerDedupService(db).FindCandidatesAsync();
        var c = Assert.Single(report.Candidates);
        Assert.False(c.Sure); // BirthYear=0 в «уверенные» не попадает
    }
}
