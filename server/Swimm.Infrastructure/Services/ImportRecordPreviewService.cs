using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Swimm.Application.Abstractions;
using Swimm.Application.Dtos;
using Swimm.Application.Mapping;
using Swimm.Infrastructure.Data;

namespace Swimm.Infrastructure.Services;

/// <summary>
/// «Сколько рекордов побьёт файл» для превью импорта (docs/data-integrity.md §12, Б2).
///
/// Считает ТЕМ ЖЕ детектором, что и карточка «New records» соревнования
/// (<see cref="CompetitionRecordsDetector"/>) — иначе превью обещало бы одно, а страница
/// соревнования показывала другое, и доверия к предупреждению не было бы. Разница только
/// во входе: строки берутся из разобранного файла, а не из БД.
/// </summary>
public class ImportRecordPreviewService(SwimmDbContext db) : IImportRecordPreviewService
{
    /// <summary>Больше не показываем: человеку нужен сигнал, а не простыня.</summary>
    private const int MaxRows = 20;

    public async Task<ImportRecordPreviewDto> AnalyzeAsync(string resultsJson, CancellationToken ct = default)
    {
        try
        {
            var rows = ParseRows(resultsJson);
            if (rows.Count == 0)
                return new ImportRecordPreviewDto { Error = "В файле нет личных заплывов с временем" };

            // Рекорды только Израиля: файлы, которые мы затягиваем, — израильские протоколы.
            // Мировые рекорды в этой проверке не нужны, а лишняя ось дала бы ложные срабатывания.
            var records = await db.Records.AsNoTracking()
                .Where(r => r.RegionType == "country" && r.RegionCode == "ISR")
                .ToListAsync(ct);

            if (records.Count == 0)
                return new ImportRecordPreviewDto { Error = "Справочник рекордов пуст — сверять не с чем" };

            var beaten = CompetitionRecordsDetector.DetectBest(records, rows);

            return new ImportRecordPreviewDto
            {
                Count = beaten.Count,
                Rows = beaten.Take(MaxRows).Select(b => new ImportRecordPreviewRow
                {
                    Kind = CompetitionRecordsDetector.Kind(b.Rec),
                    SwimmerName = $"{b.Row.FirstName} {b.Row.LastName}".Trim(),
                    Club = b.Row.Club,
                    StyleName = b.Row.StyleName,
                    Distance = b.Row.Distance,
                    Gender = b.Row.Gender,
                    Time = b.Row.TimeOriginal,
                    RecordTime = b.Rec.Time,
                    RecordHolder = b.Rec.HolderName ?? ""
                }).ToList()
            };
        }
        catch (Exception ex)
        {
            // Прибор не имеет права сорвать превью импорта.
            return new ImportRecordPreviewDto { Error = $"{ex.GetType().Name}: {ex.Message}" };
        }
    }

    /// <summary>
    /// JSON парсера → строки-кандидаты детектора. Эстафеты и незачтённые заплывы отброшены:
    /// у эстафет маппинг стилей неоднозначен (см. RecordCandidateRow), а DQ/DNS рекордов не бьют.
    /// </summary>
    private static List<RecordCandidateRow> ParseRows(string resultsJson)
    {
        var items = JsonImportService.DeserializeResultItems(resultsJson);
        var rows = new List<RecordCandidateRow>();

        foreach (var (item, index) in items.Select((x, i) => (x, i)))
        {
            if (item.IsRelay == true || item.TimeFail == true) continue;

            var ms = CompetitionRecordsDetector.ParseTimeToMs(item.Time);
            if (ms is null) continue;

            var gender = (item.EventStyleGender ?? "").Trim().ToLowerInvariant();
            if (gender is not ("male" or "female")) continue;   // смешанный заплыв рекордов не даёт

            var style = (item.EventStyleName ?? "").Trim();
            var distance = (item.EventStyleLen ?? "").Trim();
            if (style.Length == 0 || distance.Length == 0) continue;

            rows.Add(new RecordCandidateRow(
                // Id у строк файла ещё нет — превью не ссылается на результаты, хватает порядкового.
                ResultId: index,
                SwimmerId: 0,
                FirstName: item.FirstName ?? "",
                LastName: item.LastName ?? "",
                Club: item.Club ?? "",
                StyleName: style,
                Distance: distance,
                Gender: gender,
                PoolType: NormalizePool(item.PoolType),
                BirthYear: item.BirthYear,
                CompetitionDate: ParseDate(item.Date),
                TimeMilliseconds: ms.Value,
                TimeOriginal: item.Time ?? "",
                DayNumber: null,
                IsMasters: item.IsMasters == true,
                AgeGroup: item.AgeGroup ?? ""));
        }

        return rows;
    }

    /// <summary>«25» → «25m»: в Records бассейн хранится с суффиксом.</summary>
    private static string NormalizePool(string? poolType)
    {
        var p = (poolType ?? "").Trim().ToLowerInvariant();
        return p is "25" or "50" ? p + "m" : p;
    }

    /// <summary>
    /// dd/MM/yyyy из протокола. Год важен: возраст пловца детектор считает как
    /// «год соревнования − год рождения». Мусор → UtcNow, тогда возраст просто не совпадёт
    /// ни с одной ступенью и строка выпадет из age-оси (open-ось при этом продолжит работать).
    /// </summary>
    private static DateTime ParseDate(string? date) =>
        DateTime.TryParseExact(date, "dd/MM/yyyy", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var d) ? d : DateTime.UtcNow;
}
