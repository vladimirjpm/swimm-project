namespace Swimm.Domain.Entities;

/// <summary>
/// Когда человек последний раз открывал виды таба Trainings группы — опора меток внимания
/// (docs/plans/trainings-tab-redesign-plan.md, Т1б; `marks-spec.md` хендоффа). Ключ —
/// (группа, аккаунт), а не членство: у тренера-не-участника членства нет, а метка «новые видео»
/// нужна и ему (решение Влада Р5, 01.10.2026). Строка появляется при первом открытии.
/// ПРИВАТНЫЕ данные — <c>Sys_HubGroupUserSeen</c>, БЕЗ grant <c>swimm_ro</c>.
/// </summary>
public class HubGroupUserSeen
{
    public int HubGroupId { get; set; }

    public HubGroup? HubGroup { get; set; }

    public int UserId { get; set; }

    public AppUser? User { get; set; }

    /// <summary>Открыл ленту Trainings (UTC): медиа других позже этого — «новое». null — не открывал.</summary>
    public DateTime? TrainingsSeenAt { get; set; }

    /// <summary>Открыл вид Lanes (UTC): перенос тренером позже этого — золотая метка. null — не открывал.</summary>
    public DateTime? LanesSeenAt { get; set; }
}
