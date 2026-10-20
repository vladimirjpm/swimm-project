using Swimm.Application.Constants;
using Swimm.Application.Mapping;
using Swimm.Domain;
using Swimm.Domain.Entities;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// «Кому и когда» напоминать о тренировке (<see cref="TrainingReminderRules"/>,
/// docs/plans/web-push-reminders-plan.md §7, решения В1–В6): время напоминания и тихие часы,
/// кто получает, повтор не дублирует, текст и ссылка уведомления.
/// </summary>
public class TrainingReminderRulesTests
{
    // 2026-10-20 — вторник (ISO 2), 2026-10-21 — среда (ISO 3).
    private static readonly DateOnly Tue = new(2026, 10, 20);
    private static readonly DateOnly Wed = new(2026, 10, 21);

    private const int GroupId = 7;
    private const int Ann = 1;
    private const int Ben = 2;

    private static TrainingReminderRules.Group Group(
        IReadOnlyCollection<TrainingOccurrences.OneOff>? oneOffs = null,
        IReadOnlyCollection<TrainingOccurrences.Cancellation>? cancellations = null,
        params GroupTrainingSlot[] slots) =>
        new(GroupId, "דולפין מאסטרס", "dolphin-masters",
            new GroupTrainingSchedule
            {
                Slots = slots.Length > 0 ? [.. slots] : [new GroupTrainingSlot { Day = 2, Start = "18:00", End = "19:30" }],
                Place = "Wingate",
            },
            oneOffs ?? [], cancellations ?? []);

    private static TrainingReminderRules.People People(
        IReadOnlyCollection<int>? members = null,
        IReadOnlySet<int>? devices = null,
        Dictionary<int, TrainingReminderRules.Settings>? settings = null,
        HashSet<(int, int)>? muted = null,
        Dictionary<(int, DateOnly), IReadOnlySet<int>>? onBreak = null,
        Dictionary<(int, int, DateOnly, string), string>? answers = null,
        HashSet<(int, int, DateOnly, string)>? delivered = null) =>
        new(new Dictionary<int, IReadOnlyCollection<int>> { [GroupId] = members ?? [Ann] },
            devices ?? new HashSet<int> { Ann, Ben },
            settings ?? [],
            muted ?? [],
            onBreak ?? [],
            answers ?? [],
            delivered ?? []);

    private static DateTime At(DateOnly date, int h, int m = 0) => date.ToDateTime(new TimeOnly(h, m));

    // ── Время напоминания ───────────────────────────────────────────────

    [Theory]
    [InlineData("2h", 16, 0)]
    [InlineData(null, 16, 0)]        // нет настройки — за 2 часа (В1)
    [InlineData("weird", 16, 0)]     // неизвестный вид — как по умолчанию
    [InlineData("1h", 17, 0)]
    public void DueAt_Lead(string? lead, int h, int m)
    {
        Assert.Equal(At(Tue, h, m), TrainingReminderRules.DueAt(At(Tue, 18), lead));
    }

    [Fact]
    public void DueAt_Evening_IsEightPmTheDayBefore()
    {
        Assert.Equal(At(Tue, 20), TrainingReminderRules.DueAt(At(Wed, 6, 30), ReminderLead.Evening));
    }

    [Fact]
    public void DueAt_EarlyMorning_MovesToNinePmTheDayBefore()
    {
        // 06:00 за 2 часа — 04:00, тихие часы (В5) → 21:00 накануне; 08:30 за 2 часа — 06:30 → тоже.
        Assert.Equal(At(Tue, 21), TrainingReminderRules.DueAt(At(Wed, 6), ReminderLead.TwoHours));
        Assert.Equal(At(Tue, 21), TrainingReminderRules.DueAt(At(Wed, 8, 30), ReminderLead.TwoHours));
        // 09:00 за 2 часа — 07:00, уже не тихие.
        Assert.Equal(At(Wed, 7), TrainingReminderRules.DueAt(At(Wed, 9), ReminderLead.TwoHours));
    }

    [Fact]
    public void DueAt_LateEvening_MovesToNinePmSameDay()
    {
        // 23:30 за час — 22:30, тихие → 21:00 того же вечера, а не накануне.
        Assert.Equal(At(Tue, 21), TrainingReminderRules.DueAt(At(Tue, 23, 30), ReminderLead.OneHour));
    }

    // ── Кому и когда ───────────────────────────────────────────────────

    [Fact]
    public void Plan_DueReached_TrainingAhead_OneReminder()
    {
        var planned = TrainingReminderRules.Plan([Group()], People(), At(Tue, 16, 2));

        var p = Assert.Single(planned);
        Assert.Equal((Ann, Tue, "18:00", (string?)null), (p.UserId, p.Occurrence.Date, p.Occurrence.Start, p.Answer));
    }

    [Fact]
    public void Plan_NotYetDue_Nothing()
    {
        Assert.Empty(TrainingReminderRules.Plan([Group()], People(), At(Tue, 15, 58)));
    }

    [Fact]
    public void Plan_MissedTick_StillSendsBeforeStart_NotAfter()
    {
        // Рестарт съел тик в 16:00 — в 17:40 напоминание всё ещё уходит; после начала — нет.
        Assert.Single(TrainingReminderRules.Plan([Group()], People(), At(Tue, 17, 40)));
        Assert.Empty(TrainingReminderRules.Plan([Group()], People(), At(Tue, 18, 0)));
    }

    [Fact]
    public void Plan_AlreadyDelivered_NotRepeated()
    {
        var people = People(delivered: [(Ann, GroupId, Tue, "18:00")]);
        Assert.Empty(TrainingReminderRules.Plan([Group()], people, At(Tue, 16, 5)));
    }

    [Fact]
    public void Plan_Cancelled_Nothing()
    {
        var group = Group(cancellations: [new TrainingOccurrences.Cancellation(Tue, "18:00", "Pool closed")]);
        Assert.Empty(TrainingReminderRules.Plan([group], People(), At(Tue, 16, 5)));
    }

    [Fact]
    public void Plan_OneOffTraining_Reminded()
    {
        // Разовое в среду 10:00 (в расписании среды нет): за 2 часа — 08:00.
        var group = Group(oneOffs: [new TrainingOccurrences.OneOff(55, Wed, "10:00", "Open water", "technique", null)]);

        var p = Assert.Single(TrainingReminderRules.Plan([group], People(), At(Wed, 8, 1)));
        Assert.Equal(("10:00", true, "Open water"), (p.Occurrence.Start, p.Occurrence.OneOff, p.Occurrence.Title));
    }

    [Fact]
    public void Plan_OnBreak_AnsweredNo_Muted_Disabled_NoDevice_Skipped()
    {
        var now = At(Tue, 16, 5);
        Assert.Empty(TrainingReminderRules.Plan([Group()],
            People(onBreak: new() { [(GroupId, Tue)] = new HashSet<int> { Ann } }), now));
        Assert.Empty(TrainingReminderRules.Plan([Group()],
            People(answers: new() { [(GroupId, Ann, Tue, "18:00")] = TrainingRsvpAnswer.No }), now));
        Assert.Empty(TrainingReminderRules.Plan([Group()], People(muted: [(Ann, GroupId)]), now));
        Assert.Empty(TrainingReminderRules.Plan([Group()],
            People(settings: new() { [Ann] = new TrainingReminderRules.Settings(false, ReminderLead.TwoHours) }), now));
        Assert.Empty(TrainingReminderRules.Plan([Group()], People(devices: new HashSet<int> { Ben }), now));
    }

    [Fact]
    public void Plan_PerPersonLead_AndAnswerCarried()
    {
        // Ann — за час (ещё рано в 16:05), Ben — по умолчанию, ответил «иду».
        var people = People(
            members: [Ann, Ben],
            settings: new() { [Ann] = new TrainingReminderRules.Settings(true, ReminderLead.OneHour) },
            answers: new() { [(GroupId, Ben, Tue, "18:00")] = TrainingRsvpAnswer.Yes });

        var p = Assert.Single(TrainingReminderRules.Plan([Group()], people, At(Tue, 16, 5)));
        Assert.Equal((Ben, TrainingRsvpAnswer.Yes), (p.UserId, p.Answer));
        Assert.Equal(2, TrainingReminderRules.Plan([Group()], people, At(Tue, 17, 0)).Count);
    }

    [Fact]
    public void Plan_SameAccountTwiceInRoster_OneReminder()
    {
        Assert.Single(TrainingReminderRules.Plan([Group()], People(members: [Ann, Ann]), At(Tue, 16, 5)));
    }

    [Fact]
    public void Plan_QuietHoursNow_WaitsForMorning()
    {
        // Тренировка в 08:30: напоминание стояло на 21:00 накануне, а API лежал до 23:00.
        var group = Group(slots: new GroupTrainingSlot { Day = 3, Start = "08:30" });
        Assert.Empty(TrainingReminderRules.Plan([group], People(), At(Tue, 23, 0)));
        Assert.Empty(TrainingReminderRules.Plan([group], People(), At(Wed, 6, 59)));
        Assert.Single(TrainingReminderRules.Plan([group], People(), At(Wed, 7, 0)));
        // В срок — в 21:00 накануне.
        Assert.Single(TrainingReminderRules.Plan([group], People(), At(Tue, 21, 0)));
    }

    [Fact]
    public void Plan_DstChangeDay_WallClock()
    {
        // 25.10.2026 Израиль переходит на зимнее время (UTC+3 → UTC+2). Тренировка в воскресенье
        // 18:00 по стенным часам: напоминание — в 16:00 местного, то есть в 14:00 UTC.
        var sunday = new DateOnly(2026, 10, 25);
        var group = Group(slots: new GroupTrainingSlot { Day = 7, Start = "18:00" });

        var before = IsraelTime.ToLocal(new DateTime(2026, 10, 25, 13, 55, 0, DateTimeKind.Utc));
        var after = IsraelTime.ToLocal(new DateTime(2026, 10, 25, 14, 0, 0, DateTimeKind.Utc));
        Assert.Empty(TrainingReminderRules.Plan([group], People(), before));
        var p = Assert.Single(TrainingReminderRules.Plan([group], People(), after));
        Assert.Equal(sunday, p.Occurrence.Date);
    }

    // ── Текст уведомления ──────────────────────────────────────────────

    [Fact]
    public void Compose_NoAnswer_AsksAndLinksToTrainingsTab()
    {
        var p = Assert.Single(TrainingReminderRules.Plan([Group()], People(), At(Tue, 16, 5)));
        var m = TrainingReminderRules.Compose(p, At(Tue, 16, 5));

        Assert.Equal("דולפין מאסטרס", m.Title);
        Assert.Equal("Training today at 18:00 · Wingate. Are you coming?", m.Body);
        Assert.Equal("/groups/dolphin-masters?tab=trainings", m.Url);
        Assert.Equal("training-7-2026-10-20-1800", m.Tag);
    }

    [Fact]
    public void Compose_Tomorrow_Yes_OneOffTitle()
    {
        var group = Group(oneOffs: [new TrainingOccurrences.OneOff(55, Wed, "07:30", "Open water", null, null)]);
        var people = People(answers: new() { [(GroupId, Ann, Wed, "07:30")] = TrainingRsvpAnswer.Yes });

        var p = Assert.Single(TrainingReminderRules.Plan([group], people, At(Tue, 21, 0)));
        Assert.Equal("Open water tomorrow at 07:30 · Wingate", TrainingReminderRules.Compose(p, At(Tue, 21, 0)).Body);
    }
}
