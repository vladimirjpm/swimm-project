using Swimm.Application.Dtos;

namespace Swimm.Application.Abstractions;

/// <summary>
/// Источник рекордов (мировые/возрастные/мастерские) — превращает внешние данные (XLSX с
/// api.worldaquatics.com, PDF-протокол с isr.org.il, …) в плоский список <see cref="ParsedRecordDto"/>.
/// Провайдер только парсит — в БД не пишет; запись и дифф — <see cref="IRecordDiffService"/>.
/// SSRF-защита: провайдер качает ТОЛЬКО с собственных whitelist-доменов, никогда по URL от
/// пользователя. Если файлы не приложены и фетч недоступен/не настроен — кидает
/// <see cref="InvalidOperationException"/> с человекочитаемым сообщением (UI покажет и
/// предложит ручную загрузку файла).
/// </summary>
public interface IRecordSourceProvider
{
    /// <summary>Ключ источника: worldrecords | isrorg-age | isrorg-masters.</summary>
    string Source { get; }

    Task<IReadOnlyList<ParsedRecordDto>> FetchAsync(RecordSourceRequest request, CancellationToken ct = default);
}
