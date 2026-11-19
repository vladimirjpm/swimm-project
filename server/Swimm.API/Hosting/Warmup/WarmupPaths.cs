namespace Swimm.API.Hosting.Warmup;

/// <summary>
/// Что греть после старта (<see cref="StartupWarmupService"/>). Только адреса БЕЗ параметров пользователя
/// — у них один ключ кэша на всех, и первый посетитель после выкатки получает их уже готовыми.
/// Адреса с фильтрами (рекорды по категории, season-best по дистанции) не угадать — их прогрев
/// косвенный: после этого списка уже открыты соединения с базой, собрана модель EF и прогрет код.
/// Перебить можно ключом <c>Warmup:Paths</c> (массив).
/// </summary>
internal static class WarmupPaths
{
    public const string EnabledKey = "Warmup:Enabled";
    public const string PathsKey = "Warmup:Paths";

    public static readonly string[] Default =
    [
        "/api/client-config",
        "/api/categories",
        "/api/competitions",
        "/api/competitions/calendar",
        "/api/competition-overview?competitionId=last",
        "/api/records/freshness",
        "/api/records/countries",
        "/api/season-best/options",
    ];
}
