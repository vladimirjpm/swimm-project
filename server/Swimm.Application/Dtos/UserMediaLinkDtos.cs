namespace Swimm.Application.Dtos;

/// <summary>Итог прогона проверки живости ссылок UserMedia (фаза 7.5).</summary>
public sealed record UserMediaLinkCheckReport(int Checked, int Ok, int Broken);

/// <summary>Строка битой ссылки для страницы /Admin/Media.</summary>
public sealed record BrokenMediaRowDto(
    int Id, string Url, string OwnerEmail, string SwimmerName,
    string MediaType, string SourceType,
    DateTime? LinkCheckedAt, int? LinkStatusCode, string? LinkError);
