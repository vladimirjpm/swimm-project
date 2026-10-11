using Swimm.Application.Abstractions;

namespace Swimm.Application.Mapping;

/// <summary>
/// Жалобы «Report» на медиа (Р62, docs/data-integrity.md) — одно место правил: причины, статусы,
/// состояние модерации медиа, порог «сколько жалоб прячет», пределы текста и rate limit.
/// </summary>
public static class MediaReportRules
{
    // ── Причины (коды хранятся в Sys_MediaReports.Reason, подписи — на клиенте) ─────────────
    public const string ReasonWrongSwimmer = "wrong_swimmer";
    public const string ReasonInappropriate = "inappropriate";
    public const string ReasonSpam = "spam";
    public const string ReasonPrivacy = "privacy";
    /// <summary>«Other» — текст жалобы обязателен.</summary>
    public const string ReasonOther = "other";

    public static readonly string[] Reasons =
        [ReasonWrongSwimmer, ReasonInappropriate, ReasonSpam, ReasonPrivacy, ReasonOther];

    public static bool IsKnownReason(string? reason) => reason != null && Reasons.Contains(reason);

    /// <summary>Подпись причины для админки (по-русски, как вся админка).</summary>
    public static string ReasonLabelRu(string reason) => reason switch
    {
        ReasonWrongSwimmer => "не тот пловец",
        ReasonInappropriate => "неприемлемое содержимое",
        ReasonSpam => "спам / реклама",
        ReasonPrivacy => "не должно быть публичным",
        ReasonOther => "другое",
        _ => reason
    };

    // ── Статусы жалобы ──────────────────────────────────────────────────────────────────
    public const string StatusOpen = "open";
    /// <summary>Админ оставил медиа — жалоба закрыта.</summary>
    public const string StatusKept = "kept";
    /// <summary>Админ снял медиа — жалоба закрыта.</summary>
    public const string StatusRemoved = "removed";

    // ── Состояние модерации медиа (UserMedia.ModerationState) ───────────────────────────
    /// <summary>Жалоб набралось до порога — медиа спрятано со всех витрин до решения.</summary>
    public const string StateUnderReview = "under_review";
    /// <summary>Админ снял медиа: публикации отклонены, новые подать нельзя.</summary>
    public const string StateRemoved = "removed";

    // ── Порог и пределы ─────────────────────────────────────────────────────────────────
    /// <summary>Настройка: сколько ОТКРЫТЫХ жалоб прячет медиа (Admin/Settings).</summary>
    public const string HideThresholdKey = "MediaReportHideThreshold";
    public const int DefaultHideThreshold = 3;
    public const int MinHideThreshold = 1;
    public const int MaxHideThreshold = 100;

    public static int HideThreshold(ISettingsService settings) =>
        Math.Clamp(settings.GetValue(HideThresholdKey, DefaultHideThreshold), MinHideThreshold, MaxHideThreshold);

    public const int MaxCommentLength = 500;

    /// <summary>Имя политики rate limit на подачу жалоб (Program.cs).</summary>
    public const string RateLimitPolicy = "reports";

    /// <summary>Жалоб в минуту на пользователя: живому человеку больше не нужно, спамеру — мало.</summary>
    public const int RateLimitPerMinute = 10;
}
