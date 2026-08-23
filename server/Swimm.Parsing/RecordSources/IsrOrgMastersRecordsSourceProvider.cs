using Microsoft.Extensions.Configuration;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Parsing.Parsers;
using Swimm.Parsing.Parsers.IsrOrgMastersRecords;

namespace Swimm.Parsing.RecordSources;

/// <summary>
/// Мастерские (25-29…) рекорды из PDF-протокола isr.org.il. Симметрично
/// <see cref="IsrOrgAgeRecordsSourceProvider"/> — см. его комментарий по URL/SSRF/fallback:
/// без настроек URL резолвятся со страницы «שיאי ישראל» через
/// <see cref="IsrOrgRecordsPageResolver"/>, основной файл здесь — 50m.
/// </summary>
public class IsrOrgMastersRecordsSourceProvider : IRecordSourceProvider
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly IsrOrgMastersRecordsParser _parser;
    private readonly IsrOrgRecordsPageResolver _pageResolver;

    public IsrOrgMastersRecordsSourceProvider(
        IHttpClientFactory httpClientFactory, IConfiguration configuration,
        IsrOrgMastersRecordsParser parser, IsrOrgRecordsPageResolver pageResolver)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _parser = parser;
        _pageResolver = pageResolver;
    }

    public string Source => "isrorg-masters";

    public async Task<IReadOnlyList<ParsedRecordDto>> FetchAsync(RecordSourceRequest request, CancellationToken ct = default)
    {
        List<MemoryStream> owned = new();
        try
        {
            Stream primary;
            Stream? secondary;
            string primaryPool = request.PoolType ?? "50m";

            if (request.PrimaryStream != null)
            {
                primary = request.PrimaryStream;
                secondary = request.SecondaryStream;
            }
            else
            {
                var url50 = _configuration["RecordsImport:IsrOrgMastersRecordsUrl50m"];
                var url25 = _configuration["RecordsImport:IsrOrgMastersRecordsUrl25m"];

                // Ничего не задано руками — идём на страницу-оглавление за актуальными файлами.
                if (string.IsNullOrWhiteSpace(url50) && string.IsNullOrWhiteSpace(url25))
                {
                    var pageUrl = _pageResolver.PageUrl;
                    var links = await _pageResolver.ResolveAsync(pageUrl, ct);
                    url50 = IsrOrgRecordsPageResolver.Pick(links, isMasters: true, "50m")?.Url;
                    url25 = IsrOrgRecordsPageResolver.Pick(links, isMasters: true, "25m")?.Url;

                    if (string.IsNullOrWhiteSpace(url50) && string.IsNullOrWhiteSpace(url25))
                        throw new InvalidOperationException(
                            $"На странице {pageUrl} не нашлось ссылок на «שיאי מאסטרס» — "
                            + "загрузите PDF-файл(ы) вручную.");
                }

                var client = IsrOrgRecordsSource.CreateClient(_httpClientFactory);

                // Основной файл — 50m; если на странице есть только 25m, основным становится он.
                if (!string.IsNullOrWhiteSpace(url50))
                {
                    var ms50 = await IsrOrgRecordsSource.FetchWhitelistedAsync(client, url50, ct);
                    owned.Add(ms50);
                    primary = ms50;
                    primaryPool = "50m";

                    if (!string.IsNullOrWhiteSpace(url25))
                    {
                        var ms25 = await IsrOrgRecordsSource.FetchWhitelistedAsync(client, url25, ct);
                        owned.Add(ms25);
                        secondary = ms25;
                    }
                    else
                    {
                        secondary = null;
                    }
                }
                else
                {
                    var ms25 = await IsrOrgRecordsSource.FetchWhitelistedAsync(client, url25!, ct);
                    owned.Add(ms25);
                    primary = ms25;
                    primaryPool = "25m";
                    secondary = null;
                }
            }

            var parseRequest = new ParseRequest(
                primary, $"isrorg-masters-{primaryPool}.pdf",
                secondary, secondary != null ? "isrorg-masters-25m.pdf" : null,
                IsAward: false,
                PoolType: primaryPool);

            var results = _parser.Parse(parseRequest).ToList();

            var parsed = new List<ParsedRecordDto>();
            foreach (var r in results)
            {
                if (r.EventStyleGender != "male" && r.EventStyleGender != "female")
                    continue;

                var distance = r.EventStyleLen.EndsWith('m') ? r.EventStyleLen : r.EventStyleLen + "m";

                parsed.Add(new ParsedRecordDto(
                    RegionType: "country",
                    RegionCode: "ISR",
                    Category: "masters",
                    AgeKey: r.AgeGroup,
                    Gender: r.EventStyleGender,
                    PoolType: r.PoolType,
                    Style: r.EventStyleName,
                    Distance: distance,
                    Time: NormalizeTime(r.Time),
                    HolderName: $"{r.FirstName} {r.LastName}".Trim(),
                    Club: string.IsNullOrWhiteSpace(r.Club) ? null : r.Club,
                    HolderCountry: "ISR",
                    RecordDate: r.Date));
            }

            return parsed;
        }
        finally
        {
            foreach (var ms in owned) await ms.DisposeAsync();
        }
    }

    private static string NormalizeTime(string time) => time.StartsWith("00:") ? time[3..] : time;
}
