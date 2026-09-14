using Swimm.Infrastructure.Data;
using Swimm.Infrastructure.Repositories;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Страж мест сужения (docs/plans/cache-row-precision-plan.md §4-4, К4б.3): каждое место, где код
/// открывает блок <c>CacheRows</c>, обязано стоять в списке <see cref="Covered"/> — а попадает
/// туда только вместе со сценарным тестом (§4-3: «правка группы B не роняет A», «правка A роняет
/// A»…). Сужение держится на правиле, которое проверяют не типы, а сценарии; новое место без
/// сценария роняет этот тест.
///
/// Места ищутся по IL (<see cref="IlCalls"/>), как вызовы кэша в <see cref="CacheValueRulesTests"/>:
/// список нельзя забыть обновить — он сверяется с кодом.
/// </summary>
public class CacheRowsSitesTests
{
    /// <summary>
    /// Места сужения, у которых есть сценарные тесты: «Тип.Метод».
    /// </summary>
    private static readonly string[] Covered =
    [
        // Страница группы (К4б.4) — сценарии в CacheGroupPageNarrowingTests.
        "HubGroupPublicRepository.GetPageAsync",
        "HubGroupMediaService.GetGalleryAsync",
        "UserMediaPublicationService.GetApprovedForGroupAsync",
        "UserMediaPublicationService.PublishedItemsAsync",
    ];

    [Fact]
    public void EveryNarrowingSite_HasScenarioTests()
    {
        var sites = Sites(typeof(ResultRepository).Assembly);

        var uncovered = sites.Except(Covered).ToList();
        Assert.True(uncovered.Count == 0,
            "Сужение без сценарных тестов (§4-3) — добавь сценарии и строку в Covered: " + string.Join(", ", uncovered));

        // Строка списка без места в коде — сценарий, который больше ничего не сторожит.
        var stale = Covered.Except(sites).ToList();
        Assert.True(stale.Count == 0, "В Covered места, которых в коде нет: " + string.Join(", ", stale));
    }

    /// <summary>Сторож самого стража: сканер, который тихо перестал находить вызовы, зеленеет вечно.</summary>
    [Fact]
    public void Scanner_FindsTheCallsInTests()
    {
        var sites = Sites(typeof(CacheRowsSitesTests).Assembly);

        // Асинхронный тест: вызов живёт в сгенерированном MoveNext, место — имя теста.
        Assert.Contains($"{nameof(CacheRowNarrowingTests)}.{nameof(CacheRowNarrowingTests.CacheRows_InsideABuild_NarrowsTheModelTables)}", sites);
    }

    private static HashSet<string> Sites(System.Reflection.Assembly assembly) =>
        IlCalls.Find([assembly], called =>
                called.DeclaringType == typeof(CacheRowsExtensions) && called.Name == nameof(CacheRowsExtensions.CacheRows))
            // Перегрузка на один id зовёт перегрузку на список — это не место сужения.
            .Where(c => c.Caller.DeclaringType != typeof(CacheRowsExtensions))
            .Select(c => IlCalls.SiteOf(c.Caller))
            .ToHashSet();
}
