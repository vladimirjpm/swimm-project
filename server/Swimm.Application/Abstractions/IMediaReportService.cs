using Swimm.Application.Dtos;

namespace Swimm.Application.Abstractions;

/// <summary>
/// Жалобы «Report» на медиа (Р62, docs/data-integrity.md). Порог открытых жалоб прячет медиа со всех
/// витрин (<c>UserMedia.ModerationState = under_review</c>); решает админ сайта: оставить (жалобы
/// закрываются, медиа возвращается) или снять (публикации отклоняются, подать заново нельзя).
/// </summary>
public interface IMediaReportService
{
    /// <summary>
    /// Подать жалобу. Жаловаться можно на медиа, которое зрителю видно (правило лайка), и не на своё.
    /// Повтор от того же аккаунта — не ошибка и не вторая жалоба.
    /// </summary>
    Task<MediaReportSubmitResult> ReportAsync(
        int reporterUserId, int mediaId, SubmitMediaReportRequest request, bool isSiteAdmin);

    /// <summary>Очередь админки: <paramref name="open"/> — медиа с открытыми жалобами, иначе разобранные.</summary>
    Task<List<MediaReportQueueItemDto>> GetQueueAsync(bool open, int take = 200);

    /// <summary>Сколько медиа ждут решения — счётчик на дашборде/в сайдбаре.</summary>
    Task<int> CountOpenAsync();

    /// <summary>
    /// Решение админа сайта: <paramref name="keep"/> — оставить (открытые жалобы → kept, медиа снова
    /// видно; для снятого ранее — вернуть), иначе снять (открытые → removed, публикации → rejected,
    /// медиа <c>removed</c>). false — медиа нет.
    /// </summary>
    Task<bool> DecideAsync(int mediaId, bool keep, int adminUserId);

    /// <summary>
    /// Снимок жалоб перед тем, как владелец удалит своё медиа (жалобы уйдут каскадом). null — медиа
    /// не его, нет или жалоб на него не было: писать в аудит нечего.
    /// </summary>
    Task<MediaReportDeleteTrail?> CaptureBeforeOwnerDeleteAsync(int ownerUserId, int mediaId);

    /// <summary>Запись в аудит (<c>media.report.owner-delete</c>) — после успешного удаления.</summary>
    Task LogOwnerDeleteAsync(MediaReportDeleteTrail trail);
}
