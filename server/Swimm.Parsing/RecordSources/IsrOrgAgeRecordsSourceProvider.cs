using Microsoft.Extensions.Configuration;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Parsing.Parsers;
using Swimm.Parsing.Parsers.IsrOrgAgeRecords;

namespace Swimm.Parsing.RecordSources;

/// <summary>
/// Возрастные (10–18) + национальные (Israel) рекорды из PDF-протокола isr.org.il.
/// URL страницы вынесены в настройки (appsettings:RecordsImport) — могут меняться;
/// ручная загрузка файла(ов) ВСЕГДА работает как fallback (см. приёмку задания 2.6).
/// SSRF: если фетч включён, домен целевого URL проверяется против whitelist isr.org.il.
/// </summary>
public class IsrOrgAgeRecordsSourceProvider : IRecordSourceProvider
{
    private const string AllowedHost = "isr.org.il";
    private const string AllowedHostSuffix = ".isr.org.il";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly IsrOrgAgeRecordsParser _parser;

    public IsrOrgAgeRecordsSourceProvider(
        IHttpClientFactory httpClientFactory, IConfiguration configuration, IsrOrgAgeRecordsParser parser)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _parser = parser;
    }

    public string Source => "isrorg-age";

    public async Task<IReadOnlyList<ParsedRecordDto>> FetchAsync(RecordSourceRequest request, CancellationToken ct = default)
    {
        List<MemoryStream> owned = new();
        try
        {
            Stream primary;
            Stream? secondary;
            string primaryPool = request.PoolType ?? "25m";

            if (request.PrimaryStream != null)
            {
                primary = request.PrimaryStream;
                secondary = request.SecondaryStream;
            }
            else
            {
                var url25 = _configuration["RecordsImport:IsrOrgAgeRecordsUrl25m"];
                var url50 = _configuration["RecordsImport:IsrOrgAgeRecordsUrl50m"];
                if (string.IsNullOrWhiteSpace(url25))
                    throw new InvalidOperationException(
                        "URL источника Age Records не настроен (RecordsImport:IsrOrgAgeRecordsUrl25m) — загрузите PDF-файл(ы) вручную.");

                var client = _httpClientFactory.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(30);
                // Без User-Agent некоторые источники (worldaquatics точно) не отвечают вовсе.
                client.DefaultRequestHeaders.UserAgent.ParseAdd("SwimmBot/1.0");

                var ms25 = await FetchWhitelistedAsync(client, url25, ct);
                owned.Add(ms25);
                primary = ms25;
                primaryPool = "25m";

                if (!string.IsNullOrWhiteSpace(url50))
                {
                    var ms50 = await FetchWhitelistedAsync(client, url50, ct);
                    owned.Add(ms50);
                    secondary = ms50;
                }
                else
                {
                    secondary = null;
                }
            }

            var parseRequest = new ParseRequest(
                primary, "isrorg-age-25m.pdf",
                secondary, secondary != null ? "isrorg-age-50m.pdf" : null,
                IsAward: false,
                PoolType: primaryPool);

            var results = _parser.Parse(parseRequest).ToList();

            var parsed = new List<ParsedRecordDto>();
            foreach (var r in results)
            {
                if (r.EventStyleGender != "male" && r.EventStyleGender != "female")
                    continue;

                string category, ageKey;
                if (r.Note == "National Record")
                {
                    category = "open";
                    ageKey = "";
                }
                else if (r.Note is "Age adults_m Record" or "Age adults_f Record")
                {
                    category = "age";
                    ageKey = "adults";
                }
                else
                {
                    category = "age";
                    ageKey = r.EventStyleAge;
                }

                var distance = r.EventStyleLen.EndsWith('m') ? r.EventStyleLen : r.EventStyleLen + "m";

                parsed.Add(new ParsedRecordDto(
                    RegionType: "country",
                    RegionCode: "ISR",
                    Category: category,
                    AgeKey: ageKey,
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
