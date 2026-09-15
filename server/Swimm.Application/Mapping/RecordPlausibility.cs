using System.Globalization;
using Swimm.Application.Dtos;
using Swimm.Domain.Entities;

namespace Swimm.Application.Mapping;

/// <summary>
/// Сторож правдоподобия времени в диффе рекордов (docs/data-integrity.md И-20,
/// docs/plans/records-quality-plan.md §3).
///
/// Источник сам отдаёт мусор: в отчёте World Aquatics за сентябрь 2026 «мировой рекорд»
/// 100 в/с ж 50 м — 40.11 при мужском около 46 с, и строка помечена Approved. Сторож такое
/// находит, но НИЧЕГО не блокирует и не чинит: наша копия обязана совпадать с источником,
/// иначе следующий импорт молча вернёт всё назад. Находка уходит в реестр спорных рекордов
/// КАНДИДАТОМ (<see cref="RecordIssueStatuses.Candidate"/>) — статус ставит человек.
///
/// Два правила, оба без ложных срабатываний на живых данных 2026-09-15:
/// <list type="number">
/// <item>мировой рекорд улучшен за раз больше чем на <see cref="WorldMaxImprovement"/>;</item>
/// <item>не мировой рекорд (страна, возраст, мастерс) быстрее мирового той же дисциплины.</item>
/// </list>
/// Порога улучшения для национальных рекордов нет сознательно: у малых федераций скачки на
/// 5–10 % нормальны (редкие дистанции, дырявые наборы). Подбирать его — по данным Фазы 11.
/// </summary>
public static class RecordPlausibility
{
    /// <summary>
    /// Больше какой доли мировой рекорд за раз не улучшают. Самые крупные скачки — костюмная
    /// эра 2008–2009, около 2 %; 3 % — с запасом. Живые улучшения 2026 года — 0.5–1.5 %
    /// (1500 в/с м, 50 в/с ж, 50 спина ж), «40.11» против 51.68 — 22 %.
    /// </summary>
    public const double WorldMaxImprovement = 0.03;

    /// <summary>
    /// Эталон «мировой рекорд дисциплины» для правила 2: из того, что лежит в базе, и того,
    /// что приехало в этом диффе, берётся ЛУЧШЕЕ. Так эталон не портится ни в одну сторону:
    /// мусорно-быстрый новый WR (40.11) ложных находок не даёт — всё медленнее него, а сам он
    /// пойман правилом 1; мусорно-медленный новый WR не превращает честные национальные
    /// рекорды в «быстрее мирового».
    /// </summary>
    public static Dictionary<string, (int Ms, string Time)> WorldReference(
        IEnumerable<(string Gender, string PoolType, string Style, string Distance, string Time)> worldRows)
    {
        var map = new Dictionary<string, (int Ms, string Time)>();
        foreach (var (gender, pool, style, distance, time) in worldRows)
        {
            if (SwimTime.ParseToMs(time) is not int ms) continue;
            var key = DisciplineKey(gender, pool, style, distance);
            if (!map.TryGetValue(key, out var best) || ms < best.Ms) map[key] = (ms, time.Trim());
        }
        return map;
    }

    /// <summary>
    /// Проверяет новые и изменившиеся строки диффа. Время, которое не разбирается, правилам
    /// не подлежит — это другой класс дефекта.
    /// </summary>
    public static IReadOnlyList<RecordSuspiciousEntry> Check(
        IEnumerable<RecordDiffEntry> entries,
        IReadOnlyDictionary<string, (int Ms, string Time)> worldReference)
    {
        var found = new List<RecordSuspiciousEntry>();
        foreach (var e in entries)
        {
            if (SwimTime.ParseToMs(e.NewTime) is not int newMs) continue;

            if (e.RegionType == "world")
            {
                if (SwimTime.ParseToMs(e.OldTime) is not int oldMs || newMs >= oldMs) continue;

                var gain = (oldMs - newMs) / (double)oldMs;
                if (gain > WorldMaxImprovement)
                    found.Add(Finding(e, RecordIssueReasons.ImplausibleImprovement,
                        $"Мировой рекорд улучшен за раз на {Percent(gain)}: {e.OldTime} → {e.NewTime}. " +
                        $"Больше чем на {Percent(WorldMaxImprovement)} мировые рекорды не улучшают — " +
                        "похоже на ошибку ввода у источника."));
                continue;
            }

            if (worldReference.TryGetValue(DisciplineKey(e.Gender, e.PoolType, e.Style, e.Distance), out var world)
                && newMs < world.Ms)
                found.Add(Finding(e, RecordIssueReasons.FasterThanWorldRecord,
                    $"{e.NewTime} быстрее мирового рекорда той же дисциплины ({world.Time}). " +
                    "Рекорд страны, возраста или мастерса не может быть быстрее абсолютного."));
        }
        return found;
    }

    /// <summary>Пол × бассейн × стиль × дистанция — ось, на которой живёт мировой рекорд.</summary>
    public static string DisciplineKey(string gender, string poolType, string style, string distance) =>
        string.Join('|',
            gender.Trim().ToLowerInvariant(),
            poolType.Trim().ToLowerInvariant(),
            style.Trim().ToLowerInvariant(),
            // Records хранит дистанцию с суффиксом ("100m") — как и в RecordIssueKey.
            distance.Trim().ToLowerInvariant().TrimEnd('m'));

    private static RecordSuspiciousEntry Finding(RecordDiffEntry e, string reason, string note) =>
        new(e.RegionType, e.RegionCode, e.Category, e.AgeKey, e.Gender, e.PoolType, e.Style,
            e.Distance, e.NewTime, reason, note);

    private static string Percent(double share) =>
        (share * 100).ToString("0.#", CultureInfo.InvariantCulture) + " %";
}
