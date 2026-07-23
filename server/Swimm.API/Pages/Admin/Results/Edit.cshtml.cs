using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;

namespace Swimm.API.Pages.Admin.Results;

/// <summary>
/// Ручная правка одного результата (фаза 7.2 B). Security-sensitive: меняет публичные данные,
/// поэтому пишет в аудит (7.4) снимок «до/после» и полагается на инвалидацию кэша в репозитории.
/// </summary>
[Authorize(Roles = "Admin")]
public class EditModel : PageModel
{
    private readonly IResultAdminRepository _repo;
    private readonly IAdminAuditService _audit;

    public EditModel(IResultAdminRepository repo, IAdminAuditService audit)
    {
        _repo = repo;
        _audit = audit;
    }

    [BindProperty(SupportsGet = true)]
    public long Id { get; set; }

    [BindProperty]
    public ResultForm Input { get; set; } = new();

    /// <summary>Контекст результата для показа (соревнование/пловец/клуб/стиль).</summary>
    public ResultEditDto? Existing { get; private set; }

    public string? Error { get; private set; }

    public class ResultForm
    {
        public int SwimmerId { get; set; }
        public int ClubId { get; set; }
        public string Distance { get; set; } = "";
        public string Gender { get; set; } = "";
        public string AgeGroup { get; set; } = "";
        public string EventStyleAge { get; set; } = "";
        public int? Position { get; set; }
        public int? PositionAgeGroup { get; set; }
        public int Heat { get; set; }
        public int Lane { get; set; }
        public string? TimeText { get; set; }
        public bool TimeFail { get; set; }
        public string? TimeFailNote { get; set; }
        public int InternationalPoints { get; set; }
        public string? Note { get; set; }
    }

    public async Task<IActionResult> OnGetAsync()
    {
        Existing = await _repo.GetByIdAsync(Id);
        if (Existing == null)
        {
            TempData["Flash"] = $"Результат #{Id} не найден или это эстафетная строка";
            return RedirectToPage("Index");
        }
        Input = ToForm(Existing);
        return Page();
    }

    public async Task<IActionResult> OnPostSaveAsync()
    {
        // Снимок «до» для аудита (и чтобы вернуть контекст в форму при ошибке).
        var before = await _repo.GetByIdAsync(Id);
        if (before == null)
        {
            TempData["Flash"] = $"Результат #{Id} не найден или это эстафетная строка";
            return RedirectToPage("Index");
        }

        var result = await _repo.UpdateAsync(Id, ToInput(Input));
        if (!result.Success)
        {
            Error = result.Error;
            Existing = before;
            return Page();
        }

        var after = await _repo.GetByIdAsync(Id);
        await _audit.LogAsync("result.edit", "Result", Id.ToString(),
            $"Правка результата #{Id} ({before.SwimmerName}, {before.CompetitionName})",
            new { before, after });

        TempData["Flash"] = "Результат обновлён";
        return RedirectToPage("Edit", new { id = Id });
    }

    private static ResultForm ToForm(ResultEditDto d) => new()
    {
        SwimmerId = d.SwimmerId,
        ClubId = d.ClubId,
        Distance = d.Distance,
        Gender = d.Gender,
        AgeGroup = d.AgeGroup,
        EventStyleAge = d.EventStyleAge,
        Position = d.Position,
        PositionAgeGroup = d.PositionAgeGroup,
        Heat = d.Heat,
        Lane = d.Lane,
        TimeText = d.TimeText,
        TimeFail = d.TimeFail,
        TimeFailNote = d.TimeFailNote,
        InternationalPoints = d.InternationalPoints,
        Note = d.Note
    };

    private static ResultEditInputDto ToInput(ResultForm f) => new()
    {
        SwimmerId = f.SwimmerId,
        ClubId = f.ClubId,
        Distance = f.Distance,
        Gender = f.Gender,
        AgeGroup = f.AgeGroup,
        EventStyleAge = f.EventStyleAge,
        Position = f.Position,
        PositionAgeGroup = f.PositionAgeGroup,
        Heat = f.Heat,
        Lane = f.Lane,
        TimeText = f.TimeText,
        TimeFail = f.TimeFail,
        TimeFailNote = f.TimeFailNote,
        InternationalPoints = f.InternationalPoints,
        Note = f.Note
    };
}
