namespace Swimm.Application.Dtos;

/// <summary>
/// Сводка «что знает loglig» по общим пловцам пары клубов (Р112): сколько у loglig в клубе A
/// (канон), в клубе B (дубль), в обоих (одно название), в третьем клубе, в архиве федерации,
/// без loglig-id и без ответа.
/// </summary>
public sealed record ClubPairLogligSummary(
    int ClubA, int ClubB, int Both, int Other, int Archive, int NoLogligId, int Unchecked)
{
    public int Total => ClubA + ClubB + Both + Other + Archive + NoLogligId + Unchecked;
}

/// <summary>Общий пловец пары и его клуб на loglig.</summary>
public sealed record ClubPairLogligSwimmer(
    int SwimmerId,
    /// <summary>Имя на иврите (английское — фоллбек).</summary>
    string Name,
    int? BirthYear,
    int? CardClubId,
    string? CardClubName,
    int? LogligId,
    /// <summary>Клуб на карточке loglig, как напечатан; null — ответа нет.</summary>
    string? LogligClubName,
    DateTime? CheckedAt,
    /// <summary><see cref="Mapping.ClubPairLogligVerdicts"/>.</summary>
    string Verdict,
    /// <summary>Ссылка на карточку loglig с текущим seasonId; null — нет loglig-id.</summary>
    string? LogligUrl);

/// <summary>Общие пловцы пары + сводка; после кнопки «Проверить в loglig» — и счётчики похода в сеть.</summary>
public sealed record ClubPairLogligReport(
    int ClubAId,
    string ClubAName,
    int ClubBId,
    string ClubBName,
    ClubPairLogligSummary Summary,
    IReadOnlyList<ClubPairLogligSwimmer> Swimmers,
    /// <summary>Открыто карточек loglig (0 — только чтение).</summary>
    int Requests = 0,
    /// <summary>Из них не открылись (ответ не записан, повтор следующим нажатием).</summary>
    int Failed = 0,
    /// <summary>Несвежих ответов, не обойдённых из-за лимита на одно нажатие.</summary>
    int Remaining = 0);
