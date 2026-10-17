using Swimm.Domain.Entities;

namespace Swimm.Application.Mapping;

/// <summary>Участие персонажа в тест-группе: slug группы и статус (<see cref="HubGroupUserMemberStatus"/>).</summary>
public sealed record PersonaMembership(string GroupSlug, string Status);

/// <summary>Уровень избранного пловца: обычное (красное ♥), семья (золотое ♥), «это я» (★).</summary>
public enum PersonaFavoriteLevel { Favorite, Family, Me }

/// <summary>
/// Избранный пловец персонажа — по МЕСТУ в составе тест-групп (0 — первый), а не по id: состав
/// выбирается из живых пловцов один раз (<c>PersonaSeeder</c>), id в разных базах разные.
/// </summary>
public sealed record PersonaFavorite(int RosterIndex, PersonaFavoriteLevel Level);

/// <summary>
/// Медиа персонажа: ссылка <see cref="TestPersonas.MediaUrlPrefix"/> + <paramref name="Key"/> на пловце
/// <paramref name="RosterIndex"/> состава, видимость (private / public) и, если есть, публикация в
/// группу <see cref="TestPersonas.OpenGroupSlug"/> — уровень и статус.
/// </summary>
public sealed record PersonaMedia(
    string Key, string Visibility, int RosterIndex = 0,
    string? PublicationLevel = null, string? PublicationStatus = null);

/// <summary>Тест-группа: slug, имя, политика вступления и ник владельца.</summary>
public sealed record TestGroup(string Slug, string Name, string JoinPolicy, string OwnerNick);

/// <summary>
/// Тестовый персонаж — кто он и в каком он ИСХОДНОМ состоянии. Из этого описания работают все трое:
/// сидер (<c>--seed-personas</c>) его создаёт, проверка «изменён ли» с ним сравнивает, кнопка
/// «Сбросить» в /Admin/Users к нему возвращает (решение Влада 17.10.2026 — одно описание вместо
/// зеркала в коде). Не указано — пусто: участия, админства, избранного, медиа нет; роль только User.
/// Свои группы персонажа — те из <see cref="TestPersonas.Groups"/>, где он владелец.
/// </summary>
public sealed record TestPersona(string Nick, string Description)
{
    public string Email => TestPersonas.EmailOf(Nick);

    public bool IsActive { get; init; } = true;

    /// <summary>Персональный лимит групп (<c>AppUser.HubGroupLimit</c>); null — по роли.</summary>
    public int? HubGroupLimit { get; init; }

    public IReadOnlyList<string> Roles { get; init; } = [TestPersonas.RoleUser];
    public IReadOnlyList<PersonaMembership> Memberships { get; init; } = [];

    /// <summary>Slug групп, где персонаж — админ (не владелец).</summary>
    public IReadOnlyList<string> AdminOf { get; init; } = [];

    public IReadOnlyList<PersonaFavorite> Favorites { get; init; } = [];
    public IReadOnlyList<PersonaMedia> Media { get; init; } = [];
}

/// <summary>
/// Состав тестовых персонажей (docs/plans/test-personas-plan.md). Одно место на «кто есть кто и в
/// каком состоянии»: его читают сидер, переключатель персонажа, проверка изменений и сброс.
/// Все ники начинаются с <see cref="TestAccountRules.Prefix"/> — это и делает аккаунт тестовым.
/// Гостя здесь нет: гость — это отсутствие входа, а не аккаунт; в переключателе он зовётся
/// <c>utest-guest</c> (значение dev-куки, см. DevAdminBypass.Guest), в базе такого пользователя нет.
/// </summary>
public static class TestPersonas
{
    /// <summary>Зона <c>.test</c> зарезервирована (RFC 2606): письма на неё никуда не уходят.</summary>
    public const string EmailDomain = "swimm.test";

    public static string EmailOf(string nick) => $"{nick}@{EmailDomain}";

    public const string Newbie = "utest-newbie";
    public const string Member = "utest-member";
    public const string Pending = "utest-pending";
    public const string Parent = "utest-parent";
    public const string SwimmerMe = "utest-swimmer-me";
    public const string GroupAdmin = "utest-group-admin";
    public const string Coach = "utest-coach";
    public const string MediaAuthor = "utest-media-author";
    public const string Blocked = "utest-blocked";

    /// <summary>Тестовые группы сидера. Slug тоже с префиксом utest — видно, чьи они.</summary>
    public const string OpenGroupSlug = "utest-open";
    public const string ApprovalGroupSlug = "utest-approval";

    public const string RoleUser = "User";
    public const string RoleCoach = "Coach";

    /// <summary>Ссылки медиа персонажей — example.com: настоящих видео нет, сторож пометит их битыми.</summary>
    public const string MediaUrlPrefix = "https://example.com/utest/";

    /// <summary>Тест-группы. Обе — у <see cref="Coach"/>, с одним и тем же составом пловцов.</summary>
    public static readonly IReadOnlyList<TestGroup> Groups =
    [
        new(OpenGroupSlug, "[TEST] Open", HubGroupJoinPolicy.Open, Coach),
        new(ApprovalGroupSlug, "[TEST] Approval", HubGroupJoinPolicy.Approval, Coach),
    ];

    // Описания — английские: их покажет переключатель на сайте (UI только на английском).
    public static readonly IReadOnlyList<TestPersona> All =
    [
        new(Newbie, "Signed in, nothing else: no groups, favorites or media"),
        new(Member, "Active member of both test groups")
        {
            Memberships =
            [
                new(OpenGroupSlug, HubGroupUserMemberStatus.Active),
                new(ApprovalGroupSlug, HubGroupUserMemberStatus.Active),
            ],
        },
        new(Pending, "Join request to [TEST] Approval is waiting")
        {
            Memberships = [new(ApprovalGroupSlug, HubGroupUserMemberStatus.Pending)],
        },
        new(Parent, "Not in any group; two kids in favorites, marked family")
        {
            Favorites = [new(0, PersonaFavoriteLevel.Family), new(1, PersonaFavoriteLevel.Family)],
        },
        new(SwimmerMe, "Primary favorite «Me» set — must grant no rights")
        {
            Favorites = [new(2, PersonaFavoriteLevel.Me)],
        },
        new(GroupAdmin, "Admin of [TEST] Open, not the owner")
        {
            AdminOf = [OpenGroupSlug],
        },
        // Владеет обеими тест-группами (Groups), личный лимит 2 — исчерпан.
        new(Coach, "Coach role, owns both test groups, group limit used up")
        {
            Roles = [RoleUser, RoleCoach],
            HubGroupLimit = 2,
        },
        // Участник open-группы: иначе подать публикацию в группу нельзя. Четыре медиа на первом
        // пловце: личное, публичное, личное с публикацией members на одобрении, публичное с
        // одобренной публикацией public.
        new(MediaAuthor, "Media: private, public, pending and approved publications")
        {
            Memberships = [new(OpenGroupSlug, HubGroupUserMemberStatus.Active)],
            Media =
            [
                new("private-video", "private"),
                new("public-video", "public"),
                new("pending-members-video", "private", 0, UserMediaPublicationLevel.Members, UserMediaPublicationStatus.Pending),
                new("approved-public-video", "public", 0, UserMediaPublicationLevel.Public, UserMediaPublicationStatus.Approved),
            ],
        },
        new(Blocked, "Deactivated account") { IsActive = false },
    ];

    public static TestPersona? ByEmail(string? email) =>
        All.FirstOrDefault(p => string.Equals(p.Email, email, StringComparison.OrdinalIgnoreCase));
}
