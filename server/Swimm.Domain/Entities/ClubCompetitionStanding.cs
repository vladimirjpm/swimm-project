using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Swimm.Domain.Entities;

/// <summary>
/// Материализованный клубный зачёт одного соревнования: место клуба, очки, медали.
///
/// Это <b>кэш результата расчёта</b>, а не первичные данные — считается из
/// <see cref="ResultRecord"/> тем же алгоритмом, что и витрина Top clubs
/// (<c>ClubStandingCalculator</c>), и пересчитывается на импорте, пересчёте очков,
/// смене правила и merge клубов.
///
/// Зачем материализуем: страница клуба при «Season = All» поднимает зачёты за несколько
/// сезонов × ~7 групп сразу, а сезонов 20+ и число растёт. Считать это по
/// <see cref="ResultRecord"/> в рантайме — заведомо мимо бюджета p95.
///
/// ⚠ <b>Зачётная единица — соревнование или СОБЫТИЕ целиком.</b> У многодневного события
/// строка одна (на <c>Competition</c> первого дня), а не по строке на день: иначе место
/// клуба задвоится в истории и в KPI. См. docs/plans/club-page-model.md §2.3.
/// </summary>
[Index(nameof(CompetitionId), nameof(ClubId), IsUnique = true)]
[Index(nameof(CompetitionId), nameof(Rank))]
[Index(nameof(ClubId))]
public class ClubCompetitionStanding
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    /// <summary>Зачётная единица: одиночное соревнование либо ПЕРВЫЙ день события.</summary>
    public int CompetitionId { get; set; }

    [ForeignKey(nameof(CompetitionId))]
    public Competition Competition { get; set; } = null!;

    public int ClubId { get; set; }

    [ForeignKey(nameof(ClubId))]
    public Club Club { get; set; } = null!;

    /// <summary>
    /// Место клуба, 1..N. Единственная величина, сравнимая между соревнованиями:
    /// очки считаются по правилу конкретного старта, поэтому суммировать их поверх
    /// разных правил нельзя, а места — можно.
    /// Ранжирование спортивное: равные очки → равное место, следующее пропускается (1, 2, 2, 4).
    /// </summary>
    public int Rank { get; set; }

    /// <summary>Сумма клубных очков по правилу этого соревнования (с эстафетным множителем).</summary>
    public int Points { get; set; }

    /// <summary>Пловцов клуба стартовало (уникальные SwimmerId, включая владельцев эстафетных строк).</summary>
    public int SwimmerCount { get; set; }

    /// <summary>Заплывов, ПРИНЁСШИХ очки (попали в шкалу правила). Не то же, что «доплыло».</summary>
    public int ScoringSwims { get; set; }

    /// <summary>Всего заплывов клуба в зачётной единице.</summary>
    public int SwimCount { get; set; }

    public int Gold { get; set; }

    public int Silver { get; set; }

    public int Bronze { get; set; }

    /// <summary>Когда строка пересчитана (UTC) — для диагностики протухших зачётов.</summary>
    public DateTime ComputedAt { get; set; } = DateTime.UtcNow;
}
