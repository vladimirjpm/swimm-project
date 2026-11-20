using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Swimm.Application.Abstractions;

namespace Swimm.API.Controllers;

/// <summary>
/// Справки админки (docs/admin-pages/admin-notes.md): список для значков «ⓘ» и одна справка для
/// попапа. Скрипт <c>wwwroot/js/admin/admin-notes.js</c> грузит список один раз на страницу.
/// </summary>
[ApiController]
[Route("api/admin/notes")]
[Authorize(Roles = "Admin")]
[AutoValidateAntiforgeryToken]
public class AdminNotesController(IAdminNotes notes) : ControllerBase
{
    [HttpGet]
    public IActionResult List() => Ok(notes.List());

    [HttpGet("{key}")]
    public IActionResult Get(string key) => notes.Get(key) is { } note ? Ok(note) : NotFound();
}
