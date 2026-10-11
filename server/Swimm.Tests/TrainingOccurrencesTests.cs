using Swimm.Application.Mapping;
using Swimm.Domain;
using Xunit;
using static Swimm.Application.Mapping.TrainingOccurrences;

namespace Swimm.Tests;

/// <summary>
/// Занятия группы: слоты расписания + разовые («Ask who's coming») + отмены (Р9
/// docs/plans/trainings-tab-redesign-plan.md) — чистые функции <see cref="TrainingOccurrences"/>.
/// Образец — <c>GroupTrainingScheduleTests.NextOccurrences_*</c>.
/// </summary>
public class TrainingOccurrencesTests
{
    // 2026-09-07 — понедельник, 2026-09-09 — среда.
    private static readonly DateTime MondayMorning = new(2026, 9, 7, 9, 0, 0);

    private static GroupTrainingSchedule MonWed() => new()
    {
        Slots =
        [
            new GroupTrainingSlot { Day = 1, Start = "18:00", End = "19:30" },
            new GroupTrainingSlot { Day = 3, Start = "18:00", End = "19:30" },
        ],
    };

    private static OneOff Single(int id, DateOnly date, string start, string? title = "Underwater filming",
        string type = "technique", string? pool = "25m") => new(id, date, start, title, type, pool);

    private static readonly OneOff[] NoOneOffs = [];
    private static readonly Cancellation[] NoCancels = [];

    private static List<(DateOnly, string)> Keys(IEnumerable<Occurrence> list) =>
        list.Select(o => (o.Date, o.Start)).ToList();

    [Fact]
    public void Upcoming_ScheduleOnly_SameAsNextOccurrences()
    {
        var upcoming = Upcoming(MonWed(), NoOneOffs, NoCancels, MondayMorning, 3);

        Assert.Equal(
            MonWed().NextOccurrences(MondayMorning, 3).Select(n => (n.Date, n.Slot.Start)),
            Keys(upcoming));
        Assert.All(upcoming, o =>
        {
            Assert.False(o.OneOff);
            Assert.False(o.Cancelled);
            Assert.Null(o.TrainingId);
            Assert.Equal("19:30", o.End);
        });
    }

    [Fact]
    public void Upcoming_OneOffBetweenSlots_SortedByDateAndStart()
    {
        // Вторник 8.09 в 20:00 — между понедельником и средой; разовое в день слота, но раньше него — до слота.
        var oneOffs = new[]
        {
            Single(1, new DateOnly(2026, 9, 8), "20:00"),
            Single(2, new DateOnly(2026, 9, 9), "07:00", title: "Early dryland", type: "dryland", pool: null),
        };

        var upcoming = Upcoming(MonWed(), oneOffs, NoCancels, MondayMorning, 4);

        Assert.Equal(
            [(new DateOnly(2026, 9, 7), "18:00"), (new DateOnly(2026, 9, 8), "20:00"),
             (new DateOnly(2026, 9, 9), "07:00"), (new DateOnly(2026, 9, 9), "18:00")],
            Keys(upcoming));
        var tuesday = upcoming[1];
        Assert.True(tuesday.OneOff);
        Assert.Equal((1, "Underwater filming", "technique", "25m", (string?)null),
            (tuesday.TrainingId, tuesday.Title, tuesday.Type, tuesday.PoolType, tuesday.End));
        Assert.Equal(2, upcoming.Count(o => o.OneOff));   // разовых без слота — два, слоты не разовые
    }

    [Fact]
    public void Find_OneOffOnSlotKey_IsOneOccurrence_EndFromSlot_TitleFromTraining()
    {
        var wed = new DateOnly(2026, 9, 9);
        var oneOffs = new[] { Single(5, wed, "18:00", title: "Test set night", type: "test-set") };

        var found = Find(MonWed(), oneOffs, NoCancels, wed, "18:00");

        Assert.NotNull(found);
        Assert.False(found!.OneOff);                       // оно в расписании
        Assert.Equal("19:30", found.End);                  // End — от слота
        Assert.Equal((5, "Test set night", "test-set"), (found.TrainingId, found.Title, found.Type));

        // В списке ближайших то же занятие — одно, а не два.
        var upcoming = Upcoming(MonWed(), oneOffs, NoCancels, MondayMorning, 3);
        Assert.Single(upcoming, o => o.Date == wed);
    }

    [Fact]
    public void Find_NoSlotNoOneOff_Null()
    {
        Assert.Null(Find(MonWed(), NoOneOffs, NoCancels, new DateOnly(2026, 9, 8), "18:00"));    // вторник
        Assert.Null(Find(MonWed(), NoOneOffs, NoCancels, new DateOnly(2026, 9, 7), "19:00"));    // не то время
    }

    [Fact]
    public void Upcoming_OneOffAlreadyStartedToday_IsNotUpcoming()
    {
        var today = new DateOnly(2026, 9, 7);
        var oneOffs = new[] { Single(1, today, "08:00"), Single(2, today, "09:00"), Single(3, today, "09:01") };

        // 09:00 — «начавшееся — не ближайшее», как у расписания: 08:00 и 09:00 нет, 09:01 есть.
        var upcoming = Upcoming(new GroupTrainingSchedule(), oneOffs, NoCancels, MondayMorning, 3);

        Assert.Equal([(today, "09:01")], Keys(upcoming));
    }

    [Fact]
    public void Upcoming_Cancelled_StaysInListAndCountStaysFull()
    {
        var cancels = new[] { new Cancellation(new DateOnly(2026, 9, 9), "18:00", "Pool closed") };

        var upcoming = Upcoming(MonWed(), NoOneOffs, cancels, MondayMorning, 3);

        // Пн 7, Ср 9 (отменена, на своём месте), Пн 14, Ср 16 — не отменённых всё равно три.
        Assert.Equal(
            [(new DateOnly(2026, 9, 7), "18:00"), (new DateOnly(2026, 9, 9), "18:00"),
             (new DateOnly(2026, 9, 14), "18:00"), (new DateOnly(2026, 9, 16), "18:00")],
            Keys(upcoming));
        Assert.Equal([false, true, false, false], upcoming.Select(o => o.Cancelled));
        Assert.Equal("Pool closed", upcoming[1].CancelReason);
        Assert.Equal(3, upcoming.Count(o => !o.Cancelled));
    }

    [Fact]
    public void Upcoming_CancelledAfterLastTaken_NotInList()
    {
        // Отмена на среду 16.09 — уже после третьего не отменённого (Пн 7, Ср 9, Пн 14).
        var cancels = new[] { new Cancellation(new DateOnly(2026, 9, 16), "18:00", null) };

        var upcoming = Upcoming(MonWed(), NoOneOffs, cancels, MondayMorning, 3);

        Assert.Equal(
            [(new DateOnly(2026, 9, 7), "18:00"), (new DateOnly(2026, 9, 9), "18:00"), (new DateOnly(2026, 9, 14), "18:00")],
            Keys(upcoming));
        Assert.DoesNotContain(upcoming, o => o.Cancelled);
    }

    [Fact]
    public void Upcoming_CancelledOneOff_KeepsFlag()
    {
        var day = new DateOnly(2026, 9, 8);
        var cancels = new[] { new Cancellation(day, "20:00", "Coach is ill") };

        var upcoming = Upcoming(new GroupTrainingSchedule(), [Single(1, day, "20:00")], cancels, MondayMorning, 3);

        var only = Assert.Single(upcoming);
        Assert.True(only.OneOff);
        Assert.True(only.Cancelled);
        Assert.Equal("Coach is ill", only.CancelReason);
    }

    [Fact]
    public void Upcoming_NoSchedule_OnlyOneOffs()
    {
        var oneOffs = new[]
        {
            Single(2, new DateOnly(2026, 9, 12), "10:00"),
            Single(1, new DateOnly(2026, 9, 8), "20:00"),
        };

        var upcoming = Upcoming(new GroupTrainingSchedule(), oneOffs, NoCancels, MondayMorning, 3);

        Assert.Equal([(new DateOnly(2026, 9, 8), "20:00"), (new DateOnly(2026, 9, 12), "10:00")], Keys(upcoming));
        Assert.All(upcoming, o => Assert.True(o.OneOff));
    }

    [Fact]
    public void Upcoming_OneOffBeyondAnswerWindow_IsLeftOut()
    {
        // Дальше четырёх недель ответить нельзя — в Next такое занятие не нужно.
        var far = new DateOnly(2026, 9, 7).AddDays(TrainingRsvpRules.DaysAhead + 1);

        var upcoming = Upcoming(new GroupTrainingSchedule(), [Single(1, far, "10:00")], NoCancels, MondayMorning, 3);

        Assert.Empty(upcoming);
    }

    [Fact]
    public void Upcoming_ZeroCount_Empty()
    {
        Assert.Empty(Upcoming(MonWed(), NoOneOffs, NoCancels, MondayMorning, 0));
    }
}
