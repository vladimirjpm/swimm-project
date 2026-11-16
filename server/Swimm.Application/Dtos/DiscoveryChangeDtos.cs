namespace Swimm.Application.Dtos;

/// <summary>
/// Что именно случилось со стартом при синхронизации «Входящих» (строка списка изменений
/// прогона discovery, попап на /Admin/Jobs). Строки, а не enum: так они лежат в JSON
/// <c>Sys_JobRunLog.Details</c> и читаются глазами в SQL.
/// </summary>
public static class DiscoveryChangeKinds
{
    /// <summary>Впервые увиден в списке сайта.</summary>
    public const string New = "new";

    /// <summary>Будущий старт сдвинули — перенос (входит и в «обновлено»).</summary>
    public const string Postponed = "postponed";

    /// <summary>Старт вернули на первую объявленную дату — перенос отменён (входит в «обновлено»).</summary>
    public const string Restored = "restored";

    /// <summary>Сменились имя или даты без переноса (правка прошедшего старта, переименование).</summary>
    public const string Updated = "updated";

    /// <summary>Будущий старт сезона впервые пропал из списка сайта — кандидат в отменённые.</summary>
    public const string Missing = "missing";
}

/// <summary>
/// Одно изменение старта за прогон discovery. <see cref="DateStart"/>/<see cref="DateEnd"/> —
/// какими стали (у пропавшего — какими были); <c>Old*</c> — какими были до прогона, только у
/// изменённых строк и только то, что поменялось.
/// </summary>
public sealed record DiscoveryChange(
    string Kind,
    int OrgCompId,
    string Name,
    DateTime DateStart,
    DateTime DateEnd,
    string? OldName = null,
    DateTime? OldDateStart = null,
    DateTime? OldDateEnd = null);
