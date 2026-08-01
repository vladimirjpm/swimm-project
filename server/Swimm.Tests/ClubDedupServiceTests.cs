using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Тесты кандидатов на склейку клубов (ClubDedupService, docs/tasks/club-merge-plan.md,
/// фаза B): три эвристики, исключение синтетики и псевдоклубов.
/// </summary>
public class ClubDedupServiceTests
{
    private static SwimmDbContext CreateDb(string name) =>
        new(new DbContextOptionsBuilder<SwimmDbContext>()
            .UseInMemoryDatabase(name)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private static Competition NewCompetition(string name = "Meet") =>
        new() { Name = name, Date = "01/06/2026", PoolType = "25m" };

    private static ResultRecord NewResult(Swimmer s, Club c, Competition comp, Style st, string distance = "50") =>
        new()
        {
            Swimmer = s, Competition = comp, Style = st, Distance = distance,
            Club = c, Gender = "male", CompetitionDate = new DateTime(2026, 6, 1)
        };

    // ── Эвристика 0: одинаковое имя у разных Id ──────────────────────────────

    [Fact]
    public async Task SameNameHeuristic_PairsIdenticalNames_Sure()
    {
        // Реальный случай (2026-08-01): 65 групп, 68 дублей, 7793 результата — следы
        // второго импорта, где тот же клуб завёлся заново уже с NameEn.
        // До этой эвристики они не находились вовсе: суффикс требует хвоста, а
        // левенштейн совпадающие имена явно пропускает.
        await using var db = CreateDb(nameof(SameNameHeuristic_PairsIdenticalNames_Sure));
        var big = new Club { Name = "הפועל דולפין נתניה" };
        var small = new Club { Name = "הפועל דולפין נתניה", NameEn = "Hapoel Dolphin Netanya" };
        var comp = NewCompetition();
        var style = new Style { Name = "Freestyle" };
        var s1 = new Swimmer { LastName = "A", FirstName = "A", BirthYear = 2010 };
        var s2 = new Swimmer { LastName = "B", FirstName = "B", BirthYear = 2010 };
        db.AddRange(big, small, comp, style, s1, s2);
        db.Results.AddRange(
            NewResult(s1, big, comp, style),
            NewResult(s2, big, comp, style, "100"),
            NewResult(s1, small, comp, style, "200"));
        await db.SaveChangesAsync();

        var report = await new ClubDedupService(db).FindCandidatesAsync();

        var cand = Assert.Single(report.Candidates);
        Assert.Equal("same-name", cand.Heuristic);
        Assert.True(cand.Sure);
        Assert.Equal(big.Id, cand.CanonicalId);      // канон — у кого больше результатов
        Assert.Equal(small.Id, cand.DuplicateId);
    }

    [Fact]
    public async Task SameNameHeuristic_ThreeIds_AllCollapseIntoOneCanonical()
    {
        // הפועל ירושלים живёт под ТРЕМЯ Id — все дубли должны уехать в один канон,
        // иначе merge упадёт на «цепочке склеек».
        await using var db = CreateDb(nameof(SameNameHeuristic_ThreeIds_AllCollapseIntoOneCanonical));
        var a = new Club { Name = "הפועל ירושלים" };
        var b = new Club { Name = "הפועל ירושלים" };
        var c = new Club { Name = "הפועל ירושלים" };
        var comp = NewCompetition();
        var style = new Style { Name = "Freestyle" };
        var sw = new Swimmer { LastName = "A", FirstName = "A", BirthYear = 2010 };
        db.AddRange(a, b, c, comp, style, sw);
        db.Results.AddRange(
            NewResult(sw, a, comp, style),
            NewResult(sw, a, comp, style, "100"),
            NewResult(sw, b, comp, style, "200"));
        await db.SaveChangesAsync();

        var report = await new ClubDedupService(db).FindCandidatesAsync();

        Assert.Equal(2, report.Candidates.Count);
        Assert.All(report.Candidates, x => Assert.Equal(a.Id, x.CanonicalId));
        Assert.Equal([b.Id, c.Id], report.Candidates.Select(x => x.DuplicateId).OrderBy(x => x));
    }

    // ── Эвристика 1: мусорный суффикс ────────────────────────────────────────

    [Fact]
    public void StripGarbageTail_RemovesKnownTails()
    {
        Assert.Equal("הפועל דולפין נתניה", ClubDedupService.StripGarbageTail("הפועל דולפין נתניה DNS"));
        Assert.Equal("הפועל דולפין נתניה", ClubDedupService.StripGarbageTail("הפועל דולפין נתניה 4.4 SW /"));
        Assert.Equal("SWIM TLV", ClubDedupService.StripGarbageTail("SWIM TLV 8.3 SW /"));
        Assert.Equal("SWIM TLV", ClubDedupService.StripGarbageTail("SWIM TLV NS"));
        Assert.Equal("אקוותיקים", ClubDedupService.StripGarbageTail("אקוותיקים 10.2 SW / DNF"));
        // Без хвоста — без изменений (в т.ч. цифры не в хвосте).
        Assert.Equal("Maccabi 2000", ClubDedupService.StripGarbageTail("Maccabi 2000"));
    }

    [Fact]
    public async Task SuffixHeuristic_PairsGarbageWithCleanClub_Sure()
    {
        await using var db = CreateDb(nameof(SuffixHeuristic_PairsGarbageWithCleanClub_Sure));
        var clean = new Club { Name = "הפועל דולפין נתניה" };
        var garbage = new Club { Name = "הפועל דולפין נתניה DNS" };
        db.AddRange(clean, garbage);
        await db.SaveChangesAsync();

        var report = await new ClubDedupService(db).FindCandidatesAsync();

        var cand = Assert.Single(report.Candidates);
        Assert.Equal("suffix", cand.Heuristic);
        Assert.True(cand.Sure);
        Assert.Equal(clean.Id, cand.CanonicalId);   // канон — чистое название
        Assert.Equal(garbage.Id, cand.DuplicateId);
    }

    [Fact]
    public async Task SuffixHeuristic_NoCleanCounterpart_NoCandidate()
    {
        await using var db = CreateDb(nameof(SuffixHeuristic_NoCleanCounterpart_NoCandidate));
        db.Add(new Club { Name = "SWIM TLV 8.3 SW /" }); // «чистого» SWIM TLV нет
        await db.SaveChangesAsync();

        var report = await new ClubDedupService(db).FindCandidatesAsync();

        Assert.Empty(report.Candidates);
    }

    // ── Эвристика 2: пересечение пловцов (кросс-скрипт) ──────────────────────

    [Fact]
    public async Task SwimmerOverlap_CrossScriptPair_HebrewIsCanonical()
    {
        await using var db = CreateDb(nameof(SwimmerOverlap_CrossScriptPair_HebrewIsCanonical));
        var hebrew = new Club { Name = "הפועל דולפין נתניה" };
        var latin = new Club { Name = "Hapoel Dolphine Netanya" };
        var comp = NewCompetition();
        var style = new Style { Name = "Freestyle" };
        db.AddRange(hebrew, latin, comp, style);

        // Три общих пловца: приписаны к ивритскому клубу, но плавали за латинский
        // (EN-протокол Maccabiah). Латинский клуб меньше — 3 из 3 = 100% ≥ 30%.
        for (var i = 0; i < 3; i++)
        {
            var s = new Swimmer { LastName = $"Swimmer{i}", FirstName = "X", BirthYear = 2010, Club = hebrew };
            db.Add(s);
            db.Results.Add(NewResult(s, hebrew, comp, style, "50"));
            db.Results.Add(NewResult(s, latin, comp, style, "100"));
        }
        await db.SaveChangesAsync();

        var report = await new ClubDedupService(db).FindCandidatesAsync();

        var cand = Assert.Single(report.Candidates, c => c.Heuristic == "swimmers");
        Assert.False(cand.Sure);
        Assert.Equal(hebrew.Id, cand.CanonicalId);  // кросс-скрипт: канон — ивритская запись
        Assert.Equal(latin.Id, cand.DuplicateId);
        Assert.Equal(3, cand.SharedSwimmers);
    }

    [Fact]
    public async Task SwimmerOverlap_BelowThreshold_NoCandidate()
    {
        await using var db = CreateDb(nameof(SwimmerOverlap_BelowThreshold_NoCandidate));
        var a = new Club { Name = "Club Alpha" };
        var b = new Club { Name = "Club Beta" };
        var comp = NewCompetition();
        var style = new Style { Name = "Freestyle" };
        db.AddRange(a, b, comp, style);

        // По 10 пловцов в каждом, общих только 2 (< 3 минимум).
        for (var i = 0; i < 10; i++)
        {
            var sa = new Swimmer { LastName = $"A{i}", FirstName = "X", BirthYear = 2010, Club = a };
            var sb = new Swimmer { LastName = $"B{i}", FirstName = "X", BirthYear = 2010, Club = b };
            db.AddRange(sa, sb);
            if (i < 2) db.Results.Add(NewResult(sa, b, comp, style, $"{i}"));
        }
        await db.SaveChangesAsync();

        var report = await new ClubDedupService(db).FindCandidatesAsync();

        Assert.DoesNotContain(report.Candidates, c => c.Heuristic == "swimmers");
    }

    // ── Эвристика 3: Левенштейн ≤ 1 внутри одного скрипта ────────────────────

    [Fact]
    public async Task Levenshtein_TypoWithinSameScript_Candidate()
    {
        await using var db = CreateDb(nameof(Levenshtein_TypoWithinSameScript_Candidate));
        db.AddRange(
            new Club { Name = "Maccabi Haifa" },
            new Club { Name = "Macabi Haifa" },      // dist 1 — кандидат
            new Club { Name = "Hapoel Haifa" });     // dist > 1 — шум, не показываем
        await db.SaveChangesAsync();

        var report = await new ClubDedupService(db).FindCandidatesAsync();

        var cand = Assert.Single(report.Candidates);
        Assert.Equal("levenshtein", cand.Heuristic);
        Assert.False(cand.Sure);
    }

    [Fact]
    public async Task Levenshtein_CrossScript_NotCompared()
    {
        await using var db = CreateDb(nameof(Levenshtein_CrossScript_NotCompared));
        // Разные скрипты Левенштейном не сравниваются (их ловит пересечение пловцов).
        db.AddRange(new Club { Name = "מכבי" }, new Club { Name = "Mkbi" });
        await db.SaveChangesAsync();

        var report = await new ClubDedupService(db).FindCandidatesAsync();

        Assert.Empty(report.Candidates);
    }

    // ── Один клуб — дублем максимум в одной паре ─────────────────────────────

    [Fact]
    public async Task SameDuplicate_OfferedInOnePairOnly_HigherConfidenceWins()
    {
        await using var db = CreateDb(nameof(SameDuplicate_OfferedInOnePairOnly_HigherConfidenceWins));
        var hebrew = new Club { Name = "הפועל נתניה" };
        var latin = new Club { Name = "Hapoel Netanya" };
        var typo = new Club { Name = "Hapoel Netanyb" };
        var comp = NewCompetition();
        var style = new Style { Name = "Freestyle" };
        db.AddRange(hebrew, latin, typo, comp, style);

        // latin — дубль и по пересечению пловцов (с hebrew), и по Левенштейну (с typo,
        // у которого больше результатов). Должна остаться одна пара — swimmers.
        for (var i = 0; i < 3; i++)
        {
            var s = new Swimmer { LastName = $"S{i}", FirstName = "X", BirthYear = 2010, Club = hebrew };
            db.Add(s);
            db.Results.Add(NewResult(s, latin, comp, style, $"{50 + i}"));
        }
        var t = new Swimmer { LastName = "T", FirstName = "X", BirthYear = 2011, Club = typo };
        db.Add(t);
        foreach (var d in new[] { "50", "100", "200", "400" })
        {
            db.Results.Add(NewResult(t, typo, comp, style, d));
        }
        await db.SaveChangesAsync();

        var report = await new ClubDedupService(db).FindCandidatesAsync();

        var withLatinAsDup = report.Candidates.Where(c => c.DuplicateId == latin.Id).ToList();
        var cand = Assert.Single(withLatinAsDup);
        Assert.Equal("swimmers", cand.Heuristic);   // более уверенная эвристика победила
    }

    // ── Исключения: синтетика и псевдоклубы ──────────────────────────────────

    [Fact]
    public async Task SynthAndPseudoClubs_Excluded()
    {
        await using var db = CreateDb(nameof(SynthAndPseudoClubs_Excluded));
        db.AddRange(
            new Club { Name = "SYNTH Club 1" },
            new Club { Name = "SYNTH Club 2" },              // лев-дистанция 1, но синтетика
            new Club { Name = "Israel", IsPseudo = true },   // флаг ставит импорт по Countries
            new Club { Name = "Israek" });                   // dist 1 к псевдоклубу — не кандидат
        await db.SaveChangesAsync();

        var report = await new ClubDedupService(db).FindCandidatesAsync();

        Assert.Empty(report.Candidates);
        Assert.Equal(1, report.RealClubs);           // SYNTH% и псевдоклубы не считаются реальными
    }
}
