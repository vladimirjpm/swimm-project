using Swimm.Application.Dtos;

namespace Swimm.Application.Abstractions;

/// <summary>
/// Проверка «есть ли у соревнования официальный клубный зачёт (דירוג מועדונים)» и подбор
/// правила очков под его шкалу.
///
/// Зачем шов: проверка ходит в loglig по сети, а зовут её из трёх мест — превью затягивания,
/// фоновый импорт и CLI-бэкфилл.
/// </summary>
public interface IOfficialClubStandingService
{
    /// <summary>
    /// Проверить соревнование по его loglig-id, ничего не записывая. Правило подбирается по
    /// фактической шкале зачёта среди <c>PointRulesClubs</c>.
    /// </summary>
    Task<OfficialClubStandingProbe> ProbeAsync(int logligId, CancellationToken ct = default);

    /// <summary>
    /// Проверить и проставить <c>Competition.HasOfficialClubStanding</c> всем соревнованиям
    /// с этим <paramref name="orgCompId"/> (у многодневки — каждому дню).
    /// loglig-id берётся из «входящих» автозабора.
    /// </summary>
    /// <returns>null — проверить не удалось (нет loglig-id или сайт недоступен), флаг не тронут.</returns>
    Task<OfficialClubStandingProbe?> ProbeAndStampAsync(int orgCompId, CancellationToken ct = default);

    /// <summary>
    /// Разовый проход по уже импортированным соревнованиям: проставить флаг тем, у кого его
    /// ещё нет. <paramref name="force"/> — перепроверить и уже помеченные.
    /// </summary>
    Task<OfficialClubStandingBackfillReport> BackfillAsync(bool force = false, CancellationToken ct = default);
}

/// <summary>Итог разового прохода: сколько соревнований с зачётом, без, и кого не проверили.</summary>
/// <param name="Checked">Проверено соревнований (логических, не дней).</param>
/// <param name="WithStanding">Из них с официальным клубным зачётом.</param>
/// <param name="WithoutStanding">Из них без зачёта — сверять не с чем.</param>
/// <param name="Unknown">Не удалось проверить (нет loglig-id или сайт недоступен).</param>
/// <param name="Lines">Построчный отчёт для консоли.</param>
public sealed record OfficialClubStandingBackfillReport(
    int Checked,
    int WithStanding,
    int WithoutStanding,
    int Unknown,
    IReadOnlyList<string> Lines);
