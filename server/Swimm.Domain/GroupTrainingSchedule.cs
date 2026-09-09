using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Swimm.Domain;

/// <summary>
/// Регулярное расписание тренировок группы — «Пн · Ср · Пт 18:00–19:30, бассейн Нетания».
/// Лежит JSON-колонкой <c>HubGroups.TrainingSchedule</c>, каталог ключей — здесь, в коде;
/// колонку руками не читать (тот же приём и те же причины, что у
/// <see cref="EntityDisplaySettings"/>: новые ключи без миграции, приезжает одним запросом
/// вместе с группой, нового гранта не нужно — <c>HubGroups</c> уже читает <c>swimm_ro</c>).
///
/// РЕГУЛЯРНОЕ, а не список занятий (решение Влада 09.09.2026): «Next training» тогда
/// считается сам и не требует, чтобы владелец заводил каждое занятие руками. Цена решения —
/// отмену одного занятия выразить нечем: правится только расписание целиком или пишется
/// в <see cref="Note"/>.
///
/// ⚠ Время здесь — СТЕННЫЕ ЧАСЫ бассейна (местные, Израиль), а не момент времени: в поясах
/// его не переводим и в UTC не храним. <see cref="NextOccurrence"/> поэтому принимает
/// «сейчас» уже в местном времени (<c>IsraelTime</c>), а не считает его сам.
/// </summary>
public sealed class GroupTrainingSchedule
{
    /// <summary>Регулярные занятия недели. Пусто — расписания нет, слоты шапки скрыты.</summary>
    [JsonPropertyName("slots")]
    public List<GroupTrainingSlot> Slots { get; set; } = [];

    /// <summary>Где тренируемся («בריכת נתניה»). Общее на все слоты — разное пишется в Note.</summary>
    [JsonPropertyName("place")]
    public string? Place { get; set; }

    /// <summary>25m / 50m — как у соревнований; произвольная строка не запрещена.</summary>
    [JsonPropertyName("pool_type")]
    public string? PoolType { get; set; }

    /// <summary>Свободная приписка: «летом по расписанию в WhatsApp», «19.09 отменена».</summary>
    [JsonPropertyName("note")]
    public string? Note { get; set; }

    private static readonly JsonSerializerOptions Options = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Разбор колонки. Пусто, мусор или чужая форма — ПУСТОЕ расписание, а не исключение:
    /// страница группы не должна падать из-за кривого JSON (как у EntityDisplaySettings).
    /// </summary>
    public static GroupTrainingSchedule Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new GroupTrainingSchedule();
        try
        {
            return JsonSerializer.Deserialize<GroupTrainingSchedule>(json, Options)
                   ?? new GroupTrainingSchedule();
        }
        catch (JsonException)
        {
            return new GroupTrainingSchedule();
        }
    }

    public string ToJson() => JsonSerializer.Serialize(this, Options);

    /// <summary>Есть ли что показывать: хотя бы один валидный слот.</summary>
    [JsonIgnore] // вычисляемое — в колонку не пишем (иначе `HasSlots` уезжает в jsonb)
    public bool HasSlots => Slots.Any(s => s.IsValid);

    /// <summary>
    /// Ближайшее занятие в местном времени, начиная с <paramref name="nowLocal"/>.
    /// Идущее прямо сейчас занятие ближайшим НЕ считается — «Next training» отвечает на
    /// вопрос «когда следующая», и показывать сегодняшнюю после её начала было бы враньём.
    /// null — расписания нет или все слоты битые.
    /// </summary>
    public (DateOnly Date, GroupTrainingSlot Slot)? NextOccurrence(DateTime nowLocal)
    {
        (DateOnly Date, GroupTrainingSlot Slot)? best = null;
        var today = DateOnly.FromDateTime(nowLocal);

        foreach (var slot in Slots)
        {
            if (!slot.IsValid) continue;
            var start = slot.StartTime!.Value;

            // Сколько дней до этого дня недели (0 — сегодня); сегодняшнее уже начавшееся
            // занятие переносим на следующую неделю.
            var delta = ((slot.Day - (int)IsoDayOfWeek(today)) + 7) % 7;
            if (delta == 0 && TimeOnly.FromDateTime(nowLocal) >= start) delta = 7;

            var date = today.AddDays(delta);
            if (best == null || date < best.Value.Date
                || (date == best.Value.Date && start < best.Value.Slot.StartTime!.Value))
            {
                best = (date, slot);
            }
        }

        return best;
    }

    /// <summary>ISO-номер дня недели (1 = понедельник … 7 = воскресенье).</summary>
    private static DayOfWeekIso IsoDayOfWeek(DateOnly date) =>
        (DayOfWeekIso)(date.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)date.DayOfWeek);

    private enum DayOfWeekIso { Monday = 1, Sunday = 7 }
}

/// <summary>Одно регулярное занятие недели.</summary>
public sealed class GroupTrainingSlot
{
    /// <summary>
    /// День недели, ISO: 1 = понедельник … 7 = воскресенье. ISO, а не .NET-нумерация с
    /// воскресеньем-нулём: неделя в Израиле начинается с воскресенья, и «0» в JSON читалось
    /// бы как «не задан».
    /// </summary>
    [JsonPropertyName("day")]
    public int Day { get; set; }

    /// <summary>Начало, «HH:mm» по стенным часам.</summary>
    [JsonPropertyName("start")]
    public string Start { get; set; } = "";

    /// <summary>Конец, «HH:mm»; null — не указан (показываем только начало).</summary>
    [JsonPropertyName("end")]
    public string? End { get; set; }

    /// <summary>Разобранное начало; null — строка не время. Битый слот молча пропускается.</summary>
    [JsonIgnore]
    public TimeOnly? StartTime => ParseTime(Start);

    [JsonIgnore]
    public TimeOnly? EndTime => ParseTime(End);

    [JsonIgnore]
    public bool IsValid => Day is >= 1 and <= 7 && StartTime != null;

    private static TimeOnly? ParseTime(string? value) =>
        TimeOnly.TryParseExact(value, "HH:mm", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var parsed)
            ? parsed
            : null;
}
