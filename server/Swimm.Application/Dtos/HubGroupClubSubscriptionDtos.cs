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

/// <summary>Ссылка на группу в предпросмотре подписки.</summary>
public sealed class HubGroupRefDto
{
    public int Id { get; set; }
    public string Slug { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>Видимый состав (без скрытых) — по нему выбирается, в какую группу звать.</summary>
    public int MemberCount { get; set; }
}

/// <summary>
/// Что будет, если подписать группу на клуб (панель «My groups», до кнопки Follow): сколько
/// пловцов придёт, есть ли у клуба официальная группа, кто уже подписан. Тексты предупреждения
/// и подсказки считает сервер (HubGroupClubRules) — второй копии на клиенте нет.
/// </summary>
public sealed class HubGroupClubSubscriptionPreviewDto
{
    /// <summary>Канонический клуб: склеенный дубль подменяется так же, как при подписке.</summary>
    public int ClubId { get; set; }
    public string ClubName { get; set; } = "";
    public string? ClubNameEn { get; set; }

    /// <summary>Сколько пловцов клуба сейчас в окне (текущий + прошлый сезон).</summary>
    public int SwimmerCount { get; set; }

    /// <summary>Эта группа — сама официальная группа клуба: предупреждать не о чем.</summary>
    public bool IsOwnOfficialClub { get; set; }

    /// <summary>Официальная группа клуба, если это не эта группа.</summary>
    public HubGroupRefDto? OfficialGroup { get; set; }

    /// <summary>Другие группы, уже подписанные на этот клуб.</summary>
    public List<HubGroupRefDto> FollowingGroups { get; set; } = [];

    public string? Warning { get; set; }

    /// <summary>«Вступить вместо этого?» — и группа, куда ведёт ссылка.</summary>
    public string? Hint { get; set; }
    public HubGroupRefDto? HintGroup { get; set; }
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
