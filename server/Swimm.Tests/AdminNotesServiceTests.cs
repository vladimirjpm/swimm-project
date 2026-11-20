using Swimm.API.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Справки админки с диска (docs/admin-pages/admin-notes.md): список ключей, рендер, защита ключа
/// от выхода из папки, правка файла видна без перезапуска.
/// </summary>
public sealed class AdminNotesServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "swimm-admin-notes-" + Guid.NewGuid().ToString("N"));
    private readonly string _dir;

    public AdminNotesServiceTests()
    {
        _dir = Path.Combine(_root, "docs", "admin-notes");
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_root, "docs", "secret.md"), "# Secret\nне должно открыться");
        File.WriteAllText(Path.Combine(_root, "secret.md"), "# Root secret");
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private void Write(string name, string text) => File.WriteAllText(Path.Combine(_dir, name), text);

    [Fact]
    public void Lists_notes_with_title_and_date_skipping_readme_and_bad_names()
    {
        Write("README.md", "# Про папку");
        Write("job.loglig-batch.md", "# Пакетная привязка\n\nПроверено: 20.11.2026\n\nтекст");
        Write("page.db.md", "без заголовка");
        Write("Job.Upper.md", "# Не по форме");
        Write("notes.txt", "# не md");

        var list = new AdminNotesService(_dir).List();

        Assert.Equal(["job.loglig-batch", "page.db"], list.Select(n => n.Key));
        var job = list[0];
        Assert.Equal("Пакетная привязка", job.Title);
        Assert.Equal(new DateOnly(2026, 11, 20), job.Checked);
        Assert.Equal("docs/admin-notes/job.loglig-batch.md", job.Path);
        Assert.Equal("page.db", list[1].Title);
        Assert.Null(list[1].Checked);
    }

    [Fact]
    public void Get_renders_body_without_title_and_raw_html()
    {
        Write("job.x.md", "# Заголовок\nПроверено: 01.11.2026\n\n| a | b |\n|---|---|\n| 1 | 2 |\n\n- пункт\n\n<script>alert(1)</script>\n\n[план](../loglig-id-plan.md)");

        var note = new AdminNotesService(_dir).Get("job.x");

        Assert.NotNull(note);
        Assert.Equal("Заголовок", note.Title);
        Assert.Contains("<table>", note.Html);
        Assert.Contains("<li>пункт</li>", note.Html);
        Assert.DoesNotContain("<script>", note.Html);
        Assert.DoesNotContain("<h1", note.Html);
        Assert.DoesNotContain("Проверено", note.Html);
        // Ссылка на соседний MD ведёт внутрь /Admin/Docs, раздел справок.
        Assert.Contains("/Admin/Docs/notes?file=docs%2Floglig-id-plan.md", note.Html);
    }

    [Theory]
    [InlineData("../secret")]
    [InlineData("..\\secret")]
    [InlineData("../../secret")]
    [InlineData("README")]
    [InlineData("readme")]
    [InlineData("job.x/../../secret")]
    [InlineData("")]
    [InlineData("nope")]
    public void Get_refuses_anything_not_in_the_folder(string key)
    {
        Write("README.md", "# Про папку");
        Write("job.x.md", "# X");
        Assert.Null(new AdminNotesService(_dir).Get(key));
    }

    [Fact]
    public void Edit_is_visible_without_restart()
    {
        Write("job.x.md", "# Было\nтекст");
        var svc = new AdminNotesService(_dir);
        Assert.Equal("Было", svc.Get("job.x")!.Title);

        var path = Path.Combine(_dir, "job.x.md");
        File.WriteAllText(path, "# Стало\nдругой текст подлиннее");
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(1));

        Assert.Equal("Стало", svc.Get("job.x")!.Title);
        Assert.Equal("Стало", svc.List().Single().Title);
    }

    [Fact]
    public void New_and_deleted_files_show_up_in_the_list()
    {
        var svc = new AdminNotesService(_dir);
        Assert.Empty(svc.List());
        Write("job.a.md", "# A");
        Assert.Single(svc.List());
        File.Delete(Path.Combine(_dir, "job.a.md"));
        Assert.Empty(svc.List());
        Assert.Null(svc.Get("job.a"));
    }

    [Fact]
    public void No_folder_means_no_notes()
    {
        var svc = new AdminNotesService(Path.Combine(_root, "nope"));
        Assert.Empty(svc.List());
        Assert.Null(svc.Get("job.x"));
        Assert.Empty(new AdminNotesService((string?)null).List());
    }

    /// <summary>Настоящая папка репозитория: первая справка разбирается и рисует таблицу.</summary>
    [Fact]
    public void Repository_note_loglig_batch_is_well_formed()
    {
        string? repo = null;
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
            if (Directory.Exists(Path.Combine(d.FullName, "docs", "admin-notes"))) { repo = d.FullName; break; }
        Assert.NotNull(repo);

        var svc = new AdminNotesService(Path.Combine(repo, "docs", "admin-notes"));
        var note = svc.Get("job.loglig-batch");
        Assert.NotNull(note);
        Assert.Equal("Пакетная привязка loglig-id: почему выключена", note.Title);
        Assert.NotNull(note.Checked);
        Assert.Contains("<table>", note.Html);
        // Каждая справка папки — с заголовком и датой «Проверено» (формат README папки).
        Assert.All(svc.List(), n => Assert.NotNull(n.Checked));
    }
}
