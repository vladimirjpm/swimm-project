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
    public void SkinsAfterFinal_DoNotStealTheFinalMark()
    {
        // אליפות הרצליה, 01/11/2025: после финала 50 вольным плыли призовые заплывы на
        // выбывание — 8 → 2 у мужчин. Правило «последняя серия = финал» объявляло финалом
        // заплыв двух человек, а настоящий финал уезжал в prelim вместе со своими местами
        // (у прелимов Position на выдаче гасится). Финал — последняя ПОЛНОЦЕННАЯ серия.
        var prelims = Enumerable.Range(1, 44).ToArray();
        var final = Enumerable.Range(1, 8).ToArray();
        var skins = new[] { 1, 2 };

        var types = IsrOrgParser.AssignHeatTypes(
        [
            Comp("01/11/2025", len: "50", gender: "male", age: "17-99", swimmers: prelims),
            Comp("01/11/2025", len: "50", gender: "male", age: "17-99", swimmers: final),
            Comp("01/11/2025", len: "50", gender: "male", age: "17-99", swimmers: skins),
        ]);

        Assert.Equal(["prelim", "final", "prelim"], types);
    }

    [Fact]
    public void SkinsChain_FallsBackToTheLastFullHeat()
    {
        // У женщин цепочка длиннее: 8 → 4 → 2. Отбрасываем весь хвост мини-серий.
        var types = IsrOrgParser.AssignHeatTypes(
        [
            Comp("01/11/2025", len: "50", age: "16-99", swimmers: Enumerable.Range(1, 44).ToArray()),
            Comp("01/11/2025", len: "50", age: "16-99", swimmers: Enumerable.Range(1, 8).ToArray()),
            Comp("01/11/2025", len: "50", age: "16-99", swimmers: [1, 2, 3, 4]),
            Comp("01/11/2025", len: "50", age: "16-99", swimmers: [1, 2]),
        ]);

        Assert.Equal(["prelim", "final", "prelim", "prelim"], types);
    }

    [Fact]
    public void NormalFinal_StaysLast_EvenWhenSmallerThanPrelims()
    {
        // Обычная пара «прелимы 30 → финал 8»: финал меньше, но полноценный — метка на нём.
        var types = IsrOrgParser.AssignHeatTypes(
        [
            Comp("23/05/2026", swimmers: Enumerable.Range(1, 30).ToArray()),
            Comp("23/05/2026", swimmers: Enumerable.Range(1, 8).ToArray()),
        ]);

        Assert.Equal(["prelim", "final"], types);
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
