namespace Swimm.Application.Dtos;

/// <summary>
/// Итог одного забора стартового протокола (docs/plans/start-list-plan.md, шаг С4).
/// Он же ложится строкой в журнал <c>Sys_StartListPulls</c>.
/// </summary>
/// <param name="Status">ok | partial | empty | error.</param>
/// <param name="Events">Заплывов в программе соревнования.</param>
/// <param name="EventsFetched">Из них удалось прочитать (при <c>partial</c> меньше, чем <see cref="Events"/>).</param>
/// <param name="Entries">Строк заявок прочитано из источника.</param>
/// <param name="Moved">
/// Пересев: тот же пловец в той же дисциплине сменил заплыв или дорожку. Отдельно от
/// <see cref="Added"/>/<see cref="Removed"/> сознательно — иначе каждое снятие в раннем
/// заплыве выглядело бы как массовая перезапись половины протокола.
/// </param>
/// <param name="SwimmersCreated">Заведено новых пловцов (новички, которых ещё нет в базе).</param>
/// <param name="SwimmersStamped">Существующим пловцам проставлен <c>LogligId</c>, которого не было.</param>
/// <param name="ClubsUnmatched">
/// Строк, у которых клуб не нашёлся в справочнике. Клубы из стартового протокола НЕ заводятся:
/// именно переимпорт по имени плодил клубы-дубли (docs/data-integrity.md, инцидент И-13).
/// Такие заявки привязаны к псевдоклубу «No club» и ждут импорта протокола.
/// </param>
/// <remarks>Итог ПЛАНОВОГО обхода нескольких соревнований — <see cref="StartListSweepReport"/>.</remarks>
public sealed record StartListPullReport(
    int OrgCompId,
    int? LogligId,
    string Status,
    string? Error,
    int Events,
    int EventsFetched,
    int Entries,
    int Added,
    int Moved,
    int Removed,
    int Unchanged,
    int SwimmersCreated,
    int SwimmersStamped,
    int ClubsUnmatched,
    DateTime PulledAt);

/// <summary>
/// Итог одного планового обхода стартовых протоколов (шаг С10).
/// </summary>
/// <param name="DetailsChecked">Будущих стартов без <c>logligId</c> проверено.</param>
/// <param name="DetailsResolved">Из них <c>logligId</c> добыт.</param>
/// <param name="Total">Будущих стартов с <c>logligId</c> — кандидатов на забор.</param>
/// <param name="Pulled">Из них забор прошёл без исключения.</param>
public sealed record StartListSweepReport(
    int DetailsChecked,
    int DetailsResolved,
    int Total,
    int Pulled);
