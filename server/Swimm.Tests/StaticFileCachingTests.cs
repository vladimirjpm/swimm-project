using Swimm.API.Hosting;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Кэш статики витрины (21.10.2026): после деплоя браузер держал прошлый <c>results_main.js</c>,
/// потому что у файлов без хэша в имени не было <c>Cache-Control</c>. Всё, чьё имя не меняется
/// от сборки к сборке, — <c>no-cache</c>; хэшированные чанки <c>/assets/</c> — без заголовка.
/// </summary>
public class StaticFileCachingTests
{
    [Theory]
    [InlineData("/results_main.html")]   // чистый URL /competitions/... переписан сюда до статики
    [InlineData("/home.html")]           // «/» → home.html через UseDefaultFiles
    [InlineData("/Competitions.HTML")]
    [InlineData("/results_main.js")]     // входная точка Vite: entryFileNames '[name].js'
    [InlineData("/season_best.js")]
    [InlineData("/sw.js")]               // service worker push-напоминаний
    [InlineData("/data/filter-data.js")] // из public/, без хэша
    [InlineData("/js/admin/import.js")]  // скрипты админки — тоже без хэша
    public void FilesWithStableNames_AreNoCache(string path) =>
        Assert.Equal(StaticFileCaching.NoCache, StaticFileCaching.CacheControlFor(path));

    [Theory]
    [InlineData("/assets/index-BSU5N15c.js")] // хэш в имени — новая сборка = новый файл
    [InlineData("/assets/swim-row-c6K_0pbK.js")]
    [InlineData("/assets/index-Dk2s9Q1x.css")]
    [InlineData("/images/logo.png")]
    [InlineData("/favicon.ico")]
    [InlineData("/manifest.json")]
    [InlineData("")]
    [InlineData(null)]
    public void OtherFiles_GetNoHeader(string? path) =>
        Assert.Null(StaticFileCaching.CacheControlFor(path));
}
