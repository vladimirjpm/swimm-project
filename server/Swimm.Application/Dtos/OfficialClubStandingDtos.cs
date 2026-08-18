namespace Swimm.Application.Dtos;

/// <summary>
/// Что известно про ОФИЦИАЛЬНЫЙ клубный зачёт соревнования на loglig — тот, что показывает
/// кнопка «דירוג מועדונים».
///
/// Ключевая ловушка: сама кнопка есть в разметке у ВСЕХ соревнований (часть шаблона), поэтому
/// признак — непустой ответ ajax-таблицы, а не наличие кнопки
/// (docs/points-rules-per-competition-plan.md §10.1).
/// </summary>
/// <param name="HasStanding">Официальный клубный зачёт опубликован.</param>
/// <param name="Scale">
/// Шкала «место → очки», снятая с индивидуальных заплывов живой таблицы. Пустая — зачёта нет
/// либо очки не разобрались (тогда правило не подбирается).
/// </param>
/// <param name="MatchedRuleId">Правило из <c>PointRulesClubs</c>, чья шкала совпала; null — нет.</param>
/// <param name="MatchedRuleVersion">Версия совпавшего правила — для сообщения админу.</param>
/// <param name="Message">Готовое сообщение для админки: что нашли и что делать.</param>
public sealed record OfficialClubStandingProbe(
    bool HasStanding,
    IReadOnlyDictionary<int, int> Scale,
    int? MatchedRuleId,
    string? MatchedRuleVersion,
    string Message)
{
    /// <summary>Зачёта нет — сверять не с чем (лиги, мокдамот, региональные этапы).</summary>
    public static OfficialClubStandingProbe None(string message) =>
        new(false, new Dictionary<int, int>(), null, null, message);
}
