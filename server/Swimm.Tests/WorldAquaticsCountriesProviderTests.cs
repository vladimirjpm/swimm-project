using System.Net.Http;
using Swimm.Parsing.RecordSources;
using Xunit;

namespace Swimm.Tests;

/// <summary>
/// Список стран источника рекордов (этап 11.1.1): GUID ↔ alpha-3 для батч-прогона по
/// странам. Фикстура — настоящие записи ответа <c>GET /fina/countries</c> на 2026-09-16
/// (у каждой записи убраны только три поля с картинками флагов).
/// </summary>
public class WorldAquaticsCountriesProviderTests(Xunit.Abstractions.ITestOutputHelper output)
{
    /// <summary>
    /// Пять записей из живого ответа: две обычные страны, Израиль (домашний регион) и две
    /// псевдо-сборные — нейтральный атлет и сама федерация. У псевдо-сборных пустой
    /// <c>RegionName</c>: других отличий в ответе нет, и именно на этом держится отсев.
    /// </summary>
    private const string CountriesJson = """
        [
          {"Id":"27624a51-d5fb-4800-a49a-8dd1faf7b6af","Name":"Anguilla","Code":"AGU","RegionId":"237cae8b-ecfe-4128-910a-5661e742cbbf","RegionName":"Americas","LenexCode":null},
          {"Id":"6b5c6ffc-a444-44c2-8131-0344e0c43a2c","Name":"Individual Neutral Athlete","Code":"AIN","RegionId":null,"RegionName":"","LenexCode":null},
          {"Id":"78e6b142-aaa5-4e14-929e-c2ce616a97af","Name":"FINA","Code":"FINA","RegionId":null,"RegionName":"","LenexCode":null},
          {"Id":"962f77d6-d9c0-49ad-ba93-adc831c9ec9f","Name":"Israel","Code":"ISR","RegionId":"e3fa24ba-2945-479b-a20e-b21b93aafa1f","RegionName":"Europe","LenexCode":null},
          {"Id":"1dce49f3-9980-42d5-89c9-d4c6184df85a","Name":"United States of America","Code":"USA","RegionId":"237cae8b-ecfe-4128-910a-5661e742cbbf","RegionName":"Americas","LenexCode":null}
        ]
        """;

    [Fact]
    public void ParseCountries_DropsPseudoTeams()
    {
        var countries = WorldAquaticsCountriesProvider.ParseCountries(CountriesJson);

        // AIN и FINA — не страны: качать их NR бессмысленно, а RegionCode 'FINA' ещё и
        // не влезает в alpha-3 контракта Record.
        Assert.Equal(new[] { "AGU", "ISR", "USA" }, countries.Select(c => c.Code).ToArray());
        Assert.All(countries, c => Assert.False(string.IsNullOrWhiteSpace(c.Region)));
    }

    [Fact]
    public void ParseCountries_MapsCodeToSourceGuid()
    {
        var countries = WorldAquaticsCountriesProvider.ParseCountries(CountriesJson);

        var israel = Assert.Single(countries, c => c.Code == "ISR");
        Assert.Equal(WorldAquaticsSource.IsraelCountryId, israel.SourceId);
        Assert.Equal("Israel", israel.Name);
        Assert.Equal("Europe", israel.Region);
    }

    /// <summary>
    /// Битые записи пропускаются молча, а не роняют прогон по 235 странам: одна кривая
    /// строка в ответе источника не повод не качать остальные.
    /// </summary>
    [Fact]
    public void ParseCountries_SkipsBrokenRows()
    {
        const string json = """
            [
              {"Id":"not-a-guid","Name":"Broken","Code":"BRK","RegionName":"Europe"},
              {"Id":"a3a53a2a-0000-4000-8000-000000000001","Name":"No code","Code":"","RegionName":"Europe"},
              {"Id":"a3a53a2a-0000-4000-8000-000000000002","Name":"Four letters","Code":"ABCD","RegionName":"Europe"},
              {"Id":"1dce49f3-9980-42d5-89c9-d4c6184df85a","Name":"United States of America","Code":"USA","RegionId":"237cae8b-ecfe-4128-910a-5661e742cbbf","RegionName":"Americas"},
              {"Id":"a3a53a2a-0000-4000-8000-000000000003","Name":"Duplicate of USA","Code":"USA","RegionName":"Americas"}
            ]
            """;

        var countries = WorldAquaticsCountriesProvider.ParseCountries(json);

        // Дубль кода не должен добавить вторую страну: ключ прогона обязан быть уникальным,
        // иначе второй ответ молча перезаписал бы первый.
        var usa = Assert.Single(countries);
        Assert.Equal("USA", usa.Code);
        Assert.Equal("1dce49f3-9980-42d5-89c9-d4c6184df85a", usa.SourceId);
    }

    /// <summary>
    /// Ответ без единой страны — это поломка источника, а не «список пуст». Молчаливый
    /// пустой список означал бы «прогон прошёл успешно, обновлено 0 стран».
    /// </summary>
    [Fact]
    public void ParseCountries_ThrowsWhenNothingLeft()
    {
        const string onlyPseudo = """
            [{"Id":"78e6b142-aaa5-4e14-929e-c2ce616a97af","Name":"FINA","Code":"FINA","RegionName":""}]
            """;

        Assert.Throws<InvalidOperationException>(() =>
            WorldAquaticsCountriesProvider.ParseCountries(onlyPseudo));
    }

    [Fact]
    public void CountriesUrl_IsWhitelisted()
    {
        Assert.Equal(WorldAquaticsSource.ApiHost, WorldAquaticsSource.CountriesUrl.Host);

        // Предикат SSRF — один на все источники worldaquatics; чужой домен не проходит.
        Assert.Throws<InvalidOperationException>(() =>
            WorldAquaticsSource.EnsureWhitelisted("https://api.worldaquatics.com.evil.test/fina/countries"));
    }

    /// <summary>
    /// Живая проверка: источник по-прежнему отдаёт 235 реальных стран, а GUID Израиля —
    /// тот же, что зашит константой (на нём стоит дефолт NR-отчётов). В обычном прогоне
    /// пропускается (сеть), включается переменной SWIMM_NET_TESTS=1.
    /// </summary>
    [Fact]
    public async Task Live_Countries_Still235()
    {
        if (Environment.GetEnvironmentVariable("SWIMM_NET_TESTS") != "1") return;

        var provider = new WorldAquaticsCountriesProvider(new SingleClientFactory());
        var countries = await provider.GetCountriesAsync();

        output.WriteLine($"стран: {countries.Count}, регионов: {countries.Select(c => c.Region).Distinct().Count()}");

        Assert.Equal(235, countries.Count);
        Assert.All(countries, c => Assert.Equal(3, c.Code.Length));
        Assert.Equal(countries.Count, countries.Select(c => c.Code).Distinct().Count());

        // Израиль из списка не пропадает — батч 11.1.2 пропускает его сам, зная код.
        var israel = Assert.Single(countries, c => c.Code == "ISR");
        Assert.Equal(WorldAquaticsSource.IsraelCountryId, israel.SourceId);
    }

    private sealed class SingleClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }
}
