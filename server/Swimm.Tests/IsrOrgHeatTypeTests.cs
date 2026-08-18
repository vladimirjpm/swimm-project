using System.Collections.Generic;
using System.Linq;
using Swimm.Parsing.Parsers.IsrOrg;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Вывод типа заплыва (prelim/final) из порядка сессий: в loglig-экспорте слов
/// «מוקדמות/גמר» нет, признаком служит повтор дисциплины в один день — раннее событие
/// предварительные, позднее финал (бугрим-формат: обе сессии в один день).
/// </summary>
public class IsrOrgHeatTypeTests
{
    private static IsrOrgCompetitionResult Comp(
        string date, string style = "freestyle", string len = "100",
        string gender = "female", string age = "13-99", params int[] swimmers)
        => new(
            Competition: "אליפות בוגרים",
            AgeGroup: age,
            Date: date,
            Event: $"{len} {style}",
            EventStyleName: style,
            EventStyleLen: len,
            EventStyleGender: gender,
            EventStyleAge: age,
            PoolType: "50m",
            Results: swimmers.Select(n => new IsrOrgResult(
                Country: "ISR", Position: n, Heat: 1, Lane: n,
                LastName: $"Last{n}", FirstName: $"First{n}", BirthYear: 2008,
                Club: "Club", Time: "01:00.00", TimeFailNote: null,
                InternationalPoints: 500, IsRelay: false,
                RelayTeamName: null, RelaySwimmersName: null, RelaySwimmers: null)).ToList());

    [Fact]
    public void RepeatedDisciplineSameDay_EarlierPrelim_LaterFinal()
    {
        var types = IsrOrgParser.AssignHeatTypes(
        [
            Comp("23/05/2026"),                       // прелимы 100 в/с Ж
            Comp("23/05/2026", gender: "male"),       // прелимы 100 в/с М
            Comp("23/05/2026"),                       // финал 100 в/с Ж
            Comp("23/05/2026", gender: "male"),       // финал 100 в/с М
        ]);

        Assert.Equal(["prelim", "prelim", "final", "final"], types);
    }

    [Fact]
    public void SingleOccurrence_OrDifferentDays_StayNull()
    {
        var types = IsrOrgParser.AssignHeatTypes(
        [
            Comp("23/05/2026", len: "1500"),          // timed final — один раз за день
            Comp("23/05/2026"),                       // 100 в/с — второй раз только НАЗАВТРА
            Comp("24/05/2026"),
        ]);

        Assert.All(types, t => Assert.Null(t));
    }

    [Fact]
    public void ThreeSessions_OnlyLastIsFinal()
    {
        // Прелимы + финал B + финал A отдельными событиями: финал — только последнее.
        var types = IsrOrgParser.AssignHeatTypes(
            [Comp("23/05/2026"), Comp("23/05/2026"), Comp("23/05/2026")]);

        Assert.Equal(["prelim", "prelim", "final"], types);
    }

    [Fact]
    public void DifferentCategoryLabels_PairedBySubsetOfParticipants()
    {
        // Бугрим 25/05/2026: прелимы напечатаны как «13-99», финал — «14-99». Точный ключ
        // их не парит; финал опознаётся по составу (финалисты ⊂ участники прелимов).
        var types = IsrOrgParser.AssignHeatTypes(
        [
            Comp("25/05/2026", age: "13-99", swimmers: [1, 2, 3, 4, 5, 6, 7, 8, 9, 10]),
            Comp("25/05/2026", age: "14-99", swimmers: [1, 2, 3, 4, 5, 6, 7, 8]),
        ]);

        Assert.Equal(["prelim", "final"], types);
    }

    [Fact]
    public void DisjointParticipants_DifferentCategories_NotPaired()
    {
        // Маккабиада: «50 free Men» и «50 free Men U17» — разные программы, составы не
        // пересекаются. Подмножеством они не являются и prelim/final не становятся.
        var types = IsrOrgParser.AssignHeatTypes(
        [
            Comp("25/05/2026", age: "open", swimmers: [1, 2, 3, 4, 5, 6, 7, 8, 9, 10]),
            Comp("25/05/2026", age: "17", swimmers: [11, 12, 13, 14, 15]),
        ]);

        Assert.All(types, t => Assert.Null(t));
    }
}
