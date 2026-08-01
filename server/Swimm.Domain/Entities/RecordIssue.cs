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

    public static readonly string[] All =
        [Manual, LongCourseFasterThanShort, NonMonotonicLadder];
}

/// <summary>
/// Статусы претензии. ⚠ «Не найдено в протоколах» статусом НЕ является: у нас загружены
/// не все годы, отсутствие заплыва ничего не доказывает (см. <see cref="RecordVerification"/>).
/// </summary>
public static class RecordIssueStatuses
{
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

    public static readonly string[] All = [Open, Reported, Accepted, Rejected, FixedBySource];
}
