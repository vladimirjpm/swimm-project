using Swimm.Application.Dtos;

namespace Swimm.Application.Abstractions;

/// <summary>
/// Данные для открытия сайта поисковикам (A10): факты для серверных заголовков страниц и
/// список адресов для <c>sitemap.xml</c>. Только чтение публичных таблиц.
/// </summary>
public interface ISeoRepository
{
    /// <summary>null — пловца нет или это пловец закрытой группы (Р71: для витрины его нет).</summary>
    Task<SeoSwimmerFacts?> GetSwimmerAsync(int id, CancellationToken ct = default);

    /// <summary>null — клуба нет.</summary>
    Task<SeoClubFacts?> GetClubAsync(int id, CancellationToken ct = default);

    /// <summary>null — соревнования нет.</summary>
    Task<SeoCompetitionFacts?> GetCompetitionAsync(int id, CancellationToken ct = default);

    /// <summary>
    /// Адреса для sitemap.xml (пути от корня) с датой последнего старта: соревнования, клубы (без
    /// псевдо и склеенных), взрослые пловцы с результатами — без скрытых из поиска и без пловцов
    /// закрытых групп.
    /// </summary>
    Task<IReadOnlyList<SeoSitemapEntry>> GetSitemapAsync(CancellationToken ct = default);

    // Ссылки для тела страницы в голом HTML. Пловцы в них — только те, что есть в sitemap
    // (индексируемые), клубы — только живые.

    /// <summary>Все соревнования — для главной и <c>/competitions</c>.</summary>
    Task<IReadOnlyList<SeoCompetitionRef>> GetCompetitionsAsync(CancellationToken ct = default);

    /// <summary>Клуб пловца и соревнования, где у него есть результаты.</summary>
    Task<SeoSwimmerLinks> GetSwimmerLinksAsync(int swimmerId, CancellationToken ct = default);

    /// <summary>Индексируемые пловцы клуба и соревнования, где у клуба есть результаты.</summary>
    Task<SeoClubLinks> GetClubLinksAsync(int clubId, CancellationToken ct = default);

    /// <summary>Живые клубы и индексируемые пловцы с результатами в соревновании.</summary>
    Task<SeoCompetitionLinks> GetCompetitionLinksAsync(int competitionId, CancellationToken ct = default);
}

/// <summary>Флаг «скрыть пловца из поиска» (A10 шаг 1) — правка из админки.</summary>
public interface ISearchVisibilityService
{
    Task<IReadOnlyList<SearchHiddenSwimmerDto>> ListHiddenAsync(CancellationToken ct = default);

    /// <summary>false — пловца нет (или это пловец закрытой группы: публичной страницы у него и так нет).</summary>
    Task<bool> SetHiddenAsync(int swimmerId, bool hidden, CancellationToken ct = default);
}
