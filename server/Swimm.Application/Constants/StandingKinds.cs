namespace Swimm.Application.Constants;

/// <summary>
/// Роль соревнования в КЛУБНОМ ЗАЧЁТЕ сезона: у возрастной группы за сезон два чемпионата —
/// зимний в бассейне 25м и летний в 50м (плюс, в будущем, открытая вода).
///
/// Роль не вводится руками, а выводится из того, что уже есть у соревнования:
/// <c>IsChampionship</c> + <c>PoolType</c>. Правило проверено на всех 23 чемпионатах в БД
/// (2026-08-01): 12 в 25м — январь/февраль, 11 в 50м — июль/август, исключений ноль.
/// Ручное переопределение — <c>Competition.StandingKindOverride</c>.
///
/// ⚠ Чемпионата в сезоне может не быть (отменили) — это ШТАТНЫЙ случай, клетка грида
/// на странице клуба показывает пустое состояние. А вот ДВА зимних чемпионата одной группы
/// за сезон невозможны: если такое встретилось, ошибка в <c>IsChampionship</c>/<c>PoolType</c>
/// самого соревнования. См. docs/plans/club-page-model.md §2.1.
/// </summary>
public static class StandingKinds
{
    public const string Winter = "winter";
    public const string Summer = "summer";
    /// <summary>
    /// Открытая вода. ⚠ До появления поля площадки (<c>Competition.WaterKind</c>,
    /// docs/plans/open-water-course-plan.md) это же значение в
    /// <c>Competition.StandingKindOverride</c> служит ЕДИНСТВЕННЫМ признаком морского
    /// старта — по нему витрины отличают море от бассейна (решение Р20,
    /// docs/data-integrity.md §9). Значит, защита ровно настолько надёжна, насколько
    /// аккуратно проставлен override: непомеченный морской старт снова смешается с бассейном.
    /// </summary>
    public const string OpenWater = "openwater";

    /// <summary>Значение <c>StandingKindOverride</c>, явно исключающее соревнование из зачёта.</summary>
    public const string None = "none";

    public static readonly IReadOnlyList<string> All = new[] { Winter, Summer, OpenWater };

    public static bool IsValid(string? value) => value is not null && All.Contains(value);

    /// <summary>Допустимые значения переопределения (включая явное «не зачёт»).</summary>
    public static bool IsValidOverride(string? value) =>
        value is null || value == None || IsValid(value);

    /// <summary>
    /// Роль соревнования в зачёте: <c>null</c> — обычный старт (в зачёт не идёт, но в истории
    /// клуба виден). Переопределение важнее вывода по данным.
    /// </summary>
    public static string? Resolve(bool isChampionship, string? poolType, string? overrideKind)
    {
        if (overrideKind is not null)
            return overrideKind == None ? null : IsValid(overrideKind) ? overrideKind : null;

        if (!isChampionship) return null;

        return poolType switch
        {
            PoolTypes.Short => Winter,
            PoolTypes.Long => Summer,
            _ => null,   // бассейн не задан — роль вывести не из чего
        };
    }
}
