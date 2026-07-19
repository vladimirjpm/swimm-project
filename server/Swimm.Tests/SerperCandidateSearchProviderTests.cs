using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Swimm.Infrastructure.Services;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Поиск кандидатов Loglig ID через serper.dev (docs/loglig-id-plan.md, шаг 4). Реальный ответ
/// serper для "ברנצב סבינה" лежит в Fixtures/Loglig — тесты в сеть НЕ ходят
/// (см. LogligClientParseTests для образца работы с фикстурами).
/// </summary>
public class SerperCandidateSearchProviderTests
{
    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Loglig", name));

    /// <summary>HttpClient-фабрика, падающая при любом обращении к сети — для проверки, что при
    /// отсутствии ключа провайдер вообще не пытается ходить в HTTP.</summary>
    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            throw new InvalidOperationException("HTTP не должен вызываться, когда провайдер не сконфигурирован");
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public StubHttpClientFactory(HttpMessageHandler handler) => _handler = handler;
        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }

    private static IConfiguration ConfigWithApiKey(string? apiKey)
    {
        var settings = new Dictionary<string, string?>();
        if (apiKey != null)
            settings["CandidateSearch:ApiKey"] = apiKey;
        return new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
    }

    // ── ExtractPlayerIds ────────────────────────────────────────────────────────────────────

    [Fact]
    public void ExtractPlayerIds_RealFixture_OnlyPlayerCardLink()
    {
        var json = Fixture("serper-search-branzev-sabina.json");

        var ids = SerperCandidateSearchProvider.ExtractPlayerIds(json);

        Assert.Equal([304199], ids);
    }

    [Fact]
    public void ExtractPlayerIds_SyntheticJson_UniqueOrderedTruncatedToFive()
    {
        // 7 ссылок Players/Details: дубли + loglig.com без порта — должны схлопнуться до
        // уникальных ID в порядке появления и обрезаться до 5.
        var json = """
        {
          "organic": [
            {"link": "https://loglig.com:2053/Players/Details/111?seasonId=1"},
            {"link": "https://loglig.com/Players/Details/222"},
            {"link": "https://loglig.com:2053/Players/Details/111?seasonId=2"},
            {"link": "https://loglig.com:2053/Players/Details/333"},
            {"link": "https://loglig.com:2053/Players/Details/444"},
            {"link": "https://loglig.com:2053/Players/Details/555"},
            {"link": "https://loglig.com:2053/Players/Details/666"}
          ]
        }
        """;

        var ids = SerperCandidateSearchProvider.ExtractPlayerIds(json);

        Assert.Equal([111, 222, 333, 444, 555], ids);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("")]
    [InlineData("{ this is not valid json")]
    [InlineData("""{"organic": []}""")]
    [InlineData("""{"organic": "не массив"}""")]
    public void ExtractPlayerIds_MissingEmptyOrBrokenJson_ReturnsEmpty(string json)
    {
        var ids = SerperCandidateSearchProvider.ExtractPlayerIds(json);

        Assert.Empty(ids);
    }

    // ── BuildQueries ────────────────────────────────────────────────────────────────────────

    [Fact]
    public void BuildQueries_BothNames_ThreeQueriesInOrder()
    {
        var queries = SerperCandidateSearchProvider.BuildQueries("ברנצב", "סבינה");

        Assert.Equal(
            [
                "site:loglig.com \"ברנצב סבינה\"",
                "site:loglig.com \"סבינה ברנצב\"",
                "site:loglig.com \"ברנצב\"",
            ],
            queries);
    }

    [Fact]
    public void BuildQueries_OnlyLastName_OneQuery()
    {
        var queries = SerperCandidateSearchProvider.BuildQueries("ברנצב", "  ");

        Assert.Equal(["site:loglig.com \"ברנצב\""], queries);
    }

    [Fact]
    public void BuildQueries_BothEmpty_NoQueries()
    {
        var queries = SerperCandidateSearchProvider.BuildQueries("", "   ");

        Assert.Empty(queries);
    }

    // ── IsConfigured / FindCandidatesAsync graceful ────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task FindCandidatesAsync_NotConfigured_ReturnsEmptyWithoutHttpCall(string? apiKey)
    {
        var provider = new SerperCandidateSearchProvider(
            new StubHttpClientFactory(new ThrowingHandler()),
            ConfigWithApiKey(apiKey),
            NullLogger<SerperCandidateSearchProvider>.Instance);

        Assert.False(provider.IsConfigured);

        var result = await provider.FindCandidatesAsync("ברנצב", "סבינה");

        Assert.Empty(result);
    }

    [Fact]
    public void IsConfigured_ApiKeyPresent_True()
    {
        var provider = new SerperCandidateSearchProvider(
            new StubHttpClientFactory(new ThrowingHandler()),
            ConfigWithApiKey("some-key"),
            NullLogger<SerperCandidateSearchProvider>.Instance);

        Assert.True(provider.IsConfigured);
    }
}
