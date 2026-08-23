using Swimm.Application.Dtos;

namespace Swimm.Application.Abstractions;

/// <summary>
/// «Что именно скачает Fetch»: актуальные ссылки на PDF-справочники рекордов, найденные на
/// странице-оглавлении источника. Шов ради контроллера — он не должен знать про Swimm.Parsing.
/// </summary>
public interface IRecordSourceLinksProvider
{
    /// <summary>Адрес страницы-оглавления (её же показываем админу ссылкой).</summary>
    string PageUrl { get; }

    Task<IReadOnlyList<RecordSourceLinkDto>> GetLinksAsync(CancellationToken ct = default);
}
