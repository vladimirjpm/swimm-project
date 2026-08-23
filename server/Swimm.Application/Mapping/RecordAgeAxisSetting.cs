using Swimm.Application.Abstractions;

namespace Swimm.Application.Mapping;

/// <summary>
/// Настройка «RecordAgeAxis» → <see cref="RecordAgeAxis"/>. Единственное место, где строка
/// из /Admin/Settings превращается в ось: вторая копия разбора неизбежно разъедется с первой,
/// и превью импорта начнёт обещать не то, что покажет страница соревнования.
///
/// Неизвестное значение = дефолт <see cref="RecordAgeAxis.Calendar"/>: справочник ведёт
/// федерация, и по умолчанию мы сверяемся в её системе координат.
/// </summary>
public static class RecordAgeAxisSetting
{
    /// <summary>Ключ настройки в <see cref="ISettingsService"/>.</summary>
    public const string Key = "RecordAgeAxis";

    /// <summary>null — настройки в этом контексте нет (тесты, изолированные вызовы): дефолт.</summary>
    public static RecordAgeAxis From(ISettingsService? settings) =>
        settings is null ? RecordAgeAxis.Calendar : Parse(settings.GetValue(Key, "calendar"));

    public static RecordAgeAxis Parse(string? value) =>
        string.Equals(value?.Trim(), "season", StringComparison.OrdinalIgnoreCase)
            ? RecordAgeAxis.Season
            : RecordAgeAxis.Calendar;
}
