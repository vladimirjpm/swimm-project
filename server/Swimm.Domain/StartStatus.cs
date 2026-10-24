namespace Swimm.Domain;

/// <summary>
/// Стартовал ли пловец по строке протокола. Единственное место правила: зеркало на клиенте —
/// <c>HelperResults.isNoStart</c>.
///
/// Неявка (<c>NS</c>/<c>DNS</c>) — строка в протоколе есть, а заплыва не было: пловца заявили,
/// он не вышел. DQ и DNF — стартовал: проплыл, но его сняли или он сошёл.
///
/// Отсюда два разных счётчика «заплывов» на витрине: сколько стоит в протоколе и сколько
/// проплыто. Сабина ברנצב 24.10.2026 заявлена на 3, проплыла 2 — витрина пишет «2/3», а не «3».
/// </summary>
public static class StartStatus
{
    public static bool IsNoStart(bool timeFail, string? failNote) =>
        timeFail && failNote?.Trim().ToUpperInvariant() is "NS" or "DNS";
}
