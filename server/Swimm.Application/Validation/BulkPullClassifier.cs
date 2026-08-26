using Swimm.Application.Dtos;

namespace Swimm.Application.Validation;

/// <summary>
/// «Беспроблемная или нет» — единственное место, где это решается (docs/plans/bulk-pull-plan.md §4).
///
/// Чистая функция от уже полученных данных: ни сети, ни БД. Так её можно накрыть тестами по
/// кейсу на каждое правило — а правила здесь ровно те, что в одиночном превью требуют решения
/// ЧЕЛОВЕКА. Если в превью появится новый вопрос к админу, он должен появиться и здесь, иначе
/// пачка молча ответит за него.
/// </summary>
public static class BulkPullClassifier
{
    /// <param name="preview">Итог затягивания; <c>Error</c> — забор/разбор не удался.</param>
    /// <param name="regulation">Забор регламента; null — не проверяли (нет loglig-id).</param>
    /// <param name="isChampionshipByName">Имя выглядит как чемпионат Израиля.</param>
    public static (BulkPullVerdict Verdict, IReadOnlyList<string> Reasons) Classify(
        DiscoveryPreviewResult preview,
        RegulationFetchDto? regulation,
        bool isChampionshipByName)
    {
        var reasons = new List<string>();

        if (preview.Error != null)
        {
            // «Протокола нет» — это факт «тянуть нечего», а не сбой, который стоит повторять.
            var verdict = LooksLikeEmptySource(preview.Error) ? BulkPullVerdict.Empty : BulkPullVerdict.Failed;
            reasons.Add(preview.Error);
            return (verdict, reasons);
        }

        // Рекорды — всегда руками. Порога нет намеренно: настоящий рекорд редок, а пачка
        // не тот режим, в котором его стоит проглядеть (решение Влада 2026-08-23).
        var records = preview.RecordPreview?.Count ?? 0;
        if (records > 0)
            reasons.Add($"бьёт рекордов: {records} — нужна ручная проверка");

        if (preview.ExistingCompetitionId is int existing)
            reasons.Add($"соревнование уже в БД (#{existing}) — нужно решение о перезаписи");

        var days = preview.Parsed?.Competitions.Count ?? 0;
        if (days > 1)
            reasons.Add($"дней в файле: {days} — нужно решение по событию");

        // Зачёт есть, а правила под его шкалу нет: автоподбор по дате даст ЧУЖУЮ шкалу
        // (так зимний чемпионат 2025 получил не своё правило).
        var standing = preview.ClubStanding;
        if (standing is { HasStanding: true, MatchedRuleId: null })
            reasons.Add("официальный клубный зачёт есть, а правила под его шкалу нет");

        var warnings = preview.Parsed?.Warnings ?? [];
        foreach (var w in warnings.Take(3))
            reasons.Add($"предупреждение парсера: {w}");

        if (reasons.Count > 0)
            return (BulkPullVerdict.NeedsReview, reasons);

        // Чемпионат в пачку попадает только по явной галочке «включая чемпионаты», но
        // отметить его стоит: у чемпионатов медали и зачёт решаются штучно.
        if (isChampionshipByName || regulation?.Analysis?.IsChampionship == true)
            reasons.Add("чемпионат Израиля");

        if (regulation is null || !regulation.Found)
        {
            reasons.Add(regulation?.Error ?? "регламент (תקנון) не найден — медали и зачёт не проставлены");
            return (BulkPullVerdict.NoRegulation, reasons);
        }

        return (BulkPullVerdict.Clean, reasons);
    }

    /// <summary>
    /// Ровно те же формулировки, по которым одиночное превью ставит «∅ нет протокола».
    /// Списком, а не подстрокой «not found»: иначе сетевые сбои считались бы пустым файлом.
    /// </summary>
    private static bool LooksLikeEmptySource(string message) =>
        message.Contains("No competitions found", StringComparison.OrdinalIgnoreCase)
        || message.Contains("0 lines extracted", StringComparison.OrdinalIgnoreCase)
        || message.Contains("не распознал ни одного результата", StringComparison.OrdinalIgnoreCase);
}
