using Swimm.Application.Dtos;

namespace Swimm.Application.Mapping;

/// <summary>Где loglig видит общего пловца пары клубов-кандидатов в дубли (Р112). Строки — для JSON и CSS.</summary>
public static class ClubPairLogligVerdicts
{
    /// <summary>loglig: клуб A (канон пары).</summary>
    public const string ClubA = "a";
    /// <summary>loglig: клуб B (дубль пары).</summary>
    public const string ClubB = "b";
    /// <summary>Название loglig подходит обоим клубам пары — сами названия у нас совпадают.</summary>
    public const string Both = "both";
    /// <summary>Третий клуб или название, которого у нас нет.</summary>
    public const string Other = "other";
    /// <summary>«ארכיון איגוד» — пловец больше не зарегистрирован.</summary>
    public const string Archive = "archive";
    /// <summary>Нет подтверждённого loglig-id — спросить не у кого.</summary>
    public const string NoLogligId = "no-loglig-id";
    /// <summary>loglig-id есть, ответа нет (не открывали, открывали под другим id, клуба на карточке нет).</summary>
    public const string Unchecked = "unchecked";
}

/// <summary>Последний удачный ответ карточки loglig (<c>Sys_SwimmerLogligClubs</c>).</summary>
/// <param name="CardName">Имя с карточки; null — ответ записан до Р125, для пола не годится.</param>
/// <param name="Gender">Пол с карточки (<c>male</c>/<c>female</c>); null — на карточке его нет.</param>
public sealed record StoredLogligClub(int LogligId, string? ClubName, DateTime CheckedAt,
    string? CardName = null, int? CardBirthYear = null, string? Gender = null);

/// <summary>
/// Что знает loglig про общих пловцов пары клубов-кандидатов в дубли (Р112, /Admin/Clubs,
/// эвристика «общие пловцы»). Чистая функция: сеть и запись — у <c>ClubPairLogligService</c>.
///
/// Зачем: пару вроде «הפועל ירושלים» / «הפועל ירושלים אומנותית» (23 общих пловца) эвристика
/// предлагает в дубли, а на деле это дети, перешедшие из клуба в клуб (кандидаты смены клуба ждут
/// loglig, Р98), или два настоящих клуба. Если у loglig общие пловцы стоят в ОБОИХ клубах — клубы
/// разные, склеивать нельзя; если все в одном и название второго loglig не знает — похоже на дубль.
///
/// Название loglig сравнивается тем же <see cref="ClubNameMatcher"/>, что в сверке кандидатов (Р98):
/// <see cref="NameKey.Normalize"/>, прежние названия (<c>ClubNames</c>) и надгробия склеенных клубов
/// (→ итоговый канон). Ответ берётся любой давности (дата видна в списке); свежесть решает только,
/// идти ли снова в сеть по кнопке.
/// </summary>
public static class ClubPairLogligRule
{
    private static readonly string ArchiveKey = NameKey.Normalize("ארכיון");

    /// <param name="logligId">Подтверждённый loglig-id пловца; null — нет.</param>
    /// <param name="stored">Последний ответ loglig по пловцу; null — не открывали.</param>
    public static string Classify(int? logligId, StoredLogligClub? stored, int clubA, int clubB, ClubNameMatcher clubs)
    {
        if (logligId is null) return ClubPairLogligVerdicts.NoLogligId;
        if (stored is null || stored.LogligId != logligId || string.IsNullOrWhiteSpace(stored.ClubName))
            return ClubPairLogligVerdicts.Unchecked;

        if (NameKey.Normalize(stored.ClubName).Contains(ArchiveKey, StringComparison.Ordinal))
            return ClubPairLogligVerdicts.Archive;

        var matches = clubs.Find(stored.ClubName);
        var a = matches.Contains(clubs.Live(clubA) ?? clubA);
        var b = matches.Contains(clubs.Live(clubB) ?? clubB);
        return (a, b) switch
        {
            (true, true) => ClubPairLogligVerdicts.Both,
            (true, false) => ClubPairLogligVerdicts.ClubA,
            (false, true) => ClubPairLogligVerdicts.ClubB,
            _ => ClubPairLogligVerdicts.Other,
        };
    }

    /// <summary>Сводка по вердиктам общих пловцов (колонка «loglig» в таблице кандидатов).</summary>
    public static ClubPairLogligSummary Summarize(IEnumerable<string> verdicts)
    {
        int a = 0, b = 0, both = 0, other = 0, archive = 0, noId = 0, unchecked_ = 0;
        foreach (var v in verdicts)
        {
            switch (v)
            {
                case ClubPairLogligVerdicts.ClubA: a++; break;
                case ClubPairLogligVerdicts.ClubB: b++; break;
                case ClubPairLogligVerdicts.Both: both++; break;
                case ClubPairLogligVerdicts.Other: other++; break;
                case ClubPairLogligVerdicts.Archive: archive++; break;
                case ClubPairLogligVerdicts.NoLogligId: noId++; break;
                default: unchecked_++; break;
            }
        }
        return new ClubPairLogligSummary(a, b, both, other, archive, noId, unchecked_);
    }

    /// <summary>
    /// Надо ли снова открывать карточку по кнопке «Проверить в loglig»: ответа нет, он старше суток
    /// или снят под другим loglig-id. То же правило свежести, что у кэша сверки кандидатов (Р98).
    /// </summary>
    public static bool NeedsFetch(DateTime now, int? logligId, StoredLogligClub? stored) =>
        logligId is not null
        && !SwimmerClubLogligRule.CacheValid(now, stored?.CheckedAt, stored?.LogligId, logligId, stored?.ClubName);
}
