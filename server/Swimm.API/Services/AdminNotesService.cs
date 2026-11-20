using System.Collections.Concurrent;
using Swimm.API.Pages.Admin;
using Swimm.API.Pages.Admin.Shared;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Application.Mapping;

namespace Swimm.API.Services;

/// <summary>
/// Справки админки с диска: <c>docs/admin-notes/*.md</c> репозитория (docs/admin-pages/admin-notes.md).
/// В API-слое, как /Admin/Docs: тот же поиск корня репозитория (<see cref="DocsRenderer.FindRepoRoot"/>)
/// и тот же Markdown-движок с выключенным сырым HTML (<see cref="DocsRenderer.Render"/>).
///
/// Ключ из запроса в путь НЕ подставляется: справка ищется в списке файлов, который вернула сама
/// папка, — поэтому «..», слэши и прочее просто не находятся. Разбор файла кэшируется по времени
/// изменения и размеру: правка видна со следующего запроса, без перезапуска.
/// </summary>
public sealed class AdminNotesService : IAdminNotes
{
    public const string Folder = "docs/admin-notes";

    private readonly string? _dir;
    private readonly ConcurrentDictionary<string, Cached> _cache = new(StringComparer.Ordinal);

    private sealed record Cached(DateTime WriteUtc, long Length, ParsedAdminNote Note, string Html);

    public AdminNotesService(IWebHostEnvironment env)
        : this(DocsRenderer.FindRepoRoot(env.ContentRootPath) is { } root ? Path.Combine(root, "docs", "admin-notes") : null)
    {
    }

    /// <summary>Для тестов: папка справок напрямую (null — справок нет).</summary>
    public AdminNotesService(string? notesDirectory) => _dir = notesDirectory;

    public IReadOnlyList<AdminNoteInfo> List() =>
        Files().Select(f => Load(f.Key, f.Value) is { } c ? Info(f.Key, c) : null)
            .OfType<AdminNoteInfo>()
            .OrderBy(n => n.Key, StringComparer.Ordinal)
            .ToList();

    public AdminNoteView? Get(string key)
    {
        if (!AdminNoteParser.IsValidKey(key)) return null;
        if (!Files().TryGetValue(key, out var full)) return null;
        if (Load(key, full) is not { } c) return null;
        var info = Info(key, c);
        return new AdminNoteView(info.Key, info.Title, info.Checked, info.Path, c.Html);
    }

    private static AdminNoteInfo Info(string key, Cached c) =>
        new(key, c.Note.Title, c.Note.Checked, $"{Folder}/{key}.md");

    /// <summary>Белый список: ключ → полный путь, только файлы, которые реально лежат в папке.</summary>
    private Dictionary<string, string> Files()
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        if (_dir is null || !Directory.Exists(_dir)) return map;
        foreach (var full in Directory.EnumerateFiles(_dir, "*.md", SearchOption.TopDirectoryOnly))
        {
            var key = Path.GetFileNameWithoutExtension(full);
            // README.md — описание папки, не справка; имя не по форме ключа — пропускаем.
            if (string.Equals(key, "README", StringComparison.OrdinalIgnoreCase) || !AdminNoteParser.IsValidKey(key)) continue;
            map[key] = full;
        }
        return map;
    }

    private Cached? Load(string key, string full)
    {
        FileInfo fi;
        try { fi = new FileInfo(full); if (!fi.Exists) return null; }
        catch (IOException) { return null; }

        if (_cache.TryGetValue(key, out var hit) && hit.WriteUtc == fi.LastWriteTimeUtc && hit.Length == fi.Length)
            return hit;

        string text;
        try { text = File.ReadAllText(full); }
        catch (IOException) { return hit; } // файл сейчас пишут — отдать прежнюю версию
        var note = AdminNoteParser.Parse(text, key);
        var html = DocsRenderer.Render(note.Body, $"{Folder}/{key}.md", AdminNotesDocs.SectionKey);
        var fresh = new Cached(fi.LastWriteTimeUtc, fi.Length, note, html);
        _cache[key] = fresh;
        return fresh;
    }
}
