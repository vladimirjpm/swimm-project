using Swimm.Application.Dtos;

namespace Swimm.Application.Abstractions;

/// <summary>
/// Справка о предстоящем старте в админке (docs/plans/start-list-ticket-plan.md, шаг Т1):
/// время разминки по дням (вводится РУКАМИ) и ручная правка флага «чемпионат».
///
/// Флаг сам по себе ставит забор по регламенту (<c>StartListPullService</c>) — здесь его
/// можно только переопределить. Разминку забор не трогает вовсе: регламенты федерации
/// разношёрстные, и лишний автоматический источник кривых данных решено не заводить.
/// </summary>
public interface IMeetInfoAdminService
{
    /// <summary>
    /// Справка + дни программы. Дни берутся из затянутых заявок, а если их ещё нет —
    /// из диапазона дат «Входящих»: админ должен мочь ввести разминку ДО забора протокола.
    /// null — соревнование неизвестно ни там, ни там.
    /// </summary>
    Task<MeetInfoAdminDto?> GetAsync(int orgCompId, CancellationToken ct = default);

    /// <summary>Сохранить и вернуть то, что получилось. null — соревнование неизвестно.</summary>
    Task<MeetInfoAdminDto?> SaveAsync(
        int orgCompId, MeetInfoSaveRequest request, CancellationToken ct = default);
}
