using Swimm.Application.Dtos;
using Swimm.Application.Mapping;
using Swimm.Parsing.Discovery;
using Swimm.Parsing.Parsers.Loglig;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Даты дисциплин многодневки из сетки loglig (docs/plans/multiday-split-plan.md, М1).
/// Мастерс-зима 2025 (#1645, loglig 11586) и 2026 (#64, loglig 13805) легли у нас одним днём:
/// PDF печатает в шапке только диапазон дат, а дата каждой дисциплины есть лишь в сетке.
/// Фикстуры — живые страницы <c>AthleticsDisciplines</c>.
/// </summary>
public class DisciplineDatesTests
{
    private static DisciplineDates Grid(string name)
    {
        var html = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Parsing", "loglig", name));
        return LogligDisciplineDateSource.FromGrid(LogligStartListParser.ParseDisciplineGrid(html)
            .Select(r => new LogligDisciplineGridRowDto(
                r.DisciplineId, r.EventNumber, r.DisciplineRaw, r.Category, r.StyleName, r.Distance,
                r.Gender, r.AgeBand, r.IsRelay, r.MinTime, r.StartAtLocal, r.Registered, r.Participants)));
    }

    private static DateOnly D(int year, int month, int day) => new(year, month, day);

    [Fact]
    public void MastersWinter2025_TwoDays_KeysInOurVocabulary()
    {
        var dates = Grid("disciplines-11586-masters-winter-2025.html");

        Assert.Equal([D(2025, 1, 3), D(2025, 1, 4)], dates.Days);
        Assert.Empty(dates.Ambiguous);

        // Ключ — в словаре НАШИХ строк: комплекс → individual_medley, пол мастерса из «מאסטרס נ/ג»,
        // «מיקס» → mixed, эстафетная дистанция «4X50».
        Assert.Equal(D(2025, 1, 3), dates.DateOf(new("freestyle", "400", "female")));
        Assert.Equal(D(2025, 1, 3), dates.DateOf(new("individual_medley", "4X50", "male")));
        Assert.Equal(D(2025, 1, 3), dates.DateOf(new("freestyle", "4X50", "mixed")));
        Assert.Equal(D(2025, 1, 4), dates.DateOf(new("individual_medley", "4X50", "mixed")));
        Assert.Equal(D(2025, 1, 4), dates.DateOf(new("Freestyle", "4x50", "FEMALE"))); // регистр не важен

        // Граница дней проходит ВНУТРИ дисциплины: 200 баттерфляй женщины плывут вечером первого
        // дня, мужчины — утром второго.
        Assert.Equal(D(2025, 1, 3), dates.DateOf(new("butterfly", "200", "female")));
        Assert.Equal(D(2025, 1, 4), dates.DateOf(new("butterfly", "200", "male")));
    }

    [Fact]
    public void MastersWinter2026_TwoDays()
    {
        var dates = Grid("disciplines-13805-masters-winter-2026.html");

        Assert.Equal([D(2026, 1, 9), D(2026, 1, 10)], dates.Days);
        Assert.Empty(dates.Ambiguous);
    }

    /// <summary>
    /// Строка без пола (старая эстафета «none») находит день, только если у всех полов этой
    /// дисциплины он один: 400 вольным обоих полов — 03/01, а 4X50 вольным по полам разбросан.
    /// </summary>
    [Fact]
    public void RowWithoutGender_ResolvesOnlyWhenAllGendersShareTheDay()
    {
        var dates = Grid("disciplines-11586-masters-winter-2025.html");

        Assert.Equal(D(2025, 1, 3), dates.DateOf(new("freestyle", "400", "none")));
        Assert.Null(dates.DateOf(new("freestyle", "4X50", "none"))); // микст 03/01, ж/м — 04/01
        Assert.Null(dates.DateOf(new("freestyle", "1500", "male")));  // такой дисциплины нет
    }

    [Fact]
    public void Planner_SplitsRowsByDay_AllOrNothing()
    {
        var dates = Grid("disciplines-11586-masters-winter-2025.html");
        DayPlanRow[] rows =
        [
            new(1, "freestyle", "400", "female"),
            new(2, "butterfly", "200", "female"),
            new(3, "butterfly", "200", "male"),
            new(4, "individual_medley", "4X50", "mixed"),
        ];

        var plan = DisciplineDayPlanner.Plan(rows, dates);
        Assert.True(plan.CanApply);
        Assert.Equal([1L, 2L], plan.ByDay[D(2025, 1, 3)]);
        Assert.Equal([3L, 4L], plan.ByDay[D(2025, 1, 4)]);

        // Одна строка без дня — план неприменим целиком, ключ назван со счётом.
        var broken = DisciplineDayPlanner.Plan(rows.Append(new(5, "freestyle", "4X50", "none")), dates);
        Assert.False(broken.CanApply);
        Assert.Equal(1, broken.Unresolved[new DisciplineDayKey("freestyle", "4X50", "none")]);
    }

    /// <summary>
    /// Дисциплина в оба дня (отборы утром, финал назавтра) — дату не угадываем (решение Р-сп-3),
    /// а однодневный источник делить нечего.
    /// </summary>
    [Fact]
    public void AmbiguousDiscipline_AndOneDaySource_AreNotApplicable()
    {
        var key = new DisciplineDayKey("freestyle", "100", "male");
        var twoDays = new DisciplineDates([(key, D(2025, 1, 3)), (key, D(2025, 1, 4))]);
        Assert.Equal([key], twoDays.Ambiguous);
        Assert.Null(twoDays.DateOf(key));
        Assert.False(DisciplineDayPlanner.Plan([new(1, "freestyle", "100", "male")], twoDays).CanApply);

        var oneDay = new DisciplineDates([(key, D(2025, 1, 3))]);
        Assert.False(DisciplineDayPlanner.Plan([new(1, "freestyle", "100", "male")], oneDay).CanApply);
    }
}
