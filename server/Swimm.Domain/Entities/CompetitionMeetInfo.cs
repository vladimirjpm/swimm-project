namespace Swimm.Domain.Entities;

/// <summary>
/// Справка о старте, которого ещё не было: чемпионат ли это и во сколько разминка
/// (docs/plans/start-list-ticket-plan.md, шаг Т1).
///
/// Зачем отдельная таблица, а не поля в <see cref="Competition"/>: у предстоящего
/// соревнования строки в справочнике ещё НЕТ и до импорта протокола не будет — это
/// ключевое решение схемы стартового протокола (§3.1 плана, оно же держит
/// <c>BulkPullService</c> и «последнее соревнование» на главной). Идентичность здесь та же,
/// что у заявки, — <see cref="OrgCompId"/> (инвариант И7).
///
/// Зачем не <c>Sys_DiscoveredCompetitions</c>, где лежит остальное про найденный старт:
/// «Входящие» приватны (нет гранта swimm_ro), а эти два факта нужны ПУБЛИЧНОМУ read-пути —
/// из них таб Start list считает «во сколько приезжать». Таблица публичная, как
/// <see cref="CompetitionEntry"/>: приватного в ней нет.
/// </summary>
public class CompetitionMeetInfo
{
    /// <summary>compID соревнования на isr.org.il — первичный ключ. FK на
    /// <c>Competitions</c> нет намеренно, см. комментарий к классу.</summary>
    public int OrgCompId { get; set; }

    /// <summary>
    /// Чемпионат ли это ПО РЕГЛАМЕНТУ — то, что определил забор
    /// (<c>RegulationAnalyzer.IsChampionship</c>, вместе с его вето на «готовимся к
    /// чемпионату»). Перезабор идемпотентен и переписывает это поле сколько угодно раз.
    /// </summary>
    public bool IsChampionship { get; set; }

    /// <summary>
    /// Ручная правка администратора; null — «как определил забор».
    ///
    /// ⚠ Отдельное поле, а не правка <see cref="IsChampionship"/> на месте: забор запускают
    /// повторно до последнего дня (посев меняется), и он затёр бы ручное решение на
    /// следующем же прогоне. Тот же приём, что <c>Competition.StandingKindOverride</c>.
    /// Действующее значение — <see cref="ChampionshipEffective"/>.
    /// </summary>
    public bool? IsChampionshipOverride { get; set; }

    /// <summary>Регламент (תקנון), по которому проставлен флаг, — основание, которое админ
    /// может открыть и прочитать сам. null — регламента на loglig нет.</summary>
    public string? RegulationUrl { get; set; }

    /// <summary>Когда забор последний раз смотрел регламент. Отличает «регламента нет» от
    /// «мы туда ещё не ходили».</summary>
    public DateTime? RegulationCheckedAt { get; set; }

    /// <summary>Когда справку последний раз меняли — забором или руками. Админу нужно
    /// понимать, насколько свежее то, что он видит.</summary>
    public DateTime UpdatedAt { get; set; }

    /// <summary>Время разминки по дням — вводится РУКАМИ в админке (решение Влада
    /// 29.08.2026): регламенты федерации разношёрстные, и лишний автоматический источник
    /// кривых данных здесь не нужен.</summary>
    public ICollection<CompetitionWarmUp> WarmUps { get; set; } = new List<CompetitionWarmUp>();

    /// <summary>Что показывать: ручная правка сильнее забора. В модель EF не идёт
    /// (см. <c>Ignore</c> в конфигурации).</summary>
    public bool ChampionshipEffective => IsChampionshipOverride ?? IsChampionship;
}

/// <summary>
/// Время начала разминки в один день соревнования. Ключ — день, поэтому у многодневки
/// строк столько, сколько дней в программе.
/// </summary>
public class CompetitionWarmUp
{
    /// <summary>Часть составного ключа: соревнование.</summary>
    public int OrgCompId { get; set; }

    /// <summary>Часть составного ключа: календарный день. Как <c>CompetitionEntry.CompDate</c> —
    /// дата без времени и без пояса (<c>timestamp without time zone</c>).</summary>
    public DateTime Date { get; set; }

    /// <summary>
    /// Момент начала разминки (UTC). Админ вводит местное израильское время, перевод делает
    /// <c>IsraelTime.ToUtc</c> — один раз, как у <c>CompetitionEntry.HeatStartAt</c>: дальше
    /// по системе ходит момент времени, а не «часы на стене».
    /// </summary>
    public DateTime WarmUpAt { get; set; }

    public CompetitionMeetInfo? MeetInfo { get; set; }
}
