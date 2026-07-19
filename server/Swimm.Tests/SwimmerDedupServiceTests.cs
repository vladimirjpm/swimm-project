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
    public async Task FindCandidates_CrossScript_LatinMainMatchesEnFields_SureHebrewCanonical()
    {
        // Кросс-скриптовый дубль (кейс SHOUSTIN): пловец из EN-протокола Maccabiah
        // с латиницей в основных полях против ивритского с заполненными EN-полями.
        // Клубы разные — при точном совпадении имени это НЕ понижает уверенность.
        await using var db = CreateDb(nameof(FindCandidates_CrossScript_LatinMainMatchesEnFields_SureHebrewCanonical));
        var clubHe = new Club { Name = "מכבי" };
        var clubEn = new Club { Name = "Maccabi TLV" };
        var hebrew = S("שוסטין", "מקסים", 1981, clubHe);
        hebrew.LastNameEn = "SHOUSTIN";
        hebrew.FirstNameEn = "Maxim";
        var latin = S("SHOUSTIN", "Maxim", 1981, clubEn);
        db.AddRange(clubHe, clubEn, hebrew, latin);
        await db.SaveChangesAsync();

        var report = await new SwimmerDedupService(db).FindCandidatesAsync();

        var c = Assert.Single(report.Candidates);
        Assert.True(c.Sure);
        Assert.Equal(0, c.Distance);
        Assert.Equal(hebrew.Id, c.CanonicalId);   // канонический — ивритская запись
        Assert.Equal(latin.Id, c.DuplicateId);
    }

    [Fact]
    public async Task FindCandidates_CrossScript_LooseTransliteration_NotPaired()
    {
        // Транслитерационные вариации дальше dist=1 — шум, кросс-скрипт их не предлагает.
        await using var db = CreateDb(nameof(FindCandidates_CrossScript_LooseTransliteration_NotPaired));
        var hebrew = S("שוסטין", "מקסים", 1981);
        hebrew.LastNameEn = "SHUSTIN";       // dist=2 от SHOWSTEEN
        hebrew.FirstNameEn = "Maxim";
        var latin = S("SHOWSTEEN", "Maxim", 1981);
        db.AddRange(hebrew, latin);
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
    public async Task DeleteOrphans_NullIds_DeletesAllCurrentOrphans()
    {
        await using var db = CreateDb(nameof(DeleteOrphans_NullIds_DeletesAllCurrentOrphans));
        var orphan1 = S("Один", "Пловец", 2010);
        var orphan2 = S("Два", "Пловец", 2011);
        var synth = S("Synth", "One", 2000);
        synth.SwimmerOrgId = "SYNTH-1";
        db.AddRange(orphan1, orphan2, synth);
        await db.SaveChangesAsync();

        var report = await new SwimmerDedupService(db).DeleteOrphansAsync(null);

        Assert.Equal(2, report.Deleted);
        Assert.Equal(0, await db.Swimmers.CountAsync(s => s.Id == orphan1.Id || s.Id == orphan2.Id));
        Assert.Equal(1, await db.Swimmers.CountAsync()); // синтетика не тронута
    }

    [Fact]
    public async Task DeleteOrphans_SwimmerWithResults_NotDeleted()
    {
        await using var db = CreateDb(nameof(DeleteOrphans_SwimmerWithResults_NotDeleted));
        var comp = new Competition { Name = "M", Date = "01/06/2026", PoolType = "25m" };
        var style = new Style { Name = "free" };
        var swimmer = S("Есть", "Результаты", 2010);
        db.AddRange(comp, style, swimmer);
        await db.SaveChangesAsync();
        db.Results.Add(new ResultRecord { Swimmer = swimmer, Competition = comp, Style = style, Distance = "50", Gender = "male", CompetitionDate = DateTime.UtcNow });
        await db.SaveChangesAsync();

        var report = await new SwimmerDedupService(db).DeleteOrphansAsync(null);

        Assert.Equal(0, report.Deleted);
        Assert.Equal(1, await db.Swimmers.CountAsync(s => s.Id == swimmer.Id));
    }

    [Fact]
    public async Task DeleteOrphans_SwimmerWithTrainingResults_NotDeleted()
    {
        await using var db = CreateDb(nameof(DeleteOrphans_SwimmerWithTrainingResults_NotDeleted));
        var swimmer = S("Тренируется", "Пловец", 2010);
        db.Add(swimmer);
        await db.SaveChangesAsync();
        db.TrainingResults.Add(new TrainingResult { SwimmerId = swimmer.Id, Distance = "50", Gender = "male", TimeOriginal = "30.00" });
        await db.SaveChangesAsync();

        var report = await new SwimmerDedupService(db).DeleteOrphansAsync(null);

        Assert.Equal(0, report.Deleted);
        Assert.Equal(1, await db.Swimmers.CountAsync(s => s.Id == swimmer.Id));
    }

    [Fact]
    public async Task DeleteOrphans_SwimmerWithHubGroupMembership_NotDeleted()
    {
        await using var db = CreateDb(nameof(DeleteOrphans_SwimmerWithHubGroupMembership_NotDeleted));
        var group = new HubGroup { Name = "Group", Slug = "group-" + nameof(DeleteOrphans_SwimmerWithHubGroupMembership_NotDeleted) };
        var swimmer = S("Член", "Группы", 2010);
        db.AddRange(group, swimmer);
        await db.SaveChangesAsync();
        db.HubGroupMembers.Add(new HubGroupMember { HubGroupId = group.Id, SwimmerId = swimmer.Id });
        await db.SaveChangesAsync();

        var report = await new SwimmerDedupService(db).DeleteOrphansAsync(null);

        Assert.Equal(0, report.Deleted);
        Assert.Equal(1, await db.Swimmers.CountAsync(s => s.Id == swimmer.Id));
    }

    [Fact]
    public async Task DeleteOrphans_SynthExcluded()
    {
        await using var db = CreateDb(nameof(DeleteOrphans_SynthExcluded));
        var synth = S("Synth", "One", 2000);
        synth.SwimmerOrgId = "SYNTH-1";
        db.Add(synth);
        await db.SaveChangesAsync();

        var report = await new SwimmerDedupService(db).DeleteOrphansAsync(null);

        Assert.Equal(0, report.Deleted);
        Assert.Equal(1, await db.Swimmers.CountAsync());
    }

    [Fact]
    public async Task DeleteOrphans_IdsWithNonOrphan_Skipped()
    {
        await using var db = CreateDb(nameof(DeleteOrphans_IdsWithNonOrphan_Skipped));
        var comp = new Competition { Name = "M", Date = "01/06/2026", PoolType = "25m" };
        var style = new Style { Name = "free" };
        var withResults = S("Есть", "Результаты", 2010);
        var orphan = S("Одинокий", "Пловец", 2011);
        db.AddRange(comp, style, withResults, orphan);
        await db.SaveChangesAsync();
        db.Results.Add(new ResultRecord { Swimmer = withResults, Competition = comp, Style = style, Distance = "50", Gender = "male", CompetitionDate = DateTime.UtcNow });
        await db.SaveChangesAsync();

        var report = await new SwimmerDedupService(db).DeleteOrphansAsync([withResults.Id, orphan.Id]);

        Assert.Equal(1, report.Deleted);
        Assert.Equal([orphan.Id], report.DeletedIds);
        Assert.Equal([withResults.Id], report.SkippedIds);
        Assert.Equal(1, await db.Swimmers.CountAsync(s => s.Id == withResults.Id));
        Assert.Equal(0, await db.Swimmers.CountAsync(s => s.Id == orphan.Id));
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
