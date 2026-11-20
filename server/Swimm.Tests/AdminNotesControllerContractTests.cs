using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Swimm.API.Controllers;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Контракт GET /api/admin/notes и /{key} (docs/admin-pages/admin-notes.md «Контракт»): имена полей
/// JSON — то, на что опирается admin-notes.js. Сериализация — как у контроллеров (camelCase).
/// </summary>
public class AdminNotesControllerContractTests
{
    private sealed class Stub : IAdminNotes
    {
        private static readonly AdminNoteInfo Info = new("job.loglig-batch", "Почему выключена", new DateOnly(2026, 11, 20), "docs/admin-notes/job.loglig-batch.md");
        public IReadOnlyList<AdminNoteInfo> List() => [Info];
        public AdminNoteView? Get(string key) =>
            key == Info.Key ? new AdminNoteView(Info.Key, Info.Title, Info.Checked, Info.Path, "<p>x</p>") : null;
    }

    private static JsonElement Json(IActionResult result) =>
        JsonSerializer.SerializeToElement(Assert.IsType<OkObjectResult>(result).Value, new JsonSerializerOptions(JsonSerializerDefaults.Web));

    [Fact]
    public void List_contract()
    {
        var item = Json(new AdminNotesController(new Stub()).List())[0];
        Assert.Equal("job.loglig-batch", item.GetProperty("key").GetString());
        Assert.Equal("Почему выключена", item.GetProperty("title").GetString());
        Assert.Equal("2026-11-20", item.GetProperty("checked").GetString());
        Assert.Equal("docs/admin-notes/job.loglig-batch.md", item.GetProperty("path").GetString());
    }

    [Fact]
    public void Get_contract_and_404()
    {
        var c = new AdminNotesController(new Stub());
        var json = Json(c.Get("job.loglig-batch"));
        foreach (var n in new[] { "key", "title", "checked", "path", "html" })
            Assert.True(json.TryGetProperty(n, out _), $"нет поля «{n}»");
        Assert.IsType<NotFoundResult>(c.Get("../secret"));
    }
}
