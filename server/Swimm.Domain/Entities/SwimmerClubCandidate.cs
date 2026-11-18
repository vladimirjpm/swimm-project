using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Swimm.Domain.Entities;

/// <summary>
/// Вердикт сверки кандидата с loglig (<see cref="SwimmerClubCandidate.Verdict"/>), Р98.
/// Строки, а не enum: читаются прямо из SQL и стоят в CHECK <c>CK_Sys_SwimmerClubCandidates_Verdict</c>.
/// </summary>
public static class SwimmerClubLogligVerdicts
{
    /// <summary>loglig показывает НОВЫЙ клуб кандидата — смена применяется (журнал, источник loglig).</summary>
    public const string Confirmed = "confirmed";
    /// <summary>loglig показывает клуб из карточки — ждём, перепроверка завтра.</summary>
    public const string StillOld = "old";
    /// <summary>loglig пишет «ארכיון איגוד» (пловец больше не зарегистрирован) — решает человек.</summary>
    public const string Archive = "archive";
    /// <summary>loglig показывает третий клуб (или название, которого у нас нет) — решает человек.</summary>
    public const string OtherClub = "other";
    /// <summary>У пловца нет подтверждённого loglig-id — сверять не с чем.</summary>
    public const string NoLogligId = "no-loglig-id";
    /// <summary>Карточка loglig не открылась или клуба на ней нет — повтор при следующем прогоне.</summary>
    public const string Unavailable = "unavailable";

    public static readonly IReadOnlyList<string> All =
        [Confirmed, StillOld, Archive, OtherClub, NoLogligId, Unavailable];
}

/// <summary>
/// Кандидат на смену клуба в карточке пловца (Р98, §6 docs/plans/swimmer-club-change-plan.md).
///
/// Правило К2 («клуб последнего соревнования») само клуб больше НЕ меняет: импорт и догон
/// <c>--refresh-swimmer-clubs</c> заводят здесь кандидата, а фоновая служба сверяет его с
/// карточкой loglig.com (реестр федерации). Только когда loglig показывает новый клуб, смена
/// уходит в <see cref="Swimmer.ClubId"/> и журнал <see cref="SwimmerClubChange"/> (источник
/// <c>loglig</c>), а кандидат удаляется. Решение Влада 18.11.2026: «если клуб поменялся —
/// сверяйся с логлиг. если там не поменялся — пока не меняй».
///
/// Отдельная таблица, а не статус в журнале: кандидат — изменяемое СОСТОЯНИЕ (один на пловца,
/// перепроверяется каждые сутки, снимается сам), журнал — неизменная история уже сделанных смен.
/// Ключ — <see cref="SwimmerId"/>: у пловца не бывает двух ждущих смен сразу.
///
/// Последний ответ loglig (<see cref="LogligClubName"/>, <see cref="CheckedAt"/>) — это и кэш на
/// сутки: повторный прогон в те же сутки в loglig не ходит, только пересчитывает вердикт.
///
/// Sys_-таблица, БЕЗ grant swimm_ro и не в seed-tables.txt. FK намеренно нет (как у журнала).
/// </summary>
public class SwimmerClubCandidate
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public int SwimmerId { get; set; }

    /// <summary>Клуб в карточке, когда кандидат заведён/обновлён; null — клуба в карточке не было.</summary>
    public int? FromClubId { get; set; }

    /// <summary>Клуб, который дало правило К2 (клуб последнего соревнования).</summary>
    public int ToClubId { get; set; }

    /// <summary>Дата последнего старта пловца, по которой выбран <see cref="ToClubId"/>.</summary>
    public DateOnly LastSwimDate { get; set; }

    /// <summary>Периоды двух клубов пересекаются (выступал за оба одновременно) — часто дубль клуба.</summary>
    public bool Overlap { get; set; }

    /// <summary>Кто завёл кандидата: import | cli (<see cref="SwimmerClubChangeSources"/>).</summary>
    [Required, MaxLength(20)]
    public string Source { get; set; } = string.Empty;

    /// <summary>Вердикт последней сверки (<see cref="SwimmerClubLogligVerdicts"/>); null — ещё не сверяли.</summary>
    [MaxLength(20)]
    public string? Verdict { get; set; }

    /// <summary>loglig-id, по которому сверяли (кэш действителен, только пока он тот же).</summary>
    public int? LogligId { get; set; }

    /// <summary>Клуб, как его показала карточка loglig (чип «אגודה»), — то, что видит человек на дашборде.</summary>
    [MaxLength(200)]
    public string? LogligClubName { get; set; }

    /// <summary>Когда карточку loglig последний раз открывали (UTC); null — не открывали.</summary>
    public DateTime? CheckedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
