using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Swimm.Domain.Entities;

/// <summary>
/// Источник стартового протокола соревнования: один <c>compID</c> федерации, из которого
/// тянутся заявки (docs/plans/start-list-plan.md, решение от 2026-08-28).
///
/// Зачем отдельная таблица, а не поле <c>Competition.OrgCompId</c>. У одного соревнования
/// источников бывает НЕСКОЛЬКО: «אליפות ישראל ארנה 8-11 חורף 2026» в базе — один старт из
/// трёх дней, а на федерации это четыре окружных compID (север 16789, центр 16787 и 16788
/// в один день, юг 16786). Скалярное поле выражает только первый из них, и таб «Start list»
/// показывал бы один округ из четырёх — или, как было до этой таблицы, ни одного.
///
/// Отношение к <c>Competition.OrgCompId</c>: поле НЕ отменяется и остаётся штампом «этот день
/// импортирован из такого-то compID» (по нему живут <c>CompetitionResultUrl</c>, зачёт клубов,
/// сверка импорта). Эта таблица отвечает на другой вопрос — «из каких протоколов состоит
/// стартовый список соревнования», и потому допускает N строк на день.
///
/// ⚠ Таблица ПУБЛИЧНАЯ (grant swimm_ro, см. server/db/02-grants.sql): её читает овервью
/// соревнования на публичном пути (<c>SwimmReadDbContext</c>), которому <c>Sys_*</c> закрыты.
/// Ничего приватного в ней нет — это те же открытые compID, что стоят в адресе на isr.org.il.
/// </summary>
// Один и тот же источник нельзя привязать к соревнованию дважды.
[Index(nameof(CompetitionId), nameof(OrgCompId), IsUnique = true)]
// «Чьё это соревнование» — обратный поиск при сверке и в проверках целостности.
[Index(nameof(OrgCompId))]
public class CompetitionSource
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    /// <summary>День соревнования, к которому привязан источник.</summary>
    public int CompetitionId { get; set; }

    [ForeignKey(nameof(CompetitionId))]
    public Competition? Competition { get; set; }

    /// <summary>
    /// compID соревнования на isr.org.il. FK нет по той же причине, что и у
    /// <see cref="CompetitionEntry.OrgCompId"/>: справочной строки у источника может не быть.
    /// </summary>
    public int OrgCompId { get; set; }

    /// <summary>
    /// Дата протокола источника. Дублируется сюда из «Входящих» намеренно: подтаб подписан
    /// датой, а <c>Sys_DiscoveredCompetitions</c> публичному пути не видна. null — привязка
    /// сделана руками для compID, которого во «Входящих» нет.
    /// </summary>
    public DateTime? SourceDate { get; set; }

    /// <summary>
    /// Имя протокола у федерации — уходит в тултип подтаба. На иврите, поэтому в подписи
    /// самого подтаба НЕ используется (правило «UI только English»): там дата и номер.
    /// </summary>
    [MaxLength(300)]
    public string? SourceName { get; set; }

    /// <summary>Порядок подтабов; при равенстве сортируем по <see cref="SourceDate"/>.</summary>
    public int SortOrder { get; set; }
}
