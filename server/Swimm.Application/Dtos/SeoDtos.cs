namespace Swimm.Application.Dtos;

// Факты для серверных <title>/description страниц витрины и sitemap.xml (A10, шаги 2–3).
// Текст из них собирает Seo.SeoText — репозиторий отдаёт только данные.

/// <summary>
/// Пловец для заголовка страницы /swimmers/{id}. Год рождения в заголовок и описание не выносится.
/// Пловца закрытой группы (Р71) здесь не бывает: для витрины его нет, и имя не должно попасть в HTML.
/// </summary>
public sealed record SeoSwimmerFacts(
    int Id,
    string FirstName,
    string LastName,
    string FirstNameEn,
    string LastNameEn,
    string? ClubName,
    string? ClubNameEn,
    bool HideFromSearch,
    // Год рождения — только для решения «noindex для несовершеннолетних», в текст не идёт. 0 — неизвестен.
    int BirthYear,
    int Swims,
    int Competitions);

/// <summary>Клуб для заголовка страницы /clubs/{id}.</summary>
public sealed record SeoClubFacts(
    int Id,
    string Name,
    string NameEn,
    bool IsPseudo,
    int? MergedIntoId,
    int Swimmers);

/// <summary>Соревнование для заголовка страницы /competitions/{id}.</summary>
public sealed record SeoCompetitionFacts(
    int Id,
    string Name,
    string? SubName,
    string Date,
    string PoolType,
    int Swims,
    int Swimmers);

/// <summary>Готовая «голова» страницы. <see cref="CanonicalPath"/> — путь от корня, хост добавляет вызывающий.</summary>
public sealed record SeoHead(string Title, string Description, string CanonicalPath, bool NoIndex);

// ── Тело страницы в голом HTML (Search Console 01.11.2026) ──────────────────────────────
// 1 347 адресов «обнаружены, не проиндексированы»: ссылки между страницами были только в
// скрипте, и краулер знал их лишь по sitemap. Сервер кладёт в корень страницы короткий текст
// со ссылками; React заменяет его своим содержимым при первом рендере.

/// <summary>Соревнование в списке ссылок. <see cref="Date"/> — как в базе, dd/MM/yyyy.</summary>
public sealed record SeoCompetitionRef(int Id, string Name, string? SubName, string Date);

/// <summary>
/// Пловец в списке ссылок — только индексируемый (взрослый наверняка, не скрыт из поиска, не
/// пловец закрытой группы): имена детей в серверный HTML не попадают.
/// </summary>
public sealed record SeoSwimmerRef(int Id, string FirstName, string LastName, string FirstNameEn, string LastNameEn);

/// <summary>Клуб в списке ссылок — только живой (не псевдо, не склеенный).</summary>
public sealed record SeoClubRef(int Id, string Name, string NameEn);

public sealed record SeoSwimmerLinks(SeoClubRef? Club, IReadOnlyList<SeoCompetitionRef> Competitions);

public sealed record SeoClubLinks(IReadOnlyList<SeoSwimmerRef> Swimmers, IReadOnlyList<SeoCompetitionRef> Competitions);

public sealed record SeoCompetitionLinks(IReadOnlyList<SeoClubRef> Clubs, IReadOnlyList<SeoSwimmerRef> Swimmers);

/// <summary>Ссылка тела страницы. <see cref="Note"/> — короткая приписка после ссылки (дата).</summary>
public sealed record SeoLink(string Path, string Text, string? Note = null);

/// <summary>Раздел тела. <see cref="More"/> — сколько ссылок не показано из-за предела.</summary>
public sealed record SeoSection(string Title, IReadOnlyList<SeoLink> Links, int More = 0);

/// <summary>Тело страницы для голого HTML: заголовок, вводная строка, разделы ссылок.</summary>
public sealed record SeoBody(string Heading, string? Lead, IReadOnlyList<SeoSection> Sections);

/// <summary>
/// Адрес sitemap.xml. <see cref="LastModified"/> — дата последнего старта, который есть на
/// странице (null — не пишем <c>&lt;lastmod&gt;</c>).
/// </summary>
public sealed record SeoSitemapEntry(string Path, DateOnly? LastModified);

/// <summary>Пловец в списке «скрыт из поиска» на /Admin/Swimmers.</summary>
public sealed record SearchHiddenSwimmerDto(int Id, string Name, int BirthYear, string? ClubName);
