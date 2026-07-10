using Microsoft.Extensions.Configuration;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Parsing.Parsers;
using Swimm.Parsing.Parsers.IsrOrgMastersRecords;

namespace Swimm.Parsing.RecordSources;

/// <summary>
/// Мастерские (25-29…) рекорды из PDF-протокола isr.org.il. Симметрично
/// <see cref="IsrOrgAgeRecordsSourceProvider"/> — см. его комментарий по URL/SSRF/fallback.
/// </summary>
public class IsrOrgMastersRecordsSourceProvider : IRecordSourceProvider
{
    private const string AllowedHost = "isr.org.il";
    private const string AllowedHostSuffix = ".isr.org.il";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly IsrOrgMastersRecordsParser _parser;

    public IsrOrgMastersRecordsSourceProvider(
        IHttpClientFactory httpClientFactory, IConfiguration configuration, IsrOrgMastersRecordsParser parser)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _parser = parser;
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
                if (string.IsNullOrWhiteSpace(url50))
                    throw new InvalidOperationException(
                        "URL источника Masters Records не настроен (RecordsImport:IsrOrgMastersRecordsUrl50m) — загрузите PDF-файл(ы) вручную.");

                var client = _httpClientFactory.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(30);
                // Без User-Agent некоторые источники (worldaquatics точно) не отвечают вовсе.
                client.DefaultRequestHeaders.UserAgent.ParseAdd("SwimmBot/1.0");

                var ms50 = await FetchWhitelistedAsync(client, url50, ct);
                owned.Add(ms50);
                primary = ms50;
                primaryPool = "50m";

                if (!string.IsNullOrWhiteSpace(url25))
                {
                    var ms25 = await FetchWhitelistedAsync(client, url25, ct);
                    owned.Add(ms25);
                    secondary = ms25;
                }
                else
                {
                    secondary = null;
                }
            }

            var parseRequest = new ParseRequest(
                primary, "isrorg-masters-50m.pdf",
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

    private static async Task<MemoryStream> FetchWhitelistedAsync(HttpClient client, string url, CancellationToken ct)
    {
        var uri = new Uri(url);
        if (!string.Equals(uri.Host, AllowedHost, StringComparison.OrdinalIgnoreCase)
            && !uri.Host.EndsWith(AllowedHostSuffix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Домен '{uri.Host}' не в whitelist источников рекордов.");

        var response = await client.GetAsync(uri, ct);
        response.EnsureSuccessStatusCode();
        var ms = new MemoryStream();
        await response.Content.CopyToAsync(ms, ct);
        ms.Position = 0;
        return ms;
    }

    private static string NormalizeTime(string time) => time.StartsWith("00:") ? time[3..] : time;
}
