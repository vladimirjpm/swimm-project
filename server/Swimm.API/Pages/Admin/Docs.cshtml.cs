using Markdig;
using Markdig.Renderers;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Swimm.API.Pages.Admin.Shared;

namespace Swimm.API.Pages.Admin;

/// <summary>
/// /Admin/Docs/{section} — документация репозитория в админке (docs/admin-pages/docs.md).
///
/// Файлы читаются с диска при открытии и рендерятся из Markdown — копий нет, поэтому то, что
/// видно здесь, всегда совпадает с репозиторием. Что показывать и где — <see cref="DocsCatalog"/>.
/// ⚠ Нужен корень репозитория рядом с приложением: в dev он находится подъёмом от ContentRoot;
/// в опубликованной сборке файлов нет, и страница честно об этом говорит.
/// </summary>
[Authorize(Roles = "Admin")]
public class DocsModel : PageModel
{
    private readonly IWebHostEnvironment _env;

    public DocsModel(IWebHostEnvironment env) => _env = env;

    public DocSection Section { get; private set; } = null!;
    public string? FilePath { get; private set; }
    public DocEntry? Entry { get; private set; }
    public string? Html { get; private set; }
    public DateTime? LastWriteTime { get; private set; }
    public string? Error { get; private set; }

    public IActionResult OnGet(string? section, string? file)
    {
        var found = DocsCatalog.Sections.FirstOrDefault(s => s.Key == section);
        if (found is null) return RedirectToPage(new { section = DocsCatalog.Sections[0].Key });
        Section = found;

        FilePath = string.IsNullOrWhiteSpace(file) ? found.Docs.FirstOrDefault()?.Path : file.Trim();
        Entry = DocsCatalog.Sections.SelectMany(s => s.Docs).FirstOrDefault(d => d.Path == FilePath);
        if (FilePath is null) return Page();

        if (!DocsCatalog.IsAllowed(FilePath))
        {
            Error = $"Файл «{FilePath}» админка не показывает: только MD под docs/ и файлы из белого списка DocsCatalog.";
            return Page();
        }

        var root = DocsRenderer.FindRepoRoot(_env.ContentRootPath);
        if (root is null)
        {
            Error = "Рядом с приложением нет репозитория (папки docs/ и CLAUDE.md) — документация доступна в dev-сборке.";
            return Page();
        }

        var full = Path.GetFullPath(Path.Combine(root, FilePath));
        if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || !System.IO.File.Exists(full))
        {
            Error = $"Файл «{FilePath}» не найден.";
            return Page();
        }

        Html = DocsRenderer.Render(System.IO.File.ReadAllText(full), FilePath, found.Key);
        LastWriteTime = System.IO.File.GetLastWriteTime(full);
        return Page();
    }
}

/// <summary>Markdown → HTML для /Admin/Docs: сырой HTML выключен, ссылки на MD ведут внутрь админки.</summary>
public static class DocsRenderer
{
    // Сырой HTML в документах выключен: рендер идёт в страницу админки, и тег из MD исполнился
    // бы в её контексте. Таблицы, автоссылки и якоря заголовков даёт UseAdvancedExtensions.
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .DisableHtml()
        .Build();

    /// <summary>Корень репозитория — первая папка вверх от ContentRoot, где есть docs/ и CLAUDE.md.</summary>
    public static string? FindRepoRoot(string start)
    {
        var dir = new DirectoryInfo(start);
        for (var i = 0; i < 6 && dir is not null; i++, dir = dir.Parent)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "docs"))
                && System.IO.File.Exists(Path.Combine(dir.FullName, "CLAUDE.md")))
                return dir.FullName;
        }
        return null;
    }

    public static string Render(string markdown, string docPath, string sectionKey)
    {
        var document = Markdown.Parse(markdown, Pipeline);

        // Относительные ссылки на другие MD (`plans/cache-tags-plan.md`, `../CLAUDE.md`) ведут
        // внутрь админки, в тот же раздел. Ссылки на исходники (.cs, .tsx) остаются как есть.
        foreach (var link in document.Descendants<LinkInline>())
        {
            if (link.IsImage || string.IsNullOrEmpty(link.Url)) continue;
            var target = ResolveRelative(docPath, link.Url);
            if (target is null) continue;
            var (path, anchor) = target.Value;
            if (DocsCatalog.IsAllowed(path))
                link.Url = $"/Admin/Docs/{sectionKey}?file={Uri.EscapeDataString(path)}{anchor}";
        }

        using var writer = new StringWriter();
        var renderer = new HtmlRenderer(writer);
        Pipeline.Setup(renderer);
        renderer.Render(document);
        writer.Flush();
        return writer.ToString();
    }

    /// <summary>Путь цели относительной ссылки от корня репозитория; null — ссылка не относительная.</summary>
    private static (string Path, string Anchor)? ResolveRelative(string docPath, string url)
    {
        if (url.StartsWith('#') || url.StartsWith('/') || url.Contains("://")
            || url.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
            return null;

        var hash = url.IndexOf('#');
        var pathPart = hash >= 0 ? url[..hash] : url;
        var anchor = hash >= 0 ? url[hash..] : "";
        if (pathPart.Length == 0) return null;

        var slash = docPath.LastIndexOf('/');
        var segments = slash > 0 ? docPath[..slash].Split('/').ToList() : [];
        foreach (var segment in Uri.UnescapeDataString(pathPart).Replace('\\', '/').Split('/'))
        {
            if (segment is "" or ".") continue;
            if (segment == "..")
            {
                if (segments.Count == 0) return null; // выше корня репозитория — не наше
                segments.RemoveAt(segments.Count - 1);
                continue;
            }
            segments.Add(segment);
        }
        return (string.Join('/', segments), anchor);
    }
}
