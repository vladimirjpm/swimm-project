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

    /// <summary>
    /// Название с подзаголовком. Подзаголовок, который уже есть в названии («Х - 23/10/2026» и «Х»
    /// у дней многодневки), не повторяем.
    /// </summary>
    public static string CompetitionName(string name, string? subName) =>
        string.IsNullOrWhiteSpace(subName) || name.Contains(subName.Trim(), StringComparison.Ordinal)
            ? name.Trim() : $"{name.Trim()} — {subName.Trim()}";

    public static SeoHead Competition(SeoCompetitionFacts c)
    {
        var name = CompetitionName(c.Name, c.SubName);
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

    // ── Тело страницы в голом HTML (Search Console 01.11.2026) ───────────────────────────
    // Зачем: ссылки между страницами были только в скрипте, краулер знал адреса лишь по
    // sitemap и откладывал их («обнаружена, не проиндексирована»). Тело собирается только для
    // индексируемых страниц; пловцы в ссылках — только индексируемые (их отбирает репозиторий),
    // поэтому имён детей в серверном HTML нет. Заголовки разделов — английские, как интерфейс.

    /// <summary>Пределы списков: страница остаётся страницей, а не дампом базы.</summary>
    public const int MaxSwimmerLinks = 300;
    public const int MaxCompetitionLinks = 100;
    public const int HomeLatestCompetitions = 12;

    public static SeoBody SwimmerBody(SeoSwimmerFacts s, SeoHead head, SeoSwimmerLinks links)
    {
        var sections = new List<SeoSection>();
        if (links.Club is { } club)
            sections.Add(new SeoSection("Club", [ClubLink(club)]));
        sections.Add(Section("Competitions", NewestFirst(links.Competitions).Select(CompetitionLink), MaxCompetitionLinks));
        return new SeoBody(SwimmerName(s), head.Description, sections);
    }

    public static SeoBody ClubBody(SeoClubFacts c, SeoHead head, SeoClubLinks links) =>
        new(ClubName(c.Name, c.NameEn), head.Description,
        [
            Section("Swimmers", ByText(links.Swimmers.Select(SwimmerLink)), MaxSwimmerLinks),
            Section("Recent competitions", NewestFirst(links.Competitions).Select(CompetitionLink), MaxCompetitionLinks),
        ]);

    public static SeoBody CompetitionBody(SeoCompetitionFacts c, SeoHead head, SeoCompetitionLinks links) =>
        new(CompetitionName(c.Name, c.SubName), head.Description,
        [
            Section("Clubs", ByText(links.Clubs.Select(ClubLink)), int.MaxValue),
            Section("Swimmers", ByText(links.Swimmers.Select(SwimmerLink)), MaxSwimmerLinks),
        ]);

    /// <summary><c>/competitions</c> — все соревнования, новые сверху.</summary>
    public static SeoBody CompetitionsBody(IReadOnlyList<SeoCompetitionRef> competitions) =>
        new("Swimming competitions", "Competition results: times, places, medals and club points.",
            [Section("Competitions", NewestFirst(competitions).Select(CompetitionLink), int.MaxValue)]);

    /// <summary>Главная — последние соревнования; остальные — по ссылке на <c>/competitions</c>.</summary>
    public static SeoBody HomeBody(IReadOnlyList<SeoCompetitionRef> competitions) =>
        new(SiteName, "Swimming results, personal bests, records and season bests.",
            [new SeoSection("Latest competitions",
                NewestFirst(competitions).Take(HomeLatestCompetitions).Select(CompetitionLink).ToList())]);

    /// <summary>Дата соревнования из базы (dd/MM/yyyy); null — не разобрать.</summary>
    public static DateOnly? ParseCompetitionDate(string? date) =>
        DateOnly.TryParseExact(date?.Trim(), "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d : null;

    private static IEnumerable<SeoCompetitionRef> NewestFirst(IEnumerable<SeoCompetitionRef> competitions) =>
        competitions.OrderByDescending(c => ParseCompetitionDate(c.Date) ?? DateOnly.MinValue).ThenByDescending(c => c.Id);

    private static IEnumerable<SeoLink> ByText(IEnumerable<SeoLink> links) =>
        links.OrderBy(l => l.Text, StringComparer.Ordinal).ThenBy(l => l.Path, StringComparer.Ordinal);

    private static SeoSection Section(string title, IEnumerable<SeoLink> links, int max)
    {
        var all = links.ToList();
        return all.Count <= max ? new SeoSection(title, all) : new SeoSection(title, all.Take(max).ToList(), all.Count - max);
    }

    private static SeoLink CompetitionLink(SeoCompetitionRef c) =>
        new($"/competitions/{c.Id}", CompetitionName(c.Name, c.SubName),
            string.IsNullOrWhiteSpace(c.Date) ? null : c.Date.Trim());

    private static SeoLink SwimmerLink(SeoSwimmerRef s) =>
        new($"/swimmers/{s.Id}",
            Join(s.FirstName, s.LastName) is { Length: > 0 } he ? he : Join(s.FirstNameEn, s.LastNameEn));

    private static SeoLink ClubLink(SeoClubRef c) => new($"/clubs/{c.Id}", ClubName(c.Name, c.NameEn));

    private static string Join(string first, string last) => $"{first?.Trim()} {last?.Trim()}".Trim();

    private static string Count(int n, string noun) =>
        n == 1 ? $"1 {noun}" : $"{n.ToString("N0", CultureInfo.InvariantCulture)} {noun}s";

    private static string Clip(string s) =>
        s.Length <= MaxDescriptionLength ? s : s[..(MaxDescriptionLength - 1)].TrimEnd() + "…";
}
