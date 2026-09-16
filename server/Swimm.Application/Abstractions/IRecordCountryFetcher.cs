using Swimm.Application.Dtos;

namespace Swimm.Application.Abstractions;

/// <summary>
/// Отчёты рекордов источника для прогона по странам (11.1.2) — национальные по одной стране
/// и мировые один раз на весь прогон. Отдельно от <see cref="IRecordSourceProvider"/>: тому
/// страна не передаётся (она зашита в конфиг), и отчёт о непринятых строках его контракт
/// не возвращает.
/// </summary>
public interface IRecordCountryFetcher
{
    /// <summary>Ключ источника: <c>worldrecords</c>.</summary>
    string Source { get; }

    /// <summary>
    /// Качает оба бассейна (SCM + LCM) и возвращает строки категории <c>open</c> с
    /// <c>RegionCode</c> запрошенной страны. Сеть не отвечает — бросает после повторов.
    /// </summary>
    Task<RecordCountryFetchResult> FetchAsync(RecordCountryDto country, CancellationToken ct = default);

    /// <summary>
    /// Мировые рекорды (WR SCM + LCM) — <b>один раз на прогон</b>, а не на страну: 235 стран
    /// умножили бы эти два файла на 235. Нужны не только ради свежего <c>world</c>: сторож
    /// правдоподобия сверяет национальные рекорды с мировыми, и на устаревшем эталоне
    /// «быстрее мирового» сработало бы там, где рекорд просто новее.
    /// </summary>
    Task<IReadOnlyList<ParsedRecordDto>> FetchWorldAsync(CancellationToken ct = default);
}
