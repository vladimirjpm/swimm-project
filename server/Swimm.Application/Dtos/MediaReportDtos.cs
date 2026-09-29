using System.Text.Json.Serialization;

namespace Swimm.Application.Dtos;

/// <summary>Жалоба «Report» на медиа из лайтбокса (Р62): причина — код из MediaReportRules.Reasons.</summary>
public sealed class SubmitMediaReportRequest
{
    [JsonPropertyName("reason")]
    public string? Reason { get; set; }

    /// <summary>Обязателен у «other», ≤ 500 символов. Читает только админ сайта.</summary>
    [JsonPropertyName("comment")]
    public string? Comment { get; set; }
}

/// <summary>Итог подачи жалобы.</summary>
public enum MediaReportOutcome
{
    /// <summary>Жалоба принята (или уже была от этого аккаунта — повтор не ошибка).</summary>
    Accepted,
    /// <summary>Причина/текст не прошли проверку — 400.</summary>
    Invalid,
    /// <summary>Медиа нет или зритель его не видит — 404 (не раскрываем чужое приватное).</summary>
    NotFound,
    /// <summary>Своё медиа — жаловаться на него незачем, есть Delete / Withdraw.</summary>
    OwnMedia,
}

public sealed record MediaReportSubmitResult(MediaReportOutcome Outcome, string? Error = null, bool AlreadyReported = false);

/// <summary>Ответ клиенту после жалобы — без числа жалоб и без того, спрятано ли медиа.</summary>
public sealed class MediaReportResponseDto
{
    [JsonPropertyName("reported")]
    public bool Reported { get; set; } = true;

    /// <summary>Этот аккаунт уже жаловался на это медиа — вторая жалоба не засчитана.</summary>
    [JsonPropertyName("already_reported")]
    public bool AlreadyReported { get; set; }
}

/// <summary>Медиа в очереди жалоб админки (/Admin/MediaReports) — всё про него на одной карточке.</summary>
public sealed class MediaReportQueueItemDto
{
    public int MediaId { get; set; }
    public string MediaType { get; set; } = "";
    public string SourceType { get; set; } = "";
    public string Url { get; set; } = "";
    public int OwnerUserId { get; set; }
    public string? OwnerEmail { get; set; }
    public int SwimmerId { get; set; }
    public string? SwimmerName { get; set; }
    public string? ResultLabel { get; set; }
    /// <summary>null | under_review | removed.</summary>
    public string? ModerationState { get; set; }
    public List<MediaReportPublicationRowDto> Publications { get; set; } = [];
    public List<MediaReportRowDto> Reports { get; set; } = [];
    /// <summary>Последняя жалоба — порядок очереди.</summary>
    public DateTime LastReportAt { get; set; }
}

public sealed class MediaReportPublicationRowDto
{
    /// <summary>group | club.</summary>
    public string TargetType { get; set; } = "";
    public string TargetName { get; set; } = "";
    public string? TargetSlug { get; set; }
    public string Level { get; set; } = "";
    public string Status { get; set; } = "";
    /// <summary>Группа доверенная (Р56) — public выходит на карточку пловца и в протокол.</summary>
    public bool Trusted { get; set; }
}

/// <summary>Одна жалоба. Email пожаловавшегося — ТОЛЬКО для админа сайта (Р62).</summary>
public sealed class MediaReportRowDto
{
    public int Id { get; set; }
    public string? ReporterEmail { get; set; }
    public string Reason { get; set; } = "";
    public string? Comment { get; set; }
    public string Status { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// След жалоб медиа, которое владелец удаляет сам: снимается ДО удаления (жалобы уходят каскадом)
/// и пишется в аудит <c>media.report.owner-delete</c>, чтобы админ сайта знал, что медиа с жалобами
/// было и ушло не его решением. Владельцу не показывается (решение Влада 29.09.2026).
/// </summary>
public sealed record MediaReportDeleteTrail(
    int MediaId, string Url, int SwimmerId, string? ModerationState,
    int Open, int Decided,
    /// <summary>Причина → число жалоб (все статусы).</summary>
    IReadOnlyDictionary<string, int> Reasons,
    IReadOnlyList<int> ReporterUserIds);
