using Swimm.Application.Abstractions;

namespace Swimm.API.Pages.Admin.Shared;

/// <summary>
/// Раздел /Admin/Docs/notes «Справки админки» — собирается из папки <c>docs/admin-notes/</c> при
/// каждом открытии, а не строкой в <see cref="DocsCatalog"/>: добавить справку = положить файл
/// (docs/admin-pages/admin-notes.md). README папки идёт первым — как устроены справки.
/// </summary>
public static class AdminNotesDocs
{
    public const string SectionKey = "notes";

    public static DocSection Section(IAdminNotes notes)
    {
        var docs = new List<DocEntry>
        {
            new("docs/admin-notes/README.md", "Как устроены справки", "значок «ⓘ», формат файла, как привязать к странице"),
        };
        docs.AddRange(notes.List().Select(n => new DocEntry(n.Path, n.Title,
            n.Checked is { } d ? $"{n.Key} · проверено {d:dd.MM.yyyy}" : $"{n.Key} · дата проверки не указана")));
        return new DocSection(SectionKey, "Справки админки", "пояснения к службам и страницам — то, что открывает значок «ⓘ»", docs);
    }
}
