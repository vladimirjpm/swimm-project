using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Parsing.Parsers.WaMastersRecords;

namespace Swimm.Parsing.RecordSources;

/// <summary>
/// Мастерские МИРОВЫЕ рекорды (<c>world/masters</c>) из PDF World Aquatics — личные дистанции,
/// полосы 25-29 … 105-109, оба бассейна.
///
/// Почему отдельный источник, а не отчёт api.worldaquatics.com: XLSX-отчёт рекордов знает
/// только <c>recordCode=WR|NR</c>, мастерса там нет вовсе (проверено живыми запросами
/// 16.09.2026 — любой другой код даёт 400; кроме <c>WJ</c>, который принимается, но отдаёт
/// 504 — юниорские тянет JSON-выдача, <see cref="WaJuniorRecordsSourceProvider"/>).
/// Мастерские рекорды World Aquatics публикует
/// PDF-ками на своём сайте, и это единственный их машинный вид.
///
/// Осевой охват ровно такой: <c>RegionType=world</c>, <c>Category=masters</c>. С израильским
/// <c>isrorg-masters</c> (тот пишет <c>country/ISR/masters</c>) ключи упсерта не пересекаются
/// вообще, поэтому порядка «кто после кого» между ними нет — в отличие от И-13, где два
/// источника спорят за <c>country/ISR/open</c>.
///
/// Эстафеты сознательно не берём: в модели <c>Record</c> нет эстафетных стилей и пола mixed,
/// а полосы там по СУММЕ возрастов четвёрки («100-119») — это другая ось, а не другой ключ.
/// </summary>
public class WaMastersRecordsSourceProvider : IRecordSourceProvider
{
    public const string SourceKey = "wa-masters";

    /// <summary>
    /// Заголовок блока называет стиль по-английски; в осях <c>Record</c> он живёт нашим кодом.
    /// «Medley» у личных дистанций всегда комплекс — эстафетного комплекса в этом файле нет.
    /// </summary>
    private static readonly Dictionary<string, string> Styles = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Freestyle"] = "freestyle",
        ["Backstroke"] = "backstroke",
        ["Breaststroke"] = "breaststroke",
        ["Butterfly"] = "butterfly",
        ["Medley"] = "individual_medley",
    };

    private static readonly string[] Months =
        ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly WaMastersRecordsParser _parser;
    private readonly WaMastersRecordsPageResolver _pageResolver;

    public WaMastersRecordsSourceProvider(
        IHttpClientFactory httpClientFactory,
        WaMastersRecordsParser parser,
        WaMastersRecordsPageResolver pageResolver)
    {
        _httpClientFactory = httpClientFactory;
        _parser = parser;
        _pageResolver = pageResolver;
    }

    public string Source => SourceKey;

    public async Task<IReadOnlyList<ParsedRecordDto>> FetchAsync(
        RecordSourceRequest request, CancellationToken ct = default)
    {
        List<MemoryStream> owned = new();
        try
        {
            var files = new List<Stream>();

            if (request.PrimaryStream != null)
            {
                files.Add(request.PrimaryStream);
                if (request.SecondaryStream != null) files.Add(request.SecondaryStream);
            }
            else
            {
                var links = await _pageResolver.ResolveAsync(ct);
                if (links.Count == 0)
                    throw new InvalidOperationException(
                        $"На странице {_pageResolver.PageUrl} не нашлось файлов «Masters World Records "
                        + "— Individual» — загрузите PDF вручную.");

                // Таймаут щедрый: файлы около мегабайта, но отдаются медленнее отчётов API.
                var client = WorldAquaticsSource.CreateClient(_httpClientFactory, TimeSpan.FromSeconds(120));
                foreach (var link in links)
                {
                    var ms = await FetchAsync(client, link.Url, ct);
                    owned.Add(ms);
                    files.Add(ms);
                }
            }

            var parsed = new List<ParsedRecordDto>();
            foreach (var file in files)
            {
                foreach (var row in _parser.Parse(file, ct))
                {
                    var dto = ToRecord(row);
                    if (dto != null) parsed.Add(dto);
                }
            }

            return parsed;
        }
        finally
        {
            foreach (var ms in owned) await ms.DisposeAsync();
        }
    }

    /// <summary>
    /// Строка файла → строка справочника. null — строка не ложится в оси <c>Record</c>
    /// (незнакомый стиль, эстафетный пол): пропускаем молча ровно как остальные источники,
    /// расхождение числа строк видно в диффе.
    /// </summary>
    public static ParsedRecordDto? ToRecord(WaMastersRecordRow row)
    {
        if (!Styles.TryGetValue(row.StyleName, out var style)) return null;

        var gender = row.Gender switch
        {
            "Women" => "female",
            "Men" => "male",
            _ => null,
        };
        if (gender is null) return null;

        // Бассейн берём из самого файла, а не из запроса: заголовок каждого блока начинается
        // с LCM/SCM, и перепутать руками загруженный файл с его бассейном просто нечем.
        var pool = row.PoolCode switch
        {
            "LCM" => "50m",
            "SCM" => "25m",
            _ => null,
        };
        if (pool is null) return null;

        return new ParsedRecordDto(
            RegionType: "world",
            RegionCode: "",
            Category: "masters",
            AgeKey: row.AgeKey,
            Gender: gender,
            PoolType: pool,
            Style: style,
            Distance: row.Distance + "m",
            Time: row.Time,
            HolderName: string.IsNullOrWhiteSpace(row.Athlete) ? null : row.Athlete,
            Club: null,
            HolderCountry: string.IsNullOrWhiteSpace(row.Country) ? null : row.Country,
            RecordDate: NormalizeDate(row.RecordDate));
    }

    /// <summary>
    /// «24 Aug 2024» → «24/08/2024». Значение то же, форма — та, в которой даты уже лежат в
    /// <c>world/open</c>: колонка одна, и две записи в ней хуже одной.
    /// Неузнанную строку отдаём как есть — потерять дату хуже, чем показать её непривычно.
    /// </summary>
    private static string? NormalizeDate(string? source)
    {
        if (string.IsNullOrWhiteSpace(source)) return null;

        var parts = source.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3) return source.Trim();

        var month = Array.FindIndex(Months, m => m.Equals(parts[1], StringComparison.OrdinalIgnoreCase)) + 1;
        if (month == 0 || !int.TryParse(parts[0], out var day) || !int.TryParse(parts[2], out var year))
            return source.Trim();

        return $"{day:D2}/{month:D2}/{year:D4}";
    }

    private static async Task<MemoryStream> FetchAsync(HttpClient client, string url, CancellationToken ct)
    {
        var uri = WorldAquaticsSource.EnsureDocumentWhitelisted(url);

        var response = await client.GetAsync(uri, ct);
        response.EnsureSuccessStatusCode();

        var ms = new MemoryStream();
        await response.Content.CopyToAsync(ms, ct);
        ms.Position = 0;
        return ms;
    }
}
