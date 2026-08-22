using Microsoft.Extensions.Configuration;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Parsing.Parsers;
using Swimm.Parsing.Parsers.IsrOrgAgeRecords;

namespace Swimm.Parsing.RecordSources;

/// <summary>
/// Возрастные (10–18) + национальные (Israel) рекорды из PDF-протокола isr.org.il.
///
/// Fetch без настройки: URL файлов резолвится со страницы «שיאי ישראל»
/// (<see cref="IsrOrgRecordsPageResolver"/>) — прибивать их в конфиг бесполезно, федерация
/// зашивает дату обновления в имя файла и меняет адрес при каждом обновлении. Явные URL в
/// appsettings (IsrOrgAgeRecordsUrl25m/50m) остаются как ручной перехват — если заданы,
/// берутся они. Ручная загрузка файла(ов) ВСЕГДА работает как fallback (приёмка 2.6).
/// SSRF: домен любого скачиваемого URL проверяется против whitelist isr.org.il.
/// </summary>
public class IsrOrgAgeRecordsSourceProvider : IRecordSourceProvider
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly IsrOrgAgeRecordsParser _parser;
    private readonly IsrOrgRecordsPageResolver _pageResolver;

    public IsrOrgAgeRecordsSourceProvider(
        IHttpClientFactory httpClientFactory, IConfiguration configuration,
        IsrOrgAgeRecordsParser parser, IsrOrgRecordsPageResolver pageResolver)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _parser = parser;
        _pageResolver = pageResolver;
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

                // Ничего не задано руками — идём на страницу-оглавление за актуальными файлами.
                if (string.IsNullOrWhiteSpace(url25) && string.IsNullOrWhiteSpace(url50))
                {
                    var pageUrl = _pageResolver.PageUrl;
                    var links = await _pageResolver.ResolveAsync(pageUrl, ct);
                    url25 = IsrOrgRecordsPageResolver.Pick(links, isMasters: false, "25m")?.Url;
                    url50 = IsrOrgRecordsPageResolver.Pick(links, isMasters: false, "50m")?.Url;

                    if (string.IsNullOrWhiteSpace(url25) && string.IsNullOrWhiteSpace(url50))
                        throw new InvalidOperationException(
                            $"На странице {pageUrl} не нашлось ссылок на справочник рекордов "
                            + "«בוגרים ונוער» — загрузите PDF-файл(ы) вручную.");
                }

                var client = IsrOrgRecordsSource.CreateClient(_httpClientFactory);

                // Основной файл — 25m (в нём же национальные рекорды); если на странице есть
                // только 50m, он и становится основным, чтобы фетч не падал впустую.
                if (!string.IsNullOrWhiteSpace(url25))
                {
                    var ms25 = await IsrOrgRecordsSource.FetchWhitelistedAsync(client, url25, ct);
                    owned.Add(ms25);
                    primary = ms25;
                    primaryPool = "25m";

                    if (!string.IsNullOrWhiteSpace(url50))
                    {
                        var ms50 = await IsrOrgRecordsSource.FetchWhitelistedAsync(client, url50, ct);
                        owned.Add(ms50);
                        secondary = ms50;
                    }
                    else
                    {
                        secondary = null;
                    }
                }
                else
                {
                    var ms50 = await IsrOrgRecordsSource.FetchWhitelistedAsync(client, url50!, ct);
                    owned.Add(ms50);
                    primary = ms50;
                    primaryPool = "50m";
                    secondary = null;
                }
            }

            var parseRequest = new ParseRequest(
                primary, $"isrorg-age-{primaryPool}.pdf",
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

    private static string NormalizeTime(string time) => time.StartsWith("00:") ? time[3..] : time;
}
