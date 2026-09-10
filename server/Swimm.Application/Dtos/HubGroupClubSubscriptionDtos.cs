namespace Swimm.Application.Dtos;

/// <summary>Подписка группы на клуб — для панели управления группой.</summary>
public sealed class HubGroupClubSubscriptionDto
{
    public int ClubId { get; set; }
    /// <summary>Название клуба — иврит по умолчанию (правило имён), EN — рядом.</summary>
    public string ClubName { get; set; } = "";
    public string? ClubNameEn { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>Итог пересборки состава: сколько групп прошли, сколько пловцов пришло и ушло.</summary>
public sealed record HubGroupClubSyncResult(int Groups, int Added, int Removed)
{
    public static readonly HubGroupClubSyncResult Empty = new(0, 0, 0);

    public HubGroupClubSyncResult Plus(HubGroupClubSyncResult other) =>
        new(Groups + other.Groups, Added + other.Added, Removed + other.Removed);
}

/// <summary>Итог подписки: либо ошибка (текст на витрину — по-английски), либо подписка и пересборка.</summary>
public sealed record HubGroupClubSubscribeResult(
    bool Success,
    string? Error,
    HubGroupClubSubscriptionDto? Subscription,
    HubGroupClubSyncResult Sync)
{
    public static HubGroupClubSubscribeResult Ok(HubGroupClubSubscriptionDto subscription, HubGroupClubSyncResult sync) =>
        new(true, null, subscription, sync);

    public static HubGroupClubSubscribeResult Fail(string error) =>
        new(false, error, null, HubGroupClubSyncResult.Empty);
}
