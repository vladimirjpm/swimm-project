using System.Collections.Generic;
using System.Linq;
using Swimm.Application.Mapping;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Сверка «файл против БД» (docs/data-integrity.md, фаза Д1) — чистая логика.
/// Ловит класс багов, который иначе виден только глазами: строки уехали в чужой заплыв,
/// переимпорт наплодил дубликаты, соревнование задвоилось.
/// </summary>
public class ImportReconcilerTests
{
    private static Dictionary<(int, string), int> Map(params (int comp, string key, int count)[] rows) =>
        rows.ToDictionary(r => (r.comp, r.key), r => r.count);

    [Fact]
    public void EventKey_IgnoresGenderAndAge()
    {
        // Пол импорт может доопределить с пловца, возраст считается по году рождения —
        // включать их в ключ значило бы ловить ложные расхождения.
        Assert.Equal(
            ImportReconciler.EventKey("freestyle", "200", false, "mix-shabbat"),
            ImportReconciler.EventKey(" freestyle ", " 200 ", false, " mix-shabbat "));

        Assert.NotEqual(
            ImportReconciler.EventKey("freestyle", "200", false, null),
            ImportReconciler.EventKey("freestyle", "200", true, null));
    }

    [Fact]
    public void EventKeyCoarse_IgnoresCategory()
    {
        // Ретро-аудит: колонка EventCategory появилась 2026-07-28, у старых импортов она
        // пустая. Полным ключом такое соревнование выглядело бы сплошным расхождением
        // (первый прогон аудита: 348 строк «не сошлись» при полном совпадении по сути).
        Assert.Equal(
            ImportReconciler.EventKeyCoarse("freestyle", "100", false),
            ImportReconciler.EventKeyCoarse("freestyle", "100", false));

        Assert.NotEqual(
            ImportReconciler.EventKey("freestyle", "100", false, "12"),
            ImportReconciler.EventKey("freestyle", "100", false, null));

        // При этом «уехало в чужой заплыв» грубый ключ ловит по-прежнему.
        Assert.NotEqual(
            ImportReconciler.EventKeyCoarse("freestyle", "200", false),
            ImportReconciler.EventKeyCoarse("individual_medley", "4X50", true));
    }

    [Fact]
    public void Build_AllMatch_NoMismatches()
    {
        var expected = Map((1, "freestyle|50|0|12", 30), (1, "freestyle|4X50|1|mix-12", 4));
        var rows = ImportReconciler.Build(expected, expected);

        Assert.All(rows, r => Assert.False(r.IsMismatch));
        Assert.Contains("сошлось", ImportReconciler.Describe(rows));

        // Итоговая строка по соревнованию — с пустым ключом.
        var total = rows.Single(r => r.EventKey.Length == 0);
        Assert.Equal(34, total.Expected);
        Assert.Equal(34, total.Actual);
    }

    [Fact]
    public void Build_RowsLandedInWrongEvent_Detected()
    {
        // Инцидент И-1: 10 строк 200 вольным уехали в эстафету 4X50 комплексом.
        var expected = Map((1, "freestyle|200|0|", 10), (1, "individual_medley|4X50|1|", 24));
        var actual = Map((1, "freestyle|200|0|", 0), (1, "individual_medley|4X50|1|", 34));

        var rows = ImportReconciler.Build(expected, actual);

        var free = rows.Single(r => r.EventKey.StartsWith("freestyle|200"));
        Assert.True(free.IsMismatch);
        Assert.Equal(10, free.Expected);
        Assert.Equal(0, free.Actual);

        // Итог по соревнованию при этом сходится — поэтому сверять надо по заплывам,
        // а не только по общему числу строк.
        var total = rows.Single(r => r.EventKey.Length == 0);
        Assert.False(total.IsMismatch);

        Assert.Contains("РАСХОЖДЕНИЕ", ImportReconciler.Describe(rows));
    }

    [Fact]
    public void Build_LeftoverRowsInDb_Detected()
    {
        // Инцидент И-4: переимпорт вставил дубликаты эстафет вместо обновления.
        var expected = Map((1, "individual_medley|4X50|1|mix-12", 18));
        var actual = Map((1, "individual_medley|4X50|1|mix-12", 36));

        var rows = ImportReconciler.Build(expected, actual);
        var row = rows.Single(r => r.EventKey.Length > 0);

        Assert.True(row.IsMismatch);
        Assert.Equal(18, row.Expected);
        Assert.Equal(36, row.Actual);
    }

    [Fact]
    public void Build_EventPresentOnlyInDb_StillReported()
    {
        // Заплыв, которого нет в файле (остался от прошлого разбора) — тоже находка.
        var expected = Map((1, "freestyle|200|0|", 10));
        var actual = Map((1, "freestyle|200|0|", 10), (1, "individual_medley|4X50|0|", 20));

        var rows = ImportReconciler.Build(expected, actual);
        var stale = rows.Single(r => r.EventKey.StartsWith("individual_medley"));

        Assert.Equal(0, stale.Expected);
        Assert.Equal(20, stale.Actual);
        Assert.True(stale.IsMismatch);
    }

    [Fact]
    public void Build_SeveralCompetitions_TotalsPerCompetition()
    {
        // Многодневка: у каждого дня своя итоговая строка.
        var expected = Map((1, "freestyle|50|0|12", 30), (2, "freestyle|100|0|12", 20));
        var rows = ImportReconciler.Build(expected, expected);

        var totals = rows.Where(r => r.EventKey.Length == 0).OrderBy(r => r.CompetitionId).ToList();
        Assert.Equal(2, totals.Count);
        Assert.Equal(30, totals[0].Expected);
        Assert.Equal(20, totals[1].Expected);
    }

    // ── Добавки к уже загруженному дню (Эйлат 05.11.2026, comp 1874) ──────────────────────

    private static ReconciliationRow Total(int expected, int actual) => new(1874, "", expected, actual);

    private static ReconciliationImport Import(string file, int minute, int expected, int actual,
        params ReconciliationRow[] events) =>
        new(new System.DateTime(2026, 11, 5, 19, minute, 0), file, Total(expected, actual), events);

    [Fact]
    public void IsAddition_DayGrewExactlyByFile_AndFileLanded()
    {
        // Вечер: было 667, файл 99, стало 766; заплывы файла легли целиком.
        var events = new[]
        {
            new ReconciliationRow(1874, "freestyle|200|0|13", 30, 30),
            new ReconciliationRow(1874, "freestyle|4X50|1|", 21, 31),   // + эстафеты прошлой добавки
            new ReconciliationRow(1874, "butterfly|50|0|10", 0, 18),    // дневная сессия — не из файла
        };
        Assert.True(ImportReconciler.IsAddition(667, Total(99, 766), events));
    }

    [Fact]
    public void IsAddition_FullReimportWithLeftovers_IsNot()
    {
        // Лига 5 (comp 1580): было 501, тот же протокол 501, стало 515 — лишние строки, не добавка.
        Assert.False(ImportReconciler.IsAddition(501, new ReconciliationRow(1580, "", 501, 515), []));
    }

    [Fact]
    public void IsAddition_RowsOfTheFileLost_IsNot()
    {
        // День вырос на размер файла, но один заплыв файла лёг не весь (а «лишнее» пришло
        // откуда-то ещё) — это расхождение, его нельзя прятать за арифметикой итога.
        var events = new[]
        {
            new ReconciliationRow(1874, "freestyle|200|0|13", 30, 28),
            new ReconciliationRow(1874, "freestyle|200|0|14", 0, 2),
        };
        Assert.False(ImportReconciler.IsAddition(100, Total(30, 130), events));
    }

    [Fact]
    public void IsAddition_FirstImport_IsNot()
    {
        Assert.False(ImportReconciler.IsAddition(null, Total(99, 766), []));
        Assert.False(ImportReconciler.IsAddition(0, Total(99, 99), []));
    }

    [Fact]
    public void Verdict_SkipsAdditions_BackToTheDayFile()
    {
        // Ровно история 1874: дневной файл 657 сошёлся, эстафеты +10, вечер +99.
        var imports = new[]
        {
            Import("day1.json", 47, 657, 657),
            Import("day1-relays.json", 50, 10, 667, new ReconciliationRow(1874, "freestyle|4X50|1|", 10, 10)),
            Import("day1-evening.json", 55, 99, 766, new ReconciliationRow(1874, "freestyle|200|0|13", 78, 78),
                new ReconciliationRow(1874, "freestyle|4X50|1|", 21, 31)),
        };

        var verdict = ImportReconciler.Verdict(imports);

        Assert.NotNull(verdict);
        Assert.Equal("day1.json", verdict!.Value.Decisive.FileName);
        Assert.Equal(2, verdict.Value.AdditionsAfter);
        Assert.False(verdict.Value.Decisive.Total.IsMismatch);
    }

    [Fact]
    public void Verdict_MismatchedDayFile_NotHiddenByLaterAddition()
    {
        // Добавка, легшая целиком, не «чинит» расхождение дневного файла под ней.
        var imports = new[]
        {
            Import("day1.json", 47, 657, 650),
            Import("day1-evening.json", 55, 99, 749, new ReconciliationRow(1874, "freestyle|200|0|13", 99, 99)),
        };

        var verdict = ImportReconciler.Verdict(imports)!.Value;

        Assert.Equal("day1.json", verdict.Decisive.FileName);
        Assert.True(verdict.Decisive.Total.IsMismatch);
    }

    [Fact]
    public void Describe_Addition_SaysSoInsteadOfMismatch()
    {
        var rows = ImportReconciler.Build(
            Map((1874, "freestyle|200|0|13", 99)),
            Map((1874, "freestyle|200|0|13", 99), (1874, "butterfly|50|0|10", 667)));

        var text = ImportReconciler.Describe(rows, new HashSet<int> { 1874 });

        Assert.Contains("Добавка к дню", text);
        Assert.DoesNotContain("РАСХОЖДЕНИЕ", text);
    }
}
