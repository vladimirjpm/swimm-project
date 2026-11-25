namespace Swimm.Application.Abstractions;

/// <summary>
/// Шаг «игрок loglig у строки» (Р122, И-69): обходит страницы дисциплин соревнования на loglig и
/// пишет <c>Results.LogligPlayerId</c> тем нашим личным строкам, что совпали со строкой страницы
/// однозначно (дисциплина + заплыв + дорожка + время; без времени — дисциплина + пол + заплыв +
/// дорожка). По этим id проверка Health <c>swimmers.two-loglig-players</c> находит карточку, в
/// которой слиты два человека.
///
/// Что НЕ делает: пловцов не создаёт, не склеивает и не разделяет, строк не переносит,
/// <c>Swimmers.LogligId</c> не трогает, эстафеты пропускает. Запрос на дисциплину, с паузой
/// провайдера; итог каждого прогона — строка аудита <c>competition.loglig-row-players</c>.
/// </summary>
public interface ILogligRowPlayerService
{
    /// <summary>
    /// Прогон одного соревнования по compID isr.org.il. Не бросает (кроме отмены): сбой — в отчёте
    /// и в аудите, чтобы импорт и очередь шагов от него не зависели.
    /// </summary>
    /// <param name="trigger">Кто запустил: <c>import</c> (сам после импорта) или <c>cli</c>.</param>
    /// <param name="readBudget">
    /// Сколько времени читать страницы; null — без ограничения (CLI). Истёк — чтение
    /// останавливается, прочитанное сопоставляется и записывается, отчёт говорит, сколько осталось.
    /// </param>
    Task<LogligRowPlayerReport> StampAsync(
        int orgCompId, string trigger, CancellationToken ct = default, TimeSpan? readBudget = null);

    /// <summary>
    /// Р124: один день <paramref name="competitionId"/> со страницами соревнования loglig <paramref name="logligId"/> —
    /// день, который лежит на loglig отдельным соревнованием (у записи федерации номер другого дня).
    /// </summary>
    Task<LogligRowPlayerReport> StampDayAsync(
        int competitionId, int logligId, string trigger, CancellationToken ct = default, TimeSpan? readBudget = null);
}

/// <summary>Итог прогона шага «игрок loglig у строки».</summary>
/// <param name="Ok">false — прогон не состоялся (нет loglig-id, нет дней в базе, сайт не ответил).</param>
/// <param name="Events">Личных дисциплин прочитано.</param>
/// <param name="Requests">Запросов к loglig за прогон (список дисциплин + страницы).</param>
/// <param name="SourceRows">Личных строк на страницах; из них <paramref name="NoLink"/> — без ссылки на игрока.</param>
/// <param name="OurRows">Наших личных строк в днях соревнования.</param>
/// <param name="Matched">Пар доказано; из них <paramref name="Written"/> записано сейчас (остальные уже стояли).</param>
/// <param name="Ambiguous">Строк страницы с неединственным ключом — не писали.</param>
/// <param name="SourceUnmatched">Строк страницы, которым нашей строки не нашлось.</param>
/// <param name="TwoPlayerCards">Карточек, у которых в этом соревновании строки двух и более игроков.</param>
/// <param name="Unreadable">Дисциплины loglig, страница которых не прочиталась (ошибка или таймаут запроса).</param>
/// <param name="NotReached">Дисциплин, до которых чтение не дошло: истёк бюджет времени.</param>
public sealed record LogligRowPlayerReport(
    bool Ok, string Message, int Events = 0, int Requests = 0, int SourceRows = 0, int NoLink = 0,
    int OurRows = 0, int Matched = 0, int Written = 0, int Ambiguous = 0, int SourceUnmatched = 0,
    int TwoPlayerCards = 0, IReadOnlyList<int>? Unreadable = null, int NotReached = 0)
{
    /// <summary>Прочитаны не все страницы: строки без времени не сопоставлялись, прогон стоит повторить.</summary>
    public bool Incomplete => NotReached > 0 || Unreadable is { Count: > 0 };
}
