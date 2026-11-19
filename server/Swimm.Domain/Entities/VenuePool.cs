using System.ComponentModel.DataAnnotations;

namespace Swimm.Domain.Entities;

/// <summary>
/// Справочник «место проведения → длина бассейна» (этап П4, docs/plans/prod-auto-pull-plan.md §4.4,
/// решение Р-3). Протокол loglig длину бассейна не печатает, «25m» в разборе — умолчание парсера,
/// а от длины зависят рекорды, очки и PB; справочник отвечает на вопрос «какая длина у этого
/// места» без человека — там, где место однозначно.
///
/// Sys_-таблица: без гранта swimm_ro (витрина её не читает), но ЕДЕТ в синхронизацию
/// «прод → локально» (<c>server/db/seed-tables.txt</c>, триггер версии): справочник правит
/// Влад на проде, локальная копия должна видеть то же.
///
/// Написания места — в <see cref="VenuePoolAlias"/>: туда же кладётся и само
/// <see cref="Name"/>, поэтому поиск идёт по одной таблице с UNIQUE на нормализованной форме.
/// </summary>
public class VenuePool
{
    public const int NameMaxLength = 300;   // как DiscoveredCompetition.Venue
    public const int NoteMaxLength = 500;
    public const int ConfirmedByMaxLength = 200;

    [Key]
    public int Id { get; set; }

    /// <summary>Как место показывать в админке (одно из написаний, как его выбрал админ).</summary>
    [Required, MaxLength(NameMaxLength)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// «25m» / «50m» (те же значения, что <c>Competition.PoolType</c>). null — только у
    /// двусмысленного места (<see cref="IsAmbiguous"/>): длину у него решает человек на каждый старт.
    /// </summary>
    [MaxLength(10)]
    public string? PoolType { get; set; }

    /// <summary>
    /// В одном месте бывает и 25, и 50 (מכון וינגייט: 18 стартов 25m против 14 стартов 50m на 20.11.2026).
    /// Такое место справочник НЕ решает — превью и results-auto говорят «двусмысленное, выбери вручную».
    /// </summary>
    public bool IsAmbiguous { get; set; }

    /// <summary>Пояснение для админа (откуда известно, чем двусмысленно).</summary>
    [MaxLength(NoteMaxLength)]
    public string? Note { get; set; }

    /// <summary>Кто подтвердил длину (имя админа, как в аудите). Каждое сохранение на странице — подтверждение.</summary>
    [Required, MaxLength(ConfirmedByMaxLength)]
    public string ConfirmedBy { get; set; } = string.Empty;

    /// <summary>Когда подтвердил (UTC).</summary>
    public DateTime ConfirmedAt { get; set; } = DateTime.UtcNow;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<VenuePoolAlias> Aliases { get; set; } = [];
}

/// <summary>
/// Одно написание места («בריכת וייסגל», «בריכת וויסגל רחובות»…). <see cref="NormalizedAlias"/> —
/// ключ поиска (<c>VenueNameNormalizer</c> в Application), уникален во ВСЁМ справочнике: одно
/// написание не может вести к двум местам.
/// </summary>
public class VenuePoolAlias
{
    [Key]
    public int Id { get; set; }

    public int VenuePoolId { get; set; }
    public VenuePool VenuePool { get; set; } = null!;

    /// <summary>Написание как в источнике (isr.org.il).</summary>
    [Required, MaxLength(VenuePool.NameMaxLength)]
    public string Alias { get; set; } = string.Empty;

    /// <summary>Нормализованная форма — по ней ищем.</summary>
    [Required, MaxLength(VenuePool.NameMaxLength)]
    public string NormalizedAlias { get; set; } = string.Empty;
}
