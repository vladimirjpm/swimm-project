using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Swimm.Domain.Entities;

/// <summary>
/// Справочник спортсменов.
/// </summary>
[Index(nameof(LastName), nameof(FirstName))]
[Index(nameof(LastNameEn), nameof(FirstNameEn))]
[Index(nameof(LogligId), IsUnique = true)]
public class Swimmer
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    [MaxLength(100)]
    public string LastName { get; set; } = string.Empty;

    [MaxLength(100)]
    public string FirstName { get; set; } = string.Empty;

    [MaxLength(100)]
    public string LastNameEn { get; set; } = string.Empty;

    [MaxLength(100)]
    public string FirstNameEn { get; set; } = string.Empty;

    public int BirthYear { get; set; }

    /// <summary>Пол спортсмена (M / F)</summary>
    [MaxLength(10)]
    public string? Gender { get; set; }

    /// <summary>
    /// Источник записи: <c>isr</c> — из справочника isr.org.il (есть <see cref="SwimmerOrgId"/>);
    /// <c>local</c> — заведён вручную (напр. «Дельфин-мастерс»: есть тренировки, но нет в федерации).
    /// Машинерия (членство/рекорды/зачёт) одинакова для обоих. См. hubgroups-architecture.md §7.
    /// </summary>
    [MaxLength(10)]
    public string Origin { get; set; } = "isr";

    /// <summary>
    /// Пловец ТОЛЬКО этой группы (Р71, решение Влада 29.09.2026): человек без аккаунта и без
    /// loglig, которого завёл тренер, чтобы ставить на дорожки и писать времена тренировок.
    /// Виден только внутри группы — админу сайта, управляющим и активным участникам
    /// (<c>PrivateSwimmerRules</c>); для всех остальных его нет: ни страницы, ни поиска, ни
    /// состава группы в общем ответе, ни клуба, ни избранного. Так никто не заведёт публично
    /// «Горбенко» или имя с матом. null — обычный пловец. Удаляется вместе с группой.
    /// </summary>
    public int? PrivateHubGroupId { get; set; }

    [ForeignKey(nameof(PrivateHubGroupId))]
    public HubGroup? PrivateHubGroup { get; set; }

    /// <summary>ID спортсмена в федерации</summary>
    [MaxLength(50)]
    public string? SwimmerOrgId { get; set; }

    /// <summary>URL аватара</summary>
    [MaxLength(1000)]
    public string? AvatarUrl { get; set; }

    /// <summary>Ссылка на клуб (опционально)</summary>
    public int? ClubId { get; set; }

    [ForeignKey(nameof(ClubId))]
    public Club? Club { get; set; }

    /// <summary>Ссылка на страну (опционально)</summary>
    public int? CountryId { get; set; }

    [ForeignKey(nameof(CountryId))]
    public Country? Country { get; set; }

    /* === Привязка к профилю loglig.com (docs/loglig-id-plan.md) === */

    /// <summary>ID игрока на loglig.com (карточка Players/Details/{id}); уникален среди непустых значений.</summary>
    public int? LogligId { get; set; }

    /// <summary>Статус привязки: Suggested / Verified / Rejected; null — привязки нет.</summary>
    [MaxLength(20)]
    public string? LogligIdStatus { get; set; }

    /// <summary>Источник привязки: admin / user-claim / auto.</summary>
    [MaxLength(20)]
    public string? LogligIdSource { get; set; }

    /// <summary>Пользователь, предложивший привязку (аудит; FK на AppUser намеренно не заведён).</summary>
    public int? LogligIdSuggestedByUserId { get; set; }

    /// <summary>Когда предложена привязка (UTC).</summary>
    public DateTime? LogligIdSuggestedAt { get; set; }

    /// <summary>Когда привязка подтверждена (UTC).</summary>
    public DateTime? LogligIdVerifiedAt { get; set; }
}
