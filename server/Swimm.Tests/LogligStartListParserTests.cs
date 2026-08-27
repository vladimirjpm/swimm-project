using System;
using System.IO;
using System.Linq;
using Swimm.Parsing.Parsers.Loglig;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Разбор СТАРТОВОГО протокола loglig (docs/plans/start-list-plan.md, шаг С1) — плана
/// соревнования, а не его результатов. Фикстуры — живые страницы регионального чемпионата
/// «אליפות ישראל ארנה לגילאי 8-11 חורף 2026, מחוז דרום» (isr compID 16786, loglig 14208,
/// 19.02.2026): сетка из 60 заплывов, личный заплыв 76321 (девочки 10) и эстафета 76355.
///
/// Замеры этой фикстуры — те же, что записаны в плане: записалось 1056, участвует 989,
/// то есть 67 неявок. Именно эта разница и есть причина, по которой заявки не кладутся
/// в <c>Results</c>.
/// </summary>
public class LogligStartListParserTests
{
    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Parsing", "loglig", name));

    // ── Стартовый протокол личного заплыва ───────────────────────────────────

    [Fact]
    public void StartList_ParsesHeaderAndAllLanes()
    {
        var sl = LogligStartListParser.ParseStartList(Fixture("startlist-76321-girls10.html"));

        Assert.Equal("19/02/2026", sl.Date);
        Assert.Contains("אליפות ישראל", sl.CompetitionName);
        Assert.Equal("100 מעורב אישי", sl.DisciplineRaw);
        Assert.Equal("individual_medley", sl.StyleName);
        Assert.Equal("100", sl.Distance);
        Assert.False(sl.IsRelay);

        // 7 строк в стартовом протоколе против 9 записавшихся: двое снялись до посева.
        Assert.Equal(7, sl.Rows.Count);
        Assert.All(sl.Rows, r => Assert.Equal(LogligRounds.TimedFinal, r.Round));
        Assert.All(sl.Rows, r => Assert.Equal(2016, r.BirthYear));
    }

    [Fact]
    public void StartList_KeepsHeatLaneAndHeatStartTime()
    {
        var sl = LogligStartListParser.ParseStartList(Fixture("startlist-76321-girls10.html"));

        // Время старта берётся из подзаголовка секции и приписывается всем строкам заплыва.
        var heat1 = sl.Rows.Where(r => r.Heat == 1).ToList();
        var heat2 = sl.Rows.Where(r => r.Heat == 2).ToList();
        Assert.Equal(2, heat1.Count);
        Assert.Equal(5, heat2.Count);
        Assert.All(heat1, r => Assert.Equal(new TimeOnly(10, 6), r.HeatStartAt));
        Assert.All(heat2, r => Assert.Equal(new TimeOnly(10, 9), r.HeatStartAt));

        // Дорожки внутри заплыва уникальны — на это опирается ключ идентичности заявки.
        Assert.Equal(heat2.Count, heat2.Select(r => r.Lane).Distinct().Count());
    }

    [Fact]
    public void StartList_TakesLogligIdFromPlayerLink()
    {
        var sl = LogligStartListParser.ParseStartList(Fixture("startlist-76321-girls10.html"));

        // Имена тут только на иврите, поэтому матчинг пловца держится на этом id.
        Assert.All(sl.Rows, r => Assert.NotNull(r.LogligId));
        Assert.Equal(sl.Rows.Count, sl.Rows.Select(r => r.LogligId).Distinct().Count());

        var fastest = sl.Rows.Single(r => r.Heat == 2 && r.Lane == 5);
        Assert.Equal("אביגייל יבסייב", fastest.FullName);
        Assert.Equal("הפועל באר שבע", fastest.Club);
    }

    [Fact]
    public void StartList_SeedTime_NtBecomesNull()
    {
        var sl = LogligStartListParser.ParseStartList(Fixture("startlist-76321-girls10.html"));

        // «NT» — пловец эту дистанцию ещё не плыл; это не время и превращать его в 0 нельзя.
        Assert.Null(sl.Rows.Single(r => r.Heat == 2 && r.Lane == 2).SeedTime);
        Assert.Equal("01:42.72", sl.Rows.Single(r => r.Heat == 2 && r.Lane == 5).SeedTime);
        Assert.Equal("02:13.47", sl.Rows.Single(r => r.Heat == 2 && r.Lane == 6).SeedTime);
        Assert.Equal(4, sl.Rows.Count(r => r.SeedTime is null));
    }

    [Fact]
    public void StartList_MidnightHeatTime_MeansNotScheduled()
    {
        // Источник печатает «время заплыву не назначено» как 00:00 — та же условность, что
        // 00:00.00 в графе норматива. Встречено вживую: соревнование 14208, заплыв 76324,
        // «שעת הזנקה:00:00» при времени события 10:00. Принять полночь за настоящее время
        // значит показать родителю «ребёнок плывёт в полночь».
        const string html = """
            <h4 class="disciplines-title">Start list - 100 מעורב אישי - Чемпионат - 19/02/2026</h4>
            <table><tbody>
            <tr class="heat_tr"><td><h4><b>גמר ישיר</b></h4></td>
                <td colspan="5"><h4><b>מקצה: 1 שעת הזנקה:00:00</b></h4></td></tr>
            <tr class="reorder"><td>1</td><td>4</td>
                <td><a href="/Players/Details/1">שם</a></td><td>2016</td><td>מועדון</td><td>NT</td></tr>
            </tbody></table>
            """;

        var sl = LogligStartListParser.ParseStartList(html);

        Assert.Null(Assert.Single(sl.Rows).HeatStartAt);
    }

    // ── Эстафета ─────────────────────────────────────────────────────────────

    [Fact]
    public void StartList_Relay_FourLegsShareHeatAndLane()
    {
        var sl = LogligStartListParser.ParseStartList(Fixture("startlist-76355-relay-g8-9.html"));

        Assert.True(sl.IsRelay);
        Assert.Equal("4X50", sl.Distance);
        Assert.Equal("freestyle", sl.StyleName);

        // Команда эстафеты — это ЧЕТЫРЕ строки с одинаковыми заплывом и дорожкой; названия
        // команды источник не печатает, поэтому только (heat, lane) её и склеивает.
        Assert.Equal(4, sl.Rows.Count);
        Assert.Single(sl.Rows.Select(r => (r.Heat, r.Lane)).Distinct());
        Assert.Equal(new TimeOnly(12, 31), sl.Rows[0].HeatStartAt);

        // Зато у КАЖДОЙ ноги есть свой loglig-id — в результатах фамилии ног обрезаны.
        Assert.Equal(4, sl.Rows.Select(r => r.LogligId).Distinct().Count());
        Assert.All(sl.Rows, r => Assert.NotNull(r.LogligId));
    }

    // ── Сетка заплывов дня ───────────────────────────────────────────────────

    [Fact]
    public void Grid_ParsesEveryEventOnce()
    {
        var grid = LogligStartListParser.ParseDisciplineGrid(Fixture("disciplines-14208.html"));

        // На строку приходится четыре кнопки с ОДНИМ и тем же id — дубля быть не должно.
        Assert.Equal(60, grid.Count);
        Assert.Equal(60, grid.Select(r => r.DisciplineId).Distinct().Count());

        // Кнопка «весь стартовый протокол» из шапки таблицы несёт competitionId=14208
        // и не должна пройти за заплыв.
        Assert.DoesNotContain(grid, r => r.DisciplineId == 14208);
    }

    [Fact]
    public void Grid_CountsRegisteredAndParticipants()
    {
        var grid = LogligStartListParser.ParseDisciplineGrid(Fixture("disciplines-14208.html"));

        // Замер плана: 1056 записалось, 989 участвует → 67 неявок (6.3 %).
        Assert.Equal(1056, grid.Sum(r => r.Registered));
        Assert.Equal(989, grid.Sum(r => r.Participants));
    }

    [Fact]
    public void Grid_ResolvesDisciplineCategoryAndStartTime()
    {
        var grid = LogligStartListParser.ParseDisciplineGrid(Fixture("disciplines-14208.html"));

        var girls10 = grid.Single(r => r.DisciplineId == 76321);
        Assert.Equal(5, girls10.EventNumber);
        Assert.Equal("individual_medley", girls10.StyleName);
        Assert.Equal("100", girls10.Distance);
        Assert.Equal("female", girls10.Gender);
        Assert.Equal("10", girls10.AgeBand);
        Assert.False(girls10.IsRelay);
        Assert.Equal(9, girls10.Registered);
        Assert.Equal(7, girls10.Participants);

        // Время старта — местное, без часового пояса: перевод в UTC делает вызывающий.
        Assert.Equal(new DateTime(2026, 2, 19, 10, 6, 0), girls10.StartAtLocal);
        Assert.Equal(DateTimeKind.Unspecified, girls10.StartAtLocal!.Value.Kind);
    }

    [Fact]
    public void Grid_RelayRow_KeepsBandAndRelayDistance()
    {
        var grid = LogligStartListParser.ParseDisciplineGrid(Fixture("disciplines-14208.html"));

        var relay = grid.Single(r => r.DisciplineId == 76355);
        Assert.True(relay.IsRelay);
        Assert.Equal("4X50", relay.Distance);
        Assert.Equal("freestyle", relay.StyleName);
        Assert.Equal("female", relay.Gender);
        Assert.Equal("8-9", relay.AgeBand);      // полоса пишется через тире БЕЗ пробелов
        Assert.Equal(new DateTime(2026, 2, 19, 12, 31, 0), relay.StartAtLocal);
    }

    [Fact]
    public void Grid_ZeroMinTimeIsNotATime()
    {
        var grid = LogligStartListParser.ParseDisciplineGrid(Fixture("disciplines-14208.html"));

        // «00:00.00» в графе норматива значит «норматива нет», а не «ноль секунд».
        Assert.All(grid, r => Assert.NotEqual("00:00.00", r.MinTime));
        Assert.Null(grid.Single(r => r.DisciplineId == 76324).MinTime);
    }

    [Fact]
    public void Grid_MultiDay_TakesDayFromStartTime()
    {
        // У многодневки день заплыва зашит в саму дату старта — отдельного разбора вкладок
        // дней не нужно. Синтетическая сетка: два заплыва в разные дни одного события.
        const string html = """
            <table><tbody>
            <tr><td>1</td><td>50 חופשי</td><td>בנות 12</td><td></td>
                <td>23/12/2025 16:30:00</td><td>10</td><td>10</td>
                <td><a href="/LeagueTable/StartList/1001?isModal=True">Start list</a></td></tr>
            <tr><td>2</td><td>50 חופשי</td><td>בנים 12</td><td></td>
                <td>24/12/2025 09:00:00</td><td>12</td><td>11</td>
                <td><a href="/LeagueTable/StartList/1002?isModal=True">Start list</a></td></tr>
            </tbody></table>
            """;

        var grid = LogligStartListParser.ParseDisciplineGrid(html);

        Assert.Equal(2, grid.Count);
        Assert.Equal(new DateTime(2025, 12, 23), grid[0].StartAtLocal!.Value.Date);
        Assert.Equal(new DateTime(2025, 12, 24), grid[1].StartAtLocal!.Value.Date);
        Assert.Equal("male", grid[1].Gender);
    }

    [Fact]
    public void Grid_NestedTooltipTable_DoesNotSwallowTheRow()
    {
        // Регрессия: в ячейке норматива сидит всплывающая подсказка — ЦЕЛАЯ таблица со
        // своими строками. Нежадный «<tr>…</tr>» обрывал строку заплыва на её закрывающем
        // теге, и ссылки на стартовый протокол терялись: на чемпионате loglig 13627 так
        // пропадало 8 заплывов из 177, вместе с ПЕРВЫМ днём — у него норматив есть у всех.
        // Её же <td> не должны сдвигать позиционный разбор колонок.
        const string html = """
            <table><tbody>
            <tr>
              <td>7</td><td>200 חזה</td><td>נשים 19-99</td>
              <td>
                <div class="dropdown-menu">
                  <table><tbody>
                    <tr><td>נשים 19-99</td><td>18:25.00</td><td></td></tr>
                  </tbody></table>
                </div>
              </td>
              <td>23/12/2025 16:30:00</td><td>27</td><td>27</td>
              <td><a href="/LeagueTable/StartList/72968?isModal=True">Start list</a></td>
            </tr>
            </tbody></table>
            """;

        var grid = LogligStartListParser.ParseDisciplineGrid(html);

        var row = Assert.Single(grid);
        Assert.Equal(72968, row.DisciplineId);
        Assert.Equal(7, row.EventNumber);
        Assert.Equal("breaststroke", row.StyleName);
        Assert.Equal("200", row.Distance);
        Assert.Equal("female", row.Gender);
        Assert.Equal("19-99", row.AgeBand);
        Assert.Equal(new DateTime(2025, 12, 23, 16, 30, 0), row.StartAtLocal);
        Assert.Equal(27, row.Registered);
        Assert.Equal(27, row.Participants);
    }

    [Fact]
    public void Grid_EventWithoutScheduledTime_IsNotAFailure()
    {
        // У заплыва, которому ещё не назначили время, графа пустая — для предстоящего
        // старта это норма, и строка обязана дожить до вызывающего.
        const string html = """
            <table><tbody>
            <tr><td>1</td><td>100 גב</td><td>בנות 11</td><td></td><td></td><td>0</td><td>0</td>
                <td><a href="/LeagueTable/RegisteredCompetitionAthletes/2001">נרשמים</a></td></tr>
            </tbody></table>
            """;

        var grid = LogligStartListParser.ParseDisciplineGrid(html);

        var row = Assert.Single(grid);
        Assert.Equal(2001, row.DisciplineId);
        Assert.Null(row.StartAtLocal);
        Assert.Equal("backstroke", row.StyleName);
        Assert.Equal(0, row.Registered);
    }
}
