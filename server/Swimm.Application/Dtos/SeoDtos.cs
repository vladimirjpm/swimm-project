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

/// <summary>Пловец в списке «скрыт из поиска» на /Admin/Swimmers.</summary>
public sealed record SearchHiddenSwimmerDto(int Id, string Name, int BirthYear, string? ClubName);
