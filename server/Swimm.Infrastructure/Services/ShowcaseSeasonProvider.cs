using Microsoft.EntityFrameworkCore;
using Swimm.Application.Abstractions;
using Swimm.Application.Constants;
using Swimm.Application.Dtos;
using Swimm.Domain;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Services;

/// <summary>
/// Витринный сезон по данным (<see cref="IShowcaseSeasonProvider"/>): считает границу из дат
/// ЗИМНИХ чемпионатов всей базы — граница общая для продукта, а не для клуба или пловца.
///
/// Читает через <see cref="SwimmReadDbContext"/> (роль swimm_ro), как остальные публичные
/// пути. Даты кэшируются: за один запрос страницы витринный сезон спрашивают несколько
/// репозиториев сразу, а меняется он только после импорта (кэш сбрасывается целиком).
/// </summary>
public class ShowcaseSeasonProvider : IShowcaseSeasonProvider
{
    private readonly SwimmReadDbContext _read;
    private readonly ICacheService _cache;

    /// <summary>Совпадает с TTL остальных публичных полезных нагрузок.</summary>
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(5);

    /// <summary>Ключ перенесён из SwimmerPageRepository — тот же список, тот же смысл.</summary>
    private const string CacheKey = "winter-championship-dates";

    public ShowcaseSeasonProvider(SwimmReadDbContext read, ICacheService cache)
    {
        _read = read;
        _cache = cache;
    }

    public Task<int> CurrentStartYearAsync(CancellationToken ct = default) =>
        StartYearAtAsync(DateTime.UtcNow, ct);

    public async Task<int> StartYearAtAsync(DateTime now, CancellationToken ct = default) =>
        ShowcaseSeason.StartYearOf(await WinterDatesAsync(ct), now);

    public Task<ShowcaseSeasonNoticeDto?> PendingNoticeAsync(CancellationToken ct = default) =>
        PendingNoticeAtAsync(DateTime.UtcNow, ct);

    public async Task<ShowcaseSeasonNoticeDto?> PendingNoticeAtAsync(
        DateTime now, CancellationToken ct = default)
    {
        var winter = await WinterDatesAsync(ct);
        var showcase = ShowcaseSeason.StartYearOf(winter, now);
        var calendar = SeasonMath.StartYearOf(now);

        // Витринный сезон догнал календарный — объяснять нечего, витрина молчит.
        if (showcase >= calendar) return null;

        // Ближайший ЕЩЁ НЕ проплытый зимний чемпионат ждущего сезона. Его может не быть в
        // базе вовсе (расписание нового сезона не затянуто) — тогда даты не обещаем.
        var winterStarts = winter
            .Where(d => d.Date > now.Date && SeasonMath.StartYearOf(d) == calendar)
            .OrderBy(d => d)
            .Select(d => (DateTime?)d)
            .FirstOrDefault();

        return new ShowcaseSeasonNoticeDto
        {
            ShowingSeason = showcase,
            ShowingLabel = SeasonMath.Label(showcase),
            PendingSeason = calendar,
            PendingLabel = SeasonMath.Label(calendar),
            // Формат дат витрины один на продукт — dd/MM/yyyy, как в строке заплыва.
            WinterStarts = winterStarts?.ToString("dd/MM/yyyy",
                System.Globalization.CultureInfo.InvariantCulture),
        };
    }

    /// <summary>
    /// Даты зимних чемпионатов (все ступени, включая будущие — правило само отсекает то,
    /// что ещё не проплыли). Роль соревнования выводится тем же
    /// <see cref="StandingKinds.Resolve"/>, что и на странице клуба: короткая вода +
    /// признак чемпионата, ручное исключение — <c>StandingKindOverride</c>.
    /// </summary>
    private async Task<IReadOnlyList<DateTime>> WinterDatesAsync(CancellationToken ct)
    {
        var cached = await _cache.GetAsync<List<DateTime>>(CacheKey);
        if (cached is not null) return cached;

        var raw = await _read.Competitions.AsNoTracking()
            .Where(c => c.IsChampionship)
            .Select(c => new { c.Date, c.PoolType, c.StandingKindOverride })
            .ToListAsync(ct);

        var dates = raw
            .Where(c => StandingKinds.Resolve(true, c.PoolType, c.StandingKindOverride) == StandingKinds.Winter)
            .Select(c => ParseDate(c.Date))
            .Where(d => d != DateTime.MinValue)
            .ToList();

        await _cache.SetAsync(CacheKey, dates, Ttl);
        return dates;
    }

    private static DateTime ParseDate(string date) =>
        DateTime.TryParseExact(date, "dd/MM/yyyy",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var dt)
            ? dt
            : DateTime.MinValue;
}
