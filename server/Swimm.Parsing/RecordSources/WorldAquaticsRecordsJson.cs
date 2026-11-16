using System.Globalization;
using System.Text.Json;
using Swimm.Application.Dtos;

namespace Swimm.Parsing.RecordSources;

/// <summary>
/// Куда раскладывать строки JSON-выдачи World Aquatics (<c>/fina/records/SW</c>) — то, чем
/// отличаются потребители: мировые юниорские (<c>world/junior</c>, полоса возраста в AgeKey),
/// мировые и национальные открытые (<c>world/open</c>, <c>country/&lt;код&gt;/open</c>).
/// </summary>
/// <param name="RegionType">«world» / «country».</param>
/// <param name="RegionCode">
/// Код региона; null — брать из строки (<c>nationalityCode</c>): так у национальных рекордов,
/// как и у XLSX-отчёта, где регион шёл из колонки NF Code.
/// </param>
/// <param name="Category">«open» / «junior».</param>
/// <param name="AgeKey">Пол записи («male» / «female» / «mixed») → AgeKey.</param>
/// <param name="IndividualHolder">Строка → держатель личного рекорда.</param>
/// <param name="Label">Ключ источника — для текста ошибок.</param>
public sealed record WaJsonAxes(
    string RegionType,
    string? RegionCode,
    string Category,
    Func<string, string> AgeKey,
    Func<JsonElement, string> IndividualHolder,
    string Label);

/// <summary>
/// Разбор одного ответа JSON-выдачи World Aquatics в строки справочника — общий для
/// <c>wa-junior</c> и <c>worldrecords</c> (с 16.11.2026: XLSX-отчёт WR отдавал 504 из кэша
/// CloudFront, а JSON того же API — 200 за секунды).
///
/// Коды источника (J0, Э3): <c>disciplineGender</c> 0 — мужчины, 1 — женщины, 2 — смешанные;
/// <c>pool</c> 0 — LCM (50m), 1 — SCM (25m). Эстафеты: стиль без слова Relay, дистанция
/// «4X100m»; состава источник не отдаёт — держатель команда (<c>fullName</c>), и строка помечена
/// <see cref="ParsedRecordDto.HolderIsTeam"/>. Строка, не легшая в оси, — null; эстафетные такие
/// считаются (<see cref="Result.SkippedRelays"/>), чтобы их можно было залогировать, а не потерять.
/// </summary>
public static class WorldAquaticsRecordsJson
{
    public sealed record Result(IReadOnlyList<ParsedRecordDto> Records, int SkippedRelays);

    /// <summary>Индивидуальные стили; «Medley» без «Relay» — всегда комплекс.</summary>
    private static readonly Dictionary<string, string> Styles = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Freestyle"] = "freestyle",
        ["Backstroke"] = "backstroke",
        ["Breaststroke"] = "breaststroke",
        ["Butterfly"] = "butterfly",
        ["Medley"] = "individual_medley",
    };

    /// <summary>
    /// Один ответ → строки. Проверяет, что ответ полный: страница одна (<c>pageSize=50</c>), и
    /// если источник однажды начнёт резать выдачу, мы должны упасть, а не записать половину
    /// справочника.
    /// </summary>
    public static Result Parse(Stream json, WaJsonAxes axes)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!root.TryGetProperty("records", out var records) || records.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException($"{axes.Label}: в ответе нет массива records — формат источника поменялся?");

        if (root.TryGetProperty("totalRowCount", out var total) && total.TryGetInt32(out var totalRows)
            && totalRows != records.GetArrayLength())
            throw new InvalidOperationException(
                $"{axes.Label}: источник отдал {records.GetArrayLength()} строк из {totalRows} — выдача " +
                "стала постраничной, нужен проход по страницам.");

        var result = new List<ParsedRecordDto>();
        var relays = 0;
        foreach (var row in records.EnumerateArray())
        {
            var group = Str(row, "disciplineGroup");
            var isRelay = group.Contains("Relay", StringComparison.OrdinalIgnoreCase);

            var dto = ToRecord(row, group, isRelay, axes);
            if (dto != null) result.Add(dto);
            else if (isRelay) relays++;
        }

        return new Result(result, relays);
    }

    private static ParsedRecordDto? ToRecord(JsonElement row, string group, bool isRelay, WaJsonAxes axes)
    {
        // «Freestyle Relay» / «Medley Relay» → стиль без слова Relay; эстафету несёт дистанция.
        var styleKey = isRelay ? group.Replace("Relay", "", StringComparison.OrdinalIgnoreCase).Trim() : group;
        if (!Styles.TryGetValue(styleKey, out var style)) return null;
        if (isRelay && style is not ("freestyle" or "individual_medley")) return null;

        var gender = Int(row, "disciplineGender") switch
        {
            0 => "male",
            1 => "female",
            2 when isRelay => "mixed",
            _ => null,
        };
        if (gender is null) return null;

        var pool = Int(row, "pool") switch
        {
            0 => "50m",
            1 => "25m",
            _ => null,
        };
        if (pool is null) return null;

        if (Int(row, "disciplineDistance") is not int distance || distance <= 0) return null;

        // timeFormatted — ровно та форма, в которой лежит справочник («46.40», «01:42.00»);
        // сырое time («00:00:24») без долей не годится.
        var time = Str(row, "timeFormatted");
        if (time.Length == 0) return null;

        var country = Str(row, "nationalityCode");
        var regionCode = axes.RegionCode ?? country;
        if (axes.RegionType == "country" && regionCode.Length == 0) return null;

        var holder = isRelay ? Str(row, "fullName") : axes.IndividualHolder(row);

        return new ParsedRecordDto(
            RegionType: axes.RegionType,
            RegionCode: regionCode,
            Category: axes.Category,
            AgeKey: axes.AgeKey(gender),
            Gender: gender,
            PoolType: pool,
            Style: style,
            Distance: (isRelay ? "4X" : "") + distance + "m",
            Time: time,
            HolderName: holder.Length == 0 ? null : holder,
            Club: null,
            HolderCountry: country.Length == 0 ? null : country,
            RecordDate: NormalizeDate(Str(row, "date")),
            HolderIsTeam: isRelay);
    }

    /// <summary>«2021-05-14T00:00:00» → «14/05/2021» — форма дат справочника.</summary>
    private static string? NormalizeDate(string source)
    {
        if (source.Length == 0) return null;
        return DateTime.TryParse(source, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)
            : source.Trim();
    }

    public static string Str(JsonElement row, string name) =>
        row.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()?.Trim() ?? ""
            : "";

    private static int? Int(JsonElement row, string name) =>
        row.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i)
            ? i
            : null;
}
