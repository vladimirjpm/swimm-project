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
/// Модель <c>_CompChecklist.cshtml</c> — правая панель-чек-лист строки «ждёт затягивания»
/// (вариант E, этап 4, хэндофф 3.5). Html-строки шапки собирает Index.cshtml своими же
/// помощниками (те же бейджи, что у прежней панели строки).
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
    IReadOnlyList<CompChecklistStep> Steps,
    // Статусы шагов на каждую фазу хода: {"none":["done",…], "parsing":[…], …}.
    string PhasesJson,
    // Подпись «auto: …» (results-auto) — под шагом 5, если есть.
    string? AutoReason,
    bool Selected)
{
    public UnifiedSiteInfo Site => Row.Site!;

    public CompChecklistStep Step(int no) => Steps[no - 1];
}
