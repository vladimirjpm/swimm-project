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
/// Итог одного планового обхода стартовых протоколов (шаг С10; частота по близости старта —
/// П3, <c>StartListSweepRules</c>).
/// </summary>
/// <param name="DetailsChecked">Стартов без <c>logligId</c> проверено суточной догрузкой (0, если её в этот тик не было).</param>
/// <param name="DetailsResolved">Из них <c>logligId</c> добыт.</param>
/// <param name="Total">Стартов окна, которые обход рассматривал (с loglig-id или ближайшие два дня).</param>
/// <param name="Pulled">Из взятых в этот тик забор прошёл без исключения.</param>
/// <param name="Due">Взято в этот тик — по правилу частоты пора.</param>
/// <param name="TooEarly">Пропущено: смотрели недавно, по правилу ещё рано.</param>
/// <param name="Started">Пропущено: день старта, первый заплыв уже начался.</param>
/// <param name="DetailsRan">В этот тик шла суточная догрузка loglig-id.</param>
public sealed record StartListSweepReport(
    int DetailsChecked,
    int DetailsResolved,
    int Total,
    int Pulled,
    int Due,
    int TooEarly,
    int Started,
    bool DetailsRan);
