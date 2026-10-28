using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Application.Validation;

namespace Swimm.API.Pages.Admin.Series;

/// <summary>
/// Серия турнира целиком (docs/admin-pages/series.md): имя и ключевые фразы, части, версии
/// регламента с наградами, выпуски (дни, привязанные к частям) и отметка «регламент подтверждён».
///
/// Каждая форма — свой handler, после POST — редирект на GET (PRG): ошибка уходит в TempData.
/// Полосы и призы вводятся текстом (<see cref="SeriesAwardText"/>), как шкала у правил очков.
/// Мутации пишутся в аудит.
/// </summary>
[Authorize(Roles = "Admin")]
public class EditModel(ISeriesAdminRepository repo, IAdminAuditService audit) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public int Id { get; set; }

    public SeriesEditDto? Series { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        Series = await repo.GetByIdAsync(Id);
        return Series is null ? RedirectToPage("Index") : Page();
    }

    // ── Серия ─────────────────────────────────────────────────────────────────

    public Task<IActionResult> OnPostSaveSeriesAsync(string name, string? nameEn, string? matchPhrases) =>
        Done(repo.UpdateSeriesAsync(Id, new SeriesInputDto(name ?? "", nameEn, matchPhrases)),
            "series.update", "CompetitionSeries", $"Изменена серия «{name?.Trim()}» (фразы: {matchPhrases})");

    public async Task<IActionResult> OnPostDeleteSeriesAsync()
    {
        var name = (await repo.GetByIdAsync(Id))?.Name;
        var result = await repo.DeleteSeriesAsync(Id);
        if (!result.Success)
        {
            TempData["Error"] = result.Error;
            return RedirectToPage("Edit", new { id = Id });
        }
        await audit.LogAsync("series.delete", "CompetitionSeries", Id.ToString(), $"Удалена серия «{name}»");
        TempData["Flash"] = "Серия удалена";
        return RedirectToPage("Index");
    }

    // ── Части ─────────────────────────────────────────────────────────────────

    public Task<IActionResult> OnPostAddPartAsync(string name, string? nameEn, int sortOrder, int? minAge, int? maxAge) =>
        Done(repo.AddPartAsync(Id, new SeriesPartInputDto(name ?? "", nameEn, sortOrder, minAge, maxAge)),
            "series.part.create", "CompetitionSeriesPart", $"Добавлена часть «{name?.Trim()}»");

    public Task<IActionResult> OnPostSavePartAsync(int partId, string name, string? nameEn, int sortOrder, int? minAge, int? maxAge) =>
        Done(repo.UpdatePartAsync(partId, new SeriesPartInputDto(name ?? "", nameEn, sortOrder, minAge, maxAge)),
            "series.part.update", "CompetitionSeriesPart", $"Изменена часть «{name?.Trim()}»");

    public Task<IActionResult> OnPostDeletePartAsync(int partId) =>
        Done(repo.DeletePartAsync(partId), "series.part.delete", "CompetitionSeriesPart", $"Удалена часть #{partId}");

    // ── Регламенты ────────────────────────────────────────────────────────────

    public Task<IActionResult> OnPostAddRegulationAsync(DateOnly effectiveFrom, string? sourceUrl, string? description) =>
        Done(repo.AddRegulationAsync(Id, new SeriesRegulationInputDto(effectiveFrom, sourceUrl, description)),
            "series.regulation.create", "SeriesRegulation", $"Добавлена версия регламента с {effectiveFrom:dd.MM.yyyy}");

    public Task<IActionResult> OnPostSaveRegulationAsync(int regulationId, DateOnly effectiveFrom, string? sourceUrl, string? description) =>
        Done(repo.UpdateRegulationAsync(regulationId, new SeriesRegulationInputDto(effectiveFrom, sourceUrl, description)),
            "series.regulation.update", "SeriesRegulation", $"Изменена версия регламента с {effectiveFrom:dd.MM.yyyy}");

    public Task<IActionResult> OnPostCloneRegulationAsync(int regulationId, DateOnly effectiveFrom, string? sourceUrl) =>
        Done(repo.CloneRegulationAsync(regulationId, new SeriesRegulationInputDto(effectiveFrom, sourceUrl, null)),
            "series.regulation.clone", "SeriesRegulation",
            $"Регламент #{regulationId} склонирован в версию с {effectiveFrom:dd.MM.yyyy}");

    public Task<IActionResult> OnPostDeleteRegulationAsync(int regulationId) =>
        Done(repo.DeleteRegulationAsync(regulationId), "series.regulation.delete", "SeriesRegulation",
            $"Удалена версия регламента #{regulationId}");

    // ── Награды ───────────────────────────────────────────────────────────────

    public Task<IActionResult> OnPostAddAwardAsync(int regulationId, AwardForm award) =>
        SaveAward(award, input => repo.AddAwardAsync(regulationId, input), "series.award.create");

    public Task<IActionResult> OnPostSaveAwardAsync(int awardId, AwardForm award) =>
        SaveAward(award, input => repo.UpdateAwardAsync(awardId, input), "series.award.update");

    public Task<IActionResult> OnPostDeleteAwardAsync(int awardId) =>
        Done(repo.DeleteAwardAsync(awardId), "series.award.delete", "SeriesAward", $"Удалена награда #{awardId}");

    /// <summary>Поля формы награды. Числовые — nullable: пустое поле не превращается молча в 0.</summary>
    public class AwardForm
    {
        public int PartId { get; set; }
        public string Title { get; set; } = "";
        public int? CountBest { get; set; }
        public bool SplitByGender { get; set; }
        public int Places { get; set; } = 1;
        public int SortOrder { get; set; }
        public string? Bands { get; set; }
        public string? Prizes { get; set; }
    }

    private async Task<IActionResult> SaveAward(AwardForm f, Func<SeriesAwardInputDto, Task<SeriesSaveResult>> save, string action)
    {
        if (!SeriesAwardText.TryParseBands(f.Bands, out var bands, out var error)
            || !SeriesAwardText.TryParsePrizes(f.Prizes, out var prizes, out error))
        {
            TempData["Error"] = error;
            return RedirectToPage("Edit", new { id = Id });
        }

        var input = new SeriesAwardInputDto(f.PartId, f.Title ?? "", f.CountBest, f.SplitByGender, f.Places,
            f.SortOrder, bands, prizes);
        return await Done(save(input), action, "SeriesAward",
            $"Награда «{f.Title?.Trim()}»: полосы {SeriesAwardText.FormatBands(bands)}; призов {prizes.Count}");
    }

    // ── Выпуски ───────────────────────────────────────────────────────────────

    public Task<IActionResult> OnPostBindDayAsync(int competitionId, int partId) =>
        Done(repo.SetCompetitionPartAsync(competitionId, partId), "series.day.bind", "Competition",
            $"День #{competitionId} привязан к части #{partId} серии #{Id}", entityId: competitionId);

    public Task<IActionResult> OnPostUnbindDayAsync(int competitionId) =>
        Done(repo.SetCompetitionPartAsync(competitionId, null), "series.day.unbind", "Competition",
            $"День #{competitionId} отвязан от серии #{Id}", entityId: competitionId);

    public async Task<IActionResult> OnPostToggleCheckedAsync(int competitionId)
    {
        var result = await repo.ToggleRegulationCheckedAsync(competitionId, User.Identity?.Name);
        if (!result.Success)
        {
            TempData["Error"] = result.Error;
            return RedirectToPage("Edit", new { id = Id });
        }
        var on = result.Id == 1;
        await audit.LogAsync(on ? "series.regulation.checked" : "series.regulation.unchecked", "Competition",
            competitionId.ToString(), on ? "Регламент выпуска подтверждён" : "Отметка «регламент подтверждён» снята");
        TempData["Flash"] = on ? "Регламент выпуска подтверждён" : "Отметка снята";
        return RedirectToPage("Edit", new { id = Id });
    }

    /// <summary>Общий хвост мутации: ошибка → TempData, успех → аудит и флеш, затем PRG.</summary>
    private async Task<IActionResult> Done(Task<SeriesSaveResult> mutation, string action, string entity,
        string summary, int? entityId = null)
    {
        var result = await mutation;
        if (!result.Success)
        {
            TempData["Error"] = result.Error;
        }
        else
        {
            await audit.LogAsync(action, entity, (entityId ?? result.Id).ToString(), summary);
            TempData["Flash"] = "Сохранено";
        }
        return RedirectToPage("Edit", new { id = Id });
    }
}
