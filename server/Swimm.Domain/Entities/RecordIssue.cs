using System.ComponentModel.DataAnnotations;

namespace Swimm.Domain.Entities;

/// <summary>
/// Спорная запись официального справочника рекордов (<see cref="Record"/>).
///
/// ⚠ Ошибки источника мы НЕ чиним: наша копия обязана совпадать с файлом федерации, иначе
/// следующий импорт молча вернёт всё назад, а расхождение с их сайтом будет выглядеть как
/// наш баг. Вместо правки — запись в этом реестре, метка в UI и, когда наберётся список,
/// письмо в федерацию. См. docs/plans/records-quality-plan.md.
///
/// Ключ — 8 осей рекорда ПЛЮС <see cref="FlaggedTime"/>: метка висит на конкретном значении,
/// а не на клетке лестницы. Когда рекорд побьют, время в <see cref="Record"/> сменится, и
/// старая претензия автоматически перестанет относиться к текущей записи (её видно как
/// историю, но она не помечает новое достижение).
///
/// Одна ошибка источника может давать НЕСКОЛЬКО строк в <c>Records</c>: лестница
/// федерации кумулятивная — рекорд переносится вверх по возрастам, пока его не побьют
/// (62 записи из 688 растянуты на 2–4 ступени). Заводить issue на каждую строку не нужно,
/// достаточно на ту ступень, где достижение реально установлено.
/// </summary>
public class RecordIssue
{
    public int Id { get; set; }

    /* ── Ось рекорда: те же 8 полей, что образуют уникальный ключ Records ── */

    [MaxLength(10)]
    public string RegionType { get; set; } = string.Empty;

    [MaxLength(10)]
    public string RegionCode { get; set; } = string.Empty;

    [MaxLength(20)]
    public string Category { get; set; } = string.Empty;

    [MaxLength(20)]
    public string AgeKey { get; set; } = string.Empty;

    [MaxLength(10)]
    public string Gender { get; set; } = string.Empty;

    [MaxLength(10)]
    public string PoolType { get; set; } = string.Empty;

    [MaxLength(50)]
    public string Style { get; set; } = string.Empty;

    [MaxLength(20)]
    public string Distance { get; set; } = string.Empty;

    /// <summary>Оспариваемое время — строкой, как в источнике («34.08», «01:43.45»).</summary>
    [MaxLength(20)]
    public string FlaggedTime { get; set; } = string.Empty;

    /* ── Претензия ── */

    /// <summary>Код причины — см. <see cref="RecordIssueReasons"/>.</summary>
    [MaxLength(40)]
    public string Reason { get; set; } = RecordIssueReasons.Manual;

    /// <summary>Жизненный цикл — см. <see cref="RecordIssueStatuses"/>.</summary>
    [MaxLength(20)]
    public string Status { get; set; } = RecordIssueStatuses.Open;

    /// <summary>Доказательство человеческим языком: почему считаем запись спорной.</summary>
    public string Note { get; set; } = string.Empty;

    /// <summary>Кто завёл (email/имя админа) либо «auto» для находок автопроверки.</summary>
    [MaxLength(200)]
    public string CreatedBy { get; set; } = "auto";

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}

/// <summary>Причины, по которым запись попала в реестр (правила §3 плана).</summary>
public static class RecordIssueReasons
{
    /// <summary>Завёл человек.</summary>
    public const string Manual = "manual";

    /// <summary>Длинная вода быстрее короткой на той же оси — физически невозможно.</summary>
    public const string LongCourseFasterThanShort = "lcm-faster-than-scm";

    /// <summary>Рекорд ступени быстрее рекорда старшей ступени — лестница кумулятивна.</summary>
    public const string NonMonotonicLadder = "non-monotonic-ladder";

    /// <summary>
    /// Национальный / возрастной / мастерс-рекорд быстрее мирового той же дисциплины —
    /// абсолютный рекорд потому и абсолютный. Находка сторожа импорта (И-20).
    /// </summary>
    public const string FasterThanWorldRecord = "faster-than-world-record";

    /// <summary>
    /// Мировой рекорд улучшен за раз больше порога (<c>RecordPlausibility.WorldMaxImprovement</c>).
    /// Живой случай — «40.11» на 100 в/с ж 50 м против 51.68 (И-20).
    /// </summary>
    public const string ImplausibleImprovement = "implausible-improvement";

    /// <summary>
    /// Строка справочника стала МЕДЛЕННЕЕ той, что уже лежит в базе. Рекорды назад не ходят:
    /// значит источник потерял прежнего держателя или переписал строку. Живой случай — И-21:
    /// федерация заменила 200 брасс ж 70-74 (03:50.05, 2019) на 04:34.46 (2022), который
    /// медленнее её же рекорда полосы 75-79.
    /// </summary>
    public const string SlowerThanStored = "slower-than-stored";

    public static readonly string[] All =
    [
        Manual, LongCourseFasterThanShort, NonMonotonicLadder, FasterThanWorldRecord,
        ImplausibleImprovement, SlowerThanStored,
    ];
}

/// <summary>
/// Статусы претензии. ⚠ «Не найдено в протоколах» статусом НЕ является: у нас загружены
/// не все годы, отсутствие заплыва ничего не доказывает (см. <see cref="RecordVerification"/>).
/// </summary>
public static class RecordIssueStatuses
{
    /// <summary>
    /// Предложено автопроверкой при импорте, человек ещё не смотрел. ⚠ На сайте НЕ
    /// показывается: публичные выборки берут только open / reported / accepted. Правило плана
    /// (records-quality-plan.md §3): автомат ничего не помечает сам, иначе метка «спорно»
    /// обесценится — он только предлагает, статус ставит человек.
    /// </summary>
    public const string Candidate = "candidate";

    /// <summary>Заведено, в федерацию не сообщено.</summary>
    public const string Open = "open";

    /// <summary>Сообщено в федерацию, ждём ответа.</summary>
    public const string Reported = "reported";

    /// <summary>Федерация признала ошибку.</summary>
    public const string Accepted = "accepted";

    /// <summary>Разобрались — запись верна, претензия снята.</summary>
    public const string Rejected = "rejected";

    /// <summary>В источнике уже исправлено.</summary>
    public const string FixedBySource = "fixed-by-source";

    public static readonly string[] All = [Candidate, Open, Reported, Accepted, Rejected, FixedBySource];

    /// <summary>
    /// Статусы, при которых импорт НЕ БЕРЁТ это значение из источника: человек уже разобрался,
    /// что оно ошибочно, и защищает то, что лежит у нас (решение Влада 17.09.2026, И-25).
    ///
    /// ⚠ Это единственное сознательное отступление от правила «наша копия обязана совпадать с
    /// файлом федерации». Повод — живой случай: справочник от 17.09.2026 заменил рекорд 17 ж
    /// 50 в/с 50 м 25.03 (11.06.2021, подтверждён World Aquatics) на 25.23 — заплыв декабря
    /// 2020, который медленнее. Метка в реестре тут не спасает: она метит значение, а Apply
    /// всё равно перезаписал бы верное число неверным, и рекорд был бы потерян до того, как
    /// федерация исправит файл.
    ///
    /// <see cref="Candidate"/> сюда НЕ входит: автомат только предлагает, решает человек
    /// (правило §3 records-quality-plan). <see cref="Rejected"/> и <see cref="FixedBySource"/>
    /// тоже нет — там значение источника как раз признано верным.
    /// </summary>
    public static readonly string[] SourceOverruled = [Open, Reported, Accepted];
}
