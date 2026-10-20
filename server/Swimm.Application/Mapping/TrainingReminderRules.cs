using System.Globalization;
using Swimm.Domain;
using Swimm.Domain.Entities;

namespace Swimm.Application.Mapping;

/// <summary>
/// «Кому и когда» напоминать о тренировке (docs/plans/web-push-reminders-plan.md §7, решения
/// В1–В6 от 20.10.2026) — чистые функции, «сейчас» приходит параметром в МЕСТНОМ времени
/// Израиля, как у <see cref="TrainingOccurrences"/>. Данные собирает сервис
/// (<c>TrainingReminderService</c>), здесь только правило — его и проверяют юнит-тесты.
///
/// Окна «ровно за 2 часа ± период джоба» нет нарочно: напоминание уходит, когда его время
/// НАСТУПИЛО и занятие ещё не началось, а повтор отсекает журнал доставки. Так пропущенный тик
/// (рестарт, деплой) не теряет напоминание, а лишний — не дублирует.
/// </summary>
public static class TrainingReminderRules
{
    /// <summary>Как часто джоб смотрит, не пора ли (тик фоновой службы).</summary>
    public static readonly TimeSpan Tick = TimeSpan.FromMinutes(5);

    /// <summary>Тихие часы (В5): с 22:00 до 07:00 не шлём.</summary>
    public static readonly TimeOnly QuietFrom = new(22, 0);
    public static readonly TimeOnly QuietUntil = new(7, 0);

    /// <summary>Куда переносится напоминание, попавшее в тихие часы, — 21:00 вечера перед ними (В5).</summary>
    public static readonly TimeOnly QuietMoveTo = new(21, 0);

    /// <summary>«Вечером накануне» — в 20:00 (В1).</summary>
    public static readonly TimeOnly EveningAt = new(20, 0);

    /// <summary>
    /// На сколько дней вперёд смотреть занятия: самое раннее напоминание — вечер накануне или
    /// 21:00 перед тихими часами, то есть не больше суток с небольшим до начала.
    /// </summary>
    public const int DaysAhead = 2;

    /// <summary>Сколько дней держим журнал доставки: защите от дубля нужны только будущие занятия.</summary>
    public const int DeliveryKeepDays = 30;

    public static bool IsQuiet(TimeOnly time) => time >= QuietFrom || time < QuietUntil;

    /// <summary>
    /// Когда напомнить о занятии, начинающемся в <paramref name="startLocal"/>: за 1 / 2 часа или
    /// в 20:00 накануне; попало в тихие часы — 21:00 вечера перед ними (22:30 → 21:00 того же
    /// дня, 04:00 → 21:00 предыдущего). Неизвестный вид — как по умолчанию (за 2 часа).
    /// </summary>
    public static DateTime DueAt(DateTime startLocal, string? lead)
    {
        var due = lead switch
        {
            ReminderLead.OneHour => startLocal.AddHours(-1),
            ReminderLead.Evening => DateOnly.FromDateTime(startLocal).AddDays(-1).ToDateTime(EveningAt),
            _ => startLocal.AddHours(-2),
        };

        var time = TimeOnly.FromDateTime(due);
        if (!IsQuiet(time)) return due;
        var day = DateOnly.FromDateTime(due);
        return (time >= QuietFrom ? day : day.AddDays(-1)).ToDateTime(QuietMoveTo);
    }

    /// <summary>Группа с расписанием, разовыми занятиями и отменами на окно <see cref="DaysAhead"/>.</summary>
    public sealed record Group(
        int HubGroupId, string Name, string Slug, GroupTrainingSchedule Schedule,
        IReadOnlyCollection<TrainingOccurrences.OneOff> OneOffs,
        IReadOnlyCollection<TrainingOccurrences.Cancellation> Cancellations);

    /// <summary>Настройки человека; нет строки — <see cref="Default"/> (В6: включены, за 2 часа).</summary>
    public sealed record Settings(bool Enabled, string Lead)
    {
        public static readonly Settings Default = new(true, ReminderLead.Default);
    }

    /// <summary>
    /// Всё, что правилу надо знать о людях. <see cref="OnBreak"/> — по дням (перерыв может
    /// кончиться завтра); <see cref="Answers"/> — ответы аккаунтов по ключу
    /// (UserId, дата, начало); <see cref="Delivered"/> — что уже ушло (журнал доставки).
    /// </summary>
    public sealed record People(
        IReadOnlyDictionary<int, IReadOnlyCollection<int>> ActiveMembers,
        IReadOnlySet<int> UsersWithDevices,
        IReadOnlyDictionary<int, Settings> Settings,
        IReadOnlySet<(int UserId, int HubGroupId)> Muted,
        IReadOnlyDictionary<(int HubGroupId, DateOnly Date), IReadOnlySet<int>> OnBreak,
        IReadOnlyDictionary<(int HubGroupId, int UserId, DateOnly Date, string Start), string> Answers,
        IReadOnlySet<(int UserId, int HubGroupId, DateOnly Date, string Start)> Delivered);

    /// <summary>Одно напоминание к отправке. <see cref="Answer"/> — ответ человека (yes / maybe) или null.</summary>
    public sealed record Planned(
        int UserId, Group Group, TrainingOccurrences.Occurrence Occurrence, DateTime StartLocal, string? Answer);

    /// <summary>
    /// Не отменённые занятия группы, которые начинаются позже <paramref name="nowLocal"/> и не дальше
    /// <see cref="DaysAhead"/> дней: слоты расписания и разовые (<see cref="TrainingOccurrences.Find"/>).
    /// </summary>
    public static List<(TrainingOccurrences.Occurrence Occurrence, DateTime StartLocal)> UpcomingStarts(
        Group group, DateTime nowLocal)
    {
        var result = new List<(TrainingOccurrences.Occurrence, DateTime)>();
        var today = DateOnly.FromDateTime(nowLocal);
        for (var delta = 0; delta <= DaysAhead; delta++)
        {
            var date = today.AddDays(delta);
            var isoDay = date.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)date.DayOfWeek;
            var starts = group.Schedule.Slots.Where(s => s.IsValid && s.Day == isoDay).Select(s => s.Start)
                .Concat(group.OneOffs.Where(o => o.Date == date).Select(o => o.Start))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(s => s, StringComparer.Ordinal);
            foreach (var start in starts)
            {
                if (!TimeOnly.TryParseExact(start, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None,
                        out var time)) continue;
                var startLocal = date.ToDateTime(time);
                if (startLocal <= nowLocal) continue;
                var occurrence = TrainingOccurrences.Find(group.Schedule, group.OneOffs, group.Cancellations, date, start);
                if (occurrence is null or { Cancelled: true }) continue;
                result.Add((occurrence, startLocal));
            }
        }
        return result;
    }

    /// <summary>
    /// Кому слать прямо сейчас (В2): активному участнику-аккаунту с хотя бы одним устройством,
    /// у которого напоминания включены, группа не отключена, он не на перерыве и не «не тренируется»
    /// в день занятия, не ответил «No» и ещё не получил напоминание об этом занятии; время
    /// напоминания наступило, занятие не началось, и сейчас не тихие часы (опоздавшее из-за простоя
    /// напоминание ночью не будит — дождётся 07:00, если занятие ещё впереди). Одно напоминание на
    /// человека и занятие: участник — аккаунт, сколько бы детей семьи ни стояло за ним.
    /// </summary>
    public static List<Planned> Plan(IReadOnlyCollection<Group> groups, People people, DateTime nowLocal)
    {
        var planned = new List<Planned>();
        if (IsQuiet(TimeOnly.FromDateTime(nowLocal))) return planned;

        foreach (var group in groups)
        {
            if (!people.ActiveMembers.TryGetValue(group.HubGroupId, out var members) || members.Count == 0) continue;

            foreach (var (occurrence, startLocal) in UpcomingStarts(group, nowLocal))
            {
                people.OnBreak.TryGetValue((group.HubGroupId, occurrence.Date), out var onBreak);
                foreach (var userId in members.Distinct())
                {
                    if (!people.UsersWithDevices.Contains(userId)) continue;
                    var settings = people.Settings.GetValueOrDefault(userId) ?? Settings.Default;
                    if (!settings.Enabled) continue;
                    if (people.Muted.Contains((userId, group.HubGroupId))) continue;
                    if (onBreak?.Contains(userId) == true) continue;
                    if (DueAt(startLocal, settings.Lead) > nowLocal) continue;
                    if (people.Delivered.Contains((userId, group.HubGroupId, occurrence.Date, occurrence.Start))) continue;

                    people.Answers.TryGetValue((group.HubGroupId, userId, occurrence.Date, occurrence.Start), out var answer);
                    if (answer == TrainingRsvpAnswer.No) continue;
                    planned.Add(new Planned(userId, group, occurrence, startLocal, answer));
                }
            }
        }
        return planned;
    }

    /// <summary>Что показать в уведомлении. Текст — английский (правило UI), имя группы — как в базе (данные).</summary>
    public sealed record Message(string Title, string Body, string Url, string Tag);

    /// <summary>
    /// «Training today at 18:00 · Wingate» и «Are you coming?», если человек ещё не ответил. Тап
    /// открывает таб Trainings группы — адрес зеркалит <c>routes.group(slug)</c> клиента + <c>?tab=</c>.
    /// <see cref="Message.Tag"/> — ключ занятия: повтор заменяет уведомление, а не копит.
    /// </summary>
    public static Message Compose(Planned p, DateTime nowLocal)
    {
        var o = p.Occurrence;
        var today = DateOnly.FromDateTime(nowLocal);
        var when = o.Date == today ? "today"
            : o.Date == today.AddDays(1) ? "tomorrow"
            : "on " + o.Date.ToString("dddd", CultureInfo.InvariantCulture);
        var what = string.IsNullOrWhiteSpace(o.Title) ? "Training" : o.Title.Trim();
        var place = p.Group.Schedule.Place is { Length: > 0 } pl ? $" · {pl.Trim()}" : "";
        var body = $"{what} {when} at {o.Start}{place}";
        body += p.Answer is null ? ". Are you coming?" : p.Answer == TrainingRsvpAnswer.Maybe ? ". You said maybe." : "";

        var key = TrainingRsvpRules.SessionKey(o.Date, o.Start);
        return new Message(
            p.Group.Name,
            body,
            $"/groups/{Uri.EscapeDataString(p.Group.Slug)}?tab=trainings",
            $"training-{p.Group.HubGroupId}-{key}");
    }
}
