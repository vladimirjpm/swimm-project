using Swimm.Application.Dtos;

namespace Swimm.Application.Abstractions;

/// <summary>
/// «Какие страны вообще есть у источника» — пары alpha-3 ↔ внутренний GUID, по которым
/// батч-прогон Фазы 11 перебирает национальные рекорды. Шов ради контроллера и фоновой
/// задачи: они не должны знать про Swimm.Parsing.
/// </summary>
public interface IRecordCountriesProvider
{
    /// <summary>Источник, к которому относится список (<c>worldrecords</c>).</summary>
    string Source { get; }

    /// <summary>
    /// Только настоящие страны, отсортированные по коду. Псевдо-сборные источника
    /// (нейтральные атлеты, сборная беженцев, сама федерация) уже отсеяны.
    /// </summary>
    Task<IReadOnlyList<RecordCountryDto>> GetCountriesAsync(CancellationToken ct = default);
}
