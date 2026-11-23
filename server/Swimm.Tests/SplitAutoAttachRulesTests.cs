using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Application.Mapping;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>Исход автодоклейки, подписи шага 9 и чтение аудита (решение Влада 23.11.2026).</summary>
public class SplitAutoAttachRulesTests
{
    private static SplitAttachResult Result(bool applied, int teams, int swims, int legWrites, int swimWrites) =>
        new(applied, 15896, [], new SplitAttachReport(teams, 0, 0, 0, 0, 0, 0, swims, 0, 0, 0)
            { LegWrites = legWrites, SwimWrites = swimWrites }, "msg");

    [Fact]
    public void Classify_ByWrites_AndSource()
    {
        Assert.Equal(SplitAutoKind.Attached, SplitAutoAttachRules.Classify(Result(true, 4, 10, 8, 0)));
        Assert.Equal(SplitAutoKind.Attached, SplitAutoAttachRules.Classify(Result(true, 0, 10, 0, 3)));
        Assert.Equal(SplitAutoKind.NothingOnLoglig, SplitAutoAttachRules.Classify(Result(true, 0, 0, 0, 0)));
        Assert.Equal(SplitAutoKind.NothingNew, SplitAutoAttachRules.Classify(Result(true, 4, 10, 0, 0)));
        Assert.Equal(SplitAutoKind.Failed, SplitAutoAttachRules.Classify(SplitAttachResult.Failed("LogligId не найден")));
    }

    // Живой случай 23.11.2026 (compID 16917): личные промежуточные — 504 у loglig, эстафет нет.
    // Пустой источник тут — «не знаем», а не «промежуточных нет».
    [Fact]
    public void Classify_IncompleteSource_NothingWritten_IsFailed()
    {
        Assert.Equal(SplitAutoKind.Failed,
            SplitAutoAttachRules.Classify(Result(true, 0, 0, 0, 0) with { SourceIncomplete = true }));
        Assert.Equal(SplitAutoKind.Partial,
            SplitAutoAttachRules.Classify(Result(true, 4, 0, 8, 0) with { SourceIncomplete = true }));
    }

    // «Частично» — у всех дней прогона, с «!» и красной подписью; важнее «доклеено», слабее сбоя.
    [Fact]
    public void Partial_EveryDay_NeedsRetry_AndRanksBelowFailure()
    {
        Assert.Equal(SplitAutoKind.Partial, SplitAutoAttachRules.DayKind(SplitAutoKind.Partial, 0));
        Assert.Equal(SplitAutoKind.Partial, SplitAutoAttachRules.DayKind(SplitAutoKind.Partial, 5));
        Assert.True(SplitAutoAttachRules.NeedsRetry(SplitAutoKind.Partial));
        Assert.False(SplitAutoAttachRules.NeedsRetry(SplitAutoKind.NothingOnLoglig));
        Assert.Equal(SplitAutoKind.Partial, SplitAutoAttachRules.Combine([SplitAutoKind.Attached, SplitAutoKind.Partial]));
        Assert.Equal(SplitAutoKind.Failed, SplitAutoAttachRules.Combine([SplitAutoKind.Partial, SplitAutoKind.Failed]));
        Assert.Equal("частично — доклеено ног эстафет 8, часть loglig не прочиталась, повторите кнопкой",
            SplitAutoAttachRules.Text(SplitAutoKind.Partial, 0, 8));
        var note = new SplitAttachNote(DateTime.UtcNow, true, SplitAutoKind.Partial, 0, 8, "отчёт");
        Assert.True(SplitAutoAttachRules.Caption(note, true, false)!.Value.Danger);
    }

    [Fact]
    public void DayKind_OtherDayAttached_MeansNothingNewHere()
    {
        Assert.Equal(SplitAutoKind.Attached, SplitAutoAttachRules.DayKind(SplitAutoKind.Attached, 5));
        Assert.Equal(SplitAutoKind.NothingNew, SplitAutoAttachRules.DayKind(SplitAutoKind.Attached, 0));
        Assert.Equal(SplitAutoKind.Failed, SplitAutoAttachRules.DayKind(SplitAutoKind.Failed, 0));
    }

    [Fact]
    public void Combine_FailureWins_EmptyIsFailure()
    {
        Assert.Equal(SplitAutoKind.Failed, SplitAutoAttachRules.Combine([SplitAutoKind.Attached, SplitAutoKind.Failed]));
        Assert.Equal(SplitAutoKind.Attached, SplitAutoAttachRules.Combine([SplitAutoKind.NothingNew, SplitAutoKind.Attached]));
        Assert.Equal(SplitAutoKind.NothingOnLoglig, SplitAutoAttachRules.Combine([SplitAutoKind.NothingOnLoglig]));
        Assert.Equal(SplitAutoKind.Failed, SplitAutoAttachRules.Combine([]));
    }

    [Fact]
    public void Codes_RoundTrip()
    {
        foreach (var k in Enum.GetValues<SplitAutoKind>())
            Assert.Equal(k, SplitAutoAttachRules.ParseCode(SplitAutoAttachRules.Code(k)));
    }

    [Fact]
    public void Caption_PendingFirst_ThenNote_ThenFlag()
    {
        var at = new DateTime(2026, 11, 23, 12, 5, 0, DateTimeKind.Utc);
        var note = new SplitAttachNote(at, true, SplitAutoKind.Attached, 340, 48, "отчёт");

        Assert.StartsWith("промежуточные доклеиваются сами", SplitAutoAttachRules.Caption(note, true, pending: true)!.Value.Text);
        var c = SplitAutoAttachRules.Caption(note, true, false)!.Value;
        Assert.Equal("промежуточные: доклеено: личных 340, ног эстафет 48 · само после импорта 23.11 14:05", c.Text);
        Assert.Equal("отчёт", c.Title);
        Assert.False(c.Danger);

        var failed = SplitAutoAttachRules.Caption(note with { Kind = SplitAutoKind.Failed, Auto = false }, false, false)!.Value;
        Assert.Contains("не удалось — повторите кнопкой · вручную", failed.Text);
        Assert.True(failed.Danger);

        Assert.Equal("промежуточные есть", SplitAutoAttachRules.Caption(null, true, false)!.Value.Text);
        Assert.Null(SplitAutoAttachRules.Caption(null, false, false));
    }

    [Fact]
    public void NoneOnLoglig_Text()
    {
        Assert.Equal("на loglig промежуточных нет", SplitAutoAttachRules.Text(SplitAutoKind.NothingOnLoglig, 0, 0));
        Assert.Equal("доклеено: личных 3", SplitAutoAttachRules.Text(SplitAutoKind.Attached, 3, 0));
    }

    [Fact]
    public void ImportSucceeded_OnlyWhenRowsWritten()
    {
        Assert.True(ImportPostStepRules.Succeeded(new ImportResult { Created = 1 }));
        Assert.True(ImportPostStepRules.Succeeded(new ImportResult { Updated = 1 }));
        Assert.False(ImportPostStepRules.Succeeded(new ImportResult { Message = "Дубль: …" }));
    }

    // Две формы DetailsJson: автозапуск (Kind + счётчики дня) и кнопка (сериализованный SplitAttachReport).
    [Fact]
    public void Journal_Parse_AutoAndManualRows()
    {
        var at = DateTime.UtcNow;
        var auto = SplitAttachJournal.Parse(at, SplitAutoAttachRules.ActorName, "сводка",
            """{"Trigger":"import","Kind":"none-on-loglig","SwimWrites":0,"LegWrites":0,"Message":"отчёт"}""");
        Assert.True(auto.Auto);
        Assert.Equal(SplitAutoKind.NothingOnLoglig, auto.Kind);
        Assert.Equal("отчёт", auto.Summary);

        var manual = SplitAttachJournal.Parse(at, "admin@x", "Доклеены промежуточные",
            """{"Teams":4,"LegWrites":8,"SwimWrites":2}""");
        Assert.False(manual.Auto);
        Assert.Equal(SplitAutoKind.Attached, manual.Kind);
        Assert.Equal((2, 8), (manual.SwimWrites, manual.LegWrites));
        Assert.Equal("Доклеены промежуточные", manual.Summary);

        var broken = SplitAttachJournal.Parse(at, "cli", "s", "{oops");
        Assert.Equal(SplitAutoKind.NothingNew, broken.Kind);
    }

    [Fact]
    public void Checklist_SplitsNeedRetry_Step9Attention()
    {
        var input = new ChecklistInput(CompetitionRowState.Closed, Imported: true, QualityScanned: true, SplitsNeedRetry: true);
        var s = CompetitionChecklistRules.Build(input);
        Assert.Equal(ChecklistStepStatus.Attention, s[8]);
        Assert.Equal(ChecklistStepStatus.Current,
            CompetitionChecklistRules.Build(input with { SplitsNeedRetry = false })[8]);
    }
}
