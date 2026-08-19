using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Swimm.Domain.Entities;

/// <summary>
/// Денормализованная запись результата.
/// Содержит копии ключевых полей и FK к справочникам для JOIN с сущностями (Competition, Swimmer, Club, Relay, Style, Gallery).
/// Поля CompetitionDate, Distance и Gender хранятся inline для быстрых выборок и фильтрации.
/// </summary>
// (CompetitionId, CompetitionDate DESC, Position) — фильтр по соревнованию/событию с
// сортировкой публичной выдачи (ORDER BY CompetitionDate DESC, Position) одним index scan;
// без него планировщик на миллионах строк идёт по IX_CompetitionDate и фильтрует всю таблицу
// (замер фазы 3.3: 335 мс → 0.5 мс). Он же покрывает прежний одиночный IX по CompetitionId.
[Index(nameof(CompetitionId), nameof(CompetitionDate), nameof(Position), IsDescending = new[] { false, true, false })]
[Index(nameof(SwimmerId))]
[Index(nameof(StyleId), nameof(Distance))]
[Index(nameof(CompetitionId), nameof(StyleId), nameof(Distance), nameof(Gender))]
[Index(nameof(CompetitionId), nameof(TimeMillisecond))]
[Index(nameof(SwimmerId), nameof(TimeMillisecond))]
[Index(nameof(CompetitionDate))]
public class ResultRecord
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    /* === FK-ссылки на справочники === */

    public int CompetitionId { get; set; }

    [ForeignKey(nameof(CompetitionId))]
    public Competition Competition { get; set; } = null!;

    public int SwimmerId { get; set; }

    [ForeignKey(nameof(SwimmerId))]
    public Swimmer Swimmer { get; set; } = null!;

    public int ClubId { get; set; }

    [ForeignKey(nameof(ClubId))]
    public Club Club { get; set; } = null!;

    public int StyleId { get; set; }

    [ForeignKey(nameof(StyleId))]
    public Style Style { get; set; } = null!;

    public int? RelayId { get; set; }

    [ForeignKey(nameof(RelayId))]
    public Relay? Relay { get; set; }

    public int? GalleryId { get; set; }

    [ForeignKey(nameof(GalleryId))]
    public Gallery? Gallery { get; set; }

    public int? CountryId { get; set; }

    [ForeignKey(nameof(CountryId))]
    public Country? Country { get; set; }

    /* === Inline-поля для быстрых выборок === */

    public DateTime CompetitionDate { get; set; }

    /// <summary>50, 100, 200, 400, 800, 1500, 4x100, etc.</summary>
    [MaxLength(20)]
    public string Distance { get; set; } = string.Empty;

    /// <summary>male, female, mix</summary>
    [MaxLength(10)]
    public string Gender { get; set; } = string.Empty;

    /* === Поля результата === */

    /// <summary>Возрастная полоса протокола («9-10», «11-12») — ГРУБЕЕ реального заплыва.</summary>
    [MaxLength(50)]
    public string AgeGroup { get; set; } = string.Empty;

    /// <summary>
    /// Возраст события — настоящая ось заплыва. Девятилетние и десятилетние плывут РАЗНЫМИ
    /// заплывами со своими пьедесталами, хотя <see cref="AgeGroup"/> у них общий («9-10»).
    /// Поэтому два <see cref="Position"/> = 1 в одной <see cref="AgeGroup"/> — норма, а не дубль.
    /// Для эстафет тут возрастная полоса из заголовка (docs/relays.md).
    /// </summary>
    [MaxLength(20)]
    public string EventStyleAge { get; set; } = string.Empty;

    /// <summary>
    /// Место в протоколе своего заплыва — ОФИЦИАЛЬНОЕ: именно за него вручена медаль.
    /// На многодневных детских стартах медали дают за каждый день отдельно, поэтому одна
    /// дисциплина даёт несколько пьедесталов. Альтернатива — <see cref="CombinedPlace"/>
    /// (сравнительный вид поверх дней и возрастов), она официальной НЕ является.
    /// Полное объяснение — docs/competition-overview-cards.md, раздел «Почему у одного
    /// соревнования ДВА набора мест».
    /// </summary>
    public int? Position { get; set; }

    public int? PositionAgeGroup { get; set; }

    public int Heat { get; set; }

    public int Lane { get; set; }

    /// <summary>Время в миллисекундах (единый формат для сравнения и сортировки).</summary>
    public int? TimeMillisecond { get; set; }

    /// <summary>Оригинальная строка времени из протокола (как напечатано в источнике).</summary>
    [MaxLength(20)]
    public string TimeOriginal { get; set; } = string.Empty;

    [MaxLength(50)]
    public string TimeSplit { get; set; } = string.Empty;

    public bool TimeFail { get; set; }

    [MaxLength(100)]
    public string? TimeFailNote { get; set; }

    public int InternationalPoints { get; set; }

    // ── Объединённый зачёт «Combine All Results» ────────────────────────────
    // Заполняется ТОЛЬКО у соревнований с Competition.ShowCombineAllResults; у остальных null.
    // Считается сервером (CombinedPlaceCalculator) в границах события, чтобы клиенту не
    // требовался весь датасет — иначе тоггл недоступен в постраничном режиме.

    /// <summary>Место в общем зачёте дисциплины по ВСЕМУ событию: пловцы дисциплины
    /// ранжируются по лучшему времени поверх дней и заплывов. null — не рассчитывалось
    /// или заплыв незачтён.
    /// ⚠ Это СРАВНИТЕЛЬНЫЙ вид, а не официальный результат: медали и клубные очки идут по
    /// <see cref="Position"/>. См. docs/competition-overview-cards.md, раздел «Почему у одного
    /// соревнования ДВА набора мест».</summary>
    public int? CombinedPlace { get; set; }

    /// <summary>Этот заплыв — лучший у пловца в данной дисциплине за событие.
    /// Нужен, потому что повторы дисциплины реально встречаются (замер 2026-07-27:
    /// 9 групп, обычно валидный заплыв + DNS/DSQ в другой день).</summary>
    public bool? IsBestResult { get; set; }

    /// <summary>Лучшее время пловца в этой дисциплине за событие, мс.</summary>
    public int? BestTimeMs { get; set; }

    [MaxLength(500)]
    public string? Note { get; set; }

    /// <summary>
    /// Категория заплыва из заголовка протокола: <c>open</c> (Men/Women), <c>para</c>,
    /// <c>mix</c>, либо возрастная категория («17», «12», «25-29»). null — неизвестна
    /// (данные импортированы до появления поля).
    ///
    /// Нужна потому, что «дисциплина» = стиль × дистанция × пол × КАТЕГОРИЯ. Без неё три
    /// разных заплыва Маккабиады — «50m Freestyle - Men», «- U17 Boys» и «- Men Para» —
    /// сливались в одну дисциплину с тремя первыми местами, и Para-пловцы приносили клубам
    /// золото наравне с основной программой. <see cref="AgeGroup"/>/<see cref="EventStyleAge"/>
    /// для этого не годятся: они производны от года рождения пловца.
    /// </summary>
    [MaxLength(20)]
    public string? EventCategory { get; set; }

    /// <summary>
    /// Тип заплыва: <c>prelim</c> — предварительный, <c>final</c> — финал. null — заплыв
    /// единственный в своей дисциплине за день (timed final) либо данные импортированы
    /// до появления поля.
    ///
    /// В протоколах loglig слов «מוקדמות/גמר» нет — признак выводится на импорте по порядку
    /// сессий: если одна дисциплина (стиль × дистанция × пол × категория) встречается в один
    /// день дважды, раннее событие — предварительные, позднее — финал. На нём держатся
    /// <c>PointRuleSwimmers.FinalsOnly</c> (High Point бугрим п.16) и правило качества
    /// «дубль дисциплины в один день» (prelim+final — норма, не дубль).
    /// </summary>
    [MaxLength(20)]
    public string? HeatType { get; set; }

    /// <summary>
    /// РАУНД зачёта — секция протокола, к которой строка относится у организатора:
    /// <c>timed-final</c> (גמר ישיר — утренний зачёт возрастных групп), <c>final</c> (גמר —
    /// вечерний финал первенства), <c>prelim</c> (מוקדמות). null — источник раундов не
    /// различает (PDF-экспорт loglig) либо строка импортирована до появления поля.
    ///
    /// Зачем ОТДЕЛЬНО от <see cref="HeatType"/>, хотя значения похожи. HeatType — наш ВЫВОД
    /// об отборе: «этот заплыв был отборочным к тому», и на нём висит Р34 «место в
    /// предварительном — не награда». Round — то, что напечатал ИСТОЧНИК. У чемпионата
    /// «мокдамот и финал» (регламент loglig doc 3311) утренний и вечерний заплывы — ДВА
    /// САМОСТОЯТЕЛЬНЫХ зачёта: медали возрастных групп вручают утром, первенство разыгрывают
    /// вечером, и клубные очки платят ОБА. Записать утро как HeatType=prelim значило бы
    /// погасить официальные медали правилом Р34 — поэтому поля разные.
    ///
    /// Входит в ключ upsert (<see cref="Swimm.Infrastructure.Services.ResultMatcher"/>):
    /// без этого утренняя и вечерняя строки одного пловца схлопываются в одну и вторая
    /// затирает первую. Инвариант И13, разбор — docs/data-integrity.md §10.
    /// </summary>
    [MaxLength(20)]
    public string? Round { get; set; }

    // ── Качество данных ─────────────────────────────────────────────────────

    /// <summary>
    /// Строка выглядит недостоверной: null — вопросов нет, иначе КОД причины
    /// (<see cref="Swimm.Domain.SuspectReasons"/>). Заводилась под ошибки САМОГО источника,
    /// которые нельзя ни исправить, ни выбросить: напр. в протоколе Маккабиады у Elisa
    /// MOSHKOVITCH на 100 м баттерфляем напечатано 00:32.59 (её же полтинник — 27.27),
    /// и организаторы посчитали из этого времени 4702 очка. Такой заплыв «бьёт»
    /// национальный рекорд и портит рекорды/очки, хотя данные приехали корректно.
    ///
    /// Помеченный результат НЕ скрывается и НЕ удаляется — он остаётся в протоколе,
    /// но исключается из детекции рекордов.
    /// </summary>
    [MaxLength(40)]
    public string? SuspectReason { get; set; }

    /// <summary>
    /// Пометку поставил человек (<c>manual</c>), а не автопроверка. Такие переживают
    /// переимпорт, автоматические — пересчитываются заново.
    /// </summary>
    public bool SuspectIsManual { get; set; }

    /// <summary>Пояснение к пометке: чем именно строка подозрительна.</summary>
    [MaxLength(300)]
    public string? SuspectNote { get; set; }
}
