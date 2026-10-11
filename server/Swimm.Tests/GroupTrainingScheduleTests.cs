using System;
using Swimm.Domain;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Расписание группы и расчёт «Next training» (<see cref="GroupTrainingSchedule"/>).
/// Считает сервер, а не клиент — чтобы «сегодня» было израильским независимо от часов
/// зрителя и чтобы логику вообще можно было покрыть тестами (слоты шапки, 09.09.2026).
/// </summary>
public class GroupTrainingScheduleTests
{
    /// <summary>Пн · Ср 18:00–19:30 — рабочее расписание для большинства проверок.</summary>
    private static GroupTrainingSchedule MonWed() => new()
    {
        Slots =
        [
            new GroupTrainingSlot { Day = 1, Start = "18:00", End = "19:30" },
            new GroupTrainingSlot { Day = 3, Start = "18:00", End = "19:30" },
        ],
        Place = "בריכת נתניה",
        PoolType = "25m",
    };

    // 2026-09-07 — понедельник, 2026-09-09 — среда.

    [Fact]
    public void NextOccurrences_ThreeAcrossTheWeekWrap_StartedTodaySkipped()
    {
        // Среда 18:30 — сегодняшнее уже идёт: Пн 14, Ср 16, Пн 21.
        var next = MonWed().NextOccurrences(new DateTime(2026, 9, 9, 18, 30, 0), 3);

        Assert.Equal(
            [new DateOnly(2026, 9, 14), new DateOnly(2026, 9, 16), new DateOnly(2026, 9, 21)],
            next.Select(n => n.Date));
    }

    [Fact]
    public void NextOccurrences_TwoSlotsADay_BothCount_DuplicateSlotOnce()
    {
        var schedule = new GroupTrainingSchedule
        {
            Slots =
            [
                new GroupTrainingSlot { Day = 6, Start = "10:00" },
                new GroupTrainingSlot { Day = 6, Start = "08:00" },
                new GroupTrainingSlot { Day = 6, Start = "08:00" },   // дубль — одно занятие
            ],
        };

        // Суббота 12.09 в 07:00: оба занятия сегодня, третье — через неделю.
        var next = schedule.NextOccurrences(new DateTime(2026, 9, 12, 7, 0, 0), 3);

        Assert.Equal(
            [(new DateOnly(2026, 9, 12), "08:00"), (new DateOnly(2026, 9, 12), "10:00"), (new DateOnly(2026, 9, 19), "08:00")],
            next.Select(n => (n.Date, n.Slot.Start)));
    }

    [Fact]
    public void NextOccurrences_NoSlots_Empty_FirstEqualsNextOccurrence()
    {
        Assert.Empty(new GroupTrainingSchedule().NextOccurrences(new DateTime(2026, 9, 7, 9, 0, 0), 3));

        var now = new DateTime(2026, 9, 7, 9, 0, 0);
        Assert.Equal(MonWed().NextOccurrence(now)!.Value.Date, MonWed().NextOccurrences(now, 3)[0].Date);
    }

    [Fact]
    public void NextOccurrence_TodayBeforeStart_ReturnsToday()
    {
        var next = MonWed().NextOccurrence(new DateTime(2026, 9, 7, 9, 0, 0));

        Assert.NotNull(next);
        Assert.Equal(new DateOnly(2026, 9, 7), next!.Value.Date);
        Assert.Equal("18:00", next.Value.Slot.Start);
    }

    [Fact]
    public void NextOccurrence_TodayAfterStart_SkipsToNextSlot()
    {
        // Понедельник, 18:30 — занятие уже идёт. «Следующая» это среда, а не сегодня:
        // показывать начавшееся как предстоящее было бы враньём.
        var next = MonWed().NextOccurrence(new DateTime(2026, 9, 7, 18, 30, 0));

        Assert.Equal(new DateOnly(2026, 9, 9), next!.Value.Date);
    }

    [Fact]
    public void NextOccurrence_ExactlyAtStart_CountsAsStarted()
    {
        var next = MonWed().NextOccurrence(new DateTime(2026, 9, 7, 18, 0, 0));

        Assert.Equal(new DateOnly(2026, 9, 9), next!.Value.Date);
    }

    [Fact]
    public void NextOccurrence_AfterLastSlotOfWeek_WrapsToNextWeek()
    {
        // Четверг: ближайшее — понедельник следующей недели.
        var next = MonWed().NextOccurrence(new DateTime(2026, 9, 10, 20, 0, 0));

        Assert.Equal(new DateOnly(2026, 9, 14), next!.Value.Date);
    }

    [Fact]
    public void NextOccurrence_SundayIsSeven_NotZero()
    {
        // Неделя в Израиле начинается с воскресенья, и день хранится по ISO (7 = вс):
        // «0» в JSON читалось бы как «не задан».
        var schedule = new GroupTrainingSchedule
        {
            Slots = [new GroupTrainingSlot { Day = 7, Start = "07:30" }],
        };

        var next = schedule.NextOccurrence(new DateTime(2026, 9, 10, 12, 0, 0)); // четверг

        Assert.Equal(new DateOnly(2026, 9, 13), next!.Value.Date); // ближайшее воскресенье
    }

    [Fact]
    public void NextOccurrence_SameDayTwoSlots_ReturnsEarlierStillAhead()
    {
        var schedule = new GroupTrainingSchedule
        {
            Slots =
            [
                new GroupTrainingSlot { Day = 1, Start = "07:00" },
                new GroupTrainingSlot { Day = 1, Start = "18:00" },
            ],
        };

        var next = schedule.NextOccurrence(new DateTime(2026, 9, 7, 8, 0, 0)); // утро уже прошло

        Assert.Equal(new DateOnly(2026, 9, 7), next!.Value.Date);
        Assert.Equal("18:00", next.Value.Slot.Start);
    }

    [Fact]
    public void NextOccurrence_BrokenSlotsIgnored_NoScheduleReturnsNull()
    {
        var broken = new GroupTrainingSchedule
        {
            Slots =
            [
                new GroupTrainingSlot { Day = 0, Start = "18:00" },   // день вне 1..7
                new GroupTrainingSlot { Day = 2, Start = "шесть" },   // не время
                new GroupTrainingSlot { Day = 9, Start = "18:00" },
            ],
        };

        Assert.False(broken.HasSlots);
        Assert.Null(broken.NextOccurrence(new DateTime(2026, 9, 7, 9, 0, 0)));
        Assert.Null(new GroupTrainingSchedule().NextOccurrence(new DateTime(2026, 9, 7, 9, 0, 0)));
    }

    [Fact]
    public void NextOccurrence_MixOfBrokenAndValid_UsesValidOnly()
    {
        var schedule = new GroupTrainingSchedule
        {
            Slots =
            [
                new GroupTrainingSlot { Day = 2, Start = "25:00" }, // вторник, но час не бывает
                new GroupTrainingSlot { Day = 3, Start = "18:00" },
            ],
        };

        var next = schedule.NextOccurrence(new DateTime(2026, 9, 7, 9, 0, 0)); // понедельник

        Assert.Equal(new DateOnly(2026, 9, 9), next!.Value.Date); // среда, вторник отброшен
    }

    // ── Разбор колонки ──────────────────────────────────────────────────────

    [Fact]
    public void Parse_GarbageOrEmpty_ReturnsEmptySchedule_NotThrow()
    {
        Assert.False(GroupTrainingSchedule.Parse(null).HasSlots);
        Assert.False(GroupTrainingSchedule.Parse("").HasSlots);
        Assert.False(GroupTrainingSchedule.Parse("{ not json").HasSlots);
        Assert.False(GroupTrainingSchedule.Parse("[1,2,3]").HasSlots);
    }

    [Fact]
    public void ToJson_RoundTrips()
    {
        var restored = GroupTrainingSchedule.Parse(MonWed().ToJson());

        Assert.Equal(2, restored.Slots.Count);
        Assert.Equal("25m", restored.PoolType);
        Assert.Equal("19:30", restored.Slots[0].End);
        Assert.True(restored.HasSlots);
    }
}
