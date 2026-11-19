using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Swimm.Domain.Entities;

/// <summary>
/// Последний ответ карточки loglig.com по пловцу: какой клуб там напечатан (чип «אגודה») и когда
/// открывали. Р112: одно место на всех, кто спрашивает loglig про клуб пловца, — сверка кандидатов
/// на смену клуба (Р98, <c>SwimmerClubLogligCheckService</c>) и кнопка «Проверить в loglig» у пары
/// клубов-кандидатов в дубли (/Admin/Clubs, эвристика «общие пловцы»). Тот же ответ — и суточный
/// кэш: моложе <c>SwimmerClubLogligRule.CacheTtl</c> и с тем же loglig-id в сеть не ходим.
///
/// Пишется только удачный ответ (карточка открылась): неудача не кэшируется — повтор сразу.
///
/// Отдельная Sys_-таблица, а не колонки <c>Swimmers</c>: это кэш ответа чужого сайта для админки,
/// не данные витрины. <c>Swimmers</c> — публичная таблица (grant <c>swimm_ro</c>, seed, синк с
/// прода, метки кэша страниц), каждая новая колонка там — ещё и запись в реестр служебных колонок,
/// иначе проверка роняла бы кэш страниц пловца и клуба. БЕЗ grant swimm_ro, не в seed-tables.txt,
/// FK нет (как у кандидатов); склейка пловцов удаляет строку дубля.
/// </summary>
public class SwimmerLogligClub
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public int SwimmerId { get; set; }

    /// <summary>loglig-id, карточку которого открывали: ответ действителен, только пока он тот же.</summary>
    public int LogligId { get; set; }

    /// <summary>Клуб, как напечатан на карточке; null — карточка открылась, а клуба на ней нет.</summary>
    [MaxLength(200)]
    public string? ClubName { get; set; }

    /// <summary>Когда карточку открывали (UTC).</summary>
    public DateTime CheckedAt { get; set; }
}
