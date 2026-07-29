using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Application.Validation;

namespace Swimm.API.Pages.Admin.PointsRules;

/// <summary>
/// Создание/правка/удаление правила начисления очков (Э3). Одна форма на оба вида правил:
/// общие поля сверху, специфичные — в блоке своего вида. Мутации пишутся в аудит (7.4).
///
/// Шкала «место → очки» вводится текстом (<see cref="PointRuleScaleText"/>): кликать
/// 24 поля руками — основной сценарий заведения правила, и он должен быть быстрым.
/// </summary>
[Authorize(Roles = "Admin")]
public class EditModel : PageModel
{
    private readonly IPointRulesAdminRepository _repo;
    private readonly IAdminAuditService _audit;

    public EditModel(IPointRulesAdminRepository repo, IAdminAuditService audit)
    {
        _repo = repo;
        _audit = audit;
    }

    [BindProperty(SupportsGet = true)]
    public string Kind { get; set; } = "clubs";

    [BindProperty(SupportsGet = true)]
    public int? Id { get; set; }

    /// <summary>Id правила-источника: форма открывается заполненной, но сохраняется как новое.</summary>
    [BindProperty(SupportsGet = true)]
    public int? CloneFrom { get; set; }

    public PointRuleKind RuleKind => PointRulesKindParser.Parse(Kind);
    public bool IsClubs => RuleKind == PointRuleKind.Clubs;
    public bool IsNew => Id is null or 0;

    [BindProperty]
    public RuleForm Input { get; set; } = new();

    public PointRuleEditDto? Existing { get; private set; }

    public string? Error { get; private set; }

    /// <summary>Поля формы. Числовые — nullable, чтобы пустое поле не превращалось молча в 0.</summary>
    public class RuleForm
    {
        public string Version { get; set; } = "";
        public DateOnly EffectiveFrom { get; set; }
        public string? Description { get; set; }
        public string Scope { get; set; } = "all";
        public int DefaultPoints { get; set; }
        public int? MaxScoringPlace { get; set; }
        public bool ManualOnly { get; set; }

        public int RelayMultiplier { get; set; } = 2;

        public string PointsSource { get; set; } = "placement";
        public int? CountBestSwims { get; set; }
        public string GroupBy { get; set; } = "age";
        public bool SplitByGender { get; set; } = true;
        public bool IncludeRelays { get; set; }
        public int? MinSwims { get; set; }
        public int? RecordPoints { get; set; }
        public int? RecordTiePoints { get; set; }
        public bool FinalsOnly { get; set; }

        /// <summary>Шкала текстом — см. <see cref="PointRuleScaleText"/>.</summary>
        public string Scale { get; set; } = "";
    }

    public async Task<IActionResult> OnGetAsync()
    {
        Kind = PointRulesKindParser.ToSlug(RuleKind);

        var sourceId = IsNew ? CloneFrom : Id;
        if (sourceId is int src)
        {
            var dto = await _repo.GetByIdAsync(RuleKind, src);
            if (dto == null) return RedirectToPage("Index", new { kind = Kind });

            if (IsNew)
            {
                // Клон: версия обязана быть уникальной — подсказываем свободный вариант,
                // а привязок у нового правила, разумеется, нет.
                Input = ToForm(dto);
                Input.Version = dto.Version + "-copy";
            }
            else
            {
                Existing = dto;
                Input = ToForm(dto);
            }
        }
        else
        {
            Input.EffectiveFrom = DateOnly.FromDateTime(DateTime.UtcNow);
        }

        return Page();
    }

    public async Task<IActionResult> OnPostSaveAsync()
    {
        Kind = PointRulesKindParser.ToSlug(RuleKind);

        if (!PointRuleScaleText.TryParse(Input.Scale, out var entries, out var parseError))
        {
            Error = parseError;
            if (!IsNew) Existing = await _repo.GetByIdAsync(RuleKind, Id!.Value);
            return Page();
        }

        var input = new PointRuleInputDto
        {
            Version = Input.Version ?? "",
            EffectiveFrom = Input.EffectiveFrom,
            Description = Input.Description,
            Scope = Input.Scope,
            DefaultPoints = Input.DefaultPoints,
            MaxScoringPlace = Input.MaxScoringPlace,
            ManualOnly = Input.ManualOnly,
            RelayMultiplier = Input.RelayMultiplier,
            PointsSource = Input.PointsSource,
            CountBestSwims = Input.CountBestSwims,
            GroupBy = Input.GroupBy,
            SplitByGender = Input.SplitByGender,
            IncludeRelays = Input.IncludeRelays,
            MinSwims = Input.MinSwims,
            RecordPoints = Input.RecordPoints,
            RecordTiePoints = Input.RecordTiePoints,
            FinalsOnly = Input.FinalsOnly,
            Entries = entries
        };

        var result = IsNew
            ? await _repo.CreateAsync(RuleKind, input)
            : await _repo.UpdateAsync(RuleKind, Id!.Value, input);

        if (!result.Success)
        {
            Error = result.Error;
            if (!IsNew) Existing = await _repo.GetByIdAsync(RuleKind, Id!.Value);
            return Page();
        }

        var entity = IsClubs ? "PointRuleClubs" : "PointRuleSwimmers";
        await _audit.LogAsync(IsNew ? "pointrule.create" : "pointrule.update", entity,
            result.Id.ToString(),
            $"{(IsNew ? "Создано" : "Изменено")} правило {PointRulesKindParser.Title(RuleKind)} «{input.Version}» ({entries.Count} мест в шкале)");

        TempData["Flash"] = IsNew ? "Правило создано" : "Изменения сохранены";
        return RedirectToPage("Edit", new { kind = Kind, id = result.Id });
    }

    public async Task<IActionResult> OnPostDeleteAsync()
    {
        Kind = PointRulesKindParser.ToSlug(RuleKind);
        if (IsNew) return RedirectToPage("Index", new { kind = Kind });

        var version = (await _repo.GetByIdAsync(RuleKind, Id!.Value))?.Version;
        var result = await _repo.DeleteAsync(RuleKind, Id!.Value);
        if (!result.Success)
        {
            Error = result.Error;
            Existing = await _repo.GetByIdAsync(RuleKind, Id!.Value);
            if (Existing != null) Input = ToForm(Existing);
            return Page();
        }

        await _audit.LogAsync("pointrule.delete", IsClubs ? "PointRuleClubs" : "PointRuleSwimmers",
            Id!.Value.ToString(),
            $"Удалено правило {PointRulesKindParser.Title(RuleKind)} «{version}»");

        TempData["Flash"] = "Правило удалено";
        return RedirectToPage("Index", new { kind = Kind });
    }

    private static RuleForm ToForm(PointRuleEditDto dto) => new()
    {
        Version = dto.Version,
        EffectiveFrom = dto.EffectiveFrom,
        Description = dto.Description,
        Scope = dto.Scope,
        DefaultPoints = dto.DefaultPoints,
        MaxScoringPlace = dto.MaxScoringPlace,
        ManualOnly = dto.ManualOnly,
        RelayMultiplier = dto.RelayMultiplier,
        PointsSource = dto.PointsSource,
        CountBestSwims = dto.CountBestSwims,
        GroupBy = dto.GroupBy,
        SplitByGender = dto.SplitByGender,
        IncludeRelays = dto.IncludeRelays,
        MinSwims = dto.MinSwims,
        RecordPoints = dto.RecordPoints,
        RecordTiePoints = dto.RecordTiePoints,
        FinalsOnly = dto.FinalsOnly,
        Scale = PointRuleScaleText.Format(dto.Entries)
    };
}
