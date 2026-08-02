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
}
