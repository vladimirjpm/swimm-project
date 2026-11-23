using Microsoft.AspNetCore.Html;
using Swimm.Application.Dtos;
using Swimm.Application.Mapping;

namespace Swimm.API.Pages.Admin.Competitions;

/// <summary>
/// Шаг чек-листа правой панели: номер, название, подпись по умолчанию (её меняет ход затягивания
/// в JS) и статус кружка по данным строки (<see cref="CompetitionChecklistRules"/>).
/// </summary>
public sealed record CompChecklistStep(int No, string Title, string Caption, ChecklistStepStatus Status,
    string? CaptionTitle = null, bool CaptionDanger = false);

/// <summary>
/// Шаги 5–9 одной «области»: у одиночной строки она одна, у многодневного события — по дню
/// (хэндофф 3.5: «шаги 1–4 общие, шаги 5–9 — у выбранного дня»).
/// </summary>
/// <param name="Day">День события; null — одиночная строка (или строка вне базы).</param>
/// <param name="Label">Подпись дня в строке дней («День 1 · 28.07»); у одиночной не нужна.</param>
/// <param name="Steps">Шаги 5–9 (индекс 0 — шаг 5).</param>
/// <param name="PhasesJson">Статусы всех девяти шагов на каждую фазу хода — для <c>data-phases</c> области.</param>
/// <param name="Incomplete">Подпись «!» шага 5 у затянутого: Р114 (текст отметки) или Р115 («затянуто N из M дней»).</param>
/// <param name="ResultCount">Строк в базе у области (дня или одиночного).</param>
public sealed record CompChecklistScope(
    CompetitionListItemDto? Day,
    string? Label,
    IReadOnlyList<CompChecklistStep> Steps,
    string PhasesJson,
    string? Incomplete,
    int ResultCount,
    DateTime? QualityScannedAt,
    int SuspectCount)
{
    public int? DayNo => Day?.DayNumber;

    public CompChecklistStep Step(int no) => Steps[no - 5];
}

/// <summary>
/// Модель <c>_CompQuickEdit.cshtml</c> — быстрая правка строки базы (шаг 6 чек-листа): поля, общие
/// для всех дней события, «Сохранить» (handler QuickSave с параметрами списка и sel своей строки)
/// и «Проверить регламент».
/// </summary>
public sealed record CompQuickEditView(
    CompetitionRowDto Db,
    CompetitionListItemDto Head,
    int? SiteOrgCompId,
    Dictionary<string, string> Route,
    IReadOnlyList<CategoryTagDto> AllCategories,
    IReadOnlyList<string> PoolTypes,
    IReadOnlyList<PointRuleRowDto> ClubRules,
    IReadOnlyList<PointRuleRowDto> SwimmerRules);

/// <summary>
/// Модель <c>_CompChecklist.cshtml</c> — правая панель-чек-лист строки любого состояния (вариант E,
/// этапы 4 и 6, хэндофф 3.5). Html-строки шапки собирает Index.cshtml своими же помощниками
/// (те же бейджи, что у прежней панели строки); форма быстрой правки — отдельный partial.
/// </summary>
public sealed record CompChecklistView(
    UnifiedCompetitionRowDto Row,
    string Key,
    string Name,
    string PanelDate,
    string StateText,
    string StateChipCls,
    string TrophyHtml,
    string HeaderChipsHtml,
    string MetaHtml,
    // Шаги 1–4 — общие для всей строки.
    IReadOnlyList<CompChecklistStep> HeadSteps,
    // Статусы шагов 1–4 на каждую фазу: {"none":[…9…], "parsing":[…], …} — data-phases панели.
    string PhasesJson,
    IReadOnlyList<CompChecklistScope> Scopes,
    // Подпись «auto: …» (results-auto) — под шагом 5, если есть.
    string? AutoReason,
    bool Selected,
    // Edit «головы» строки (одиночное или первый день события) с back= и sel своей строки.
    string? EditUrl,
    // Edit каждого дня события (по Id дня).
    IReadOnlyDictionary<int, string> DayEditUrls,
    // Публичная страница каждого дня события (swimhub ↗).
    IReadOnlyDictionary<int, string> DayPublicUrls,
    // Форма быстрой правки (шаг 6 у строк в базе) и «Проверить регламент».
    IHtmlContent? QuickEdit,
    // Категории строки (чипы) — в шапке, как в прежней панели.
    IHtmlContent? Categories)
{
    public UnifiedSiteInfo? Site => Row.Site;

    public CompetitionRowDto? Db => Row.Db;

    public bool Imported => Row.Db != null;

    public bool IsEvent => Row.Db?.IsEvent == true;

    public CompChecklistStep Step(int no) => HeadSteps[no - 1];
}
