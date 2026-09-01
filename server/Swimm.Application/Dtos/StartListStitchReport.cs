namespace Swimm.Application.Dtos;

/// <summary>
/// Итог сшивки заявок с результатами (docs/plans/start-list-plan.md, шаг С9).
/// </summary>
/// <param name="Days">Дней соревнования нашлось в справочнике.</param>
/// <param name="Entries">Заявок в скоупе сшивки.</param>
/// <param name="Linked">Заявок привязано к дню (<c>CompetitionId</c> проставлен).</param>
/// <param name="Swum">Заявок нашли свой результат.</param>
/// <param name="NoShow">
/// Заявок остались без результата — неявки дня старта. Ради этого числа заявки и не
/// стираются: иначе на вопрос «почему в протоколе меньше, чем заявлено» отвечать нечем.
/// </param>
/// <param name="MatchedByDiscipline">
/// Из них сматчено не по дорожке, а по дисциплине: на месте пловца пересадили в день старта.
/// Отдельно, потому что это единственный «мягкий» шов сшивки — по нему видно, стоит ли ему
/// доверять.
/// </param>
/// <param name="Unlinked">Заявок не удалось отнести ни к одному дню (дата не совпала).</param>
public sealed record StartListStitchReport(
    int OrgCompId,
    int Days,
    int Entries,
    int Linked,
    int Swum,
    int NoShow,
    int MatchedByDiscipline,
    int Unlinked);
