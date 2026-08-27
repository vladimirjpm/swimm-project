using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Swimm.Domain.Entities;

/// <summary>Состояние заявки относительно результата (<see cref="CompetitionEntry.Status"/>).</summary>
public static class CompetitionEntryStatus
{
    /// <summary>Заявлен; протокол дня ещё не импортирован — про исход ничего не известно.</summary>
    public const string Entered = "entered";

    /// <summary>Проплыл: заявке нашёлся <see cref="CompetitionEntry.ResultId"/>.</summary>
    public const string Swum = "swum";

    /// <summary>Не явился: протокол дня загружен, а результата этой заявке не нашлось.</summary>
    public const string NoShow = "no-show";
}

/// <summary>
/// Заявка на заплыв — строка СТАРТОВОГО протокола: кто, в каком заплыве, на какой дорожке
/// и во сколько плывёт. План соревнования, а не его результат
/// (docs/plans/start-list-plan.md, решение В1 от 2026-08-27).
///
/// Почему отдельная таблица, а не строки в <c>Results</c> — тот же довод, по которому
/// отдельно живут <see cref="TrainingResult"/>: это данные другого происхождения, и
/// единственный надёжный способ не дать им протечь в рекорды, зачёт и медали — не класть
/// их в таблицу, из которой эти витрины считают. Плюс заявок ВСЕГДА больше, чем результатов
/// (замер соревнования 14208: записалось 1056, проплыло 989), а инвариант И1 требует
/// строгого равенства «строк в БД = строк в протоколе».
///
/// ⚠ Таблица ПУБЛИЧНАЯ (grant swimm_ro): это те же открытые протоколы федерации, что и
/// <c>Results</c>. Но приватность здесь другого класса — заявка говорит, где ребёнок БУДЕТ,
/// а не где он был; см. §8 плана.
/// </summary>
// Ключ идентичности при перезаборе: заплыв × дорожка × пловец. SwimmerId в ключе — в отличие
// от ResultMatchKey — обязателен: четыре ноги эстафеты делят один heat и одну lane.
[Index(nameof(OrgDisciplineId), nameof(Heat), nameof(Lane), nameof(SwimmerId), IsUnique = true)]
// Программа дня и «когда плывёт мой» — две выдачи, ради которых всё и затевалось.
[Index(nameof(OrgCompId), nameof(HeatStartAt))]
[Index(nameof(SwimmerId), nameof(HeatStartAt))]
// «Кто из клуба плывёт на этом старте».
[Index(nameof(ClubId), nameof(OrgCompId))]
[Index(nameof(CompetitionId))]
public class CompetitionEntry
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    /* === Идентичность соревнования === */

    /// <summary>
    /// compID соревнования на isr.org.il — идентичность заявки (инвариант И7: соревнование
    /// определяется <c>OrgCompId</c>, а не названием).
    ///
    /// FK тут сознательно НЕТ, и это главное решение схемы: у предстоящего соревнования в
    /// справочнике <c>Competitions</c> ещё нет строки, и заводить её заранее нельзя.
    /// Иначе <c>BulkPullService</c> перестанет видеть соревнование в списке «затянуть»
    /// (он фильтрует по <c>MatchedCompetitionId is null</c>), «последнее соревнование» на
    /// главной уедет в будущее, а проверка <c>competitions.no-club-point-rule</c> начнёт
    /// требовать правило очков за недели до старта. Разбор — §3.1 плана.
    /// </summary>
    public int OrgCompId { get; set; }

    /// <summary>
    /// День соревнования в справочнике — проставляется БЭКФИЛЛОМ по <see cref="OrgCompId"/>,
    /// когда протокол импортирован и справочная строка появилась. null — соревнование ещё
    /// не проходило либо не импортировано, и это нормальное состояние заявки.
    /// </summary>
    public int? CompetitionId { get; set; }

    [ForeignKey(nameof(CompetitionId))]
    public Competition? Competition { get; set; }

    /// <summary>Дата дня соревнования. Денормализована: справочника на момент заявки нет,
    /// а публичному read-пути неоткуда взять её — <c>Sys_DiscoveredCompetitions</c> ему
    /// недоступна (нет гранта swimm_ro).</summary>
    [Column(TypeName = "timestamp without time zone")]
    public DateTime CompDate { get; set; }

    /// <summary>Название соревнования на момент забора. Денормализовано по той же причине,
    /// что и <see cref="CompDate"/>; источником истины после импорта становится
    /// <see cref="Competition"/>.</summary>
    [MaxLength(500)]
    public string CompName { get; set; } = string.Empty;

    /* === Кто === */

    public int SwimmerId { get; set; }

    [ForeignKey(nameof(SwimmerId))]
    public Swimmer Swimmer { get; set; } = null!;

    /// <summary>Клуб НА МОМЕНТ ЗАЯВКИ: пловец мог сменить клуб между заявкой и стартом.</summary>
    public int ClubId { get; set; }

    [ForeignKey(nameof(ClubId))]
    public Club Club { get; set; } = null!;

    /* === Что плывёт === */

    public int StyleId { get; set; }

    [ForeignKey(nameof(StyleId))]
    public Style Style { get; set; } = null!;

    /// <summary>50, 100, 200… либо «4X50» у эстафеты — формат как в <see cref="ResultRecord.Distance"/>.</summary>
    [MaxLength(20)]
    public string Distance { get; set; } = string.Empty;

    /// <summary>male / female / none. Выводится из категории заплыва («בנות 10»), а не из
    /// отдельной графы: строкой источник пол не печатает.</summary>
    [MaxLength(20)]
    public string Gender { get; set; } = string.Empty;

    /// <summary>Категория заплыва как напечатана: «בנות 10», «בנים 8-9». Та же ось, что
    /// <see cref="ResultRecord.EventCategory"/>.</summary>
    [MaxLength(100)]
    public string? EventCategory { get; set; }

    /// <summary>Возрастная полоса из категории: «10», «8-9». Ось фильтра на витрине.</summary>
    [MaxLength(50)]
    public string? AgeBand { get; set; }

    /// <summary>Номер заплыва в программе дня («מספר משחה»). По нему ориентируются на бортике.</summary>
    public int? OrgEventNumber { get; set; }

    /// <summary>
    /// id заплыва на loglig (76321…) — не соревнования. Часть ключа идентичности: при
    /// перезаборе строки матчатся по нему, а не по нашему <see cref="Id"/>.
    /// </summary>
    public int OrgDisciplineId { get; set; }

    /* === Где и когда === */

    public int Heat { get; set; }

    /// <summary>Дорожка. У эстафеты одна дорожка на четыре строки-ноги — команду склеивает
    /// пара (<see cref="Heat"/>, <see cref="Lane"/>), названия команды источник не печатает.</summary>
    public int Lane { get; set; }

    /// <summary>
    /// Момент старта ЗАПЛЫВА в UTC. Источник печатает местное израильское время без часового
    /// пояса — перевод делается один раз на импорте, в зоне <c>Asia/Jerusalem</c>.
    /// Хранить строкой, как <see cref="Competition.Date"/>, тут нельзя: это время, на которое
    /// человек ставит будильник. null — заплыву ещё не назначили время.
    ///
    /// ⚠ Программа в бассейне регулярно отстаёт на 20–40 минут: на витрине время
    /// приблизительное, а ориентир — <see cref="OrgEventNumber"/> и <see cref="Heat"/>.
    /// </summary>
    public DateTime? HeatStartAt { get; set; }

    /// <summary>timed-final / prelim / final — та же ось, что <see cref="ResultRecord.Round"/>.</summary>
    [MaxLength(20)]
    public string? Round { get; set; }

    /* === Посевное время === */

    /// <summary>
    /// Посевное время в миллисекундах. ⚠ Это личный рекорд пловца С ДРУГОГО старта, по
    /// которому его посеяли, — НЕ результат этого соревнования. Третий класс качества времени
    /// (И11): показывать его в общем виде вместе с результатами без пометки нельзя.
    /// null — в протоколе «NT», пловец эту дистанцию ещё не плыл.
    /// </summary>
    public int? SeedTimeMs { get; set; }

    /// <summary>Посевное время как напечатано («01:42.72»); пустая строка — «NT».</summary>
    [MaxLength(50)]
    public string SeedTimeOriginal { get; set; } = string.Empty;

    /* === Связь с результатом === */

    /// <summary>
    /// Результат, которым эта заявка обернулась. Проставляется после импорта протокола дня
    /// по (день, дисциплина, заплыв, дорожка). null при <see cref="CompetitionEntryStatus.NoShow"/>
    /// — это и есть неявка, единственный источник ответа «почему заявлено 1056, а проплыло 989».
    /// </summary>
    public long? ResultId { get; set; }

    [ForeignKey(nameof(ResultId))]
    public ResultRecord? Result { get; set; }

    /// <summary>entered | swum | no-show (см. <see cref="CompetitionEntryStatus"/>).
    /// Считается по факту импорта протокола, а не по часам: «время старта прошло» ещё
    /// ничего не значит.</summary>
    [MaxLength(20)]
    public string Status { get; set; } = CompetitionEntryStatus.Entered;

    /// <summary>Когда строка последний раз подтверждена забором (UTC). Посев меняется до
    /// последнего дня — по этой метке витрина пишет «обновлено в HH:MM».</summary>
    public DateTime PulledAt { get; set; } = DateTime.UtcNow;
}
