namespace Swimm.Application.Dtos;

/// <summary>Сводный статус данных для дашборда /Admin: дубли пловцов/клубов, привязка
/// Loglig ID, разбор Discovery. Один запрос вместо четырёх с клиента.</summary>
public sealed record DashboardStatusSummary(
    DashboardSwimmerStatus Swimmers,
    DashboardClubStatus Clubs,
    DashboardLogligStatus Loglig,
    DashboardDiscoveryStatus Discovery,
    DashboardMediaStatus Media);

public sealed record DashboardSwimmerStatus(int Orphans, int SureCandidates, int UnsureCandidates);

public sealed record DashboardClubStatus(int SureCandidates, int UnsureCandidates);

public sealed record DashboardLogligStatus(int Verified, int Suggested, int Rejected, int Unlinked);

public sealed record DashboardDiscoveryStatus(int Imported, int New, int Other);

/// <summary>Здоровье ссылок UserMedia (фаза 7.5): сколько всего, сколько битых, сколько ещё не проверено.</summary>
public sealed record DashboardMediaStatus(int Total, int Broken, int Unchecked);
