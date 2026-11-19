using Swimm.Application.Dtos;

namespace Swimm.Application.Mapping;

/// <summary>
/// Где клуб встречается в протоколах — сырьё для <see cref="ClubPairOverlapRule"/>. Ключ
/// соревнования — <see cref="ClubPairOverlapRule.MeetKey"/>: многодневное соревнование одно.
/// </summary>
public sealed class ClubProtocolPresence
{
    /// <summary>Соревнования клуба: ключ → (id одного из дней, последняя дата старта).</summary>
    public Dictionary<long, (int CompetitionId, DateTime Date)> Meets { get; } = [];

    /// <summary>Соревнования, где у клуба есть свой клубный зачёт.</summary>
    public HashSet<long> StandingMeets { get; } = [];

    /// <summary>Индивидуальные старты: (соревнование, пловец). Эстафеты не в счёт.</summary>
    public HashSet<(long Meet, int SwimmerId)> Swims { get; } = [];

    public void AddResult(long meet, int competitionId, DateTime date, int swimmerId, bool relay)
    {
        if (!Meets.TryGetValue(meet, out var m) || date > m.Date) Meets[meet] = (competitionId, date);
        if (!relay) Swims.Add((meet, swimmerId));
    }

    /// <summary>Первый и последний старт клуба (по датам соревнований); null — строк нет.</summary>
    public ClubSpan? Span() => Meets.Count == 0 ? null
        : new ClubSpan(DateOnly.FromDateTime(Meets.Values.Min(m => m.Date)), DateOnly.FromDateTime(Meets.Values.Max(m => m.Date)));
}

/// <summary>
/// Пара клубов-кандидатов в дубли в одних протоколах (Р113, /Admin/Clubs). Влад 19.11.2026: «а не
/// стоит проверить, что нет в результатах клуба, который склеится? … добавить такую инфу в
/// админке, чтобы не склеить то, что не нужно». Все четыре спорные пары на проде оказались двумя
/// клубами: протокол печатал их рядом как разные команды, с отдельным зачётом, и ни один пловец не
/// плыл за оба в одном соревновании. Чистая функция, выборка — у <c>ClubProtocolOverlapQuery</c>.
/// </summary>
public static class ClubPairOverlapRule
{
    /// <summary>Сколько общих соревнований показывать примерами.</summary>
    public const int ExampleCount = 3;

    /// <summary>Ключ соревнования: многодневное (<c>EventId</c>) — одно, отрицательным числом,
    /// чтобы не совпасть с id однодневного.</summary>
    public static long MeetKey(int competitionId, int? eventId) => eventId is int e ? -(long)e : competitionId;

    /// <summary>Строка предупреждения для dry-run склейки (и подсказки страницы).</summary>
    public static string Warning(ClubPairOverlap o)
    {
        var text = $"⚠ оба клуба в одних протоколах: соревнований {o.Meets}"
            + (o.SeparateStandings > 0 ? $", отдельный клубный зачёт в {o.SeparateStandings}" : "")
            + (o.SameSwimmerMeets > 0 ? $", один пловец за оба клуба в {o.SameSwimmerMeets}" : ", ни один пловец не записан за оба");
        if (o.Examples.Count > 0)
            text += " (" + string.Join("; ", o.Examples.Select(e => $"#{e.CompetitionId} {e.Name} {e.Date}")) + ")";
        return text + (o.TwoClubsLikely
            ? " — скорее ДВА клуба: склейка сольёт их зачёты; проверь, что это действительно один клуб"
            : " — похоже на одно название, записанное двумя способами, но проверь протокол");
    }

    public static ClubPairOverlap Build(ClubProtocolPresence? a, ClubProtocolPresence? b,
        Func<int, ClubPairOverlapMeet?>? describe = null)
    {
        if (a is null || b is null) return ClubPairOverlap.None;
        var shared = a.Meets.Keys.Where(b.Meets.ContainsKey).ToList();
        if (shared.Count == 0) return ClubPairOverlap.None with { SpanA = a.Span(), SpanB = b.Span() };

        var standings = shared.Count(m => a.StandingMeets.Contains(m) && b.StandingMeets.Contains(m));
        var sameSwimmer = a.Swims.Where(b.Swims.Contains).Select(s => s.Meet).Distinct().Count();
        var examples = describe is null ? [] : shared
            .Select(m => a.Meets[m])
            .OrderByDescending(m => m.Date)
            .Take(ExampleCount)
            .Select(m => describe(m.CompetitionId))
            .OfType<ClubPairOverlapMeet>()
            .ToList();
        return new ClubPairOverlap(shared.Count, standings, sameSwimmer, examples, a.Span(), b.Span());
    }
}
