using Swimm.Application.Dtos;

namespace Swimm.Application.Abstractions;

/// <summary>
/// «Что именно скачает Fetch»: актуальные ссылки на PDF-справочники рекордов, найденные на
/// странице-оглавлении источника. Шов ради контроллера — он не должен знать про Swimm.Parsing.
/// </summary>
public interface IRecordSourceLinksProvider
{
    /// <summary>
    /// Ключи источников (<see cref="IRecordSourceProvider.Source"/>), чьи ссылки живут на этой
    /// странице-оглавлении. Их больше одного у isr.org.il: одна страница «שיאי ישראל» кормит и
    /// возрастной справочник, и мастерский. Страниц же теперь тоже несколько — по этим ключам
    /// контроллер и выбирает, к какой идти.
    /// </summary>
    IReadOnlyCollection<string> Sources { get; }

    /// <summary>Адрес страницы-оглавления (её же показываем админу ссылкой).</summary>
    string PageUrl { get; }

    Task<IReadOnlyList<RecordSourceLinkDto>> GetLinksAsync(CancellationToken ct = default);
}
