using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Swimm.Domain.Entities;

/// <summary>Кто сменил клуб в карточке пловца (<see cref="SwimmerClubChange.Source"/>).</summary>
public static class SwimmerClubChangeSources
{
    /// <summary>Пересчёт после импорта — по затронутым пловцам.</summary>
    public const string Import = "import";
    /// <summary>Разовый догон <c>--refresh-swimmer-clubs</c>.</summary>
    public const string Cli = "cli";
    /// <summary>Ручная правка на /Admin/Swimmers (заодно пишется и <see cref="AdminAudit"/>).</summary>
    public const string Admin = "admin";
}

/// <summary>
/// Журнал смен клуба в карточке пловца (<c>Swimmers.ClubId</c>), Р95 / Ф1
/// docs/plans/swimmer-club-change-plan.md. Клуб теперь меняется сам при импорте — журнал делает
/// автоматику видимой (карточка дашборда «Club changes, 7 days») и даёт откатить ошибку руками.
///
/// Sys_-таблица, БЕЗ grant swimm_ro и не в seed-tables.txt: у прода свой журнал, у локальной свой.
/// FK намеренно нет (как у <see cref="DedupIgnoredPair"/>): запись переживает склейку и удаление
/// пловца или клуба и остаётся просто историей.
/// </summary>
public class SwimmerClubChange
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    public int SwimmerId { get; set; }

    /// <summary>Клуб до смены; null — в карточке клуба не было.</summary>
    public int? FromClubId { get; set; }

    /// <summary>Клуб после смены; null — админ очистил поле.</summary>
    public int? ToClubId { get; set; }

    /// <summary>import | cli | admin (<see cref="SwimmerClubChangeSources"/>).</summary>
    [Required, MaxLength(20)]
    public string Source { get; set; } = string.Empty;

    /// <summary>Дата последнего старта пловца, по которой выбран клуб (для admin — null).</summary>
    public DateOnly? LastSwimDate { get; set; }

    public DateTime ChangedAt { get; set; } = DateTime.UtcNow;
}
