using System.Globalization;
using Swimm.Domain;

namespace Swimm.Application.Mapping;

/// <summary>
/// Правила ответов на тренировку (docs/plans/entity-hero-roles-plan.md, Ш2) — чистые функции,
/// «сейчас» приходит параметром в МЕСТНОМ времени Израиля (как у
/// <see cref="GroupTrainingSchedule.NextOccurrence"/>).
///
/// Занятие адресуется ключом <c>yyyy-MM-dd-HHmm</c> («2026-09-29-2000»): дата и начало слота
/// по стенным часам. Ключ без двоеточий — он живёт в пути URL.
/// </summary>
public static class TrainingRsvpRules
{
    /// <summary>На сколько дней вперёд можно отвечать: дальше — не планы, а гадание.</summary>
    public const int DaysAhead = 28;

    /// <summary>
    /// Сколько дней после занятия его ответы ещё может править управляющий — поправить «кто
    /// пришёл» по факту. Участнику после начала занятия ответ уже не менять.
    /// </summary>
    public const int ManagerDaysBack = 7;

    /// <summary>Потолок причины отмены занятия (Р9) — символов.</summary>
    public const int CancelReasonMax = 200;

    /// <summary>Сколько ближайших занятий отдаёт Next (<c>GET …/next-trainings</c>, не считая отменённых).</summary>
    public const int NextCount = 3;

    public static string SessionKey(DateOnly date, string start) =>
        $"{date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}-{start.Replace(":", "")}";

    /// <summary>Разбор ключа занятия; false — не ключ (формат, дата или время кривые).</summary>
    public static bool TryParseSessionKey(string? key, out DateOnly date, out string start)
    {
        date = default;
        start = "";
        if (key is not { Length: 15 } || key[10] != '-') return false;
        if (!DateOnly.TryParseExact(key[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
            return false;
        if (!TimeOnly.TryParseExact(key[11..], "HHmm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
            return false;
        start = time.ToString("HH:mm", CultureInfo.InvariantCulture);
        return true;
    }

    /// <summary>Слот расписания, которому отвечает занятие; null — такого занятия в расписании нет.</summary>
    public static GroupTrainingSlot? FindSlot(GroupTrainingSchedule schedule, DateOnly date, string start)
    {
        var isoDay = date.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)date.DayOfWeek;
        return schedule.Slots.FirstOrDefault(s => s.IsValid && s.Day == isoDay && s.Start == start);
    }

    /// <summary>
    /// Можно ли СМОТРЕТЬ ответы на это занятие: оно есть (слот расписания или разовое —
    /// <see cref="TrainingOccurrences.Find"/>) и лежит в окне
    /// [сегодня − <see cref="ManagerDaysBack"/>; сегодня + <see cref="DaysAhead"/>]. Отменённое
    /// смотреть можно: ответы при отмене не удаляются, а тренеру нужно видеть, что отменил.
    /// </summary>
    public static bool IsViewable(TrainingOccurrences.Occurrence? occurrence, DateTime nowLocal)
    {
        if (occurrence == null) return false;
        var today = DateOnly.FromDateTime(nowLocal);
        return occurrence.Date >= today.AddDays(-ManagerDaysBack) && occurrence.Date <= today.AddDays(DaysAhead);
    }

    /// <summary>
    /// Можно ли МЕНЯТЬ ответ. Участник — пока занятие не началось; управляющий — ещё
    /// <see cref="ManagerDaysBack"/> дней после (поправить по факту). Вперёд — не дальше
    /// <see cref="DaysAhead"/> дней. Отменённое — нельзя никому. null — можно, иначе причина для
    /// ответа 400.
    /// </summary>
    public static string? EditBlockReason(TrainingOccurrences.Occurrence? occurrence, DateTime nowLocal, bool isManager)
    {
        if (occurrence == null) return "There is no such training in the schedule.";
        if (occurrence.Cancelled) return "This training is cancelled.";

        var date = occurrence.Date;
        var today = DateOnly.FromDateTime(nowLocal);
        if (date > today.AddDays(DaysAhead)) return $"You can answer at most {DaysAhead} days ahead.";

        var startsAt = date.ToDateTime(TimeOnly.ParseExact(occurrence.Start, "HH:mm", CultureInfo.InvariantCulture));
        if (isManager)
            return date < today.AddDays(-ManagerDaysBack) ? "This training is too old to change." : null;
        return nowLocal >= startsAt ? "This training has already started." : null;
    }
}
