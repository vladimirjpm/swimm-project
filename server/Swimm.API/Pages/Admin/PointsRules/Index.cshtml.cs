using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Application.Validation;
using Swimm.Domain.Entities;

namespace Swimm.API.Pages.Admin.PointsRules;

/// <summary>
/// Список правил начисления очков (Э3): два таба — клубные очки и High Point.
/// CRUD — на странице Edit.
/// </summary>
[Authorize(Roles = "Admin")]
public class IndexModel : PageModel
{
    private readonly IPointRulesAdminRepository _repo;
    private readonly IAdminAuditService _audit;

    public IndexModel(IPointRulesAdminRepository repo, IAdminAuditService audit)
    {
        _repo = repo;
        _audit = audit;
    }

    /// <summary>"clubs" | "swimmers" — активный таб (по умолчанию клубный).</summary>
    [BindProperty(SupportsGet = true)]
    public string Kind { get; set; } = "clubs";

    public PointRuleKind RuleKind => PointRulesKindParser.Parse(Kind);

    public IReadOnlyList<PointRuleRowDto> Rules { get; private set; } = [];

    /// <summary>Соревнования каждого правила (Id правила → строки панели). Рендерятся сразу,
    /// панель просто скрыта: правил единицы, отдельный AJAX-эндпоинт того не стоит.</summary>
    public IReadOnlyDictionary<int, IReadOnlyList<PointRuleCompetitionRowDto>> Competitions
    { get; private set; } = new Dictionary<int, IReadOnlyList<PointRuleCompetitionRowDto>>();

    public async Task OnGetAsync()
    {
        Kind = PointRulesKindParser.ToSlug(RuleKind);
        Rules = await _repo.GetAllAsync(RuleKind);

        var byRule = new Dictionary<int, IReadOnlyList<PointRuleCompetitionRowDto>>();
        foreach (var rule in Rules.Where(r => r.CompetitionCount > 0))
            byRule[rule.Id] = await _repo.GetCompetitionsAsync(RuleKind, rule.Id);
        Competitions = byRule;
    }

    /// <summary>Строка формы перепривязки: соревнование → выбранное правило (пусто — снять привязку).</summary>
    public class ReassignRow
    {
        public int CompetitionId { get; set; }
        public int? RuleId { get; set; }
    }

    /// <summary>
    /// Кнопки проверки в строке панели: «Проверено вручную» (сверено с официальным протоколом)
    /// и «Принято как верное» (официальных очков нет). Состояния взаимоисключающие; повторный
    /// клик по текущему снимает отметку. Ставится всем дням события, на расчёт не влияет.
    /// </summary>
    public async Task<IActionResult> OnPostToggleVerifiedAsync(
        int ruleId, int verifyCompetitionId, string verifiedKind)
    {
        var kindSlug = PointRulesKindParser.ToSlug(RuleKind);
        var result = await _repo.ToggleVerifiedAsync(
            RuleKind, verifyCompetitionId, verifiedKind, User.Identity?.Name);

        if (!result.Success)
        {
            TempData["Flash"] = $"Не сохранено: {result.Error}";
            return RedirectToPage("Index", new { kind = kindSlug });
        }

        var nowVerified = result.Id == 1;
        var label = verifiedKind switch
        {
            PointsVerifiedKinds.Accepted => "принято как верное (официальных очков нет)",
            PointsVerifiedKinds.Mismatch => "расходится с официальными (ошибка у организатора, верны наши)",
            _ => "сверено с официальным протоколом"
        };

        await _audit.LogAsync("pointrule.verify", "Competition", verifyCompetitionId.ToString(),
            $"Ручная проверка очков ({kindSlug}, правило #{ruleId}) у соревнования #{verifyCompetitionId}: " +
            (nowVerified ? label : "отметка снята"));

        TempData["Flash"] = nowVerified
            ? verifiedKind switch
            {
                PointsVerifiedKinds.Accepted => "Принято как верное",
                PointsVerifiedKinds.Mismatch => "Отмечено расхождение с официальными",
                _ => "Отмечено как проверенное"
            }
            : "Отметка снята";
        return RedirectToPage("Index", new { kind = kindSlug });
    }

    /// <summary>
    /// Пояснение к бейджу «★ расхождение»: чем именно официальные очки неверны. Уезжает на
    /// публичную страницу (попап «Points system»), где читатель сам переключает язык — поэтому
    /// принимаем все три сразу. Табличка расхождения вводится строкой «21:5>6, 22:3>5»,
    /// где после черт идёт контекст протокола «заплыв | время | пловец | клуб» — в том же
    /// порядке, что колонки таблицы, — а подпись говорит, какой заплыв разобран.
    /// Пустой ввод стирает пояснение целиком.
    /// </summary>
    public async Task<IActionResult> OnPostSaveMismatchNoteAsync(
        int noteCompetitionId, string? noteEn, string? noteRu, string? noteHe,
        string? scaleDiff, string? sourceUrl, string? scaleDiffCaption)
    {
        var kindSlug = PointRulesKindParser.ToSlug(RuleKind);

        if (!ScaleDiffText.TryParse(scaleDiff, out var diff, out var parseError))
        {
            TempData["Flash"] = $"Не сохранено: {parseError}";
            return RedirectToPage("Index", new { kind = kindSlug });
        }

        var result = await _repo.SetClubMismatchNoteAsync(noteCompetitionId, new CompetitionNoteInputDto
        {
            Texts = new Dictionary<string, string?>
            {
                [CompetitionNoteLangs.En] = noteEn,
                [CompetitionNoteLangs.Ru] = noteRu,
                [CompetitionNoteLangs.He] = noteHe,
            },
            ScaleDiff = diff,
            SourceUrl = sourceUrl,
            ScaleDiffCaption = scaleDiffCaption
        });

        if (!result.Success)
        {
            TempData["Flash"] = $"Не сохранено: {result.Error}";
            return RedirectToPage("Index", new { kind = kindSlug });
        }

        var saved = result.Id == 1;
        await _audit.LogAsync("pointrule.verify", "Competition", noteCompetitionId.ToString(),
            saved
                ? $"Пояснение к расхождению клубных очков у соревнования #{noteCompetitionId} сохранено " +
                  $"(языков: {new[] { noteEn, noteRu, noteHe }.Count(t => !string.IsNullOrWhiteSpace(t))}, " +
                  $"строк расхождения: {diff.Count})"
                : $"Пояснение к расхождению клубных очков у соревнования #{noteCompetitionId} стёрто");

        TempData["Flash"] = saved ? "Пояснение сохранено" : "Пояснение стёрто";
        return RedirectToPage("Index", new { kind = kindSlug });
    }

    /// <summary>
    /// Сохранение панели «Соревнования»: меняет правило у выбранных соревнований (у многодневных —
    /// всем дням) и возвращает на список, чтобы счётчики в верхней таблице пересчитались.
    /// </summary>
    public async Task<IActionResult> OnPostReassignAsync(int ruleId, List<ReassignRow>? rows)
    {
        var kindSlug = PointRulesKindParser.ToSlug(RuleKind);
        var items = (rows ?? [])
            .Select(r => new PointRuleReassignItem(r.CompetitionId, r.RuleId))
            .ToList();

        var result = await _repo.ReassignCompetitionsAsync(RuleKind, items);

        if (!result.Success)
        {
            TempData["Flash"] = $"Не сохранено: {result.Error}";
            return RedirectToPage("Index", new { kind = kindSlug });
        }

        if (result.Id == 0)
        {
            TempData["Flash"] = "Изменений не было";
            return RedirectToPage("Index", new { kind = kindSlug });
        }

        await _audit.LogAsync("pointrule.reassign",
            RuleKind == PointRuleKind.Clubs ? "PointRuleClubs" : "PointRuleSwimmers",
            ruleId.ToString(),
            $"Перепривязка из панели правила #{ruleId} ({kindSlug}): изменено соревнований: {result.Id}");

        TempData["Flash"] = $"Привязка обновлена: {result.Id} соревнований";
        return RedirectToPage("Index", new { kind = kindSlug });
    }
}

/// <summary>Разбор слага таба (<c>clubs</c>/<c>swimmers</c>) — общий для Index и Edit.</summary>
public static class PointRulesKindParser
{
    public static PointRuleKind Parse(string? slug) =>
        string.Equals(slug, "swimmers", StringComparison.OrdinalIgnoreCase)
            ? PointRuleKind.Swimmers
            : PointRuleKind.Clubs;

    public static string ToSlug(PointRuleKind kind) => kind == PointRuleKind.Swimmers ? "swimmers" : "clubs";

    public static string Title(PointRuleKind kind) => kind == PointRuleKind.Swimmers ? "High Point" : "Клубные очки";
}
