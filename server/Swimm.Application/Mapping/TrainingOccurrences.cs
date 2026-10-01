using System.Globalization;
using Swimm.Domain;

namespace Swimm.Application.Mapping;

/// <summary>
/// Занятия группы — слоты недельного расписания, разовые занятия («Ask who's coming») и отмены
/// (Р9, docs/plans/trainings-tab-redesign-plan.md) — чистые функции, «сейчас» приходит параметром
/// в МЕСТНОМ времени Израиля (как у <see cref="GroupTrainingSchedule.NextOccurrences"/>).
///
/// Занятие адресуется ключом <c>yyyy-MM-dd-HHmm</c> (<see cref="TrainingRsvpRules.SessionKey"/>):
/// разовое, совпавшее по дате и времени со слотом расписания, — ОДНО занятие; отмена тоже по
/// ключу и годится и слоту, и разовому. Какие тренировки считать разовыми (<c>AsksRsvp</c> и
/// заданное начало) и какие отмены брать — решает вызывающий (сервис): сюда приходят готовые списки.
/// </summary>
public static class TrainingOccurrences
{
    /// <summary>Разовое занятие — тренировка группы с галкой «Ask who's coming».</summary>
    public sealed record OneOff(int TrainingId, DateOnly Date, string Start, string? Title, string Type, string? PoolType);

    /// <summary>Отмена занятия на дату и начало.</summary>
    public sealed record Cancellation(DateOnly Date, string Start, string? Reason);

    /// <summary>
    /// Занятие. <see cref="OneOff"/> — оно только разовое (в расписании такого слота нет); разовое,
    /// совпавшее со слотом, — <c>false</c>, но <see cref="TrainingId"/> / <see cref="Title"/> /
    /// <see cref="Type"/> у него от тренировки, а <see cref="End"/> — от слота.
    /// </summary>
    public sealed record Occurrence(DateOnly Date, string Start, string? End, int? TrainingId, string? Title,
        string? Type, string? PoolType, bool OneOff, bool Cancelled, string? CancelReason);

    /// <summary>
    /// Занятие с этим ключом: слот расписания (по дню недели и <paramref name="start"/>) или разовое;
    /// null — такого занятия нет. Если разовых на ключ несколько, берётся заведённое раньше (меньший
    /// <see cref="OneOff.TrainingId"/>).
    /// </summary>
    public static Occurrence? Find(GroupTrainingSchedule schedule, IReadOnlyCollection<OneOff> oneOffs,
        IReadOnlyCollection<Cancellation> cancellations, DateOnly date, string start)
    {
        var slot = TrainingRsvpRules.FindSlot(schedule, date, start);
        var oneOff = oneOffs.Where(o => o.Date == date && o.Start == start)
            .OrderBy(o => o.TrainingId).FirstOrDefault();
        if (slot == null && oneOff == null) return null;

        var cancel = cancellations.FirstOrDefault(c => c.Date == date && c.Start == start);
        return new Occurrence(date, start, slot?.End, oneOff?.TrainingId, oneOff?.Title, oneOff?.Type,
            oneOff?.PoolType, OneOff: slot == null, Cancelled: cancel != null, CancelReason: cancel?.Reason);
    }

    /// <summary>
    /// <paramref name="count"/> ближайших НЕ отменённых занятий и отменённые между ними, по дате и
    /// началу. Начавшееся уже не ближайшее — как у <see cref="GroupTrainingSchedule.NextOccurrences"/>
    /// (то же правило для разовых). Дальше окна <see cref="TrainingRsvpRules.DaysAhead"/> занятия не
    /// берём: на них всё равно нельзя ответить. Отменённое после последнего взятого в список не
    /// попадает.
    /// </summary>
    public static List<Occurrence> Upcoming(GroupTrainingSchedule schedule, IReadOnlyCollection<OneOff> oneOffs,
        IReadOnlyCollection<Cancellation> cancellations, DateTime nowLocal, int count)
    {
        var result = new List<Occurrence>();
        if (count <= 0) return result;

        var today = DateOnly.FromDateTime(nowLocal);
        var last = today.AddDays(TrainingRsvpRules.DaysAhead);

        // Слотов берём с запасом на отмены: отменённое в счёт не идёт, и на его место нужен следующий.
        var cancelledAhead = cancellations.Count(c => c.Date >= today);
        var keys = schedule.NextOccurrences(nowLocal, count + cancelledAhead)
            .Select(n => (n.Date, n.Slot.Start))
            .ToHashSet();

        // Разовое — по дате + началу, «начавшееся — не ближайшее» (строго позже «сейчас»).
        foreach (var o in oneOffs)
        {
            if (!TimeOnly.TryParseExact(o.Start, "HH:mm", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var time)) continue;
            if (o.Date.ToDateTime(time) > nowLocal) keys.Add((o.Date, o.Start));
        }

        var taken = 0;
        foreach (var (date, start) in keys.Where(k => k.Date <= last)
                     .OrderBy(k => k.Date).ThenBy(k => k.Start, StringComparer.Ordinal))
        {
            var occurrence = Find(schedule, oneOffs, cancellations, date, start);
            if (occurrence == null) continue;
            result.Add(occurrence);
            if (!occurrence.Cancelled && ++taken == count) break;
        }
        return result;
    }
}
