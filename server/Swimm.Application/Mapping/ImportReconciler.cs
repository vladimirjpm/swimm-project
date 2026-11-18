namespace Swimm.Application.Mapping;

/// <summary>Строка сверки: заплыв, сколько обещал файл, сколько легло в БД.</summary>
public sealed record ReconciliationRow(int CompetitionId, string EventKey, int Expected, int Actual)
{
    public bool IsMismatch => Expected != Actual;
    public string Status => IsMismatch ? "mismatch" : "ok";
}

/// <summary>Одна сверка (один загруженный файл) соревнования: итог и строки по заплывам.</summary>
public sealed record ReconciliationImport(
    DateTime ImportedAt, string FileName, ReconciliationRow Total, IReadOnlyList<ReconciliationRow> Events);

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

    /// <summary>
    /// Файл был ДОБАВКОЙ к уже загруженному дню (вечерняя сессия, эстафеты, затягивание идущего
    /// соревнования по частям — docs/pull-to-prod.md, <c>overwriteExisting</c> без
    /// <c>deleteMissing</c>), а не полным протоколом дня.
    ///
    /// Сверка «файл против всего дня» для такого файла бессмысленна: в файле 99 строк вечера, в
    /// дне 766 (Эйлат 05.11.2026, comp 1874 — ложная ошибка <c>import.reconciliation-mismatch</c>).
    /// Признак берётся из фактов, без флага импорта: день вырос РОВНО на размер файла
    /// (<c>было + в файле = стало</c>) и каждый заплыв из файла лёг целиком. Переимпорт полного
    /// протокола, после которого остались лишние строки, этому не удовлетворяет (Лига 5, comp
    /// 1580: 501 + 501 ≠ 515) — и остаётся расхождением, ради которого сверка и есть.
    /// </summary>
    /// <param name="previousTotalActual">Сколько строк было в дне по предыдущей сверке; null — сверок не было.</param>
    /// <param name="total">Итоговая строка (пустой EventKey) этой сверки.</param>
    /// <param name="eventRows">Строки по заплывам этой сверки.</param>
    public static bool IsAddition(int? previousTotalActual, ReconciliationRow total, IEnumerable<ReconciliationRow> eventRows)
    {
        if (previousTotalActual is not int previous || previous <= 0) return false;
        if (total.Expected <= 0 || previous + total.Expected != total.Actual) return false;
        return eventRows.Where(r => r.EventKey.Length > 0 && r.Expected > 0).All(r => r.Actual >= r.Expected);
    }

    /// <summary>
    /// Какая сверка решает судьбу соревнования. Идём от последней назад, пропуская добавки
    /// (<see cref="IsAddition"/>): после дневного файла и двух добавок вердикт — у дневного файла.
    /// Добавка, в которой строки НЕ легли, — сама расхождение и стоит на месте.
    /// </summary>
    /// <param name="imports">Сверки одного соревнования по времени, старые первыми.</param>
    /// <returns>Решающая сверка и сколько добавок после неё пропущено; null — сверок нет.</returns>
    public static (ReconciliationImport Decisive, int AdditionsAfter)? Verdict(IReadOnlyList<ReconciliationImport> imports)
    {
        if (imports.Count == 0) return null;
        var i = imports.Count - 1;
        while (i > 0 && imports[i].Total.IsMismatch
               && IsAddition(imports[i - 1].Total.Actual, imports[i].Total, imports[i].Events))
            i--;
        return (imports[i], imports.Count - 1 - i);
    }

    /// <summary>Человекочитаемая сводка для лога импорта и панели затягивания.</summary>
    /// <param name="additions">Соревнования, для которых файл — добавка (<see cref="IsAddition"/>).</param>
    public static string Describe(IReadOnlyCollection<ReconciliationRow> rows, IReadOnlySet<int>? additions = null)
    {
        var totals = rows.Where(r => r.EventKey.Length == 0).ToList();
        var added = totals.Where(t => additions?.Contains(t.CompetitionId) == true).ToList();
        var bad = rows.Where(r => r.EventKey.Length > 0 && r.IsMismatch
                                  && additions?.Contains(r.CompetitionId) != true).ToList();

        var addedNote = added.Count == 0 ? "" : " Добавка к дню: " + string.Join("; ", added.Select(t =>
            $"comp {t.CompetitionId} +{t.Expected} строк легли, всего в дне {t.Actual}")) + ".";

        if (bad.Count == 0)
            return added.Count > 0 && added.Count == totals.Count
                ? $"Сверка с файлом: сошлось.{addedNote}"
                : $"Сверка с файлом: сошлось ({totals.Where(t => !added.Contains(t)).Sum(t => t.Expected)} строк).{addedNote}";

        var details = string.Join("; ", bad.Take(5).Select(r => $"{r.EventKey}: файл {r.Expected}, БД {r.Actual}"));
        var tail = bad.Count > 5 ? $" и ещё {bad.Count - 5}" : "";
        return $"Сверка с файлом: РАСХОЖДЕНИЕ в {bad.Count} заплыв(ах) — {details}{tail}. "
             + "Разбор — docs/data-integrity.md §8; обычно лечится переимпортом с «удалять лишние».";
    }
}
