using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Swimm.Domain.Entities;

/// <summary>
/// Подписка группы на клуб (docs/plans/hubgroup-club-subscription-plan.md): состав группы
/// сам пересобирается из пловцов клуба — строки <see cref="HubGroupMember"/> с
/// <see cref="HubGroupMember.Source"/> = club.
///
/// Одна подписка на группу (решение Влада 10.09.2026) держится уникальным индексом по
/// <see cref="HubGroupId"/>; хранение сознательно допускает больше — разрешить несколько
/// клубов = снять уникальность, модель менять не придётся.
///
/// Бизнес-таблица, публичная: её читает анонимный путь (каталог групп, строка «Follows club»
/// на странице группы) → грант swimm_ro в 02-grants.sql.
/// </summary>
public class HubGroupClubSubscription
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    public int HubGroupId { get; set; }

    [ForeignKey(nameof(HubGroupId))]
    public HubGroup? HubGroup { get; set; }

    /// <summary>Всегда НЕсклеенный клуб: подписка на дубль перевешивается на канонический.</summary>
    public int ClubId { get; set; }

    [ForeignKey(nameof(ClubId))]
    public Club? Club { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Кто подписал. Без FK на Sys_AppUsers, как актор аудита: строка переживает удаление
    /// пользователя, а синтетический dev-админ (id 0) не роняет вставку.
    /// </summary>
    public int? CreatedByUserId { get; set; }
}
