using Swimm.Application.Dtos;

namespace Swimm.Application.Mapping;

/// <summary>
/// Выгрузка разобранных строк справочника в CSV — формат архива источников
/// (`!records-sources/README.md`).
///
/// Зачем одно место на двух потребителей: так выгружает и ручной
/// <c>--records-dump &lt;источник&gt;</c>, и боевой прогон по странам, который пишет CSV сам.
/// Две копии этого кода разъехались бы на первой же правке, а вся ценность архива — в том,
/// что выгрузки РАЗНЫХ дней сравнимы построчно. Разъехался формат — сравнивать нечего.
///
/// Три решения, которые нельзя менять «для красоты»:
/// <list type="bullet">
/// <item><b>Порядок строк — по восьми осям, <see cref="StringComparer.Ordinal"/>.</b> Без
/// устойчивого порядка <c>git diff</c> показывал бы перестановки вместо изменений. Ordinal,
/// а не культурный: сортировка обязана совпасть на Windows и на Linux-раннере.</item>
/// <item><b>Перевод строки — всегда LF</b>, а не <c>Environment.NewLine</c>. Файл коммитится
/// и сверяется контрольной суммой; CRLF на Windows разъехал бы её на ровном месте (та же
/// ловушка, что с бандлом админки, см. <c>.gitattributes</c>).</item>
/// <item><b>Кавычки по RFC 4180.</b> Перевод строки в поле справочника не встречался, но
/// одна такая строка сдвинула бы весь CSV и сделала бы diff нечитаемым.</item>
/// </list>
/// </summary>
public static class RecordCsvDump
{
    public const string Header =
        "RegionType,RegionCode,Category,AgeKey,Gender,PoolType,Style,Distance,Time,HolderName,Club,HolderCountry,RecordDate";

    /// <summary>Готовое содержимое файла, включая завершающий перевод строки.</summary>
    public static string Build(IEnumerable<ParsedRecordDto> rows)
    {
        var lines = new List<string> { Header };

        lines.AddRange(rows
            .OrderBy(r => r.RegionType, StringComparer.Ordinal)
            .ThenBy(r => r.RegionCode, StringComparer.Ordinal)
            .ThenBy(r => r.Category, StringComparer.Ordinal)
            .ThenBy(r => r.AgeKey, StringComparer.Ordinal)
            .ThenBy(r => r.Gender, StringComparer.Ordinal)
            .ThenBy(r => r.PoolType, StringComparer.Ordinal)
            .ThenBy(r => r.Style, StringComparer.Ordinal)
            .ThenBy(r => r.Distance, StringComparer.Ordinal)
            .Select(r => string.Join(',', new[]
            {
                Csv(r.RegionType), Csv(r.RegionCode), Csv(r.Category), Csv(r.AgeKey), Csv(r.Gender),
                Csv(r.PoolType), Csv(r.Style), Csv(r.Distance), Csv(r.Time), Csv(r.HolderName),
                Csv(r.Club), Csv(r.HolderCountry), Csv(r.RecordDate),
            })));

        return string.Join("\n", lines) + "\n";
    }

    private static string Csv(string? value)
    {
        value ??= "";
        var needsQuotes = value.IndexOfAny(['"', ',', '\r', '\n']) >= 0;
        return needsQuotes ? '"' + value.Replace("\"", "\"\"") + '"' : value;
    }
}
