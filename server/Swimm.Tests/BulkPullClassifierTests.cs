using Swimm.Application.Dtos;
using Swimm.Application.Validation;
using Swimm.Infrastructure.Repositories;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Классификатор пакетного затягивания: «беспроблемная строка или нет»
/// (docs/plans/bulk-pull-plan.md §4). По кейсу на каждое правило — если в превью появится
/// новый вопрос к человеку, он обязан появиться и здесь, иначе пачка ответит за админа сама.
/// </summary>
public class BulkPullClassifierTests
{
    private static DiscoveryPreviewResult Preview(
        int resultCount = 100,
        int days = 1,
        int records = 0,
        int? existingCompetitionId = null,
        OfficialClubStandingProbe? standing = null,
        params string[] warnings)
    {
        var parsed = new ParsedCompetition
        {
            Format = "IsrOrg",
            ResultsJson = "[]",
            ResultCount = resultCount,
            Competitions = Enumerable.Range(1, days)
                .Select(i => new ParsedCompetitionSummary($"День {i}", $"0{i}/11/2025", resultCount / days))
                .ToList(),
            Warnings = warnings
        };

        return new DiscoveryPreviewResult(
            Guid.NewGuid(), parsed, ["he"], existingCompetitionId, [],
            new ImportRecordPreviewDto { Count = records }, standing);
    }

    private static RegulationFetchDto Regulation(
        bool medals = true, bool clubStanding = false, bool championship = false) =>
        new(true, "https://loglig.com:2053/LeagueTable/ShowLeagueDoc/3185",
            new RegulationAnalysisDto(medals, clubStanding, championship, []));

    [Fact]
    public void Clean_WhenNothingNeedsADecision()
    {
        var (verdict, reasons) = BulkPullClassifier.Classify(Preview(), Regulation(), isChampionshipByName: false);

        Assert.Equal(BulkPullVerdict.Clean, verdict);
        Assert.Empty(reasons);
    }

    [Fact]
    public void NeedsReview_WhenFileBreaksEvenOneRecord()
    {
        // Порога нет намеренно: рекорд — всегда ручная проверка (решение Влада 2026-08-23).
        var (verdict, reasons) = BulkPullClassifier.Classify(
            Preview(records: 1), Regulation(), isChampionshipByName: false);

        Assert.Equal(BulkPullVerdict.NeedsReview, verdict);
        Assert.Contains(reasons, r => r.Contains("рекорд"));
    }

    [Fact]
    public void NeedsReview_WhenCompetitionAlreadyInDb()
    {
        var (verdict, reasons) = BulkPullClassifier.Classify(
            Preview(existingCompetitionId: 42), Regulation(), isChampionshipByName: false);

        Assert.Equal(BulkPullVerdict.NeedsReview, verdict);
        Assert.Contains(reasons, r => r.Contains("перезапис"));
    }

    [Fact]
    public void NeedsReview_WhenFileHasSeveralDays()
    {
        var (verdict, reasons) = BulkPullClassifier.Classify(
            Preview(days: 3), Regulation(), isChampionshipByName: false);

        Assert.Equal(BulkPullVerdict.NeedsReview, verdict);
        Assert.Contains(reasons, r => r.Contains("дней в файле"));
    }

    [Fact]
    public void NeedsReview_WhenClubStandingHasNoMatchingRule()
    {
        // Иначе соревнование уедет на автоподбор правила по дате и получит ЧУЖУЮ шкалу.
        var probe = new OfficialClubStandingProbe(
            true, new Dictionary<int, int> { [1] = 40 }, MatchedRuleId: null, null, "зачёт есть");

        var (verdict, reasons) = BulkPullClassifier.Classify(
            Preview(standing: probe), Regulation(), isChampionshipByName: false);

        Assert.Equal(BulkPullVerdict.NeedsReview, verdict);
        Assert.Contains(reasons, r => r.Contains("правила под его шкалу нет"));
    }

    [Fact]
    public void Clean_WhenClubStandingMatchesExistingRule()
    {
        var probe = new OfficialClubStandingProbe(
            true, new Dictionary<int, int> { [1] = 40 }, MatchedRuleId: 7, "40pt.24pl.2026", "зачёт есть");

        var (verdict, _) = BulkPullClassifier.Classify(
            Preview(standing: probe), Regulation(), isChampionshipByName: false);

        Assert.Equal(BulkPullVerdict.Clean, verdict);
    }

    [Fact]
    public void NeedsReview_WhenParserWarns()
    {
        var (verdict, reasons) = BulkPullClassifier.Classify(
            Preview(warnings: "Эстафета без состава"), Regulation(), isChampionshipByName: false);

        Assert.Equal(BulkPullVerdict.NeedsReview, verdict);
        Assert.Contains(reasons, r => r.Contains("предупреждение парсера"));
    }

    [Fact]
    public void NoRegulation_StillImportableButFlagged()
    {
        // Решение Влада: строку без регламента импортируем, но помечаем — галочку можно снять.
        var (verdict, reasons) = BulkPullClassifier.Classify(
            Preview(), regulation: null, isChampionshipByName: false);

        Assert.Equal(BulkPullVerdict.NoRegulation, verdict);
        Assert.NotEmpty(reasons);
    }

    [Fact]
    public void Empty_WhenParserFoundNothing()
    {
        var (verdict, _) = BulkPullClassifier.Classify(
            DiscoveryPreviewResult.Failed("No competitions found in PDF (language=he)"),
            Regulation(), isChampionshipByName: false);

        Assert.Equal(BulkPullVerdict.Empty, verdict);
    }

    [Fact]
    public void Failed_WhenFetchBroke()
    {
        // Сетевой сбой — повод вернуться, а не «тянуть нечего».
        var (verdict, _) = BulkPullClassifier.Classify(
            DiscoveryPreviewResult.Failed("Результаты не опубликованы (нет loglig-id)."),
            Regulation(), isChampionshipByName: false);

        Assert.Equal(BulkPullVerdict.Failed, verdict);
    }

    [Fact]
    public void ChampionshipIsNoted_ByNameOrByRegulation()
    {
        var byName = BulkPullClassifier.Classify(Preview(), Regulation(), isChampionshipByName: true);
        Assert.Contains(byName.Reasons, r => r.Contains("чемпионат"));

        var byRegulation = BulkPullClassifier.Classify(
            Preview(), Regulation(championship: true), isChampionshipByName: false);
        Assert.Contains(byRegulation.Reasons, r => r.Contains("чемпионат"));
    }

    // ── два источника «чемпионата»: имя и регламент ───────────────────────────

    [Theory]
    [InlineData("אליפות ישראל \"ארנה\" קיץ 2025", true)]      // спонсор между словами
    [InlineData("אליפות מכבי נוער ובוגרים", false)]           // клубный, не израильский
    [InlineData("Israel Championship 2026", true)]
    public void ChampionshipByName_UsesTheSameRuleAsTheList(string name, bool expected) =>
        Assert.Equal(expected, CompetitionAdminRepository.IsChampionship(name));

    // ── контракт с панелью ────────────────────────────────────────────────────

    [Fact]
    public void Verdict_IsSerializedAsString()
    {
        // Панель различает вердикты по имени; глобального конвертера enum'ов у API нет,
        // и без атрибута наружу уходил бы числовой «2» — молча ломается при вставке
        // нового значения в середину списка.
        var json = System.Text.Json.JsonSerializer.Serialize(BulkPullVerdict.NoRegulation);

        Assert.Equal("\"NoRegulation\"", json);
    }

    // ── ссылка на регламент на странице loglig ────────────────────────────────

    [Fact]
    public void ParseRegulationDocId_FindsTakanonLink()
    {
        // Снапшот страницы Маккаби-2026: ссылка «תקנון» → ShowLeagueDoc/3185.
        var html = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory, "Fixtures", "Loglig", "loglig-disciplines-14668.html"));

        Assert.Equal(3185, LogligClient.ParseRegulationDocId(html));
    }

    [Fact]
    public void ParseRegulationDocId_NullWhenNoLink()
    {
        // Ссылку ставят не всем — отсутствие это норма, а не сбой.
        Assert.Null(LogligClient.ParseRegulationDocId("<html><body>нет документов</body></html>"));
    }
}
