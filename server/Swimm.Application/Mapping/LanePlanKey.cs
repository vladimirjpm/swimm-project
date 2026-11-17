namespace Swimm.Application.Mapping;

/// <summary>
/// Ключ плана дорожек (docs/plans/trainings-coach-flow-plan.md, CF5, решение 1): план принадлежит
/// ЗАНЯТИЮ — дата + начало по стенным часам, тот же ключ <c>yyyy-MM-dd-HHmm</c>, что у ответов
/// (<see cref="TrainingRsvpRules.SessionKey"/>). <see cref="Start"/> null — старый план «на день»,
/// которому миграция не нашла занятия: только для чтения, адрес — голая дата <c>yyyy-MM-dd</c>.
///
/// Голая дата в адресе — ещё и старые ссылки участников на Lanes (до CF5 план адресовался датой):
/// чтение по ней находит план дня (<see cref="IsSession"/> false), писать по ней нельзя.
/// </summary>
public readonly record struct LanePlanKey(DateOnly Date, string? Start)
{
    /// <summary>Ключ занятия (можно писать); false — голая дата.</summary>
    public bool IsSession => Start != null;

    /// <summary>План занятия <paramref name="date"/> + <paramref name="start"/> («HH:mm»).</summary>
    public static LanePlanKey Session(DateOnly date, string start) => new(date, start);

    /// <summary>Ключ из адреса: <c>yyyy-MM-dd-HHmm</c> (занятие) или <c>yyyy-MM-dd</c> (день).</summary>
    public static bool TryParse(string? raw, out LanePlanKey key)
    {
        key = default;
        if (TrainingRsvpRules.TryParseSessionKey(raw, out var date, out var start))
        {
            if (date.Year is < 2000 or > 2100) return false;
            key = Session(date, start);
            return true;
        }
        if (!LanePlanRules.TryParseDate(raw, out var day)) return false;
        key = new LanePlanKey(day, null);
        return true;
    }

    /// <summary>Ключ для адреса и ответа API: <c>yyyy-MM-dd-HHmm</c>, у плана дня — <c>yyyy-MM-dd</c>.</summary>
    public override string ToString() =>
        Start == null ? LanePlanRules.FormatDate(Date) : TrainingRsvpRules.SessionKey(Date, Start);
}
