using System.Globalization;
using Swimm.Application.Dtos;
using Swimm.Domain;

namespace Swimm.Application.Mapping;

/// <summary>
/// Заплыв ростера для списка стартов группы — плоская проекция строки результата (заполняет
/// репозиторий, группирует <see cref="HubGroupCompetitionsBuilder"/>).
/// </summary>
public sealed class HubGroupCompetitionSwim
{
    public long Id { get; init; }
    /// <summary>День старта (<c>Competitions.Id</c>).</summary>
    public int CompetitionId { get; init; }
    /// <summary>Турнир многодневки; null — старт однодневный.</summary>
    public int? EventId { get; init; }
    /// <summary>Имя турнира (у многодневки) или дня.</summary>
    public string Name { get; init; } = "";
    public DateTime CompetitionDate { get; init; }
    /// <summary>Владелец строки; у эстафеты — нога-якорь, не обязательно из ростера.</summary>
    public int SwimmerId { get; init; }
    public int? RelayId { get; init; }
    public int? Position { get; init; }
    public bool TimeFail { get; init; }
    public string? HeatType { get; init; }
    public string? Round { get; init; }
    public bool IsAward { get; init; }

    /// <summary>
    /// Медаль — по единому правилу продукта (зеркало клиентского <c>HelperResults.isMedalPlace</c>
    /// и «последнего старта»): соревнование награждаемое, время зачтено, место не из
    /// предварительного/дополнительного заплыва (Р34) и не из общей секции «כללי» (Р43).
    /// </summary>
    public bool IsMedal =>
        IsAward && !TimeFail && HeatTypes.GivesOfficialPlace(HeatType)
        && Round != ResultRounds.FinalOpen && Position is >= 1 and <= 3;
}

/// <summary>
/// Список стартов группы для таба Results (чип «≡ Results», решение Влада 28.09.2026): строка
/// на ТУРНИР — дни многодневки под одним <c>EventId</c> сложены, как у «последнего старта»;
/// свежие сверху. У строки — сколько пловцов ростера плыло (эстафета — всеми ногами из ростера,
/// docs/relays.md), заплывы, медали и сколько действующих рекордов группы поставлено там.
///
/// Чистая функция без базы: запросы (и двухшаговый забор эстафет) — в репозитории.
/// </summary>
public static class HubGroupCompetitionsBuilder
{
    /// <param name="swims">Заплывы ростера, эстафеты уже добраны; дубли по Id недопустимы.</param>
    /// <param name="rosterIds">Пловцы ростера.</param>
    /// <param name="relayRosterLegs">Эстафета → ноги ИЗ РОСТЕРА (по <c>RelayMembers</c>).</param>
    /// <param name="bests">Рекорды группы — каждый несёт день, где поставлен.</param>
    public static List<HubGroupCompetitionDto> Build(
        IEnumerable<HubGroupCompetitionSwim> swims,
        IReadOnlySet<int> rosterIds,
        IReadOnlyDictionary<int, List<int>> relayRosterLegs,
        IEnumerable<HubGroupBestDto> bests)
    {
        var recordsByDay = bests
            .GroupBy(b => b.CompetitionId)
            .ToDictionary(g => g.Key, g => g.Count());

        return swims
            .GroupBy(r => r.EventId is int eventId ? $"e{eventId}" : $"c{r.CompetitionId}")
            .Select(g =>
            {
                var swimmers = new HashSet<int>();
                foreach (var r in g)
                {
                    // Эстафета — это вся команда: считаем её ноги из ростера, а не владельца
                    // строки (он может быть чужим). Нет членства (старая эстафета текстом) —
                    // остаётся владелец, если он наш.
                    if (r.RelayId is int relayId && relayRosterLegs.TryGetValue(relayId, out var legs))
                        swimmers.UnionWith(legs);
                    else if (rosterIds.Contains(r.SwimmerId))
                        swimmers.Add(r.SwimmerId);
                }
                var medals = g.Where(r => r.IsMedal).Select(r => r.Position).ToList();
                var last = g.OrderByDescending(r => r.CompetitionDate).ThenByDescending(r => r.Id).First();
                return new
                {
                    Last = last.CompetitionDate,
                    Dto = new HubGroupCompetitionDto
                    {
                        CompetitionId = last.CompetitionId,
                        EventId = last.EventId,
                        Name = last.Name,
                        DateFrom = g.Min(r => r.CompetitionDate).ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
                        DateTo = last.CompetitionDate.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
                        Swimmers = swimmers.Count,
                        Swims = g.Count(),
                        Golds = medals.Count(p => p == 1),
                        Silvers = medals.Count(p => p == 2),
                        Bronzes = medals.Count(p => p == 3),
                        Records = g.Select(r => r.CompetitionId).Distinct()
                            .Sum(id => recordsByDay.GetValueOrDefault(id)),
                    },
                };
            })
            .OrderByDescending(x => x.Last)
            .Select(x => x.Dto)
            .ToList();
    }
}
