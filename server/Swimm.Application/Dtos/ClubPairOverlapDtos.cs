namespace Swimm.Application.Dtos;

/// <summary>
/// Пара клубов-кандидатов в одних протоколах (Р113): в скольких соревнованиях оба клуба стоят как
/// разные команды, у скольких из них отдельный клубный зачёт и плыл ли кто-то за оба клуба в одном
/// соревновании. Многодневное соревнование (<c>EventId</c>) считается одним.
/// </summary>
public sealed record ClubPairOverlap(
    /// <summary>Соревнований, где у обоих клубов есть строки результатов.</summary>
    int Meets,
    /// <summary>Из них — где у обоих свой клубный зачёт (<c>ClubCompetitionStandings</c>).</summary>
    int SeparateStandings,
    /// <summary>Соревнований, где один и тот же пловец (индивидуальный старт) записан за оба клуба.</summary>
    int SameSwimmerMeets,
    /// <summary>До трёх последних общих соревнований — для подсказки.</summary>
    IReadOnlyList<ClubPairOverlapMeet> Examples,
    /// <summary>Период клуба A (канон) в протоколах: первый и последний старт; null — строк нет.</summary>
    ClubSpan? SpanA = null,
    /// <summary>Период клуба B (дубль) в протоколах.</summary>
    ClubSpan? SpanB = null)
{
    /// <summary>
    /// «Скорее два клуба»: протоколы печатают их рядом как разные команды, и ни один пловец не
    /// записан за оба. Один клуб двумя названиями так не выглядит: его пловцы были бы в одном
    /// клубе, а если протокол путает написание — один и тот же пловец встречается под обоими.
    /// </summary>
    public bool TwoClubsLikely => Meets > 0 && SameSwimmerMeets == 0;

    public static readonly ClubPairOverlap None = new(0, 0, 0, []);

    /// <summary>Периоды клубов не пересекаются: один кончился раньше, чем начался другой.</summary>
    public bool SpansApart => SpanA is { } a && SpanB is { } b && (a.To < b.From || b.To < a.From);
}

/// <summary>Общее соревнование пары: id дня (для ссылки), название и дата как в протоколе.</summary>
public sealed record ClubPairOverlapMeet(int CompetitionId, string Name, string Date);

/// <summary>Первый и последний старт клуба в протоколах.</summary>
public sealed record ClubSpan(DateOnly From, DateOnly To);

/// <summary>Вывод по паре (Р113): <c>ClubPairVerdicts</c> и почему — одной фразой для человека.</summary>
public sealed record ClubPairRecommendation(string Verdict, string Reason);
