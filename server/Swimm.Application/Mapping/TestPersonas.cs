namespace Swimm.Application.Mapping;

/// <summary>Тестовый персонаж: ник (он же начало email) и кто это — для списка переключателя.</summary>
public sealed record TestPersona(string Nick, string Description)
{
    public string Email => TestPersonas.EmailOf(Nick);
}

/// <summary>
/// Состав тестовых персонажей (docs/plans/test-personas-plan.md). Одно место на «кто есть кто»:
/// его читает сидер <c>--seed-personas</c> и будет читать переключатель персонажа (этап 3).
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

    // Описания — английские: их покажет переключатель на сайте (UI только на английском).
    public static readonly IReadOnlyList<TestPersona> All =
    [
        new(Newbie, "Signed in, nothing else: no groups, favorites or media"),
        new(Member, "Active member of both test groups"),
        new(Pending, "Join request to [TEST] Approval is waiting"),
        new(Parent, "Not in any group; two kids in favorites, marked family"),
        new(SwimmerMe, "Primary favorite «Me» set — must grant no rights"),
        new(GroupAdmin, "Admin of [TEST] Open, not the owner"),
        new(Coach, "Coach role, owns both test groups, group limit used up"),
        new(MediaAuthor, "Media: private, public, pending and approved publications"),
        new(Blocked, "Deactivated account"),
    ];
}
