using Microsoft.Extensions.Configuration;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Parsing.Parsers;
using Swimm.Parsing.Parsers.WorldRecords;

namespace Swimm.Parsing.RecordSources;

/// <summary>
/// Мировые + национальные рекорды с api.worldaquatics.com. Восстановлено из
/// снесённого <c>Swimm.Parser/Controllers/PdfController.FetchWorldRecordsAsync</c>
/// (commit 61af61b^) — та же схема из 4 XLSX-отчётов (WR SCM/LCM + NR SCM/LCM).
/// Чьи NR качать, задаёт RecordsImport:WorldAquaticsNationalCountryId (внутренний GUID
/// страны в API worldaquatics); по умолчанию — Израиль (домашний регион проекта).
/// SSRF: URL целиком собираются из константы домена + жёстко заданных query-параметров —
/// пользовательский ввод в URL никогда не попадает.
/// </summary>
public class WorldRecordsSourceProvider : IRecordSourceProvider
{
    private const string ApiHost = "api.worldaquatics.com";
    private const string DefaultNationalCountryId = "962f77d6-d9c0-49ad-ba93-adc831c9ec9f"; // Израиль

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly WorldRecordsParser _parser;
    private readonly string _nationalCountryId;

    public WorldRecordsSourceProvider(IHttpClientFactory httpClientFactory, WorldRecordsParser parser,
        IConfiguration? configuration = null)
    {
        _httpClientFactory = httpClientFactory;
        _parser = parser;
        var configured = configuration?["RecordsImport:WorldAquaticsNationalCountryId"];
        _nationalCountryId = string.IsNullOrWhiteSpace(configured) ? DefaultNationalCountryId : configured;
    }

    public string Source => "worldrecords";

    public async Task<IReadOnlyList<ParsedRecordDto>> FetchAsync(RecordSourceRequest request, CancellationToken ct = default)
    {
        List<MemoryStream> owned = new();
        try
        {
            Stream primary, secondary;
            List<(Stream Stream, string FileName)> extra = new();

            if (request.PrimaryStream != null)
            {
                // Ручная загрузка (fallback) — тот же порядок файлов, что и авто-фетч.
                primary = request.PrimaryStream;
                secondary = request.SecondaryStream
                    ?? throw new InvalidOperationException("Нужны минимум 2 файла (WR SCM + WR LCM).");
            }
            else
            {
                var urls = new (string Url, string FileName)[]
                {
                    (BuildUrl("pool=SCM&recordCode=WR"), "WR_SCM.xlsx"),
                    (BuildUrl("pool=LCM&recordCode=WR"), "WR_LCM.xlsx"),
                    (BuildUrl($"recordCode=NR&pool=SCM&countryId={_nationalCountryId}"), "NR_SCM.xlsx"),
                    (BuildUrl($"recordCode=NR&pool=LCM&countryId={_nationalCountryId}"), "NR_LCM.xlsx"),
                };

                var client = _httpClientFactory.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(30);
                // Без User-Agent api.worldaquatics.com не отвечает вовсе (запрос висит до
                // таймаута) — проверено на приёмке 2.6; конкретное значение не важно.
                client.DefaultRequestHeaders.UserAgent.ParseAdd("SwimmBot/1.0");

                var streams = new List<MemoryStream>();
                foreach (var (url, _) in urls)
                {
                    var uri = new Uri(url);
                    if (!string.Equals(uri.Host, ApiHost, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException($"Домен '{uri.Host}' не в whitelist источников рекордов.");

                    var response = await client.GetAsync(uri, ct);
                    response.EnsureSuccessStatusCode();
                    var ms = new MemoryStream();
                    await response.Content.CopyToAsync(ms, ct);
                    ms.Position = 0;
                    streams.Add(ms);
                }

                owned.AddRange(streams);
                primary = streams[0];
                secondary = streams[1];
                extra.Add((streams[2], urls[2].FileName));
                extra.Add((streams[3], urls[3].FileName));
            }

            var parseRequest = new ParseRequest(
                primary, "WR_SCM.xlsx",
                secondary, "WR_LCM.xlsx",
                IsAward: false,
                PoolType: null,
                ExtraStreams: extra.Count > 0 ? extra : null);

            var results = _parser.Parse(parseRequest).ToList();

            var parsed = new List<ParsedRecordDto>();
            foreach (var r in results)
            {
                if (r.EventStyleGender != "male" && r.EventStyleGender != "female")
                    continue; // mixed-relay — вне модели осей Record (open male/female), пропускаем

                var isWorld = string.Equals(r.Note, "WR", StringComparison.OrdinalIgnoreCase);
                var regionType = isWorld ? "world" : "country";
                var regionCode = isWorld ? "" : (r.Note ?? "").Trim().ToUpperInvariant();
                var distance = r.EventStyleLen.EndsWith('m') ? r.EventStyleLen : r.EventStyleLen + "m";

                parsed.Add(new ParsedRecordDto(
                    RegionType: regionType,
                    RegionCode: regionCode,
                    Category: "open",
                    AgeKey: "",
                    Gender: r.EventStyleGender,
                    PoolType: r.PoolType,
                    Style: r.EventStyleName,
                    Distance: distance,
                    Time: r.Time,
                    HolderName: $"{r.FirstName} {r.LastName}".Trim(),
                    Club: null,
                    HolderCountry: r.Country,
                    RecordDate: r.Date));
            }

            return parsed;
        }
        finally
        {
            foreach (var ms in owned) await ms.DisposeAsync();
        }
    }

    private static string BuildUrl(string query) => $"https://{ApiHost}/fina/records/report?{query}";
}
