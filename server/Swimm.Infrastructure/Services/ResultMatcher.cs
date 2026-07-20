using Swimm.Domain.Entities;

namespace Swimm.Infrastructure.Services;

/// <summary>
/// Ключ физической идентичности результата (докс: import-upsert-plan.md, Р2).
/// Заплыв+дорожка не меняются при перевыпуске протокола; SwimmerId сознательно не входит
/// в ключ (анонимные relay-леги, опечатки в имени пловца).
/// </summary>
public readonly record struct ResultMatchKey(
    int CompetitionId,
    int StyleId,
    string Distance,
    string Gender,
    int Heat,
    int Lane,
    bool IsRelay);

/// <summary>
/// Результат матчинга старых (уже в БД) и новых (из импортируемого файла) строк результата.
/// </summary>
public sealed class ResultMatch<TOld, TNew>
{
    public List<(TOld Old, TNew New)> Matched { get; } = [];
    public List<TNew> Inserted { get; } = [];
    public List<TOld> Deleted { get; } = [];
}

/// <summary>
/// Чистая функция матчинга результатов при переимпорте (upsert). Без доступа к БД — принимает
/// уже загруженные old/new строки и функции извлечения ключа, возвращает matched/insert/delete.
/// </summary>
/// <remarks>
/// Коллизии ключа (несколько строк с одинаковым ключом — в реальных протоколах не встречается,
/// но формат не запрещает) разрешаются по порядку следования: старые строки с одинаковым ключом
/// матчатся к новым строкам с тем же ключом в порядке появления (FIFO). Если новых строк с данным
/// ключом больше, чем старых — излишки становятся inserted; если меньше — излишки старых становятся
/// deleted (unmatched).
/// </remarks>
public static class ResultMatcher
{
    public static ResultMatch<TOld, TNew> Match<TOld, TNew>(
        IReadOnlyList<TOld> oldRows,
        IReadOnlyList<TNew> newRows,
        Func<TOld, ResultMatchKey> oldKeySelector,
        Func<TNew, ResultMatchKey> newKeySelector)
    {
        var result = new ResultMatch<TOld, TNew>();

        // FIFO-очереди старых строк по ключу — сохраняет порядок следования при коллизиях.
        var oldByKey = new Dictionary<ResultMatchKey, Queue<TOld>>();
        foreach (var old in oldRows)
        {
            var key = oldKeySelector(old);
            if (!oldByKey.TryGetValue(key, out var queue))
            {
                queue = new Queue<TOld>();
                oldByKey[key] = queue;
            }
            queue.Enqueue(old);
        }

        foreach (var @new in newRows)
        {
            var key = newKeySelector(@new);
            if (oldByKey.TryGetValue(key, out var queue) && queue.Count > 0)
            {
                result.Matched.Add((queue.Dequeue(), @new));
            }
            else
            {
                result.Inserted.Add(@new);
            }
        }

        foreach (var queue in oldByKey.Values)
            while (queue.Count > 0)
                result.Deleted.Add(queue.Dequeue());

        return result;
    }

    /// <summary>Ключ для строки результата, уже сохранённой в БД (RelayId — надёжный источник IsRelay).</summary>
    public static ResultMatchKey KeyOfPersisted(ResultRecord r) =>
        new(r.CompetitionId, r.StyleId, r.Distance, r.Gender, r.Heat, r.Lane, r.RelayId != null);

    /// <summary>Ключ для ещё не сохранённой строки результата (Relay — навигация, RelayId ещё не проставлен).</summary>
    public static ResultMatchKey KeyOfTransient(ResultRecord r) =>
        new(r.CompetitionId, r.StyleId, r.Distance, r.Gender, r.Heat, r.Lane, r.Relay != null || r.RelayId != null);
}
