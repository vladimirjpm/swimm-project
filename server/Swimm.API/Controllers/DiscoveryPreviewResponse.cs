using System.Globalization;
using Swimm.Application.Dtos;

namespace Swimm.API.Controllers;

/// <summary>
/// Ответ превью затягивания — общий для «Затянуть» (<see cref="DiscoveryAdminController"/>) и «Затянуть этот
/// день» (<see cref="ProgrammeCheckAdminController"/>, Р124): одна разметка шага 6 на странице. Вынесено из
/// контроллера, чтобы второй контроллер не звал статику первого (ревью PR #284).
/// </summary>
internal static class DiscoveryPreviewResponse
{
    /// <summary>Ответ превью — один на «Затянуть» и «Затянуть этот день» (Р124): одна разметка шага 6.</summary>
    internal static object Build(DiscoveryPreviewResult p)
    {
        return new
        {
            previewId = p.PreviewId,
            format = p.Parsed!.Format,
            resultCount = p.Parsed.ResultCount,
            competitions = p.Parsed.Competitions,
            warnings = p.Parsed.Warnings,
            languages = p.Languages,
            existingCompetitionId = p.ExistingCompetitionId,
            existingCompetitions = p.ExistingCompetitions,
            recordPreview = p.RecordPreview,
            officialClubStanding = StandingResponse(p.ClubStanding, p.Parsed),
            // Флаги соревнования (медали, чемпионат, мастерс, «зачёт не ведётся», бассейн)
            // с обоснованиями. Предложение — галочки в превью изменяемы.
            flags = p.Flags,
            // Р119: протокол свёрнут в одну дату и покрывает уже затянутые дни — «Импортировать»
            // заблокирована (и сервер такой импорт откатит).
            collapsedProtocol = p.CollapsedProtocol is { } c
                ? new { message = c.Message, fileDate = c.FileDate, fileRows = c.FileRows, otherDaysRows = c.OtherDaysRows, days = c.Days }
                : null
        };
    }

    /// <summary>
    /// Ответ превью про клубный зачёт. Когда зачёт есть, а правила под его шкалу нет, отдаём
    /// заготовку для кнопки «Завести правило»: имя версии по конвенции и саму шкалу — админ
    /// видит, что именно заведёт, и правит имя, если нужно.
    /// </summary>
    internal static object StandingResponse(OfficialClubStandingProbe? probe, ParsedCompetition parsed)
    {
        if (probe is null)
            return new
            {
                known = false, hasStanding = false, ruleId = (int?)null, ruleVersion = (string?)null,
                canCreateRule = false, suggestedVersion = (string?)null,
                message = "Про официальный клубный зачёт ничего сказать нельзя: нет loglig-id или сайт недоступен."
            };

        var scale = probe.Scale.OrderBy(p => p.Key).ToList();
        var canCreateRule = probe.HasStanding && probe.MatchedRuleId is null && scale.Count > 0;

        return new
        {
            known = true,
            hasStanding = probe.HasStanding,
            ruleId = probe.MatchedRuleId,
            ruleVersion = probe.MatchedRuleVersion,
            scale = scale.Select(p => new { place = p.Key, points = p.Value }),
            canCreateRule,
            suggestedVersion = canCreateRule ? SuggestRuleVersion(scale, parsed) : null,
            message = probe.Message
        };
    }

    /// <summary>
    /// Имя версии по конвенции проекта — «(очки за 1 место)pt.(мест)pl.(год)»
    /// (docs/admin-pages/pointsrules.md): в выпадашке привязки сразу видно шкалу, а не только дату.
    /// </summary>
    internal static string SuggestRuleVersion(
        IReadOnlyList<KeyValuePair<int, int>> scale, ParsedCompetition parsed)
    {
        var top = scale[0].Value;
        var places = scale[^1].Key;
        var year = ParseCompetitionYear(parsed) ?? DateTime.UtcNow.Year;
        return $"{top}pt.{places}pl.{year}";
    }

    internal static int? ParseCompetitionYear(ParsedCompetition parsed) =>
        DateOnly.TryParseExact(parsed.Competitions.FirstOrDefault()?.Date, "dd/MM/yyyy",
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d.Year
            : null;
}
