using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Swimm.Domain.Entities;

// Серия турнира и официальное награждение по её регламенту (docs/plans/official-awards-plan.md §5).
// Турнир повторяется из года в год с теми же правилами — поэтому награды живут на серии, а не на
// конкретном соревновании: новый выпуск привязывается к части серии и сам их получает.

/// <summary>
/// Повторяющийся турнир («רודובסקי/נימרי», «Red Sea»): выпуски разных лет — это его дни
/// (<see cref="Competition.SeriesPartId"/>), а не похожие названия — названия у выпусков разные.
/// </summary>
public class CompetitionSeries
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    /// <summary>Имя серии на иврите (как у федерации).</summary>
    [MaxLength(300)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(300)]
    public string? NameEn { get; set; }

    /// <summary>
    /// Ключевые фразы для узнавания нового выпуска при затягивании, через «;» («רודובסקי ועדן
    /// נימרי»). Выпуск подходит, если его нормализованное название СОДЕРЖИТ фразу (§5.8 плана).
    /// Пусто — серия сама не узнаётся, только ручная привязка.
    /// </summary>
    [MaxLength(500)]
    public string? MatchPhrases { get; set; }

    public ICollection<CompetitionSeriesPart> Parts { get; set; } = [];
    public ICollection<SeriesRegulation> Regulations { get; set; } = [];

    /// <summary>Фразы <see cref="MatchPhrases"/> списком, без пустых.</summary>
    public IReadOnlyList<string> MatchPhraseList() =>
        (MatchPhrases ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

/// <summary>
/// Часть турнира — смысл дня («נוער ובוגרים», «צעירים»), а не его номер: порядок дней между
/// выпусками может отличаться. У однодневного турнира часть одна.
/// </summary>
public class CompetitionSeriesPart
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    public int SeriesId { get; set; }

    [ForeignKey(nameof(SeriesId))]
    public CompetitionSeries Series { get; set; } = null!;

    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Подпись части на витрине («Juniors &amp; Seniors»): видимый UI — только английский.</summary>
    [MaxLength(200)]
    public string? NameEn { get; set; }

    public int SortOrder { get; set; }

    /// <summary>
    /// Кто плывёт в этот день — сезонный возраст (<see cref="SeasonMath.AgeInSeason"/>); null — без
    /// границы. Нужно только для узнавания части у нового выпуска (§5.8). Это НЕ полосы наград: у
    /// денежной награды 23.10 полос нет совсем.
    /// </summary>
    public int? MinSeasonAge { get; set; }
    public int? MaxSeasonAge { get; set; }
}

/// <summary>
/// Версия регламента серии. Выпуск считается по версии с самым поздним <see cref="EffectiveFrom"/>,
/// не позже даты дня. Регламент меняется документом целиком — поэтому версия на нём, а не на
/// каждой награде.
/// </summary>
public class SeriesRegulation
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    public int SeriesId { get; set; }

    [ForeignKey(nameof(SeriesId))]
    public CompetitionSeries Series { get; set; } = null!;

    public DateOnly EffectiveFrom { get; set; }

    /// <summary>PDF регламента (תקנון) — ссылка «per meet regulations» на карточке.</summary>
    [MaxLength(500)]
    public string? SourceUrl { get; set; }

    [MaxLength(300)]
    public string? Description { get; set; }

    public ICollection<SeriesAward> Awards { get; set; } = [];
}

/// <summary>Вид награды регламента (<see cref="SeriesAward.Kind"/>).</summary>
public static class SeriesAwardKinds
{
    /// <summary>Сумма очков FINA (InternationalPoints) N лучших личных стартов.</summary>
    public const string PointsSum = "points-sum";
}

/// <summary>Одна награда регламента: кубки младшим по группам, деньги лучшим взрослым…</summary>
public class SeriesAward
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    public int RegulationId { get; set; }

    [ForeignKey(nameof(RegulationId))]
    public SeriesRegulation Regulation { get; set; } = null!;

    /// <summary>Часть турнира (день), на которой вручается награда.</summary>
    public int PartId { get; set; }

    [ForeignKey(nameof(PartId))]
    public CompetitionSeriesPart Part { get; set; } = null!;

    /// <summary>См. <see cref="SeriesAwardKinds"/>. Пока считается только points-sum.</summary>
    [MaxLength(20)]
    public string Kind { get; set; } = SeriesAwardKinds.PointsSum;

    /// <summary>Заголовок блока на карточке, по-английски («Outstanding young swimmers»).</summary>
    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    /// <summary>Сколько лучших стартов пловца идут в сумму; null — все.</summary>
    public int? CountBest { get; set; }

    /// <summary>Девушки и юноши — раздельно.</summary>
    public bool SplitByGender { get; set; } = true;

    /// <summary>Сколько призёров в группе: 1 у кубков, 3 у денег.</summary>
    public int Places { get; set; } = 1;

    public int SortOrder { get; set; }

    /// <summary>Группы по сезонному возрасту; нет строк — все возраста вместе.</summary>
    public ICollection<SeriesAwardBand> Bands { get; set; } = [];

    /// <summary>Приз за место — только подпись.</summary>
    public ICollection<SeriesAwardPrize> Prizes { get; set; } = [];
}

/// <summary>
/// Возрастная группа награды. Возраст СЕЗОННЫЙ: годы рождения из регламента каждый сезон
/// сдвигаются, а возрастные полосы держатся (как у категорий, MinAge/MaxAge).
/// </summary>
public class SeriesAwardBand
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    public int AwardId { get; set; }

    [ForeignKey(nameof(AwardId))]
    public SeriesAward Award { get; set; } = null!;

    /// <summary>"male" | "female" (как Results.Gender); null — оба пола.</summary>
    [MaxLength(10)]
    public string? Gender { get; set; }

    /// <summary>null — без нижней границы.</summary>
    public int? MinSeasonAge { get; set; }

    /// <summary>null — «и старше».</summary>
    public int? MaxSeasonAge { get; set; }
}

/// <summary>Приз за место: «1 000 ₪ + Cup», «Cup». Текстом — считать с ним нечего.</summary>
public class SeriesAwardPrize
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    public int AwardId { get; set; }

    [ForeignKey(nameof(AwardId))]
    public SeriesAward Award { get; set; } = null!;

    public int Place { get; set; }

    [MaxLength(200)]
    public string Text { get; set; } = string.Empty;
}
