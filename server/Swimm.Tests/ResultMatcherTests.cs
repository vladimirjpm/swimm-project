using System.Linq;
using Swimm.Domain.Entities;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Матчер результатов upsert-импорта (docs/plans/import-upsert-plan.md, Р2) — чистая функция,
/// без доступа к БД.
/// </summary>
public class ResultMatcherTests
{
    private static ResultRecord Old(int id, int compId = 1, int styleId = 1, string distance = "50",
        string gender = "male", int heat = 1, int lane = 1, int? relayId = null, int swimmerId = 0,
        string? round = null) => new()
    {
        Id = id,
        CompetitionId = compId,
        StyleId = styleId,
        Distance = distance,
        Gender = gender,
        Heat = heat,
        Lane = lane,
        RelayId = relayId,
        SwimmerId = swimmerId == 0 ? id : swimmerId,
        Round = round
    };

    private static ResultRecord New(int compId = 1, int styleId = 1, string distance = "50",
        string gender = "male", int heat = 1, int lane = 1, bool isRelay = false, string? note = null,
        int swimmerId = 0, string? round = null) => new()
    {
        CompetitionId = compId,
        StyleId = styleId,
        Distance = distance,
        Gender = gender,
        Heat = heat,
        Lane = lane,
        Relay = isRelay ? new Relay() : null,
        Note = note,
        SwimmerId = swimmerId,
        Round = round
    };

    private static ResultMatch<ResultRecord, ResultRecord> RunMatch(
        IReadOnlyList<ResultRecord> oldRows, IReadOnlyList<ResultRecord> newRows) =>
        ResultMatcher.Match(oldRows, newRows,
            ResultMatcher.KeyOfPersisted, ResultMatcher.KeyOfTransient,
            ResultMatcher.DiscriminatorOfPersisted, ResultMatcher.DiscriminatorOfTransient);

    [Fact]
    public void SameKey_Matches()
    {
        var old = Old(1);
        var @new = New(note: "updated");

        var result = RunMatch([old], [@new]);

        Assert.Single(result.Matched);
        Assert.Equal(old, result.Matched[0].Old);
        Assert.Equal(@new, result.Matched[0].New);
        Assert.Empty(result.Inserted);
        Assert.Empty(result.Deleted);
    }

    [Fact]
    public void NewRowNotInOld_IsInserted()
    {
        var old = Old(1, lane: 1);
        var @new = New(lane: 2);

        var result = RunMatch([old], [@new]);

        Assert.Empty(result.Matched);
        Assert.Single(result.Inserted);
        Assert.Single(result.Deleted);
        Assert.Equal(old, result.Deleted[0]);
    }

    [Fact]
    public void OldRowNotInNew_IsDeleted()
    {
        var old = Old(1);

        var result = RunMatch([old], []);

        Assert.Empty(result.Matched);
        Assert.Empty(result.Inserted);
        Assert.Single(result.Deleted);
        Assert.Equal(old, result.Deleted[0]);
    }

    [Fact]
    public void DifferentCompetition_NotMatched()
    {
        var old = Old(1, compId: 1);
        var @new = New(compId: 2);

        var result = RunMatch([old], [@new]);

        Assert.Empty(result.Matched);
        Assert.Single(result.Inserted);
        Assert.Single(result.Deleted);
    }

    [Fact]
    public void DifferentHeatOrLane_NotMatched()
    {
        var old1 = Old(1, heat: 1, lane: 1);
        var new1 = New(heat: 2, lane: 1);
        var result1 = RunMatch([old1], [new1]);
        Assert.Empty(result1.Matched);

        var old2 = Old(2, heat: 1, lane: 1);
        var new2 = New(heat: 1, lane: 2);
        var result2 = RunMatch([old2], [new2]);
        Assert.Empty(result2.Matched);
    }

    [Fact]
    public void RelayVsIndividual_SameSwimLane_NotMatched()
    {
        // Одна и та же дорожка/заплыв, но один результат индивидуальный, другой — эстафетный:
        // это разные физические заплывы (IsRelay входит в ключ).
        var old = Old(1, relayId: null);
        var @new = New(isRelay: true);

        var result = RunMatch([old], [@new]);

        Assert.Empty(result.Matched);
        Assert.Single(result.Inserted);
        Assert.Single(result.Deleted);
    }

    [Fact]
    public void RelayVsRelay_SameKey_Matches()
    {
        var old = Old(1, relayId: 42);
        var @new = New(isRelay: true);

        var result = RunMatch([old], [@new]);

        Assert.Single(result.Matched);
    }

    [Fact]
    public void KeyCollision_TwoOldTwoNew_MatchesInEncounterOrder()
    {
        // Два старых результата с одинаковым ключом (в реальности не встречается, но формат
        // не запрещает) — матчатся к новым в порядке следования (FIFO), не по значению полей.
        var old1 = Old(1, lane: 5);
        var old2 = Old(2, lane: 5);
        var new1 = New(lane: 5, note: "first-in-file");
        var new2 = New(lane: 5, note: "second-in-file");

        var result = RunMatch([old1, old2], [new1, new2]);

        Assert.Equal(2, result.Matched.Count);
        Assert.Equal(old1, result.Matched[0].Old);
        Assert.Equal(new1, result.Matched[0].New);
        Assert.Equal(old2, result.Matched[1].Old);
        Assert.Equal(new2, result.Matched[1].New);
        Assert.Empty(result.Inserted);
        Assert.Empty(result.Deleted);
    }

    [Fact]
    public void KeyCollision_MoreOldThanNew_ExcessOldBecomesDeleted()
    {
        var old1 = Old(1, lane: 5);
        var old2 = Old(2, lane: 5);
        var old3 = Old(3, lane: 5);
        var new1 = New(lane: 5);

        var result = RunMatch([old1, old2, old3], [new1]);

        Assert.Single(result.Matched);
        Assert.Equal(old1, result.Matched[0].Old);
        Assert.Empty(result.Inserted);
        Assert.Equal(2, result.Deleted.Count);
        Assert.Contains(old2, result.Deleted);
        Assert.Contains(old3, result.Deleted);
    }

    [Fact]
    public void KeyCollision_MoreNewThanOld_ExcessNewBecomesInserted()
    {
        var old1 = Old(1, lane: 5);
        var new1 = New(lane: 5);
        var new2 = New(lane: 5);
        var new3 = New(lane: 5);

        var result = RunMatch([old1], [new1, new2, new3]);

        Assert.Single(result.Matched);
        Assert.Equal(new1, result.Matched[0].New);
        Assert.Equal(2, result.Inserted.Count);
        Assert.Contains(new2, result.Inserted);
        Assert.Contains(new3, result.Inserted);
        Assert.Empty(result.Deleted);
    }

    [Fact]
    public void KeyCollision_TwoTeamsOfSameClub_MatchedByRoster_NotByOrder()
    {
        // Реальный случай (comp #1513, «הפועל עמק חפר»): две команды клуба в одной дисциплине,
        // один и тот же пловец в обеих, heat/lane совпали. SwimmerId их не разводит — обе
        // строки его. Различает состав, и матчер обязан идти по нему, а не по порядку в файле:
        // иначе перестановка блоков в протоколе перекидывает время одной команды на другую.
        var rosterA = "אלון בן, עומר ג׳יוסי, אור כהן, עומר דמתי";
        var rosterB = "אלון בן, עומר ג׳יוסי, מריה גברילוב, אליס במירושניקו";

        var oldA = Old(1, lane: 9, heat: 3, relayId: 3037, swimmerId: 8238);
        oldA.Relay = new Relay { Id = 3037, TeamName = "הפועל עמק חפר", SwimmersName = rosterA };
        var oldB = Old(2, lane: 9, heat: 3, relayId: 3089, swimmerId: 8238);
        oldB.Relay = new Relay { Id = 3089, TeamName = "הפועל עמק חפר", SwimmersName = rosterB };

        // В новом файле блоки идут в обратном порядке — FIFO дал бы перекрёстный матч.
        var newB = New(lane: 9, heat: 3, isRelay: true, swimmerId: 8238, note: "B");
        newB.Relay!.TeamName = "הפועל עמק חפר";
        newB.Relay.SwimmersName = rosterB;
        var newA = New(lane: 9, heat: 3, isRelay: true, swimmerId: 8238, note: "A");
        newA.Relay!.TeamName = "הפועל עמק חפר";
        newA.Relay.SwimmersName = rosterA;

        var result = RunMatch([oldA, oldB], [newB, newA]);

        Assert.Equal(2, result.Matched.Count);
        Assert.Equal("A", result.Matched.Single(m => m.Old.Id == 1).New.Note);
        Assert.Equal("B", result.Matched.Single(m => m.Old.Id == 2).New.Note);
        Assert.Empty(result.Inserted);
        Assert.Empty(result.Deleted);
    }

    [Fact]
    public void KeyCollision_RosterUnknown_FallsBackToEncounterOrder()
    {
        // Состав не разобран (пусто с обеих сторон) — поведение прежнее, FIFO: фикс не должен
        // ухудшать случай, где различать нечем.
        var old1 = Old(1, lane: 5, relayId: 10, swimmerId: 77);
        old1.Relay = new Relay { Id = 10 };
        var old2 = Old(2, lane: 5, relayId: 11, swimmerId: 77);
        old2.Relay = new Relay { Id = 11 };
        var new1 = New(lane: 5, isRelay: true, swimmerId: 77, note: "first");
        var new2 = New(lane: 5, isRelay: true, swimmerId: 77, note: "second");

        var result = RunMatch([old1, old2], [new1, new2]);

        Assert.Equal(2, result.Matched.Count);
        Assert.Equal("first", result.Matched.Single(m => m.Old.Id == 1).New.Note);
        Assert.Equal("second", result.Matched.Single(m => m.Old.Id == 2).New.Note);
    }

    [Fact]
    public void MultipleCompetitionsInOneFile_MatchedPerCompetitionIndependently()
    {
        // Многодневный файл: один и тот же lane/heat/style в разных Competition.Id — не должны
        // перепутаться между собой (footgun из плана: матчер работает per-Competition).
        var oldDay1 = Old(1, compId: 10, lane: 1);
        var oldDay2 = Old(2, compId: 20, lane: 1);
        var newDay1 = New(compId: 10, lane: 1, note: "day1");
        var newDay2 = New(compId: 20, lane: 1, note: "day2");

        var result = RunMatch([oldDay1, oldDay2], [newDay1, newDay2]);

        Assert.Equal(2, result.Matched.Count);
        var day1Match = result.Matched.Single(m => m.Old.Id == 1);
        Assert.Equal("day1", day1Match.New.Note);
        var day2Match = result.Matched.Single(m => m.Old.Id == 2);
        Assert.Equal("day2", day2Match.New.Note);
    }

    [Fact]
    public void KeyCollision_SwimmerIdReordered_MatchesBySwimmerIdNotPosition()
    {
        // Инцидент 2026-07-20 (Маккабиада): фикс парсера сменил порядок восстановленных
        // relay-ног в файле. Чистый FIFO сматчил бы old1↔new (первый по позиции) неверно —
        // с SwimmerId-приоритетом каждая нога находит СВОЮ старую строку независимо от того,
        // в каком порядке она появилась в новом файле.
        var old1 = Old(1, lane: 5, swimmerId: 100, relayId: null);
        var old2 = Old(2, lane: 5, swimmerId: 200, relayId: null);
        var old3 = Old(3, lane: 5, swimmerId: 300, relayId: null);
        // Новый файл: та же тройка ног, но в другом порядке следования.
        var new3 = New(lane: 5, swimmerId: 300, note: "leg-c");
        var new1 = New(lane: 5, swimmerId: 100, note: "leg-a");
        var new2 = New(lane: 5, swimmerId: 200, note: "leg-b");

        var result = RunMatch([old1, old2, old3], [new3, new1, new2]);

        Assert.Equal(3, result.Matched.Count);
        Assert.Contains(result.Matched, m => m.Old.Id == 1 && m.New.Note == "leg-a");
        Assert.Contains(result.Matched, m => m.Old.Id == 2 && m.New.Note == "leg-b");
        Assert.Contains(result.Matched, m => m.Old.Id == 3 && m.New.Note == "leg-c");
        Assert.Empty(result.Inserted);
        Assert.Empty(result.Deleted);
    }

    [Fact]
    public void KeyCollision_MembershipShrunkAndReordered_UpdatesSurvivorsAndDeletesMissing()
    {
        // Инцидент: между переимпортами состав коллизионной группы меняется (нога пропала),
        // а не только переставляется. SwimmerId-приоритет обязан обновить переживших ног на
        // месте (Matched, Id сохранён) невзирая на смену позиции, а пропавшую — удалить, а не
        // "переиспользовать" её строку под случайного соседа по FIFO.
        var old1 = Old(1, lane: 5, swimmerId: 100);
        var old2 = Old(2, lane: 5, swimmerId: 200);
        var old3 = Old(3, lane: 5, swimmerId: 300); // эта нога пропадёт из нового файла
        // Новый файл: leg 100 и 200 остались, но в другом порядке следования; leg 300 исчезла
        // (парсер перестал восстанавливать эту ногу) — новых строк меньше, чем старых.
        var new200 = New(lane: 5, swimmerId: 200, note: "still-here");
        var new100 = New(lane: 5, swimmerId: 100, note: "still-here-too");

        var result = RunMatch([old1, old2, old3], [new200, new100]);

        Assert.Equal(2, result.Matched.Count);
        Assert.Contains(result.Matched, m => m.Old.Id == 1 && m.New.Note == "still-here-too");
        Assert.Contains(result.Matched, m => m.Old.Id == 2 && m.New.Note == "still-here");
        Assert.Empty(result.Inserted);
        Assert.Single(result.Deleted);
        Assert.Equal(3, result.Deleted[0].Id);
    }

    [Fact]
    public void KeyCollision_MembershipGrownAndReordered_UpdatesSurvivorsAndInsertsNew()
    {
        // Симметричный случай: новая нога появилась (парсер научился восстанавливать ещё одну),
        // а существующие переставились местами в файле — SwimmerId всё равно находит их старые
        // строки, лишняя новая строка становится insert, а не путается с существующими по FIFO.
        var old1 = Old(1, lane: 5, swimmerId: 100);
        var old2 = Old(2, lane: 5, swimmerId: 200);
        var new200 = New(lane: 5, swimmerId: 200, note: "still-here");
        var new100 = New(lane: 5, swimmerId: 100, note: "still-here-too");
        var new300 = New(lane: 5, swimmerId: 300, note: "brand-new-leg");

        var result = RunMatch([old1, old2], [new200, new100, new300]);

        Assert.Equal(2, result.Matched.Count);
        Assert.Contains(result.Matched, m => m.Old.Id == 1 && m.New.Note == "still-here-too");
        Assert.Contains(result.Matched, m => m.Old.Id == 2 && m.New.Note == "still-here");
        Assert.Single(result.Inserted);
        Assert.Equal("brand-new-leg", result.Inserted[0].Note);
        Assert.Empty(result.Deleted);
    }

    [Fact]
    public void KeyCollision_AnonymousToNamedWholeGroup_StillMatchesByPosition()
    {
        // Обычный анонимный→именованный переход (Р5, "правильно и желаемо"): весь состав
        // группы был анонимным (у каждой ноги — одноразовый SwimmerId с прошлого импорта) и
        // становится именованным разом. SwimmerId никогда не совпадёт (новые ID выданы заново
        // для анонимных строк на каждом импорте) — доматч должен провалиться в FIFO по позиции,
        // как и раньше, а не оставить строки unmatched.
        var old1 = Old(1, lane: 5, swimmerId: 9001); // анонимная заглушка, импорт №1
        var old2 = Old(2, lane: 5, swimmerId: 9002); // анонимная заглушка, импорт №1
        var new1 = New(lane: 5, swimmerId: 501, note: "named-first"); // именован при переимпорте
        var new2 = New(lane: 5, swimmerId: 502, note: "named-second");

        var result = RunMatch([old1, old2], [new1, new2]);

        Assert.Equal(2, result.Matched.Count);
        Assert.Contains(result.Matched, m => m.Old.Id == 1 && m.New.Note == "named-first");
        Assert.Contains(result.Matched, m => m.Old.Id == 2 && m.New.Note == "named-second");
        Assert.Empty(result.Inserted);
        Assert.Empty(result.Deleted);
    }

    [Fact]
    public void NoCollision_SingleRowPerKey_SwimmerIdChangeStillMatches()
    {
        // Без коллизии (обычный одиночный результат на дорожку) SwimmerId-логика вообще не
        // участвует — единственная старая строка матчится единственной новой независимо от
        // SwimmerId (Р5: анонимная заглушка → именованный пловец сохраняет Id результата).
        var old = Old(1, lane: 3, swimmerId: 42);
        var @new = New(lane: 3, swimmerId: 999, note: "renamed");

        var result = RunMatch([old], [@new]);

        Assert.Single(result.Matched);
        Assert.Equal(1, result.Matched[0].Old.Id);
        Assert.Equal("renamed", result.Matched[0].New.Note);
    }

    [Fact]
    public void EmptyOldAndNew_NoOp()
    {
        var result = RunMatch([], []);
        Assert.Empty(result.Matched);
        Assert.Empty(result.Inserted);
        Assert.Empty(result.Deleted);
    }
    /// <summary>
    /// Раунд входит в ключ: утренний зачёт возрастных групп и вечерний финал — РАЗНЫЕ строки,
    /// даже когда совпадает всё остальное. Без этого вторая сессия затирала бы первую при
    /// переимпорте, а официально обе дают медали и клубные очки (И13, data-integrity §10).
    /// </summary>
    [Fact]
    public void DifferentRounds_AreDifferentRows()
    {
        var oldRows = new[]
        {
            Old(1, heat: 4, lane: 4, swimmerId: 7, round: "timed-final"),
            Old(2, heat: 1, lane: 4, swimmerId: 7, round: "final")
        };
        var newRows = new[]
        {
            New(heat: 4, lane: 4, swimmerId: 7, round: "timed-final"),
            New(heat: 1, lane: 4, swimmerId: 7, round: "final")
        };

        var match = RunMatch(oldRows, newRows);

        Assert.Equal(2, match.Matched.Count);
        Assert.Empty(match.Inserted);
        Assert.Empty(match.Deleted);
        Assert.All(match.Matched, pair => Assert.Equal(pair.Old.Round, pair.New.Round));
    }

    /// <summary>
    /// Появление раунда у строки — это НОВАЯ строка, а старая уходит в Deleted: ключ сменился.
    /// Отсюда правило «первый переимпорт соревнования с раундами идёт с --delete-missing»,
    /// иначе рядом останутся старые безраундовые строки (тот же механизм, что в инциденте И-4).
    /// </summary>
    [Fact]
    public void RoundAppearing_ChangesKey_SoOldRowIsDeleted()
    {
        var oldRows = new[] { Old(1, heat: 4, lane: 4, swimmerId: 7) };          // без раунда
        var newRows = new[] { New(heat: 4, lane: 4, swimmerId: 7, round: "timed-final") };

        var match = RunMatch(oldRows, newRows);

        Assert.Empty(match.Matched);
        Assert.Single(match.Inserted);
        Assert.Single(match.Deleted);
    }

    /// <summary>Старые данные: раунда нет ни у кого — ключи прежние, переимпорт не «поедет».</summary>
    [Fact]
    public void NoRounds_KeysUnchanged()
    {
        var match = RunMatch([Old(1, heat: 2, lane: 5, swimmerId: 7)],
                             [New(heat: 2, lane: 5, swimmerId: 7)]);

        Assert.Single(match.Matched);
        Assert.Empty(match.Inserted);
        Assert.Empty(match.Deleted);
    }

}
