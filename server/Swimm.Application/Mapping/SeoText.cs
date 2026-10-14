using System.Globalization;
using Swimm.Application.Dtos;

namespace Swimm.Application.Mapping;

/// <summary>
/// Тексты серверных заголовков страниц витрины (A10 шаг 2): <c>&lt;title&gt;</c>, описание,
/// canonical и <c>noindex</c>. Единственное место, где решается, что видит поисковик в голом
/// HTML, — до того, как страницу дорисует скрипт.
///
/// Правила (azure-deploy-plan.md A10):
/// <list type="bullet">
/// <item>имена пловцов и названия клубов — ивритские, английские только фоллбеком (правило имён);</item>
/// <item>год рождения в заголовок и описание НЕ выносим — это профиль ребёнка;</item>
/// <item>пловец «скрыт из поиска» — <c>noindex</c>, но страница та же (данные протокола не прячем, Р76);</item>
/// <item>пловец, о котором нельзя сказать наверняка, что ему 18 (<see cref="IsCertainlyAdult"/>), —
///   <c>noindex</c> и вне sitemap (решение Влада 14.10.2026 после ревью /privacy: страницы детей не
///   индексируются; соревнования и клубы, где их имена есть как в протоколе, — индексируются);</item>
/// <item>псевдоклуб и склеенный клуб — <c>noindex</c>: их нет в sitemap и
///   не должно быть в выдаче.</item>
/// </list>
/// Интерфейс сайта английский — значит, и обвязка текста английская; имена — данные.
/// </summary>
public static class SeoText
{
    public const string SiteName = "SwimHub";

    /// <summary>Длина описания, после которой поисковики всё равно режут сниппет.</summary>
    public const int MaxDescriptionLength = 160;

    public static string SwimmerName(SeoSwimmerFacts s) =>
        Join(s.FirstName, s.LastName) is { Length: > 0 } he ? he : Join(s.FirstNameEn, s.LastNameEn);

    public static string ClubName(string? name, string? nameEn) =>
        !string.IsNullOrWhiteSpace(name) ? name.Trim() : (nameEn ?? "").Trim();

    /// <summary>
    /// Совершеннолетний НАВЕРНЯКА: известен только год рождения, поэтому 18 исполнилось точно лишь
    /// с 1 января года, когда «по году» стало 19. Год неизвестен (0) — считаем ребёнком: ошибка в
    /// эту сторону стоит странице в выдаче, в обратную — профиля ребёнка в поиске. Это не
    /// <c>SeasonMath.AgeInSeason</c>: там спортивный возраст, здесь — возраст человека.
    /// </summary>
    public static bool IsCertainlyAdult(int birthYear, int currentYear) =>
        birthYear > 0 && currentYear - birthYear >= 19;

    /// <summary>Самый поздний год рождения, при котором пловец в <paramref name="currentYear"/> наверняка взрослый.</summary>
    public static int LatestAdultBirthYear(int currentYear) => currentYear - 19;

    public static SeoHead Swimmer(SeoSwimmerFacts s, int currentYear)
    {
        var name = SwimmerName(s);
        var club = ClubName(s.ClubName, s.ClubNameEn);
        var who = club.Length > 0 ? $"{name} ({club})" : name;
        var description = s.Swims > 0
            ? $"{who}: {Count(s.Swims, "swim")} at {Count(s.Competitions, "competition")}. " +
              "Personal bests by event, latest results and progress."
            : $"{who}: swimming results, personal bests and progress.";
        return new SeoHead(
            Title: $"{name} — swimming results · {SiteName}",
            Description: Clip(description),
            CanonicalPath: $"/swimmers/{s.Id}",
            NoIndex: s.HideFromSearch || !IsCertainlyAdult(s.BirthYear, currentYear));
    }

    public static SeoHead Club(SeoClubFacts c)
    {
        var name = ClubName(c.Name, c.NameEn);
        var description = c.Swimmers > 0
            ? $"{name}: {Count(c.Swimmers, "swimmer")}. Club roster, medals, records and season bests."
            : $"{name}: club roster, results, medals and records.";
        return new SeoHead(
            Title: $"{name} — swimming club · {SiteName}",
            Description: Clip(description),
            // Склеенный клуб: canonical на того, в кого склеен, — страница-дубль не конкурирует в выдаче.
            CanonicalPath: $"/clubs/{c.MergedIntoId ?? c.Id}",
            NoIndex: c.IsPseudo || c.MergedIntoId != null);
    }

    public static SeoHead Competition(SeoCompetitionFacts c)
    {
        var name = string.IsNullOrWhiteSpace(c.SubName) ? c.Name.Trim() : $"{c.Name.Trim()} — {c.SubName.Trim()}";
        var when = string.IsNullOrWhiteSpace(c.Date) ? "" : $", {c.Date.Trim()}";
        var pool = string.IsNullOrWhiteSpace(c.PoolType) ? "" : $" ({c.PoolType.Trim()} pool)";
        var description = c.Swims > 0
            ? $"Results of {name}{when}{pool}: {Count(c.Swimmers, "swimmer")}, {Count(c.Swims, "swim")}. " +
              "Times, places, medals and club points."
            : $"{name}{when}{pool}: start list and results.";
        return new SeoHead(
            Title: $"{c.Name.Trim()}{(string.IsNullOrWhiteSpace(c.Date) ? "" : $" {c.Date.Trim()}")} — results · {SiteName}",
            Description: Clip(description),
            CanonicalPath: $"/competitions/{c.Id}",
            NoIndex: false);
    }

    private static string Join(string first, string last) => $"{first?.Trim()} {last?.Trim()}".Trim();

    private static string Count(int n, string noun) =>
        n == 1 ? $"1 {noun}" : $"{n.ToString("N0", CultureInfo.InvariantCulture)} {noun}s";

    private static string Clip(string s) =>
        s.Length <= MaxDescriptionLength ? s : s[..(MaxDescriptionLength - 1)].TrimEnd() + "…";
}
