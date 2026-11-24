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

    /// <summary>
    /// В графе результата стоит отметка судьи — <c>DNS</c>, <c>DQ…</c>/<c>DSQ</c>, <c>DNF…</c>
    /// («DQ / SW 6.5», «DNF / SW 10.2»): заплыв был, по этой строке времени не будет никогда.
    /// Нужно правилу «проплыто ли» (<c>CompetitionProgress</c>, Р120).
    ///
    /// ⚠ <c>NS</c> сюда НЕ входит, хотя для «стартовал ли» (<see cref="IsNoStart"/>) это неявка.
    /// PDF loglig печатает «NS» в строке, по которой результат ещё не внесён: у неполного протокола
    /// непроплытые события идут строками «NS» (comp 1768, забран 10.10.2026: 140 «NS», в свежем PDF
    /// у 134 из них время), у непроведённого старта — все строки «0 NS» (14676, Р116). «DNS» же
    /// вносит организатор (там же: одна строка стала «DNS»). Поэтому «NS» не доказывает, что заплыв
    /// прошёл, а «DNS» доказывает.
    /// </summary>
    public static bool IsOfficialMark(bool timeFail, string? failNote)
    {
        if (!timeFail || string.IsNullOrWhiteSpace(failNote)) return false;
        var head = failNote.Trim().Split([' ', '/'], 2, StringSplitOptions.RemoveEmptyEntries)[0].ToUpperInvariant();
        return head is "DNS" or "DQ" or "DSQ" or "DNF";
    }
}
