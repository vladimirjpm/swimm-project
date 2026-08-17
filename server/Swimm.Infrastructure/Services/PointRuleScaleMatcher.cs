using Swimm.Domain.Entities;

namespace Swimm.Infrastructure.Services;

/// <summary>
/// Подбор правила клубных очков по ФАКТИЧЕСКОЙ шкале официального зачёта loglig.
///
/// Зачем: у каждого чемпионата своя шкала в своём регламенте (§10.2 плана), а регламент —
/// PDF, местами картинкой. Зато живая таблица зачёта показывает очки за каждое место, и по
/// ним правило опознаётся однозначно: две разные шкалы федерации расходятся уже в первой
/// десятке (30/28/26… против 25/22/20… против 40/34/30…).
///
/// Матч строгий — совпасть должно КАЖДОЕ наблюдаемое место. Правило, которое «почти
/// подходит», хуже отсутствия правила: расхождение в хвосте даёт −3 очка на полосу и
/// всплывает только при ручной сверке (случай лета-2025, §10.4).
/// </summary>
public static class PointRuleScaleMatcher
{
    /// <summary>
    /// Мест, которых достаточно для вывода. Меньше пяти — это мелкий заплыв, где совпадёт
    /// половина шкал федерации.
    /// </summary>
    public const int MinPlacesForMatch = 5;

    /// <summary>
    /// Правило с точно такой шкалой; null — совпадения нет (шкала новая либо наблюдений мало).
    /// Правила перебираются в порядке <paramref name="rules"/>; при нескольких совпадениях
    /// берётся первое — одинаковые шкалы с разными версиями для расчёта равнозначны.
    /// </summary>
    public static PointRuleClubs? Match(
        IReadOnlyDictionary<int, int> observedScale, IReadOnlyCollection<PointRuleClubs> rules)
    {
        if (observedScale.Count < MinPlacesForMatch) return null;

        return rules.FirstOrDefault(rule => Fits(observedScale, rule));
    }

    /// <summary>Правило объясняет каждое наблюдаемое «место → очки».</summary>
    private static bool Fits(IReadOnlyDictionary<int, int> observedScale, PointRuleClubs rule)
    {
        var byPlace = rule.Entries.ToDictionary(e => e.Place, e => e.Points);

        foreach (var (place, points) in observedScale)
        {
            // За пределами шкалы правило платит DefaultPoints; в живой таблице такие места
            // просто без очков, поэтому наблюдение туда попасть не должно вовсе.
            var expected = rule.MaxScoringPlace is int max && place > max
                ? rule.DefaultPoints
                : byPlace.GetValueOrDefault(place, rule.DefaultPoints);

            if (expected != points) return false;
        }

        return true;
    }
}
