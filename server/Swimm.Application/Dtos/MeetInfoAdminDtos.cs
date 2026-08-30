namespace Swimm.Application.Dtos;

/// <summary>
/// Один день соревнования в редакторе справки о старте.
/// </summary>
/// <param name="WarmUpLocal">
/// Начало разминки как «HH:mm» по МЕСТНОМУ (израильскому) времени — админ вводит стенные
/// часы из регламента. В базе лежит момент UTC; перевод — <c>IsraelTime</c>, один на проект.
/// null — не введено.
/// </param>
/// <param name="Entries">Сколько заявок затянуто на этот день — чтобы админ видел, что день
/// настоящий, а не опечатка в датах.</param>
public sealed record MeetInfoDayDto(DateTime Date, string? WarmUpLocal, int Entries);

/// <summary>
/// Справка о предстоящем старте для админки (шаг Т1): чемпионат + разминка по дням.
/// </summary>
/// <param name="IsChampionship">Что определил забор по регламенту — только для чтения.</param>
/// <param name="IsChampionshipOverride">Ручная правка; null — «как определил забор».</param>
/// <param name="ChampionshipEffective">Что видит витрина: <c>Override ?? IsChampionship</c>.</param>
public sealed record MeetInfoAdminDto(
    int OrgCompId,
    string CompName,
    bool IsChampionship,
    bool? IsChampionshipOverride,
    bool ChampionshipEffective,
    string? RegulationUrl,
    DateTime? RegulationCheckedAt,
    DateTime? UpdatedAt,
    IReadOnlyList<MeetInfoDayDto> Days);

/// <summary>Что админ отправляет на сохранение.</summary>
/// <param name="Days">
/// Дни целиком: пустая строка времени = «стереть разминку этого дня». Присланный список
/// заменяет прежний — иначе удалить однажды введённое время было бы нечем.
/// </param>
public sealed record MeetInfoSaveRequest(
    bool? IsChampionshipOverride,
    IReadOnlyList<MeetInfoDaySaveDto> Days);

/// <param name="WarmUpLocal">«HH:mm» местного времени; null или пусто — стереть.</param>
public sealed record MeetInfoDaySaveDto(DateTime Date, string? WarmUpLocal);
