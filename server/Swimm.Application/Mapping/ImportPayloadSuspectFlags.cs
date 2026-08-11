using System.Text.Json;
using System.Text.Json.Nodes;

namespace Swimm.Application.Mapping;

/// <summary>Одна галочка «пометить сомнительным» из превью импорта: строка файла + обоснование.</summary>
/// <param name="RowIndex">Порядковый номер строки в разобранном файле (Id в БД ещё нет).</param>
public sealed record ImportSuspectFlag(int RowIndex, string? Note);

/// <summary>
/// Проставляет <c>suspect_note</c> строкам payload'а импорта, отмеченным галочкой в превью
/// (docs/admin-pages/competitions.md, «Пометить сомнительным прямо в превью»).
///
/// Адрес строки — её порядковый номер в разобранном массиве, тот же, по которому превью
/// считало рекорды (<c>ImportRecordPreviewRow.RowIndex</c>). Сопоставлять по имени и времени
/// было бы гаданием: в протоколе бывают тёзки и одинаковые времена.
/// </summary>
public static class ImportPayloadSuspectFlags
{
    /// <summary>Заметка по умолчанию: пометка без причины через полгода неотличима от случайного клика.</summary>
    public const string DefaultNote = "Помечено при импорте: заплыв бьёт рекорд";

    /// <summary>Длина колонки Results.SuspectNote.</summary>
    private const int MaxNoteLength = 300;

    /// <summary>
    /// Возвращает JSON с проставленными пометками. Файл парсера приходит в двух формах —
    /// голым массивом и обёрткой <c>{results:[…]}</c>; поддержаны обе, потому что их
    /// поддерживает импорт. Любая неожиданность (нечитаемый JSON, индекс за границей)
    /// оставляет payload как есть: потерять пометку неприятно, сорвать импорт — хуже.
    /// </summary>
    public static string Apply(string resultsJson, IReadOnlyList<ImportSuspectFlag>? flags)
    {
        if (flags is null || flags.Count == 0) return resultsJson;

        try
        {
            var root = JsonNode.Parse(resultsJson);
            var array = root as JsonArray ?? root?["results"] as JsonArray;
            if (array is null) return resultsJson;

            foreach (var flag in flags)
            {
                if (flag.RowIndex < 0 || flag.RowIndex >= array.Count) continue;
                if (array[flag.RowIndex] is not JsonObject row) continue;

                var note = (flag.Note ?? "").Trim();
                if (note.Length == 0) note = DefaultNote;
                if (note.Length > MaxNoteLength) note = note[..MaxNoteLength];
                row["suspect_note"] = note;
            }

            return root!.ToJsonString();
        }
        catch (JsonException)
        {
            return resultsJson;
        }
    }
}
