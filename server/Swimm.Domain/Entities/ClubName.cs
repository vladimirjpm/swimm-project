using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Swimm.Domain.Entities;

/// <summary>
/// Прежнее название живого клуба (docs/plans/swimmer-club-change-plan.md §5, Н1).
///
/// Появляется при склейке видом «Переименование»: клуб сменил название, и строка-надгробие
/// старого клуба (<see cref="Club.MergedIntoId"/>) уже ничего не помнит о том, КОГДА и КАК его
/// звали. Здесь — старые <see cref="Name"/>/<see cref="NameEn"/> и годы, когда под ними
/// выступали (первый и последний старт дубля, посчитанные ДО переноса результатов).
///
/// Кто читает: шапка и SEO страницы клуба («Formerly: …»), поиск клуба по старому названию,
/// импорт (протокол со старым названием попадает в канон, а не заводит новый клуб), окно
/// истории клубов пловца («then: …»). Строки результатов, напечатанные под этим названием,
/// помечены <see cref="ResultRecord.ClubNameId"/>.
///
/// Публичная таблица: SELECT для <c>swimm_ro</c> (server/db/02-grants.sql), переносится дампом
/// (server/db/seed-tables.txt).
/// </summary>
public class ClubName
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    /// <summary>
    /// Живой клуб, которому принадлежит название. При склейке этого клуба в другой строка
    /// переезжает на приёмника — так у названия всегда живой хозяин, цепочек нет (Н4).
    /// </summary>
    public int ClubId { get; set; }

    [ForeignKey(nameof(ClubId))]
    public Club Club { get; set; } = null!;

    /// <summary>Название как было в справочнике (иврит).</summary>
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Английское название, как было. Пусто — его не было.</summary>
    [MaxLength(200)]
    public string NameEn { get; set; } = string.Empty;

    /// <summary>Первый старт под этим названием (по результатам дубля). null — стартов не было.</summary>
    public DateTime? UsedFrom { get; set; }

    /// <summary>Последний старт под этим названием.</summary>
    public DateTime? UsedTo { get; set; }

    /// <summary><see cref="ClubNameKinds"/>: <c>former</c> — переименование, <c>spelling</c> — вариант написания.</summary>
    [MaxLength(16)]
    public string Kind { get; set; } = ClubNameKinds.Former;

    /// <summary>
    /// Клуб-надгробие, из которого название пришло (склеенный в <see cref="ClubId"/>). null —
    /// название заведено руками. Нужен аудиту и повторной склейке: одно надгробие — одна строка.
    /// </summary>
    public int? MergedClubId { get; set; }

    [ForeignKey(nameof(MergedClubId))]
    public Club? MergedClub { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Виды прежнего названия клуба (<see cref="ClubName.Kind"/>).</summary>
public static class ClubNameKinds
{
    /// <summary>Клуб сменил название: «Formerly: …» на витрине.</summary>
    public const string Former = "former";

    /// <summary>Другое написание того же названия (дубль). На витрине не показывается.</summary>
    public const string Spelling = "spelling";
}
