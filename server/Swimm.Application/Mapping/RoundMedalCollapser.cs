namespace Swimm.Application.Mapping;

/// <summary>
/// Схлопывание медалей, задвоенных РАУНДАМИ одного чемпионата (И13, docs/data-integrity.md §10).
///
/// У чемпионата «мокдамот и финал» одна возрастная ступень разыгрывается дважды за день:
/// утренний зачёт возрастных групп и вечерний финал первенства. Клубные очки федерация
/// платит за оба — это её арифметика, и мы её повторяем. А вот МЕДАЛЬ у ступени одна:
/// «медали раздавали по каждой возрастной ступени» (ответ из федерации, 2026-08-20), и
/// восемь золотых за четыре дистанции — артефакт нашего подсчёта строк, а не факт.
///
/// Поэтому в счётчиках наград строки одной ступени, различающиеся ТОЛЬКО раундом,
/// считаются один раз — по лучшему месту (утро 1-е, вечер 2-е → золото).
///
/// Срабатывает ТОЛЬКО там, где раунды есть: у обычных соревнований <c>Round</c> пуст,
/// группа однородна, и список возвращается нетронутым. Клубных очков это не касается
/// вовсе — там официальное задвоение сохраняется.
/// </summary>
public static class RoundMedalCollapser
{
    /// <param name="medalKey">Единица награждения: пловец + дисциплина + возрастная ступень.</param>
    /// <param name="round">Раунд строки; null/пусто — раундов у источника нет.</param>
    /// <param name="position">Место; null — места нет (в медали и так не попадёт).</param>
    public static List<T> Collapse<T>(
        IEnumerable<T> rows,
        Func<T, string> medalKey,
        Func<T, string?> round,
        Func<T, int?> position)
    {
        var result = new List<T>();

        foreach (var group in rows.GroupBy(medalKey))
        {
            var rounds = group
                .Select(round)
                .Where(r => !string.IsNullOrEmpty(r))
                .Distinct()
                .Count();

            if (rounds < 2)
            {
                result.AddRange(group);       // раундов нет или он один — считаем как считали
                continue;
            }

            var best = group
                .OrderBy(r => position(r) ?? int.MaxValue)
                .First();
            result.Add(best);
        }

        return result;
    }
}
