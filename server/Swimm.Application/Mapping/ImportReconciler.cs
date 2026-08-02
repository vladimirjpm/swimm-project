namespace Swimm.Application.Mapping;

/// <summary>Строка сверки: заплыв, сколько обещал файл, сколько легло в БД.</summary>
public sealed record ReconciliationRow(int CompetitionId, string EventKey, int Expected, int Actual)
{
    public bool IsMismatch => Expected != Actual;
    public string Status => IsMismatch ? "mismatch" : "ok";
}

/// <summary>
/// Сверка «файл против БД» (docs/data-integrity.md, фаза Д1). Чистая функция без EF —
/// вся её логика проверяема тестом, а не только через реальный импорт.
///
/// Ключ заплыва СОЗНАТЕЛЬНО не включает пол и возраст: пол результата импорт может
/// доопределить с пловца (смешанные заплывы), а возрастная ступень считается по году
/// рождения — сравнивать по ним значило бы ловить ложные расхождения. Стиля, дистанции,
/// признака эстафеты и категории программы достаточно: именно они разъезжаются, когда
/// строки уезжают в чужой заплыв.
/// </summary>
public static class ImportReconciler
{
    /// <summary>Ключ заплыва: <c>стиль|дистанция|эстафета?|категория</c>.</summary>
    public static string EventKey(string? styleName, string? distance, bool isRelay, string? eventCategory) =>
        $"{(styleName ?? "").Trim()}|{(distance ?? "").Trim()}|{(isRelay ? 1 : 0)}|{(eventCategory ?? "").Trim()}";

    /// <summary>
    /// Ключ без категории — для РЕТРО-сверки (аудита) старых импортов.
    ///
    /// Зачем отдельный: колонка <c>EventCategory</c> появилась 2026-07-28, и у всего, что
    /// залито раньше, она пустая. Сверка полным ключом объявляла бы такие соревнования
    /// сплошным расхождением (первый прогон аудита: 348 строк «не сошлись», хотя по
    /// стилю и дистанции всё совпадало до строки). Категория — свойство разбора, а не
    /// факт протокола; для вопроса «строки легли в свой заплыв?» достаточно
    /// стиля, дистанции и признака эстафеты.
    ///
    /// В штатной сверке импорта (Д1) используется полный ключ: там обе стороны родом
    /// из одного прогона парсера, и категория добавляет чувствительности бесплатно.
    /// </summary>
    public static string EventKeyCoarse(string? styleName, string? distance, bool isRelay) =>
        $"{(styleName ?? "").Trim()}|{(distance ?? "").Trim()}|{(isRelay ? 1 : 0)}";

    /// <summary>
    /// Сводит ожидаемое и фактическое в строки сверки. Ключи, встретившиеся только с одной
    /// стороны, тоже попадают в результат — «в БД есть заплыв, которого нет в файле» это
    /// ровно тот случай, ради которого сверка и делается (лишние строки после переимпорта).
    /// Плюс итоговая строка по каждому соревнованию с пустым EventKey.
    /// </summary>
    public static List<ReconciliationRow> Build(
        IReadOnlyDictionary<(int CompetitionId, string EventKey), int> expected,
        IReadOnlyDictionary<(int CompetitionId, string EventKey), int> actual)
    {
        var rows = new List<ReconciliationRow>();

        foreach (var key in expected.Keys.Union(actual.Keys).OrderBy(k => k.CompetitionId).ThenBy(k => k.EventKey, StringComparer.Ordinal))
        {
            rows.Add(new ReconciliationRow(
                key.CompetitionId, key.EventKey,
                expected.GetValueOrDefault(key),
                actual.GetValueOrDefault(key)));
        }

        // Итог по соревнованию — отдельной строкой с пустым ключом: по ней видно масштаб
        // расхождения, не складывая десятки заплывов глазами.
        foreach (var competitionId in rows.Select(r => r.CompetitionId).Distinct().ToList())
        {
            var forComp = rows.Where(r => r.CompetitionId == competitionId).ToList();
            rows.Add(new ReconciliationRow(
                competitionId, string.Empty,
                forComp.Sum(r => r.Expected),
                forComp.Sum(r => r.Actual)));
        }

        return rows
            .OrderBy(r => r.CompetitionId)
            .ThenBy(r => r.EventKey, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Человекочитаемая сводка для лога импорта и панели затягивания.</summary>
    public static string Describe(IReadOnlyCollection<ReconciliationRow> rows)
    {
        var totals = rows.Where(r => r.EventKey.Length == 0).ToList();
        var bad = rows.Where(r => r.EventKey.Length > 0 && r.IsMismatch).ToList();

        if (bad.Count == 0)
            return $"Сверка с файлом: сошлось ({totals.Sum(t => t.Expected)} строк).";

        var details = string.Join("; ", bad.Take(5).Select(r => $"{r.EventKey}: файл {r.Expected}, БД {r.Actual}"));
        var tail = bad.Count > 5 ? $" и ещё {bad.Count - 5}" : "";
        return $"Сверка с файлом: РАСХОЖДЕНИЕ в {bad.Count} заплыв(ах) — {details}{tail}. "
             + "Разбор — docs/data-integrity.md §8; обычно лечится переимпортом с «удалять лишние».";
    }
}
