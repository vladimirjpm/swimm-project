namespace Swimm.Application.Dtos;

public class ResultFilter
{
    public string? Competition { get; set; }
    /// <summary>Фильтр по событию (все дни многодневного соревнования).</summary>
    public int? EventId { get; set; }
    /// <summary>Фильтр по конкретному соревнованию (одному дню/однодневному).</summary>
    public int? CompetitionId { get; set; }
    /// <summary>Последнее по дате соревнование (?competitionId=last); если оно — день события, берутся все дни.</summary>
    public bool Latest { get; set; }
    public string? Name { get; set; }
    public string? Club { get; set; }
    public string? StyleName { get; set; }
    public string? Distance { get; set; }
    public string? Gender { get; set; }
    public string? PoolType { get; set; }
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }

    /* Параметры paged-режима (фаза 3.2, контракт docs/tasks/phase3-paged-results-contract.md) */

    /// <summary>Клиентский фильтр Age — диапазон ГОДОВ РОЖДЕНИЯ (Swimmer.BirthYear), не возрастов.</summary>
    public int? BirthYearFrom { get; set; }
    public int? BirthYearTo { get; set; }
    /// <summary>Masters-режим фильтра Age: точное совпадение строки Results.AgeGroup ("25-29", …).</summary>
    public string? AgeGroup { get; set; }
    /// <summary>Верхняя граница места: 10 (top) / 3 (podium); null — без фильтра. Маппинг из query — в контроллере.</summary>
    public int? PositionMax { get; set; }
    /// <summary>Зеркало клиентской семантики: top ОСТАВЛЯЕТ строки без места (DSQ/DNS), podium — исключает.</summary>
    public bool PositionKeepUnranked { get; set; }
    /// <summary>Конкретный день события: CompetitionDate == дата.</summary>
    public DateTime? EventDate { get; set; }

    /// <summary>Ограничение по ростеру группы (HubGroups «Competitions» tab) — null = без ограничения.</summary>
    public List<int>? SwimmerIds { get; set; }
}
