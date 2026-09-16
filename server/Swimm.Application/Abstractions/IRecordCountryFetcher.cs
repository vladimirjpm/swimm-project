using Swimm.Application.Dtos;

namespace Swimm.Application.Abstractions;

/// <summary>
/// Национальные рекорды ОДНОЙ страны по её идентификатору у источника — кирпич, из которого
/// собирается батч-прогон Фазы 11 (11.1.2). Отдельно от <see cref="IRecordSourceProvider"/>:
/// тому страна не передаётся (она зашита в конфиг), и отчёт о непринятых строках его
/// контракт не возвращает.
///
/// Мировые рекорды сюда не входят: WR качается один раз на весь прогон, а не на страну.
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
}
