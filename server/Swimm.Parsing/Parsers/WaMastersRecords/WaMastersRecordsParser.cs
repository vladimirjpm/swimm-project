using System.Text;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace Swimm.Parsing.Parsers.WaMastersRecords;

/// <summary>
/// Разбор PDF «Masters World Records» World Aquatics (личные дистанции, LCM и SCM).
///
/// Файл устроен предсказуемо: сначала ~18 страниц оглавления (одни названия дисциплин, без
/// времён), дальше на каждый рекорд идут ТРИ строки подряд —
/// <code>
/// LCM Women 50m Freestyle (25-29)
/// Time      NF    Athlete            Date
/// 25.37     USA   CUNDIFF Missy      24 Aug 2024
/// </code>
/// Поэтому парсер — маленький автомат: заголовок блока → строка шапки (из неё берём X-границы
/// колонок) → строка данных. Оглавление отсеивается само: там нет ни шапки, ни данных.
///
/// ⚠ Колонки режем ПО X, а не по пробелам. Когда время широкое, оно печатается вплотную к коду
/// страны и склеивается с ним в одно слово («04:00.96RUS» — 7 таких строк на 1095). Границы
/// колонок каждый раз берутся из СВОЕЙ шапки, а не зашиты числами: сдвинет источник вёрстку —
/// парсер поедет за ней.
/// </summary>
public class WaMastersRecordsParser
{
    /// <summary>«LCM Women 50m Freestyle (25-29)» — заголовок блока рекорда.</summary>
    private static readonly Regex EventRx = new(
        @"^(?<pool>LCM|SCM)\s+(?<gender>Women|Men|Mixed)\s+(?<dist>\d+)m\s+(?<style>[A-Za-z]+)\s*"
        + @"\((?<from>\d+)\s*-\s*(?<to>\d+)\)$",
        RegexOptions.Compiled);

    /// <summary>Строки одного baseline считаем одной строкой таблицы: у источника шаг ~15 pt.</summary>
    private const double LineTolerance = 2.0;

    /// <summary>Разбор одного файла. Ошибки формата не глотаем — пустой список тоже ответ.</summary>
    public IReadOnlyList<WaMastersRecordRow> Parse(Stream pdf, CancellationToken ct = default)
    {
        var rows = new List<WaMastersRecordRow>();

        using var doc = PdfDocument.Open(pdf);

        Match? pending = null;          // заголовок блока, чьи данные ещё не встретились
        double[]? columns = null;       // левые границы колонок из шапки этого блока

        foreach (var page in doc.GetPages())
        {
            ct.ThrowIfCancellationRequested();

            foreach (var line in LinesOf(page))
            {
                var text = Flatten(string.Join(" ", line.Select(w => w.Text)));
                if (text.Length == 0) continue;

                var head = EventRx.Match(text);
                if (head.Success)
                {
                    pending = head;
                    columns = null;
                    continue;
                }

                if (pending is null) continue;

                if (columns is null)
                {
                    if (IsColumnHeader(text)) columns = line.Select(w => w.BoundingBox.Left).ToArray();
                    continue;
                }

                var cells = SplitByColumns(line, columns);
                rows.Add(new WaMastersRecordRow(
                    PoolCode: pending.Groups["pool"].Value,
                    Gender: pending.Groups["gender"].Value,
                    Distance: pending.Groups["dist"].Value,
                    StyleName: pending.Groups["style"].Value,
                    AgeKey: $"{pending.Groups["from"].Value}-{pending.Groups["to"].Value}",
                    Time: cells.Length > 0 ? cells[0] : "",
                    Country: cells.Length > 1 ? cells[1] : "",
                    Athlete: cells.Length > 2 ? cells[2] : "",
                    RecordDate: cells.Length > 3 ? cells[3] : ""));

                pending = null;
                columns = null;
            }
        }

        return rows;
    }

    /// <summary>«Time NF Athlete Date» — шапка таблицы блока (пробелов между колонками в PDF нет).</summary>
    private static bool IsColumnHeader(string text)
    {
        var squashed = text.Replace(" ", "");
        return squashed.StartsWith("Time", StringComparison.Ordinal)
               && squashed.Contains("Athlete", StringComparison.Ordinal)
               && squashed.EndsWith("Date", StringComparison.Ordinal);
    }

    /// <summary>Слова страницы, сгруппированные в строки по базовой линии и слева направо.</summary>
    private static IEnumerable<IReadOnlyList<Word>> LinesOf(Page page)
    {
        var words = page.GetWords()
            .Where(w => !string.IsNullOrWhiteSpace(w.Text))
            .OrderByDescending(w => w.BoundingBox.Bottom)
            .ToList();

        var current = new List<Word>();
        double? baseline = null;

        foreach (var w in words)
        {
            if (baseline is double y && Math.Abs(y - w.BoundingBox.Bottom) > LineTolerance)
            {
                yield return Sorted(current);
                current = new List<Word>();
                baseline = null;
            }

            baseline ??= w.BoundingBox.Bottom;
            current.Add(w);
        }

        if (current.Count > 0) yield return Sorted(current);

        static IReadOnlyList<Word> Sorted(List<Word> line) =>
            line.OrderBy(w => w.BoundingBox.Left).ToList();
    }

    /// <summary>
    /// Раскладывает строку по колонкам. Работаем на уровне БУКВ: слово может начаться в одной
    /// колонке и закончиться в следующей («04:00.96RUS»), и тогда резать его надо по X, а не
    /// по пробелу, которого там нет. Куски в одной колонке склеиваются пробелом — так «CUNDIFF»
    /// и «Missy» снова становятся одним именем.
    /// </summary>
    private static string[] SplitByColumns(IReadOnlyList<Word> line, double[] columns)
    {
        var parts = new List<string>[columns.Length];
        for (var i = 0; i < parts.Length; i++) parts[i] = new List<string>();

        foreach (var word in line)
        {
            var chunk = new StringBuilder();
            var chunkColumn = -1;

            foreach (var letter in word.Letters)
            {
                var column = ColumnOf(letter.GlyphRectangle.Left, columns);
                if (column != chunkColumn)
                {
                    Flush(parts, chunkColumn, chunk);
                    chunkColumn = column;
                }
                chunk.Append(letter.Value);
            }

            Flush(parts, chunkColumn, chunk);
        }

        return parts.Select(p => Flatten(string.Join(" ", p))).ToArray();

        static void Flush(List<string>[] parts, int column, StringBuilder chunk)
        {
            if (column >= 0 && chunk.Length > 0) parts[column].Add(chunk.ToString());
            chunk.Clear();
        }
    }

    /// <summary>Колонка буквы — последняя, чья левая граница ещё левее буквы (с допуском).</summary>
    private static int ColumnOf(double x, double[] columns)
    {
        var found = 0;
        for (var i = 0; i < columns.Length; i++)
            if (x >= columns[i] - LineTolerance) found = i;
        return found;
    }

    /// <summary>
    /// Лигатуры в «Butterﬂy»/«ﬁ» приезжают из PDF одним символом — без развёртки стиль
    /// «butterfly» не нашёлся бы никогда.
    /// </summary>
    private static string Flatten(string text) =>
        text.Replace("ﬁ", "fi")
            .Replace("ﬂ", "fl")
            .Replace("–", "-")
            .Trim();
}
